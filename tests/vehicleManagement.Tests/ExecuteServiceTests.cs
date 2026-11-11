using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Execute;
using vehicleManagement.BLL.Location;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>扫码页保存使用记录的三条主链路（P0-2 / P0-3 回归）。</summary>
    public class ExecuteServiceTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly ExecuteService _service;

        public ExecuteServiceTests()
        {
            // 普通使用人员：只能给自己登记，部门取自身部门
            var user = new StubCurrentUser(new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 100,
                UserName = "machi",
                Role = Models.Roles.User,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10
            });
            var audit = new AuditService(_factory, user, NullLogger<AuditService>.Instance);
            var locationService = new LocationService(new LocationRepository(_factory), _factory, audit);
            _service = new ExecuteService(_factory, locationService, user, audit,
                NullLogger<ExecuteService>.Instance);
        }

        [Fact]
        public async Task SaveRecordAsync_WithoutFinalLocation_ReturnsError()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 1,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10,
                WaypointIds = new List<int> { 1 },
                FinalLocationId = 0            // 未选最终停车位置
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.False(ok);
            Assert.Equal("请选择最终停车位置", err);

            using (var db = _factory.CreateDbContext())
                Assert.Equal(0, await db.VehicleUsageRecords.CountAsync());
        }

        [Fact]
        public async Task SaveRecordAsync_WithWaypoints_PersistsContinuousSequenceOrder()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.Locations.Add(new Models.Location
                {
                    Id = 3,
                    Name = "食堂",
                    Category = Models.LocationCategory.Location,
                    SortOrder = 2,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 1,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10,
                WaypointIds = new List<int> { 3, 1, 2 },   // 顺序即用户点击顺序
                FinalLocationId = 2
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
            {
                var record = await db.VehicleUsageRecords.SingleAsync();
                Assert.Equal("3,1,2", record.PurposeSequence);

                var purposes = await db.VehicleUsageRecordPurposes
                    .Where(p => p.UsageRecordId == record.Id)
                    .OrderBy(p => p.SequenceOrder)
                    .Select(p => new { p.SequenceOrder, p.PurposeId })
                    .ToListAsync();

                Assert.Equal(new[] { 1, 2, 3 }, purposes.Select(p => p.SequenceOrder).ToArray());
                Assert.Equal(new[] { 3, 1, 2 }, purposes.Select(p => p.PurposeId).ToArray());
            }
        }

        [Fact]
        public async Task SaveRecordAsync_SameDepartment_WritesUsageAndKeepsVehicleAvailable()
        {
            // 同部门：扫本部门的车 → 使用登记，记录 IsBorrow=false，车辆不被置为已借用
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 1,                          // DeptA 的车
                DepartmentId = TestData.DeptB,          // 前端伪造：服务端必须丢弃
                PersonnelId = 10,
                WaypointIds = new List<int> { 1 },
                FinalLocationId = 2
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
            {
                var record = await db.VehicleUsageRecords.SingleAsync();
                Assert.False(record.IsBorrow);
                Assert.Equal(TestData.DeptA, record.DepartmentId);
                Assert.False((await db.Vehicles.FindAsync(1)).IsBorrowed);
            }
        }

        [Fact]
        public async Task SaveRecordAsync_CrossDepartment_WritesBorrowButLeavesVehicleAvailable()
        {
            // 跨部门：扫其他部门的车 → 记录 IsBorrow=true；提交即结束，车辆不得残留「已借用」
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 2,                          // DeptB 的车
                DepartmentId = TestData.DeptB,          // 前端伪造：归属仍取登录人部门 DeptA
                PersonnelId = 10,
                WaypointIds = new List<int> { 1 },
                FinalLocationId = 2
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
            {
                var record = await db.VehicleUsageRecords.SingleAsync();
                Assert.True(record.IsBorrow);
                Assert.Equal(TestData.DeptA, record.DepartmentId);
                Assert.False((await db.Vehicles.FindAsync(2)).IsBorrowed);
                Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "UsageRecord.Borrow"));
            }
        }

        [Theory]
        [InlineData(TestData.DeptA, 0, TestData.DeptA, false)]   // 同部门 → 使用
        [InlineData(TestData.DeptA, 0, TestData.DeptB, true)]    // 跨部门 → 借车
        [InlineData(TestData.DeptA, TestData.DeptB, TestData.DeptB, true)] // 前端伪造部门无效，仍按自身部门
        [InlineData(null, TestData.DeptA, TestData.DeptA, false)] // Admin 无部门：按表单归属判定
        [InlineData(null, TestData.DeptB, TestData.DeptA, true)]
        [InlineData(null, 0, TestData.DeptA, false)]              // 表单未填：回落到车辆部门
        public void ResolveIsBorrow_OnlyComparesDepartments(
            int? scopeDepartmentId, int requestedDepartmentId, int vehicleDepartmentId, bool expected)
        {
            Assert.Equal(expected,
                ExecuteService.ResolveIsBorrow(scopeDepartmentId, requestedDepartmentId, vehicleDepartmentId));
        }

        public void Dispose() => _factory.Dispose();
    }
}

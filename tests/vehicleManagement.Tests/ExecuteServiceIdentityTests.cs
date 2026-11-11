using System;
using System.Collections.Generic;
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
    /// <summary>回归：前端伪装的 PersonnelId / DepartmentId 必须被服务端丢弃（Blazor 无 HTTP 表单可伪造，只能直接调 BLL 验证）。</summary>
    public class ExecuteServiceIdentityTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly ExecuteService _service;

        public ExecuteServiceIdentityTests()
        {
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
        public async Task SaveRecordAsync_ForgedPersonnelAndDepartment_AreReplacedByIdentity()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 2,               // B 部门的车：跨部门用车是真实业务，允许但必须落审计
                DepartmentId = 999,          // 伪造：应被忽略
                PersonnelId = 3,             // 伪造：库里没有 id=3 的人员，应被忽略
                WaypointIds = new List<int> { 1 },
                FinalLocationId = 2
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
            {
                var record = await db.VehicleUsageRecords.SingleAsync();
                Assert.Equal(10, record.PersonnelId);                  // 取自身份快照
                Assert.Equal(TestData.DeptA, record.DepartmentId);     // 取自身份快照

                // 借车只写记录标记，不把车辆置为已借用
                var vehicle = await db.Vehicles.FindAsync(2);
                Assert.False(vehicle.IsBorrowed);

                // 跨部门必须留痕
                Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "UsageRecord.CrossDepartment"));
            }
        }

        [Fact]
        public async Task SaveRecordAsync_PersonnelIdZeroForUser_FallsBackToIdentity()
        {
            // User 角色的 PersonnelId 恒取自身，前端传 0 也不该报「请选择使用人员」
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var request = new SaveUsageRecordRequest
            {
                VehicleId = 1,
                PersonnelId = 0,
                WaypointIds = new List<int> { 1 },
                FinalLocationId = 2
            };

            var (ok, err) = await _service.SaveRecordAsync(request);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
                Assert.Equal(10, (await db.VehicleUsageRecords.SingleAsync()).PersonnelId);
        }

        public void Dispose() => _factory.Dispose();
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>维修人 / 维修部门只认登录身份，且「跨部门维修」按记录维度标注。</summary>
    public class MaintenanceRecordIdentityTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();
        private readonly StubCurrentUser _user;
        private readonly MaintenanceRecordService _service;

        public MaintenanceRecordIdentityTests()
        {
            // 本组用例验证「维修人/部门快照只认登录身份」与「跨部门标记」，需要能看到全部部门的记录：
            // 必须是 Admin（ScopeDepartmentId 恒为 null）；DepartmentId/PersonnelId 保留为 DeptA 的「马存宝」，
            // 才能断言写出的快照。部门账号的可见范围见 MaintenanceRecordScopeTests。
            _user = new StubCurrentUser(new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 100,
                UserName = "machi",
                Role = Models.Roles.Admin,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10
            });
            _service = new MaintenanceRecordService(
                new MaintenanceRecordRepository(_factory),
                _factory,
                new FileUploadService(_env),
                _user,
                new AuditService(_factory, _user, NullLogger<AuditService>.Instance),
                NullLogger<MaintenanceRecordService>.Instance);
        }

        private static Models.VehicleMaintenanceRecord NewRecord(int vehicleId) => new()
        {
            VehicleId = vehicleId,
            MaintenanceDate = ChinaTime.Now,
            Description = "更换刹车片",
            Cost = 600m
        };

        [Fact]
        public async Task CreateAsync_CrossDepartment_FlagsCrossDepartmentMaintenance()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            // 扫 DeptB 的车做维修 → 维修部门 DeptA 与车辆部门不一致
            await _service.CreateAsync(NewRecord(2));

            var page = await _service.GetPagedAsync("", 1, 10);
            var record = page.Items.Single();

            Assert.Equal(TestData.DeptA, record.MaintainerDepartmentId);
            Assert.Equal(10, record.MaintainerPersonnelId);
            Assert.Equal("马存宝", record.MaintainerName);
            Assert.Equal("信息安防技术室", record.MaintainerDepartmentName);
            Assert.True(record.IsCrossDepartment);
        }

        [Fact]
        public async Task CreateAsync_SameDepartment_DoesNotFlagCrossDepartment()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            // 扫自己部门（DeptA）的车做维修
            await _service.CreateAsync(NewRecord(1));

            var record = (await _service.GetPagedAsync("", 1, 10)).Items.Single();

            Assert.Equal(TestData.DeptA, record.MaintainerDepartmentId);
            Assert.False(record.IsCrossDepartment);
        }

        [Fact]
        public async Task CreateAsync_ForgedMaintainer_IsReplacedByLoginIdentity()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var forged = NewRecord(1);
            forged.MaintainerPersonnelId = 20;              // 伪造：DeptB 的人员
            forged.MaintainerDepartmentId = TestData.DeptB; // 伪造：DeptB 的部门
            await _service.CreateAsync(forged);

            var record = (await _service.GetPagedAsync("", 1, 10)).Items.Single();

            Assert.Equal(10, record.MaintainerPersonnelId);
            Assert.Equal(TestData.DeptA, record.MaintainerDepartmentId);
        }

        [Fact]
        public async Task GetPagedAsync_LegacyNullMaintainer_FallsBackToUnknown()
        {
            // 老库补列后历史记录维修人/部门为 NULL：读取侧必须回退「未知」，且不算跨部门
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleMaintenanceRecords.Add(NewRecord(2));
                await db.SaveChangesAsync();
            }

            var record = (await _service.GetPagedAsync("", 1, 10)).Items.Single();

            Assert.Null(record.MaintainerDepartmentId);
            Assert.Equal("未知", record.MaintainerName);
            Assert.Equal("未知", record.MaintainerDepartmentName);
            Assert.Null(record.IsCrossDepartment);
        }

        [Fact]
        public async Task UpdateAsync_KeepsOriginalMaintainerSnapshot()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            await _service.CreateAsync(NewRecord(1));
            var created = (await _service.GetPagedAsync("", 1, 10)).Items.Single();

            // 编辑页只回填可编辑字段，维修人/部门为 null；落库时不得把快照抹掉
            var patch = await _service.GetByIdAsync(created.Id);
            patch.Description = "更换雨刷";
            patch.MaintainerPersonnelId = null;
            patch.MaintainerDepartmentId = null;
            await _service.UpdateAsync(patch);

            var updated = (await _service.GetPagedAsync("", 1, 10)).Items.Single();
            Assert.Equal("更换雨刷", updated.Description);
            Assert.Equal(10, updated.MaintainerPersonnelId);
            Assert.Equal(TestData.DeptA, updated.MaintainerDepartmentId);
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}

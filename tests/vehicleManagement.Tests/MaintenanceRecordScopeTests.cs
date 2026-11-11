using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Export;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// 维修记录的部门隔离（BLL 层兜底）：
    /// 部门账号（ScopeDepartmentId 非 null）只能看到「被维修车辆属于本部门」的记录 ——
    /// 列表 / 计数 / 详情 / 按 Id 取 / 编辑 / 删除 / 导出全部同一判据；全局管理员行为不变。
    /// </summary>
    public class MaintenanceRecordScopeTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();

        public MaintenanceRecordScopeTests()
        {
            using var db = _factory.CreateDbContext();
            TestData.SeedBasicsAsync(db).GetAwaiter().GetResult();
        }

        /// <summary>部门账号：DeptAdmin 绑定 DeptA（车辆 1 属该部门，车辆 2 属 DeptB）。</summary>
        private static UserIdentity DeptAAccount() => new()
        {
            IsAuthenticated = true,
            UserId = 2,
            UserName = "anfang",
            Role = Models.Roles.DeptAdmin,
            DepartmentId = TestData.DeptA
        };

        /// <summary>全局管理员：DepartmentId 为空 → ScopeDepartmentId 为 null（不限）。</summary>
        private static UserIdentity AdminAccount() => new()
        {
            IsAuthenticated = true,
            UserId = 1,
            UserName = "admin",
            Role = Models.Roles.Admin,
            DepartmentId = null
        };

        private MaintenanceRecordService CreateService(UserIdentity identity)
        {
            var user = new StubCurrentUser(identity);
            return new MaintenanceRecordService(
                new MaintenanceRecordRepository(_factory),
                _factory,
                new FileUploadService(_env),
                user,
                new AuditService(_factory, user, NullLogger<AuditService>.Instance),
                NullLogger<MaintenanceRecordService>.Instance);
        }

        private VehicleExportService CreateExportService(UserIdentity identity)
            => new(_factory, new StubCurrentUser(identity));

        /// <summary>直接落库（绕过 CreateAsync 的身份覆盖），seedA 条本部门车 1、seedB 条他部门车 2。</summary>
        private async Task SeedRecordsAsync(int seedA, int seedB)
        {
            using var db = _factory.CreateDbContext();
            for (var i = 1; i <= seedA; i++)
                db.VehicleMaintenanceRecords.Add(NewRecord(1, $"A 部门维修 {i}", TestData.DeptA));
            for (var i = 1; i <= seedB; i++)
                db.VehicleMaintenanceRecords.Add(NewRecord(2, $"B 部门维修 {i}", TestData.DeptB));
            await db.SaveChangesAsync();
        }

        private static Models.VehicleMaintenanceRecord NewRecord(int vehicleId, string description, int deptId)
            => new()
            {
                VehicleId = vehicleId,
                MaintenanceDate = ChinaTime.Now.AddDays(-1),
                Description = description,
                Cost = 100m,
                MaintainerDepartmentId = deptId,
                CreatedAt = ChinaTime.Now
            };

        private async Task<int> FirstRecordIdOfVehicleAsync(int vehicleId)
        {
            using var db = _factory.CreateDbContext();
            return await db.VehicleMaintenanceRecords.AsNoTracking()
                .Where(r => r.VehicleId == vehicleId)
                .Select(r => r.Id)
                .FirstAsync();
        }

        // ===== 列表：过滤 + 计数一致 =====

        [Fact]
        public async Task GetPagedAsync_DeptAccount_OnlySeesOwnDepartmentRecords()
        {
            await SeedRecordsAsync(seedA: 3, seedB: 7);

            var result = await CreateService(DeptAAccount()).GetPagedAsync("", 1, 10);

            Assert.Equal(3, result.TotalCount);                 // 过滤后的总数，不是全库 10
            Assert.Equal(3, result.Items.Count);
            Assert.All(result.Items, r => Assert.Equal(1, r.VehicleId));
        }

        [Fact]
        public async Task GetPagedAsync_DeptAccount_TotalCountMatchesPaging()
        {
            await SeedRecordsAsync(seedA: 3, seedB: 7);
            var service = CreateService(DeptAAccount());

            var firstPage = await service.GetPagedAsync("", 1, 2);
            var secondPage = await service.GetPagedAsync("", 2, 2);

            // 「显示 N 条但共 M 条」的矛盾的根因就是 Count 没走同一过滤：这里必须都是 3
            Assert.Equal(3, firstPage.TotalCount);
            Assert.Equal(3, secondPage.TotalCount);
            Assert.Equal(2, firstPage.Items.Count);
            Assert.Single(secondPage.Items);
            Assert.All(secondPage.Items, r => Assert.Equal(1, r.VehicleId));
        }

        [Fact]
        public async Task GetPagedAsync_Admin_SeesAllDepartments()
        {
            await SeedRecordsAsync(seedA: 3, seedB: 7);

            var result = await CreateService(AdminAccount()).GetPagedAsync("", 1, 20);

            Assert.Equal(10, result.TotalCount);
            Assert.Equal(10, result.Items.Count);
            Assert.Contains(result.Items, r => r.VehicleId == 1);
            Assert.Contains(result.Items, r => r.VehicleId == 2);
        }

        [Fact]
        public async Task GetPagedAsync_DeptAccount_SearchCannotPierceScope()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 5);

            // 关键字命中他部门内容也不得返回（过滤先于搜索）
            var result = await CreateService(DeptAAccount()).GetPagedAsync("B 部门维修", 1, 10);

            Assert.Equal(0, result.TotalCount);
            Assert.Empty(result.Items);
        }

        // ===== 详情 / 按 Id 取：越权一律 null =====

        [Fact]
        public async Task GetDetailAsync_OtherDepartment_ReturnsNullAndLeaksNothing()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            var detail = await CreateService(DeptAAccount()).GetDetailAsync(otherId);

            Assert.Null(detail);        // 与「记录不存在」同一个结果，不回显任何字段
        }

        [Fact]
        public async Task GetDetailAsync_OwnDepartment_ReturnsRecord()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 1);
            var ownId = await FirstRecordIdOfVehicleAsync(1);

            var detail = await CreateService(DeptAAccount()).GetDetailAsync(ownId);

            Assert.NotNull(detail);
            Assert.Equal("京A88601", detail.Vehicle.LicensePlate);
            Assert.Equal("A 部门维修 1", detail.Description);
        }

        [Fact]
        public async Task GetDetailAsync_Admin_StillSeesOtherDepartment()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            var detail = await CreateService(AdminAccount()).GetDetailAsync(otherId);

            Assert.NotNull(detail);
            Assert.Equal("京B12345", detail.Vehicle.LicensePlate);
        }

        [Fact]
        public async Task GetByIdAsync_OtherDepartment_ReturnsNull()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            Assert.Null(await CreateService(DeptAAccount()).GetByIdAsync(otherId));
        }

        // ===== 编辑 / 删除：写前断归属 =====

        [Fact]
        public async Task UpdateAsync_OtherDepartment_RejectedAndDataUnchanged()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            var patch = new Models.VehicleMaintenanceRecord
            {
                Id = otherId,
                MaintenanceDate = ChinaTime.Now,
                Description = "越权改名",
                Cost = 999m
            };
            var (ok, error) = await CreateService(DeptAAccount()).UpdateAsync(patch);

            Assert.False(ok);
            Assert.Equal("访问被拒绝：该记录不属于你所属的部门。", error);

            using var db = _factory.CreateDbContext();
            var stored = await db.VehicleMaintenanceRecords.AsNoTracking().FirstAsync(r => r.Id == otherId);
            Assert.Equal("B 部门维修 1", stored.Description);
            Assert.Equal(100m, stored.Cost);
            // 被拒的写操作不得留审计 Update 记录（审计只记成功的写）
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "MaintenanceRecord.Update"));
        }

        [Fact]
        public async Task UpdateAsync_OwnDepartment_Succeeds()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 0);
            var ownId = await FirstRecordIdOfVehicleAsync(1);
            var service = CreateService(DeptAAccount());

            var patch = await service.GetDetailAsync(ownId);
            patch.Description = "本部门改名";
            var (ok, error) = await service.UpdateAsync(patch);

            Assert.True(ok);
            Assert.Null(error);

            using var db = _factory.CreateDbContext();
            Assert.Equal("本部门改名",
                (await db.VehicleMaintenanceRecords.AsNoTracking().FirstAsync(r => r.Id == ownId)).Description);
        }

        [Fact]
        public async Task UpdateAsync_Admin_StillUpdatesOtherDepartment()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);
            var service = CreateService(AdminAccount());

            var patch = await service.GetDetailAsync(otherId);
            patch.Description = "管理员改名";
            var (ok, _) = await service.UpdateAsync(patch);

            Assert.True(ok);

            using var db = _factory.CreateDbContext();
            Assert.Equal("管理员改名",
                (await db.VehicleMaintenanceRecords.AsNoTracking().FirstAsync(r => r.Id == otherId)).Description);
        }

        [Fact]
        public async Task DeleteAsync_OtherDepartment_RejectedAndRecordKept()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            var (ok, error) = await CreateService(DeptAAccount()).DeleteAsync(otherId);

            Assert.False(ok);
            Assert.Equal("访问被拒绝：该记录不属于你所属的部门。", error);

            using var db = _factory.CreateDbContext();
            Assert.True(await db.VehicleMaintenanceRecords.AnyAsync(r => r.Id == otherId));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "MaintenanceRecord.Delete"));
        }

        [Fact]
        public async Task DeleteAsync_Admin_StillDeletesOtherDepartment()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);
            var otherId = await FirstRecordIdOfVehicleAsync(2);

            var (ok, _) = await CreateService(AdminAccount()).DeleteAsync(otherId);

            Assert.True(ok);

            using var db = _factory.CreateDbContext();
            Assert.False(await db.VehicleMaintenanceRecords.AnyAsync(r => r.Id == otherId));
        }

        // ===== 导出（相邻路径，含维修记录整段）=====

        [Fact]
        public async Task ExportAsync_OtherDepartment_ReturnsNull()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);

            Assert.Null(await CreateExportService(DeptAAccount()).ExportAsync(2));
        }

        [Fact]
        public async Task ExportAsync_OwnDepartment_Succeeds()
        {
            await SeedRecordsAsync(seedA: 1, seedB: 0);

            var bytes = await CreateExportService(DeptAAccount()).ExportAsync(1);

            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 0);
        }

        [Fact]
        public async Task ExportAsync_Admin_ExportsAnyDepartment()
        {
            await SeedRecordsAsync(seedA: 0, seedB: 1);

            Assert.NotNull(await CreateExportService(AdminAccount()).ExportAsync(2));
        }

        // ===== 登记：跨部门维修是合法业务，但归属仍按车辆部门收窄 =====

        [Fact]
        public async Task CreateAsync_CrossDepartment_AllowedButVisibleOnlyToVehicleDepartment()
        {
            var me = DeptAAccount();

            // DeptA 的人为 DeptB 的车登记维修 —— 合法（「跨部门维修」就是这类记录）
            var created = await CreateService(me).CreateAsync(NewRecord(2, "跨部门更换刹车片", TestData.DeptA));
            Assert.True(created.Id > 0);

            // 记录归属被维修车辆：DeptA 账号看不到自己刚登记的这一条
            var aResult = await CreateService(me).GetPagedAsync("", 1, 10);
            Assert.Empty(aResult.Items);
            Assert.Equal(0, aResult.TotalCount);

            // 车主部门（DeptB）与管理员可见
            var deptBIdentity = new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 3,
                UserName = "shebei",
                Role = Models.Roles.DeptAdmin,
                DepartmentId = TestData.DeptB
            };
            Assert.Equal(1, (await CreateService(deptBIdentity).GetPagedAsync("", 1, 10)).TotalCount);
            Assert.Equal(1, (await CreateService(AdminAccount()).GetPagedAsync("", 1, 10)).TotalCount);
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Execute;
using vehicleManagement.BLL.Location;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>起始里程预填：取该车最近一条「有结束里程」的记录，无历史保持为空。</summary>
    public class ExecuteServiceMileagePrefillTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly ExecuteService _service;

        public ExecuteServiceMileagePrefillTests()
        {
            // 普通使用人员：DeptA 的「马存宝」，PersonnelId=10
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

        private static Models.VehicleUsageRecord Usage(
            int id, int vehicleId, DateTime createdAt, double? mileageEnd)
            => new()
            {
                Id = id,
                VehicleId = vehicleId,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10,
                FinalLocationId = 2,
                PurposeSequence = "",
                MileageEnd = mileageEnd,
                CreatedAt = createdAt
            };

        [Fact]
        public async Task GetLastMileageEndAsync_NoHistory_ReturnsNull()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var value = await _service.GetLastMileageEndAsync(1);

            // 无历史必须为空（不能返回 0，否则界面会显示 0 而不是占位符）
            Assert.Null(value);
        }

        [Fact]
        public async Task GetLastMileageEndAsync_SingleRecord_ReturnsItsEnd()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleUsageRecords.Add(Usage(1, 2, ChinaTime.Now, 1234.5));
                await db.SaveChangesAsync();
            }

            Assert.Equal(1234.5, await _service.GetLastMileageEndAsync(2));
        }

        [Fact]
        public async Task GetLastMileageEndAsync_MultipleRecords_TakesLatestByCreatedAt()
        {
            var now = ChinaTime.Now;
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleUsageRecords.AddRange(
                    Usage(1, 2, now.AddDays(-2), 900),
                    Usage(2, 2, now.AddDays(-1), 1000),
                    Usage(3, 2, now.AddDays(-3), 800));
                await db.SaveChangesAsync();
            }

            Assert.Equal(1000, await _service.GetLastMileageEndAsync(2));
        }

        [Fact]
        public async Task GetLastMileageEndAsync_SameCreatedAt_UsesIdDescAsTieBreaker()
        {
            var now = ChinaTime.Now;
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                // 同秒两条：只按 CreatedAt 排会抖动，必须靠 Id 降序兜底
                db.VehicleUsageRecords.AddRange(
                    Usage(1, 2, now, 100),
                    Usage(2, 2, now, 200));
                await db.SaveChangesAsync();
            }

            Assert.Equal(200, await _service.GetLastMileageEndAsync(2));
        }

        [Fact]
        public async Task GetLastMileageEndAsync_LatestRecordWithoutEnd_FallsBackToEarlierWithEnd()
        {
            var now = ChinaTime.Now;
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                // 最近一条还没填结束里程（车辆未归还）→ 必须继续往前找，而不是预填空值
                db.VehicleUsageRecords.AddRange(
                    Usage(1, 2, now.AddDays(-1), 1000),
                    Usage(2, 2, now, null));
                await db.SaveChangesAsync();
            }

            Assert.Equal(1000, await _service.GetLastMileageEndAsync(2));
        }

        [Fact]
        public async Task LoadPageDataAsync_PreFillSharedByBothBranches()
        {
            var now = ChinaTime.Now;
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleUsageRecords.AddRange(
                    Usage(1, 1, now, 5000),   // DeptA 的车：同部门 → 使用分支
                    Usage(2, 2, now, 7000));  // DeptB 的车：跨部门 → 借车分支
                await db.SaveChangesAsync();
            }

            // 使用人员（DeptA）扫本部门车：同部门判据成立，预填照常带出
            var sameDept = await _service.LoadPageDataAsync("token-a");
            Assert.NotNull(sameDept);
            Assert.False(sameDept!.IsBorrow);
            Assert.Equal(5000, sameDept.PreFilledMileageStart);

            // 同一个登录人扫其他部门的车：借车分支，预填同样带出
            var crossDept = await _service.LoadPageDataAsync("token-b");
            Assert.NotNull(crossDept);
            Assert.True(crossDept!.IsBorrow);
            Assert.Equal(7000, crossDept.PreFilledMileageStart);
        }

        public void Dispose() => _factory.Dispose();
    }
}

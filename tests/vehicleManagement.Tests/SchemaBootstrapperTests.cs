using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>DDL 引导回归：SQL 里的花括号会被当成复合格式占位符抛 FormatException。</summary>
    public class SchemaBootstrapperTests : System.IDisposable
    {
        private readonly TestDbFactory _factory = new();

        [Fact]
        public async Task ApplyAsync_IsExecutableAndIdempotent()
        {
            using (var db = _factory.CreateDbContext())
                await SchemaBootstrapper.ApplyAsync(db, NullLogger.Instance);

            // 连跑第二次不得因索引已存在而失败
            using (var db = _factory.CreateDbContext())
                await SchemaBootstrapper.ApplyAsync(db, NullLogger.Instance);

            using (var db = _factory.CreateDbContext())
            {
                foreach (var index in new[]
                {
                    "IX_Personnel_DepartmentId_IsActive",
                    "IX_Locations_Category_IsActive_SortOrder",
                    "IX_Vehicles_Status_LicensePlate",
                    "IX_VehicleUsageRecords_PersonnelId",
                    "IX_VehicleUsageRecords_FinalLocationId",
                    "IX_VehicleUsageRecords_DepartmentId_CreatedAt",
                    "IX_VehicleUsageRecords_VehicleId_CreatedAt",
                    "IX_VehicleMaintenanceRecords_MaintenanceDate"
                })
                {
                    var exists = await db.Database
                        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='index' AND name = {0}", index)
                        .SingleAsync();
                    Assert.True(exists == 1, $"索引未建立：{index}");
                }
            }
        }

        [Fact]
        public async Task ApplyAsync_ResetsLegacyVehicleBorrowedFlag_ButKeepsRecordFlag()
        {
            // 存量：车辆 IsBorrowed=1，同时有一条历史借车记录 IsBorrow=1
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                (await db.Vehicles.FindAsync(1))!.IsBorrowed = true;
                db.VehicleUsageRecords.Add(new Models.VehicleUsageRecord
                {
                    VehicleId = 1,
                    DepartmentId = TestData.DeptA,
                    PersonnelId = 10,
                    FinalLocationId = 2,
                    PurposeSequence = "",
                    IsBorrow = true,
                    CreatedAt = ChinaTime.Now
                });
                await db.SaveChangesAsync();
            }

            using (var db = _factory.CreateDbContext())
                await SchemaBootstrapper.ApplyAsync(db, NullLogger.Instance);

            using (var db = _factory.CreateDbContext())
            {
                Assert.False((await db.Vehicles.FindAsync(1))!.IsBorrowed);               // 车辆状态归零
                Assert.True((await db.VehicleUsageRecords.SingleAsync()).IsBorrow);       // 历史借车标记必须保留
            }

            // 幂等：第二次执行不得报错，也不得动记录标记
            using (var db = _factory.CreateDbContext())
                await SchemaBootstrapper.ApplyAsync(db, NullLogger.Instance);
        }

        public void Dispose() => _factory.Dispose();
    }
}

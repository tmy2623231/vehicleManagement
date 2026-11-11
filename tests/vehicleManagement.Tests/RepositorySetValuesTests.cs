using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>仓储读-改-写回归：改动要落库，且不得覆盖同时加载的其它列与导航属性。</summary>
    public class RepositorySetValuesTests : System.IDisposable
    {
        private readonly TestDbFactory _factory = new();

        private async Task SeedAsync()
        {
            using var db = _factory.CreateDbContext();
            await TestData.SeedBasicsAsync(db);
            db.VehicleMaintenanceRecords.Add(new Models.VehicleMaintenanceRecord
            {
                Id = 1,
                VehicleId = 1,
                MaintenanceDate = ChinaTime.Now,
                Description = "首次保养",
                Cost = 300m,
                Remarks = "备注原文",
                ImagePaths = "/Folder/a.jpg"
            });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task VehicleRepository_UpdateAsync_PersistsChangeAndKeepsSiblings()
        {
            await SeedAsync();
            var repo = new VehicleRepository(_factory);

            var vehicle = await repo.GetByIdAsync(1);
            vehicle.Color = "蓝色";
            await repo.UpdateAsync(vehicle);

            using var db = _factory.CreateDbContext();
            var saved = await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 1);
            Assert.Equal("蓝色", saved.Color);
            Assert.Equal("京A88601", saved.LicensePlate);
            Assert.Equal(TestData.DeptA, saved.DepartmentId);

            // 部门表不能被车辆更新连带覆盖
            var dept = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == TestData.DeptA);
            Assert.Equal("信息安防技术室", dept.Name);
        }

        [Fact]
        public async Task DepartmentRepository_UpdateAsync_PersistsChange()
        {
            await SeedAsync();
            var repo = new DepartmentRepository(_factory);

            var dept = await repo.GetByIdAsync(TestData.DeptA);
            dept.Description = "改过的描述";
            await repo.UpdateAsync(dept);

            using var db = _factory.CreateDbContext();
            var saved = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == TestData.DeptA);
            Assert.Equal("改过的描述", saved.Description);
            Assert.Equal("信息安防技术室", saved.Name);
        }

        [Fact]
        public async Task LocationRepository_UpdateAsync_PersistsChangeWithoutTouchingCategory()
        {
            await SeedAsync();
            var repo = new LocationRepository(_factory);

            var location = await repo.GetByIdAsync(2);
            location.SortOrder = 42;
            await repo.UpdateAsync(location);   // Location 无 UpdatedAt 列，只换 SetValues

            using var db = _factory.CreateDbContext();
            var saved = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == 2);
            Assert.Equal(42, saved.SortOrder);
            Assert.Equal(Models.LocationCategory.Purpose, saved.Category);
            Assert.True(saved.IsActive);
        }

        [Fact]
        public async Task MaintenanceRecordRepository_UpdateAsync_PersistsChangeWithoutChangingVehicle()
        {
            await SeedAsync();
            var repo = new MaintenanceRecordRepository(_factory);

            var record = await repo.GetByIdAsync(1);
            record.Cost = 880m;
            await repo.UpdateAsync(record);

            using var db = _factory.CreateDbContext();
            var saved = await db.VehicleMaintenanceRecords.AsNoTracking().FirstAsync(r => r.Id == 1);
            Assert.Equal(880m, saved.Cost);
            Assert.Equal("首次保养", saved.Description);
            Assert.Equal(1, saved.VehicleId);
            Assert.Equal("/Folder/a.jpg", saved.ImagePaths);
        }

        public void Dispose() => _factory.Dispose();
    }
}

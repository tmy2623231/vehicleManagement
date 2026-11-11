using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Vehicle;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>P0-4 回归：有维修记录的车辆必须「返回失败」而不是「抛 DbUpdateException」。</summary>
    public class VehicleServiceTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();
        private readonly VehicleService _service;

        public VehicleServiceTests()
        {
            var user = StubCurrentUser.Admin();
            var audit = new AuditService(_factory, user, NullLogger<AuditService>.Instance);
            _service = new VehicleService(
                new VehicleRepository(_factory),
                new DepartmentRepository(_factory),
                _factory,
                new FileUploadService(_env),
                user,
                audit,
                NullLogger<VehicleService>.Instance);
        }

        [Fact]
        public async Task DeleteVehicleAsync_WithMaintenanceRecord_FailsGracefullyAndKeepsVehicle()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleMaintenanceRecords.Add(new Models.VehicleMaintenanceRecord
                {
                    VehicleId = 2,
                    MaintenanceDate = ChinaTime.Now,
                    Description = "更换机油机滤",
                    Cost = 520m
                });
                await db.SaveChangesAsync();
            }

            var (ok, err) = await _service.DeleteVehicleAsync(2);

            Assert.False(ok);
            Assert.Contains("维修记录", err);

            using (var db = _factory.CreateDbContext())
                Assert.NotNull(await db.Vehicles.FindAsync(2));
        }

        [Fact]
        public async Task EnsureQrCodeTokenAsync_GeneratesOnceAndKeepsDepartment()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                // 无 token 的车：验证「首次生成、二次不变」且部门归属不被覆盖
                db.Vehicles.Add(new Models.Vehicle
                {
                    Id = 3,
                    DepartmentId = TestData.DeptA,
                    LicensePlate = "京C00001",
                    Status = Models.VehicleStatus.Active
                });
                await db.SaveChangesAsync();
            }

            var qr = new QrCodeService();
            var first = await _service.EnsureQrCodeTokenAsync(await _service.GetVehicleByIdAsync(3), qr);
            Assert.False(string.IsNullOrEmpty(first));

            var second = await _service.EnsureQrCodeTokenAsync(await _service.GetVehicleByIdAsync(3), qr);
            Assert.Equal(first, second);   // 已存在 token 不重新生成

            using (var db = _factory.CreateDbContext())
            {
                var saved = await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 3);
                Assert.Equal(first, saved.QrCodeToken);
                Assert.Equal(TestData.DeptA, saved.DepartmentId);
            }
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}

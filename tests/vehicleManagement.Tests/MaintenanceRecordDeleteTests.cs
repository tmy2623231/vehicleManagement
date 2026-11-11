using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>P2-7 第三处：维修记录同样「先落库、后删附件」，附件清理失败不阻断删除。</summary>
    public class MaintenanceRecordDeleteTests : System.IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();
        private readonly MaintenanceRecordService _service;

        private const string StoredPath = "/Folder/dept/plate/person/2026-02-02/b.jpg";

        public MaintenanceRecordDeleteTests()
        {
            var user = StubCurrentUser.Admin();
            _service = new MaintenanceRecordService(
                new MaintenanceRecordRepository(_factory),
                _factory,
                new FileUploadService(_env),
                user,
                new AuditService(_factory, user, NullLogger<AuditService>.Instance),
                NullLogger<MaintenanceRecordService>.Instance);
        }

        private async Task<int> SeedAsync()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleMaintenanceRecords.Add(new Models.VehicleMaintenanceRecord
                {
                    VehicleId = 1,
                    MaintenanceDate = ChinaTime.Now,
                    Description = "更换刹车片",
                    Cost = 600m,
                    ImagePaths = $"{StoredPath};/Folder/dept/plate/person/2026-02-02/c.jpg"
                });
                await db.SaveChangesAsync();
            }

            var relative = StoredPath.Substring("/Folder/".Length).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.FolderRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            await File.WriteAllBytesAsync(fullPath, new byte[] { 9 });

            using var db2 = _factory.CreateDbContext();
            return (await db2.VehicleMaintenanceRecords.AsNoTracking().FirstAsync()).Id;
        }

        [Fact]
        public async Task DeleteAsync_RemovesRowAndExistingAttachmentAndAudits()
        {
            var id = await SeedAsync();
            var relative = StoredPath.Substring("/Folder/".Length).Replace('/', Path.DirectorySeparatorChar);
            var existing = Path.Combine(_env.FolderRoot, relative);
            Assert.True(File.Exists(existing));

            var (ok, err) = await _service.DeleteAsync(id);

            Assert.True(ok);
            Assert.Null(err);
            Assert.False(File.Exists(existing));   // 存在的附件被清理

            using var db = _factory.CreateDbContext();
            Assert.False(await db.VehicleMaintenanceRecords.AnyAsync());
            // 另一条路径本就不存在，属「孤儿文件」分支，不得阻断删除
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "MaintenanceRecord.Delete"));
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}

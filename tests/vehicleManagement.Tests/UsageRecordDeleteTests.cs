using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.UsageRecord;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>P2-7 + P2-8 回归：删除记录要「先落库、后删文件」，且必须留审计。</summary>
    public class UsageRecordDeleteTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();

        private UsageRecordService CreateService(ICurrentUser user)
            => new(new UsageRecordRepository(_factory), _factory, new FileUploadService(_env),
                user, new AuditService(_factory, user, NullLogger<AuditService>.Instance),
                NullLogger<UsageRecordService>.Instance);

        private const string StoredPath = "/Folder/信息安防技术室/京A88601/马存宝/2026-01-01/a.jpg";

        private async Task<int> SeedRecordWithAttachmentAsync()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleUsageRecords.Add(new Models.VehicleUsageRecord
                {
                    VehicleId = 1,
                    DepartmentId = TestData.DeptA,
                    PersonnelId = 10,
                    FinalLocationId = 2,
                    PurposeSequence = "1",
                    ImagePaths = StoredPath
                });
                await db.SaveChangesAsync();
            }

            // 真实落一份附件，验证删记录时文件也被清理
            var relative = StoredPath.Substring("/Folder/".Length).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.FolderRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            await File.WriteAllBytesAsync(fullPath, new byte[] { 1, 2, 3 });

            using var db2 = _factory.CreateDbContext();
            return (await db2.VehicleUsageRecords.AsNoTracking().FirstAsync()).Id;
        }

        [Fact]
        public async Task DeleteAsync_RemovesRowAttachmentAndWritesAudit()
        {
            var recordId = await SeedRecordWithAttachmentAsync();
            var relative = StoredPath.Substring("/Folder/".Length).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.FolderRoot, relative);
            Assert.True(File.Exists(fullPath));

            var service = CreateService(StubCurrentUser.Admin());
            var (ok, err) = await service.DeleteAsync(recordId);

            Assert.True(ok);
            Assert.Null(err);
            Assert.False(File.Exists(fullPath));          // 附件已清理

            using (var db = _factory.CreateDbContext())
            {
                Assert.False(await db.VehicleUsageRecords.AnyAsync());
                Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "UsageRecord.Delete"));
            }
        }

        [Fact]
        public async Task DeleteAsync_DeptAdmin_IsReadOnly()
        {
            var recordId = await SeedRecordWithAttachmentAsync();

            var deptAdmin = new StubCurrentUser(new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 2,
                UserName = "anfang",
                Role = Models.Roles.DeptAdmin,
                DepartmentId = TestData.DeptA
            });

            var (ok, err) = await CreateService(deptAdmin).DeleteAsync(recordId);

            Assert.False(ok);
            Assert.Contains("无删除权限", err);

            using (var db = _factory.CreateDbContext())
                Assert.True(await db.VehicleUsageRecords.AnyAsync());
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}

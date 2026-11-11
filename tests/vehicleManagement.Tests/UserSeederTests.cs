using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>账号播种的三条不变量：幂等、绝不覆盖口令、同名人员用户名互不相同。</summary>
    public class UserSeederTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly IPasswordHasher<Models.AppUser> _hasher = new PasswordHasher<Models.AppUser>();

        private Task SeedUsersAsync()
            => UserSeeder.EnsureSeedUsersAsync(_factory, _hasher, NullLogger.Instance);

        [Fact]
        public async Task EnsureSeedUsersAsync_RunTwice_DoesNotDuplicateAccounts()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            await SeedUsersAsync();

            int afterFirst;
            using (var db = _factory.CreateDbContext())
                afterFirst = await db.AppUsers.CountAsync();

            Assert.True(afterFirst > 0);

            await SeedUsersAsync();

            using (var db = _factory.CreateDbContext())
                Assert.Equal(afterFirst, await db.AppUsers.CountAsync());
        }

        [Fact]
        public async Task EnsureSeedUsersAsync_DoesNotOverwriteExistingPassword()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            await SeedUsersAsync();

            const string manualHash = "AQAAAAEAACcQAAAAEMANUALLY-RESET-BY-OPERATOR";
            using (var db = _factory.CreateDbContext())
            {
                var account = await db.AppUsers.FirstAsync(u => u.UserName == "machi");
                account.PasswordHash = manualHash;
                await db.SaveChangesAsync();
            }

            await SeedUsersAsync();

            using (var db = _factory.CreateDbContext())
                Assert.Equal(manualHash, (await db.AppUsers.FirstAsync(u => u.UserName == "machi")).PasswordHash);
        }

        [Fact]
        public async Task EnsureSeedUsersAsync_DuplicatePersonnelNames_GetDistinctUserNames()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                // 与 id=12 同名的第二个人
                db.Personnel.Add(new Models.Personnel
                {
                    Id = 13,
                    DepartmentId = TestData.DeptA,
                    Name = "马驰",
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            await SeedUsersAsync();

            using (var db = _factory.CreateDbContext())
            {
                var userNames = await db.AppUsers
                    .Where(u => u.PersonnelId == 12 || u.PersonnelId == 13)
                    .Select(u => u.UserName)
                    .ToListAsync();

                Assert.Equal(2, userNames.Count);
                Assert.NotEqual(userNames[0], userNames[1]);
            }
        }

        [Fact]
        public async Task EnsureSeedUsersAsync_InactivePersonnel_GetsNoAccount()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);

                // 停用（离职）人员：不该拿到可登录账号，否则「停用」只是名单上的标记
                var leaving = await db.Personnel.FirstAsync(p => p.Id == 11);
                leaving.IsActive = false;
                await db.SaveChangesAsync();
            }

            await SeedUsersAsync();

            using (var db = _factory.CreateDbContext())
            {
                Assert.False(await db.AppUsers.AnyAsync(u => u.PersonnelId == 11));
                Assert.True(await db.AppUsers.AnyAsync(u => u.PersonnelId == 12));   // 在职人员照常开通
            }
        }

        public void Dispose() => _factory.Dispose();
    }
}

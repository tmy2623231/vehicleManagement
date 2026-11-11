using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// 校验删除账号的服务端契约：只能动本部门普通使用人员、必须落 User.Delete 审计、且是软删（否则会被启动种子复活）。
    /// </summary>
    public class AccountServiceDeleteTests : System.IDisposable
    {
        /// <summary>UserSeeder 给存量人员用的出厂口令。</summary>
        private const string FactoryPagePassword = "123456";

        private readonly TestDbFactory _factory = new();
        private readonly IPasswordHasher<Models.AppUser> _hasher = new PasswordHasher<Models.AppUser>();

        private AccountService CreateService(UserIdentity identity)
        {
            var user = new StubCurrentUser(identity);
            return new AccountService(
                _factory,
                user,
                new AuthService(_factory, _hasher),
                new AuditService(_factory, user, NullLogger<AuditService>.Instance));
        }

        private static UserIdentity Admin() => new()
        {
            IsAuthenticated = true,
            UserId = 1,
            UserName = "admin",
            Role = Models.Roles.Admin
        };

        private async Task SeedAccountsAsync()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            await UserSeeder.EnsureSeedUsersAsync(_factory, _hasher, NullLogger.Instance);
        }

        /// <summary>按绑定人员 Id 取账号（不硬编码拼音用户名）。</summary>
        private async Task<Models.AppUser> GetAccountByPersonnelAsync(int personnelId)
        {
            using var db = _factory.CreateDbContext();
            return await db.AppUsers.AsNoTracking().FirstAsync(u => u.PersonnelId == personnelId);
        }

        [Fact]
        public async Task DeleteAccountAsync_KeepsRowDisablesLoginAndAudits()
        {
            await SeedAccountsAsync();
            var account = await GetAccountByPersonnelAsync(12);   // 马驰，A 部门

            var (ok, err) = await CreateService(Admin()).DeleteAccountAsync(account.Id);

            Assert.True(ok);
            Assert.Null(err);

            using (var db = _factory.CreateDbContext())
            {
                var saved = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == account.Id);
                Assert.NotNull(saved);                       // 软删：行必须还在
                Assert.False(saved.IsActive);
                Assert.NotEqual(account.PasswordHash, saved.PasswordHash);   // 旧口令失效
                Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "User.Delete" && a.TargetId == account.Id));
            }

            // 出厂口令不再可用：这才是「删除」对用户的可见承诺
            var auth = new AuthService(_factory, _hasher);
            Assert.Null(await auth.VerifyAsync(account.UserName, FactoryPagePassword));
        }

        [Fact]
        public async Task DeleteAccountAsync_ThenRestartSeeding_DoesNotResurrectAccount()
        {
            await SeedAccountsAsync();
            var account = await GetAccountByPersonnelAsync(12);

            var (ok, _) = await CreateService(Admin()).DeleteAccountAsync(account.Id);
            Assert.True(ok);

            // 模拟应用重启：启动种子再跑一遍
            await UserSeeder.EnsureSeedUsersAsync(_factory, _hasher, NullLogger.Instance);

            using var db = _factory.CreateDbContext();
            var afterRestart = await db.AppUsers.AsNoTracking()
                                        .Where(u => u.PersonnelId == 12)
                                        .ToListAsync();

            Assert.Single(afterRestart);                       // 不得多出一行
            Assert.False(afterRestart[0].IsActive);            // 也不得变成可登录的 123456
            Assert.Equal(account.Id, afterRestart[0].Id);

            var auth = new AuthService(_factory, _hasher);
            Assert.Null(await auth.VerifyAsync(account.UserName, FactoryPagePassword));
        }

        [Fact]
        public async Task CreateAccountAsync_AfterDelete_ReopensSameRow()
        {
            await SeedAccountsAsync();
            var account = await GetAccountByPersonnelAsync(12);

            var (deleted, deleteError) = await CreateService(Admin()).DeleteAccountAsync(account.Id);
            Assert.True(deleted, deleteError);

            // 人员列表在软删后显示「开通账号」，点它就是这条路径
            var (reopened, reopenError) = await CreateService(Admin())
                .CreateAccountAsync(12, account.UserName, "pass1234");

            Assert.True(reopened, reopenError);

            using (var db = _factory.CreateDbContext())
            {
                var rows = await db.AppUsers.AsNoTracking().Where(u => u.PersonnelId == 12).ToListAsync();
                Assert.Single(rows);                              // 复用同一行，不撞唯一索引
                Assert.Equal(account.Id, rows[0].Id);             // Id 不变，审计链不断
                Assert.True(rows[0].IsActive);
            }

            var auth = new AuthService(_factory, _hasher);
            Assert.NotNull(await auth.VerifyAsync(account.UserName, "pass1234"));
        }

        [Fact]
        public async Task DeleteAccountAsync_AdminAccount_IsRejected()
        {
            await SeedAccountsAsync();

            int adminId;
            using (var db = _factory.CreateDbContext())
                adminId = await db.AppUsers.Where(u => u.Role == Models.Roles.Admin).Select(u => u.Id).FirstAsync();

            var (ok, err) = await CreateService(Admin()).DeleteAccountAsync(adminId);

            Assert.False(ok);
            Assert.Contains("普通使用人员", err);

            using (var db = _factory.CreateDbContext())
            {
                var admin = await db.AppUsers.AsNoTracking().FirstAsync(u => u.Id == adminId);
                Assert.True(admin.IsActive);   // 不会把自己锁在外面
            }
        }

        [Fact]
        public async Task DeleteAccountAsync_OtherDepartmentAccount_IsRejected()
        {
            await SeedAccountsAsync();
            var otherDeptAccount = await GetAccountByPersonnelAsync(21);   // 胡克汉，B 部门

            var deptAdminA = new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 2,
                UserName = "anfang",
                Role = Models.Roles.DeptAdmin,
                DepartmentId = TestData.DeptA
            };

            var (ok, err) = await CreateService(deptAdminA).DeleteAccountAsync(otherDeptAccount.Id);

            Assert.False(ok);
            Assert.Contains("本部门", err);

            using (var db = _factory.CreateDbContext())
            {
                var saved = await db.AppUsers.AsNoTracking().FirstAsync(u => u.Id == otherDeptAccount.Id);
                Assert.True(saved.IsActive);
            }
        }

        public void Dispose() => _factory.Dispose();
    }
}

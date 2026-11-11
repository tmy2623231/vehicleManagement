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
    /// 后台账号（Admin / DeptAdmin）管理契约：只看后台账号、新部门可开号、
    /// 部门唯一管理员、角色白名单、防锁死、口令走哈希、写操作落审计。
    /// </summary>
    public class AccountServiceBackOfficeTests : System.IDisposable
    {
        private const int NewDeptId = 8;

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

        private static UserIdentity Admin(int userId = 1, string userName = "admin") => new()
        {
            IsAuthenticated = true,
            UserId = userId,
            UserName = userName,
            Role = Models.Roles.Admin
        };

        private async Task SeedAsync()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);

                // 模拟「后续新建的部门」：种子里没有它，只能靠页面开号
                db.Departments.Add(new Models.Department { Id = NewDeptId, Name = "新设综合室" });
                await db.SaveChangesAsync();
            }

            await UserSeeder.EnsureSeedUsersAsync(_factory, _hasher, NullLogger.Instance);
        }

        private Models.AppUser GetByName(string userName)
        {
            using var db = _factory.CreateDbContext();
            return db.AppUsers.AsNoTracking().First(u => u.UserName == userName);
        }

        [Fact]
        public async Task GetPagedAccountsAsync_ReturnsOnlyBackOfficeAccounts()
        {
            await SeedAsync();

            var result = await CreateService(Admin()).GetPagedAccountsAsync("", "created", false, 1, 50);

            Assert.NotEmpty(result.Items);
            Assert.All(result.Items, u => Assert.True(u.Role == Models.Roles.Admin || u.Role == Models.Roles.DeptAdmin));

            using var db = _factory.CreateDbContext();
            var backOfficeCount = await db.AppUsers.CountAsync(u => u.Role != Models.Roles.User);
            Assert.Equal(backOfficeCount, result.TotalCount);
        }

        [Fact]
        public async Task GetPagedAccountsAsync_DeptAdminSeesOnlyOwnDepartment()
        {
            await SeedAsync();

            var deptAdminA = new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 2,
                UserName = "anfang",
                Role = Models.Roles.DeptAdmin,
                DepartmentId = TestData.DeptA
            };

            var result = await CreateService(deptAdminA).GetPagedAccountsAsync("", "created", false, 1, 50);

            Assert.All(result.Items, u => Assert.Equal(TestData.DeptA, u.DepartmentId));
        }

        [Fact]
        public async Task CreateBackOfficeAccountAsync_NewDepartment_CanLoginAndIsAudited()
        {
            await SeedAsync();

            var (ok, err) = await CreateService(Admin())
                .CreateBackOfficeAccountAsync("zonghe", "init5678", Models.Roles.DeptAdmin, NewDeptId, "综合室管理员");

            Assert.True(ok, err);

            var saved = GetByName("zonghe");
            Assert.Equal(Models.Roles.DeptAdmin, saved.Role);
            Assert.Equal(NewDeptId, saved.DepartmentId);
            Assert.True(saved.IsActive);
            Assert.NotEqual("init5678", saved.PasswordHash);      // 明文绝不入库

            var auth = new AuthService(_factory, _hasher);
            Assert.NotNull(await auth.VerifyAsync("zonghe", "init5678"));

            using var db = _factory.CreateDbContext();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "User.Create" && a.TargetId == saved.Id));
        }

        [Fact]
        public async Task CreateBackOfficeAccountAsync_DepartmentAlreadyHasAdmin_IsRejected()
        {
            await SeedAsync();   // 信息安防技术室 已被种子开了 anfang

            var (ok, err) = await CreateService(Admin())
                .CreateBackOfficeAccountAsync("anfang2", "pass1234", Models.Roles.DeptAdmin, TestData.DeptA, null);

            Assert.False(ok);
            Assert.Contains("已有管理员", err);
        }

        [Fact]
        public async Task CreateBackOfficeAccountAsync_ByDeptAdmin_IsRejected()
        {
            await SeedAsync();

            var deptAdminA = new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 2,
                UserName = "anfang",
                Role = Models.Roles.DeptAdmin,
                DepartmentId = TestData.DeptA
            };

            var (ok, err) = await CreateService(deptAdminA)
                .CreateBackOfficeAccountAsync("padmin", "pass1234", Models.Roles.Admin, null, null);

            Assert.False(ok);
            Assert.Contains("仅全局管理员", err);
        }

        [Fact]
        public async Task CreateBackOfficeAccountAsync_UserRoleIsRejected()
        {
            await SeedAsync();

            var (ok, err) = await CreateService(Admin())
                .CreateBackOfficeAccountAsync("someone", "pass1234", Models.Roles.User, NewDeptId, null);

            Assert.False(ok);
            Assert.Contains("只能创建全局管理员或部门管理员", err);
        }

        [Fact]
        public async Task CreateBackOfficeAccountAsync_AdminHasNoDepartmentAndDuplicateNameRejected()
        {
            await SeedAsync();
            var service = CreateService(Admin());

            var (ok, err) = await service.CreateBackOfficeAccountAsync("admin2", "pass1234", Models.Roles.Admin, NewDeptId, "第二管理员");
            Assert.True(ok, err);
            Assert.Null(GetByName("admin2").DepartmentId);        // 全量视野，部门必须为 null

            var (duplicate, duplicateErr) = await service
                .CreateBackOfficeAccountAsync("admin2", "pass1234", Models.Roles.Admin, null, null);
            Assert.False(duplicate);
            Assert.Contains("已被占用", duplicateErr);
        }

        [Fact]
        public async Task UpdateBackOfficeAccountAsync_MovesDepartmentAndAudits()
        {
            await SeedAsync();
            var anfang = GetByName("anfang");

            var (ok, err) = await CreateService(Admin())
                .UpdateBackOfficeAccountAsync(anfang.Id, "安防室管理员", NewDeptId);

            Assert.True(ok, err);

            var saved = GetByName("anfang");
            Assert.Equal(NewDeptId, saved.DepartmentId);
            Assert.Equal("安防室管理员", saved.DisplayName);

            using var db = _factory.CreateDbContext();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "User.Update" && a.TargetId == anfang.Id));
        }

        [Fact]
        public async Task UpdateBackOfficeAccountAsync_TargetDepartmentOccupied_IsRejected()
        {
            await SeedAsync();   // DeptB 已有 shebei
            var anfang = GetByName("anfang");

            var (ok, err) = await CreateService(Admin())
                .UpdateBackOfficeAccountAsync(anfang.Id, null, TestData.DeptB);

            Assert.False(ok);
            Assert.Contains("已有管理员", err);
            Assert.Equal(TestData.DeptA, GetByName("anfang").DepartmentId);
        }

        [Fact]
        public async Task ResetPasswordAsync_BackOfficeAccount_NewPasswordWorksOldFails()
        {
            await SeedAsync();
            var anfang = GetByName("anfang");
            var auth = new AuthService(_factory, _hasher);

            var (ok, err) = await CreateService(Admin()).ResetPasswordAsync(anfang.Id, "newpass9");
            Assert.True(ok, err);

            Assert.NotNull(await auth.VerifyAsync("anfang", "newpass9"));   // 新口令可用
            Assert.Null(await auth.VerifyAsync("anfang", "123456"));        // 出厂口令失效
        }

        [Fact]
        public async Task SetActiveAsync_DeptAdmin_DisablesLoginAndAudits()
        {
            await SeedAsync();
            var anfang = GetByName("anfang");

            var (ok, err) = await CreateService(Admin()).SetActiveAsync(anfang.Id, false);
            Assert.True(ok, err);

            var auth = new AuthService(_factory, _hasher);
            Assert.Null(await auth.VerifyAsync("anfang", "123456"));

            using var db = _factory.CreateDbContext();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "User.SetActive" && a.TargetId == anfang.Id));
        }

        [Fact]
        public async Task SetActiveAsync_SelfAccount_IsProtected()
        {
            await SeedAsync();
            var admin = GetByName("admin");

            var (ok, err) = await CreateService(Admin(admin.Id, admin.UserName)).SetActiveAsync(admin.Id, false);

            Assert.False(ok);
            Assert.Contains("当前登录", err);
            Assert.True(GetByName("admin").IsActive);
        }

        [Fact]
        public async Task SetActiveAsync_LastActiveAdmin_IsProtected()
        {
            await SeedAsync();   // 种子里只有一个启用中的 Admin
            var admin = GetByName("admin");

            var (ok, err) = await CreateService(Admin(999, "ghost")).SetActiveAsync(admin.Id, false);

            Assert.False(ok);
            Assert.Contains("至少一个", err);
            Assert.True(GetByName("admin").IsActive);
        }

        [Fact]
        public async Task SetActiveAsync_SecondAdmin_CanBeDisabledAndReEnabled()
        {
            await SeedAsync();
            var service = CreateService(Admin());

            var (created, createErr) = await service
                .CreateBackOfficeAccountAsync("admin2", "pass1234", Models.Roles.Admin, null, "第二管理员");
            Assert.True(created, createErr);
            var admin2 = GetByName("admin2");

            var (disabled, disableErr) = await service.SetActiveAsync(admin2.Id, false);
            Assert.True(disabled, disableErr);
            Assert.False(GetByName("admin2").IsActive);

            var (enabled, enableErr) = await service.SetActiveAsync(admin2.Id, true);
            Assert.True(enabled, enableErr);
            Assert.True(GetByName("admin2").IsActive);
        }

        public void Dispose() => _factory.Dispose();
    }
}

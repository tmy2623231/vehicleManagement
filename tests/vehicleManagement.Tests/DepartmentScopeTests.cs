using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Personnel;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>部门隔离回归：同一个 GetPagedAsync 换身份看总数（A 部门 3 人 / B 部门 2 人）。</summary>
    public class DepartmentScopeTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();

        [Theory]
        [InlineData(null, 5)]                 // Admin：不限部门
        [InlineData(TestData.DeptA, 3)]       // 部门管理员：只有 6 号部门
        [InlineData(TestData.DeptB, 2)]       // 部门管理员：只有 7 号部门
        public async Task GetPagedAsync_AppliesDepartmentScope(int? scopeDepartmentId, int expected)
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var identity = scopeDepartmentId is null
                ? new UserIdentity
                {
                    IsAuthenticated = true,
                    UserId = 1,
                    UserName = "admin",
                    Role = Models.Roles.Admin
                }
                : new UserIdentity
                {
                    IsAuthenticated = true,
                    UserId = 2,
                    UserName = "deptadmin",
                    Role = Models.Roles.DeptAdmin,
                    DepartmentId = scopeDepartmentId
                };

            var user = new StubCurrentUser(identity);
            var audit = new AuditService(_factory, user, NullLogger<AuditService>.Instance);
            var service = new PersonnelService(
                new PersonnelRepository(_factory),
                new DepartmentRepository(_factory),
                _factory,
                user,
                audit);

            var page = await service.GetPagedAsync(null, "name", true, 1, 10);

            Assert.Equal(expected, page.TotalCount);
            Assert.Equal(expected, page.Items.Count);
            if (scopeDepartmentId is int scoped)
                Assert.All(page.Items, p => Assert.Equal(scoped, p.DepartmentId));
        }

        [Fact]
        public async Task GetPagedAsync_ClampsPageSizeAndPage()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var user = StubCurrentUser.Admin();
            var audit = new AuditService(_factory, user, NullLogger<AuditService>.Instance);
            var service = new PersonnelService(
                new PersonnelRepository(_factory),
                new DepartmentRepository(_factory),
                _factory,
                user,
                audit);

            // pageSize 越界 → 收到上限 100；页码越界 → 回退到最后一页
            var page = await service.GetPagedAsync(null, null, true, 99, 100000);

            Assert.Equal(100, page.PageSize);
            Assert.Equal(1, page.CurrentPage);
            Assert.Equal(5, page.TotalCount);
        }

        public void Dispose() => _factory.Dispose();
    }
}

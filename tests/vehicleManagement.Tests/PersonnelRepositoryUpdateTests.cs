using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>回归：UpdateAsync 用 CurrentValues.SetValues，只覆盖标量列与 FK，不动 GetByIdAsync 里 Include 进来的导航属性。</summary>
    public class PersonnelRepositoryUpdateTests : System.IDisposable
    {
        private readonly TestDbFactory _factory = new();

        [Fact]
        public async Task UpdateAsync_DoesNotOverwriteIncludedDepartment()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var repo = new PersonnelRepository(_factory);

            // 1) 读-改-写路线上的第一段：拿到带 Department 的实体
            var personnel = await repo.GetByIdAsync(10);
            Assert.NotNull(personnel);
            Assert.Equal("信息安防技术室", personnel.Department.Name);

            // 2) 期间别人把部门改名（模拟并发期间的真实变化）
            using (var db = _factory.CreateDbContext())
            {
                var dept = await db.Departments.FindAsync(TestData.DeptA);
                dept.Name = "改名后的部门";
                await db.SaveChangesAsync();
            }

            // 3) 只改人员姓名后保存
            personnel.Name = "马存宝（已改名）";
            await repo.UpdateAsync(personnel);

            using (var db = _factory.CreateDbContext())
            {
                var saved = await db.Personnel.AsNoTracking().FirstAsync(p => p.Id == 10);
                Assert.Equal("马存宝（已改名）", saved.Name);
                Assert.Equal(TestData.DeptA, saved.DepartmentId);   // 部门归属未被导航快照改动

                var dept = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == TestData.DeptA);
                Assert.Equal("改名后的部门", dept.Name);             // 部门表未被反向覆盖
            }
        }

        [Fact]
        public async Task UpdateAsync_MissingRow_ReturnsPatchWithoutThrowing()
        {
            var repo = new PersonnelRepository(_factory);
            var ghost = new Models.Personnel
            {
                Id = 999,
                DepartmentId = TestData.DeptA,
                Name = "不存在的人"
            };

            var returned = await repo.UpdateAsync(ghost);

            Assert.NotNull(returned);   // 保持「不抛异常」的既有返回契约
        }

        public void Dispose() => _factory.Dispose();
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    /// <summary>
    /// 部门仓储实现
    /// </summary>
    public class DepartmentRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public DepartmentRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<Department>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Department>)db.Departments).AsNoTracking().ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
        public async Task<Department> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Departments.FindAsync(id);
        }

        public async Task<Department> AddAsync(Department department)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            return department;
        }

        public async Task<Department> UpdateAsync(Department patch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.Departments.FindAsync(patch.Id);
            if (existing is null) return patch;      // 保持现有「不抛异常」返回契约
            patch.UpdatedAt = ChinaTime.Now;
            db.Entry(existing).CurrentValues.SetValues(patch);   // 只覆盖标量列与 FK，导航属性一律不动
            await db.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var department = await db.Departments.FindAsync(id);
            if (department == null) return false;

            db.Departments.Remove(department);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Department>)db.Departments).AnyAsync(d => d.Id == id);
        }
    }
}

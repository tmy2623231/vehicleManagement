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
            return await ((IQueryable<Department>)db.Departments).ToListAsync();
        }

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

        public async Task<Department> UpdateAsync(Department department)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            department.UpdatedAt = ChinaTime.Now;
            db.Departments.Update(department);
            await db.SaveChangesAsync();
            return department;
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

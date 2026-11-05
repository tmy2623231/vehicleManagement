using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    /// <summary>
    /// 人员仓储实现
    /// </summary>
    public class PersonnelRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public PersonnelRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<Personnel>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Personnel.Include(p => p.Department).ToListAsync();
        }

        public async Task<Personnel> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Personnel.Include(p => p.Department).FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Personnel> AddAsync(Personnel personnel)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Personnel.Add(personnel);
            await db.SaveChangesAsync();
            return personnel;
        }

        public async Task<Personnel> UpdateAsync(Personnel personnel)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            personnel.UpdatedAt = ChinaTime.Now;
            db.Personnel.Update(personnel);
            await db.SaveChangesAsync();
            return personnel;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var personnel = await db.Personnel.FindAsync(id);
            if (personnel == null) return false;

            db.Personnel.Remove(personnel);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Personnel>)db.Personnel).AnyAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<Personnel>> GetByDepartmentIdAsync(int departmentId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Personnel>)db.Personnel)
                .Where(p => p.DepartmentId == departmentId)
                .ToListAsync();
        }
    }
}

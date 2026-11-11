using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    /// <summary>
    /// 地点仓储实现
    /// </summary>
    public class LocationRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public LocationRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<Location>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Location>)db.Locations).AsNoTracking().ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
        public async Task<Location> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Locations.FindAsync(id);
        }

        public async Task<Location> AddAsync(Location location)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            return location;
        }

        public async Task<Location> UpdateAsync(Location patch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.Locations.FindAsync(patch.Id);
            if (existing is null) return patch;      // 保持现有「不抛异常」返回契约
            db.Entry(existing).CurrentValues.SetValues(patch);   // 只覆盖标量列与 FK，导航属性一律不动
            await db.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var location = await db.Locations.FindAsync(id);
            if (location == null) return false;

            db.Locations.Remove(location);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Location>)db.Locations).AnyAsync(l => l.Id == id);
        }

        public async Task<IEnumerable<Location>> GetByCategoryAsync(LocationCategory category, bool activeOnly = false)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = ((IQueryable<Location>)db.Locations).Where(l => l.Category == category);
            if (activeOnly) query = query.Where(l => l.IsActive);
            return await query.AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync();
        }
    }
}

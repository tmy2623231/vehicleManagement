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
            return await ((IQueryable<Location>)db.Locations).ToListAsync();
        }

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

        public async Task<Location> UpdateAsync(Location location)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Locations.Update(location);
            await db.SaveChangesAsync();
            return location;
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

        public async Task<IEnumerable<Location>> GetByCategoryAsync(LocationCategory category)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Location>)db.Locations)
                .Where(l => l.Category == category)
                .OrderBy(l => l.SortOrder)
                .ToListAsync();
        }
    }
}

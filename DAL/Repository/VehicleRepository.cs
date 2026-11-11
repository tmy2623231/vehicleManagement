using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    public class VehicleRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public VehicleRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<Vehicle>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Vehicle>)db.Vehicles).AsNoTracking().ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
        public async Task<Vehicle> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Vehicles.FindAsync(id);
        }

        public async Task<Vehicle> AddAsync(Vehicle vehicle)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            return vehicle;
        }

        public async Task<Vehicle> UpdateAsync(Vehicle patch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.Vehicles.FindAsync(patch.Id);
            if (existing is null) return patch;      // 保持现有「不抛异常」返回契约
            patch.UpdatedAt = ChinaTime.Now;
            db.Entry(existing).CurrentValues.SetValues(patch);   // 只覆盖标量列与 FK，导航属性一律不动
            await db.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var vehicle = await db.Vehicles.FindAsync(id);
            if (vehicle == null) return false;

            db.Vehicles.Remove(vehicle);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Vehicle>)db.Vehicles).AnyAsync(e => e.Id == id);
        }
    }
}

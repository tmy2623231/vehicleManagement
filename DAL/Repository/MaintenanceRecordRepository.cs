using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    public class MaintenanceRecordRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public MaintenanceRecordRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<VehicleMaintenanceRecord>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle).ThenInclude(v => v.Department)
                .AsNoTracking()
                .OrderByDescending(r => r.MaintenanceDate)
                .ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
        public async Task<VehicleMaintenanceRecord> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.VehicleMaintenanceRecords.FindAsync(id);
        }

        public async Task<VehicleMaintenanceRecord> AddAsync(VehicleMaintenanceRecord record)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.VehicleMaintenanceRecords.Add(record);
            await db.SaveChangesAsync();
            return record;
        }

        public async Task<VehicleMaintenanceRecord> UpdateAsync(VehicleMaintenanceRecord patch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.VehicleMaintenanceRecords.FindAsync(patch.Id);
            if (existing is null) return patch;      // 保持现有「不抛异常」返回契约
            patch.UpdatedAt = ChinaTime.Now;
            db.Entry(existing).CurrentValues.SetValues(patch);   // 只覆盖标量列与 FK，导航属性一律不动
            await db.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleMaintenanceRecords.FindAsync(id);
            if (record == null) return false;
            db.VehicleMaintenanceRecords.Remove(record);
            await db.SaveChangesAsync();
            return true;
        }
    }
}

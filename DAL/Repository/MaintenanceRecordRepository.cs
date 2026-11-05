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
                .OrderByDescending(r => r.MaintenanceDate)
                .ToListAsync();
        }

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

        public async Task<VehicleMaintenanceRecord> UpdateAsync(VehicleMaintenanceRecord record)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            record.UpdatedAt = ChinaTime.Now;
            db.VehicleMaintenanceRecords.Update(record);
            await db.SaveChangesAsync();
            return record;
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

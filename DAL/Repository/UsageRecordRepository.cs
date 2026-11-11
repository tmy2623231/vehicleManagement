using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL.Repository
{
    /// <summary>
    /// 使用记录仓储实现
    /// </summary>
    public class UsageRecordRepository
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public UsageRecordRepository(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<VehicleUsageRecord>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.VehicleUsageRecords
                .Include(r => r.Vehicle)
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Include(r => r.FinalLocation)
                .Include(r => r.UsageRecordPurposes)
                    .ThenInclude(p => p.Purpose)
                .AsNoTracking()
                .ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
        public async Task<VehicleUsageRecord> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.VehicleUsageRecords
                .Include(r => r.Vehicle)
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Include(r => r.FinalLocation)
                .Include(r => r.UsageRecordPurposes)
                    .ThenInclude(p => p.Purpose)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<VehicleUsageRecord> AddAsync(VehicleUsageRecord record)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.VehicleUsageRecords.Add(record);
            await db.SaveChangesAsync();
            return record;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleUsageRecords.FindAsync(id);
            if (record == null) return false;

            db.VehicleUsageRecords.Remove(record);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<VehicleUsageRecord>)db.VehicleUsageRecords).AnyAsync(r => r.Id == id);
        }

        public async Task<VehicleUsageRecord> GetLastRecordByVehicleIdAsync(int vehicleId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<VehicleUsageRecord>)db.VehicleUsageRecords)
                .Where(r => r.VehicleId == vehicleId)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
    }
}

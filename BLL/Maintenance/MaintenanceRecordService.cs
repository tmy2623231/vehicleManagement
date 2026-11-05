using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Repository;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Maintenance
{
    public class MaintenanceRecordService
    {
        private readonly MaintenanceRecordRepository _repo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly FileUploadService _fileService;
        private readonly ILogger<MaintenanceRecordService> _logger;

        public MaintenanceRecordService(MaintenanceRecordRepository repo, IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService, ILogger<MaintenanceRecordService> logger)
        {
            _repo = repo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _logger = logger;
        }

        public async Task<PagedResult<Models.VehicleMaintenanceRecord>> GetPagedAsync(
            string searchKeyword, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle).ThenInclude(v => v.Department)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(r =>
                    r.Vehicle.LicensePlate.Contains(kw) ||
                    (r.Description != null && r.Description.Contains(kw)));
            }

            query = query.OrderByDescending(r => r.MaintenanceDate);

            var totalCount = await query.CountAsync();
            if (page < 1) page = 1;
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Models.VehicleMaintenanceRecord>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
        }

        public async Task<Models.VehicleMaintenanceRecord> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);

        public async Task<Models.VehicleMaintenanceRecord> CreateAsync(Models.VehicleMaintenanceRecord record)
        {
            return await _repo.AddAsync(record);
        }

        public async Task<Models.VehicleMaintenanceRecord> UpdateAsync(Models.VehicleMaintenanceRecord record)
        {
            return await _repo.UpdateAsync(record);
        }

        public async Task<(bool Success, string Error)> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleMaintenanceRecords.FindAsync(id);
            if (record == null) return (false, "删除失败：记录不存在。");

            // 先清理附件物理文件，失败则阻止 DB 删除
            try
            {
                if (!string.IsNullOrEmpty(record.ImagePaths))
                {
                    var paths = record.ImagePaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var path in paths)
                        _fileService.DeleteFile(path);
                    _logger.LogInformation("删除维修记录 {Id} 时清理了 {Count} 个附件文件", id, paths.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除维修记录 {Id} 的附件文件失败，已阻止数据库删除", id);
                return (false, "删除失败：附件文件被占用或无法访问，请稍后重试。");
            }

            db.VehicleMaintenanceRecords.Remove(record);
            await db.SaveChangesAsync();
            _logger.LogInformation("维修记录 {Id} 已删除", id);
            return (true, null);
        }
    }
}

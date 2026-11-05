using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;
using vehicleManagement.DAL.Repository;

namespace vehicleManagement.BLL.UsageRecord
{
    /// <summary>
    /// 使用记录服务实现
    /// </summary>
    public class UsageRecordService
    {
        private readonly UsageRecordRepository _recordRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly FileUploadService _fileService;
        private readonly ILogger<UsageRecordService> _logger;

        /// <summary>
        /// 支持的图片扩展名
        /// </summary>
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        public UsageRecordService(UsageRecordRepository recordRepo, IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService, ILogger<UsageRecordService> logger)
        {
            _recordRepo = recordRepo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _logger = logger;
        }

        /// <summary>
        /// 分页查询使用记录列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<VehicleUsageRecord>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleUsageRecords
                .Include(r => r.Vehicle)
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Include(r => r.FinalLocation)
                .Include(r => r.UsageRecordPurposes)
                    .ThenInclude(p => p.Purpose)
                .AsQueryable();

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(r =>
                    (r.Vehicle != null && r.Vehicle.LicensePlate.Contains(kw)) ||
                    (r.Personnel != null && r.Personnel.Name.Contains(kw)) ||
                    (r.Department != null && r.Department.Name.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "vehicle" => sortAscending
                    ? query.OrderBy(r => r.Vehicle != null ? r.Vehicle.LicensePlate : "")
                    : query.OrderByDescending(r => r.Vehicle != null ? r.Vehicle.LicensePlate : ""),
                "person" => sortAscending
                    ? query.OrderBy(r => r.Personnel != null ? r.Personnel.Name : "")
                    : query.OrderByDescending(r => r.Personnel != null ? r.Personnel.Name : ""),
                _ => sortAscending ? query.OrderBy(r => r.CreatedAt) : query.OrderByDescending(r => r.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            if (page > 1 && page > (int)Math.Ceiling((double)totalCount / pageSize))
                page = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<VehicleUsageRecord>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// 查询最近 N 天内的使用记录
        /// </summary>
        public async Task<PagedResult<VehicleUsageRecord>> GetRecentAsync(int days, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var since = ChinaTime.Now.Date.AddDays(-days);

            var query = db.VehicleUsageRecords
                .Include(r => r.Vehicle)
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Where(r => r.CreatedAt >= since)
                .OrderByDescending(r => r.CreatedAt)
                .AsQueryable();

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<VehicleUsageRecord>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// 删除使用记录（含附件文件清理）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleUsageRecords.FindAsync(id);
            if (record == null) return (false, "删除失败：记录不存在。");

            // 先清理所有附件物理文件，失败则阻止 DB 删除
            try
            {
                var files = new List<string>();
                if (!string.IsNullOrEmpty(record.ImagePaths))
                    files.AddRange(record.ImagePaths.Split(';', StringSplitOptions.RemoveEmptyEntries));
                if (!string.IsNullOrEmpty(record.VideoPaths))
                    files.AddRange(record.VideoPaths.Split(';', StringSplitOptions.RemoveEmptyEntries));
                foreach (var path in files)
                    _fileService.DeleteFile(path);
                if (files.Count > 0)
                    _logger.LogInformation("删除使用记录 {Id} 时清理了 {Count} 个附件文件", id, files.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除使用记录 {Id} 的附件文件失败，已阻止数据库删除", id);
                return (false, "删除失败：附件文件被占用或无法访问，请稍后重试。");
            }

            db.VehicleUsageRecords.Remove(record);
            await db.SaveChangesAsync();
            _logger.LogInformation("使用记录 {Id} 已删除", id);
            return (true, null);
        }

        /// <summary>
        /// 获取记录的所有附件路径列表（图片+视频合并）
        /// </summary>
        public List<string> GetAllFiles(VehicleUsageRecord record)
        {
            var files = new List<string>();
            if (!string.IsNullOrEmpty(record.ImagePaths))
                files.AddRange(record.ImagePaths.Split(';', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrEmpty(record.VideoPaths))
                files.AddRange(record.VideoPaths.Split(';', StringSplitOptions.RemoveEmptyEntries));
            return files;
        }

        /// <summary>
        /// 判断文件路径是否为图片
        /// </summary>
        public bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ImageExtensions.Contains(ext);
        }
    }
}

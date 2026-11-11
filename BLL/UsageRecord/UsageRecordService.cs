using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Auth;
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
        private readonly ICurrentUser _currentUser;
        private readonly AuditService _auditService;
        private readonly ILogger<UsageRecordService> _logger;

        /// <summary>
        /// 支持的图片扩展名（与 MaintenanceRecordService 同一口径，两处附件查看器行为一致）
        /// </summary>
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        /// <summary>
        /// 支持的视频扩展名（非图片附件的内嵌预览）
        /// </summary>
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".wmv" };

        public UsageRecordService(UsageRecordRepository recordRepo, IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService, ICurrentUser currentUser, AuditService auditService,
            ILogger<UsageRecordService> logger)
        {
            _recordRepo = recordRepo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _currentUser = currentUser;
            _auditService = auditService;
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

            // 部门隔离：Admin 为 null（不限），其余只能看本部门
            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                query = query.Where(r => r.DepartmentId == scopedDeptId);

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
                    ? query.OrderBy(r => r.Vehicle != null ? r.Vehicle.LicensePlate : "").ThenBy(r => r.Id)
                    : query.OrderByDescending(r => r.Vehicle != null ? r.Vehicle.LicensePlate : "").ThenByDescending(r => r.Id),
                "person" => sortAscending
                    ? query.OrderBy(r => r.Personnel != null ? r.Personnel.Name : "").ThenBy(r => r.Id)
                    : query.OrderByDescending(r => r.Personnel != null ? r.Personnel.Name : "").ThenByDescending(r => r.Id),
                // 类型列取记录维度的 IsBorrow，不读车辆当前借用状态
                "type" => sortAscending
                    ? query.OrderBy(r => r.IsBorrow).ThenBy(r => r.Id)
                    : query.OrderByDescending(r => r.IsBorrow).ThenByDescending(r => r.Id),
                _ => sortAscending
                    ? query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                    : query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            };

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
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
                .ThenByDescending(r => r.Id)
                .AsQueryable();

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 删除使用记录（含附件文件清理）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            // 部门管理员对使用记录只有只读权
            var me = await _currentUser.GetAsync();
            if (me.IsDeptAdmin)
                return (false, "部门管理员无删除权限。");

            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleUsageRecords.FindAsync(id);
            if (record == null) return (false, "删除失败：记录不存在。");
            if (!me.CanAccessDepartment(record.DepartmentId))
                return (false, "无权操作该部门数据。");

            // 先落库再删文件：记录指向已删文件无法恢复，孤儿文件只是浪费磁盘
            var files = GetAllFiles(record);
            var departmentId = record.DepartmentId;
            var vehicleId = record.VehicleId;

            db.VehicleUsageRecords.Remove(record);
            await db.SaveChangesAsync();

            foreach (var path in files)
            {
                try { _fileService.DeleteFile(path); }
                catch (Exception ex) { _logger.LogWarning(ex, "附件清理失败（孤儿文件）：{Path}", path); }
            }

            _logger.LogInformation("使用记录 {Id} 已删除", id);
            await _auditService.WriteAsync("UsageRecord.Delete", "VehicleUsageRecord", id,
                $"车辆Id={vehicleId}，部门Id={departmentId}，附件数={files.Count}");
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

        /// <summary>
        /// 判断文件路径是否为视频（两者都不是时页面走「在新标签页打开」兜底）
        /// </summary>
        public bool IsVideoFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return VideoExtensions.Contains(ext);
        }
    }
}

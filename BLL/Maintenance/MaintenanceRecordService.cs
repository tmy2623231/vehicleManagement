using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Repository;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Maintenance
{
    public class MaintenanceRecordService
    {
        /// <summary>支持的图片扩展名（与 UsageRecordService 同一口径，附件查看器共用）</summary>
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        /// <summary>支持的视频扩展名（非图片附件的兜底预览）</summary>
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".wmv" };

        private readonly MaintenanceRecordRepository _repo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly FileUploadService _fileService;
        private readonly ICurrentUser _currentUser;
        private readonly AuditService _auditService;
        private readonly ILogger<MaintenanceRecordService> _logger;

        public MaintenanceRecordService(MaintenanceRecordRepository repo, IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService, ICurrentUser currentUser, AuditService auditService,
            ILogger<MaintenanceRecordService> logger)
        {
            _repo = repo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _currentUser = currentUser;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>
        /// 可见范围收窄的唯一判据：维修记录归属「被维修车辆所属部门」。
        /// Admin（ScopeDepartmentId 为 null）不限；部门账号只看本部门车辆，越权记录连计数都不出现。
        /// 必须放在 CountAsync / Skip-Take 之前，TotalCount 才会随过滤同步收窄。
        /// </summary>
        private static IQueryable<Models.VehicleMaintenanceRecord> ApplyDepartmentScope(
            IQueryable<Models.VehicleMaintenanceRecord> query, UserIdentity me)
            => me.ScopeDepartmentId is int scopedDeptId
                ? query.Where(r => r.Vehicle != null && r.Vehicle.DepartmentId == scopedDeptId)
                : query;

        public async Task<PagedResult<Models.VehicleMaintenanceRecord>> GetPagedAsync(
            string searchKeyword, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle).ThenInclude(v => v.Department)
                .Include(r => r.Maintainer)
                .Include(r => r.MaintainerDepartment)
                .AsNoTracking();

            // 部门隔离先行：后续关键字过滤与分页都作用在同一 query 上
            var me = await _currentUser.GetAsync();
            query = ApplyDepartmentScope(query, me);

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(r =>
                    r.Vehicle.LicensePlate.Contains(kw) ||
                    (r.Description != null && r.Description.Contains(kw)) ||
                    (r.Maintainer != null && r.Maintainer.Name.Contains(kw)));
            }

            // 唯一列兜底排序：同一天多条记录时 Id 决定顺序，分页不会跳行
            query = query.OrderByDescending(r => r.MaintenanceDate).ThenByDescending(r => r.Id);

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 按 Id 取记录（含部门隔离）：不可见与不存在一律返回 null，调用方不得据此回显任何字段。
        /// </summary>
        public async Task<Models.VehicleMaintenanceRecord?> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var me = await _currentUser.GetAsync();
            var query = db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle)
                .AsNoTracking()
                .Where(r => r.Id == id);
            query = ApplyDepartmentScope(query, me);
            return await query.FirstOrDefaultAsync();
        }

        /// <summary>
        /// 详情弹框用的只读加载：带齐车辆（含所属部门）与维修人/维修部门快照，
        /// 「类型」才有 Vehicle.DepartmentId 可比。只读查询必须 AsNoTracking()。
        /// 越权访问（记录属于其他部门车辆）与记录不存在返回同一个 null —— 页面文案「该记录已不存在」，
        /// 既不回显字段，也不泄露记录是否存在。
        /// </summary>
        public async Task<Models.VehicleMaintenanceRecord?> GetDetailAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var me = await _currentUser.GetAsync();
            var query = db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle).ThenInclude(v => v.Department)
                .Include(r => r.Maintainer)
                .Include(r => r.MaintainerDepartment)
                .AsNoTracking()
                .Where(r => r.Id == id);
            query = ApplyDepartmentScope(query, me);
            return await query.FirstOrDefaultAsync();
        }

        /// <summary>拆分记录附件（ImagePaths 分号分隔），空串/空项一律丢弃。</summary>
        public List<string> GetAttachmentFiles(Models.VehicleMaintenanceRecord record)
            => string.IsNullOrEmpty(record.ImagePaths)
                ? new List<string>()
                : record.ImagePaths
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();

        /// <summary>判断附件是否为图片（附件查看器选择 img / video / 链接兜底）。</summary>
        public bool IsImageFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext is not null && Array.IndexOf(ImageExtensions, ext) >= 0;
        }

        /// <summary>判断附件是否为视频。</summary>
        public bool IsVideoFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext is not null && Array.IndexOf(VideoExtensions, ext) >= 0;
        }

        /// <summary>
        /// 登记维修：维修人 / 维修部门只认服务端登录身份，前端传值一律覆盖（Admin 不绑部门则为空，展示回退「未知」）。
        /// 此处**不**限制车辆归属 —— 「跨部门维修」是合法业务（记录维度的「类型」就是为它设计的）；
        /// 记录的可见范围由被维修车辆所属部门决定，见 <see cref="ApplyDepartmentScope"/>。
        /// </summary>
        public async Task<Models.VehicleMaintenanceRecord> CreateAsync(Models.VehicleMaintenanceRecord record)
        {
            // 维修人 / 维修部门只认服务端登录身份，前端传值一律覆盖（Admin 不绑部门则为空，展示回退「未知」）
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                throw new InvalidOperationException("登录状态已失效，请重新登录");

            record.MaintainerPersonnelId = me.PersonnelId;
            record.MaintainerDepartmentId = me.DepartmentId;

            var created = await _repo.AddAsync(record);
            await _auditService.WriteAsync("MaintenanceRecord.Create", "VehicleMaintenanceRecord", created.Id,
                $"车辆Id={created.VehicleId}，维修日期={created.MaintenanceDate:yyyy-MM-dd}，"
                + $"维修人Id={created.MaintainerPersonnelId?.ToString() ?? "未知"}，"
                + $"维修部门Id={created.MaintainerDepartmentId?.ToString() ?? "未知"}");
            return created;
        }

        /// <summary>
        /// 保存编辑：只改业务字段（维修日期 / 维修项目 / 费用 / 备注 / 附件）。
        /// 车辆归属、维修人身份快照、登记时间一律以库内为准 —— 否则任何一处页面（或前端传值）
        /// 都能借「编辑」把记录挪到另一辆车，从而篡改跨部门判定与「类型」文案。
        /// 写前先断归属：越权返回「无权」，不返回库内任何字段值。
        /// </summary>
        public async Task<(bool Success, string Error)> UpdateAsync(Models.VehicleMaintenanceRecord record)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录");

            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == record.Id);
            if (existing is null)
                return (false, "保存失败：记录不存在。");

            // 归属以库内车辆部门为准（不用前端传值）；车辆缺失（理论上被 Restrict 拦住）按默认值拒绝非 Admin
            if (!me.CanAccessDepartment(existing.Vehicle?.DepartmentId ?? 0))
                return (false, "访问被拒绝：该记录不属于你所属的部门。");

            var before = Snapshot(existing);
            record.VehicleId = existing.VehicleId;
            record.MaintainerPersonnelId = existing.MaintainerPersonnelId;
            record.MaintainerDepartmentId = existing.MaintainerDepartmentId;
            record.CreatedAt = existing.CreatedAt;

            var updated = await _repo.UpdateAsync(record);
            await _auditService.WriteAsync("MaintenanceRecord.Update", "VehicleMaintenanceRecord", updated.Id,
                DescribeChanges(before, record));
            return (true, null);
        }

        /// <summary>取编辑前的业务字段副本，用于审计里写「改了什么」。</summary>
        private static (DateTime Date, string Description, decimal Cost, string Remarks, string ImagePaths, int VehicleId)
            Snapshot(Models.VehicleMaintenanceRecord r)
            => (r.MaintenanceDate, r.Description, r.Cost, r.Remarks, r.ImagePaths, r.VehicleId);

        private static string DescribeChanges(
            (DateTime Date, string Description, decimal Cost, string Remarks, string ImagePaths, int VehicleId) before,
            Models.VehicleMaintenanceRecord after)
        {
            var changes = new List<string>();
            if (before.Date != after.MaintenanceDate)
                changes.Add($"维修日期 {before.Date:yyyy-MM-dd}→{after.MaintenanceDate:yyyy-MM-dd}");
            if (!string.Equals(before.Description, after.Description, StringComparison.Ordinal))
                changes.Add($"维修项目 {before.Description}→{after.Description}");
            if (before.Cost != after.Cost)
                changes.Add($"费用 {before.Cost:F2}→{after.Cost:F2}");
            if (!string.Equals(before.Remarks, after.Remarks, StringComparison.Ordinal))
                changes.Add($"备注 {before.Remarks ?? "(空)"}→{after.Remarks ?? "(空)"}");
            if (!string.Equals(before.ImagePaths, after.ImagePaths, StringComparison.Ordinal))
                changes.Add($"附件 {CountAttachments(before.ImagePaths)}→{CountAttachments(after.ImagePaths)} 个");

            var summary = changes.Count == 0 ? "无字段变更" : string.Join("，", changes);
            return $"车辆Id={before.VehicleId}，{summary}";
        }

        private static int CountAttachments(string imagePaths)
            => string.IsNullOrEmpty(imagePaths)
                ? 0
                : imagePaths.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>删除记录：先断归属再落库，越权返回「无权」且不动附件。</summary>
        public async Task<(bool Success, string Error)> DeleteAsync(int id)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录");

            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (record == null) return (false, "删除失败：记录不存在。");
            if (!me.CanAccessDepartment(record.Vehicle?.DepartmentId ?? 0))
                return (false, "访问被拒绝：该记录不属于你所属的部门。");

            var vehicleId = record.VehicleId;
            var maintenanceDate = record.MaintenanceDate;

            // 先落库再删文件：记录指向已删文件无法恢复，孤儿文件只是浪费磁盘
            var files = string.IsNullOrEmpty(record.ImagePaths)
                ? Array.Empty<string>()
                : record.ImagePaths.Split(';', StringSplitOptions.RemoveEmptyEntries);

            db.VehicleMaintenanceRecords.Remove(record);
            await db.SaveChangesAsync();

            foreach (var path in files)
            {
                try { _fileService.DeleteFile(path); }
                catch (Exception ex) { _logger.LogWarning(ex, "附件清理失败（孤儿文件）：{Path}", path); }
            }

            _logger.LogInformation("维修记录 {Id} 已删除", id);
            await _auditService.WriteAsync("MaintenanceRecord.Delete", "VehicleMaintenanceRecord", id,
                $"车辆Id={vehicleId}，维修日期={maintenanceDate:yyyy-MM-dd}，附件数={files.Length}");
            return (true, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Vehicle
{
    /// <summary>
    /// 车辆服务实现
    /// </summary>
    public class VehicleService
    {
        private readonly VehicleRepository _vehicleRepo;
        private readonly DepartmentRepository _deptRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly FileUploadService _fileService;
        private readonly ICurrentUser _currentUser;
        private readonly AuditService _auditService;
        private readonly ILogger<VehicleService> _logger;

        public VehicleService(
            VehicleRepository vehicleRepo,
            DepartmentRepository deptRepo,
            IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService,
            ICurrentUser currentUser,
            AuditService auditService,
            ILogger<VehicleService> logger)
        {
            _vehicleRepo = vehicleRepo;
            _deptRepo = deptRepo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _currentUser = currentUser;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>
        /// 根据部门ID获取在职人员列表。超出自身可见范围时返回空列表（不报错，不泄露部门存在性）。
        /// </summary>
        public async Task<List<Models.Personnel>> GetPersonnelByDepartmentAsync(int departmentId)
        {
            var me = await _currentUser.GetAsync();
            if (!me.CanAccessDepartment(departmentId))
                return new List<Models.Personnel>();

            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Models.Personnel>)db.Personnel)
                .Where(p => p.DepartmentId == departmentId && p.IsActive)
                .ToListAsync();
        }

        /// <summary>
        /// 分页查询车辆列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Vehicle>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            IQueryable<Models.Vehicle> query = db.Vehicles.Include(v => v.Department);

            // 部门隔离：Admin 为 null（不限），其余只能看本部门
            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                query = query.Where(v => v.DepartmentId == scopedDeptId);

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(v =>
                    v.LicensePlate.Contains(kw) ||
                    (v.Brand != null && v.Brand.Contains(kw)) ||
                    (v.Model != null && v.Model.Contains(kw)) ||
                    (v.Color != null && v.Color.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "plate" => sortAscending
                    ? query.OrderBy(v => v.LicensePlate).ThenBy(v => v.Id)
                    : query.OrderByDescending(v => v.LicensePlate).ThenByDescending(v => v.Id),
                "brand" => sortAscending
                    ? query.OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Id)
                    : query.OrderByDescending(v => v.Brand).ThenByDescending(v => v.Model).ThenByDescending(v => v.Id),
                "color" => sortAscending
                    ? query.OrderBy(v => v.Color).ThenBy(v => v.Id)
                    : query.OrderByDescending(v => v.Color).ThenByDescending(v => v.Id),
                _ => sortAscending
                    ? query.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id)
                    : query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
            };

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 获取所有部门列表
        /// </summary>
        public async Task<List<Models.Department>> GetAllDepartmentsAsync()
        {
            return (await _deptRepo.GetAllAsync()).ToList();
        }

        /// <summary>
        /// 根据ID获取车辆
        /// </summary>
        public async Task<Models.Vehicle> GetVehicleByIdAsync(int id)
        {
            return await _vehicleRepo.GetByIdAsync(id);
        }

        /// <summary>
        /// 根据二维码Token获取车辆（含部门信息）
        /// </summary>
        public async Task<Models.Vehicle> GetByQrCodeTokenAsync(string token)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Vehicles
                .Include(v => v.Department)
                .FirstOrDefaultAsync(v => v.QrCodeToken == token);
        }

        /// <summary>
        /// 获取所有在用状态的车辆列表（按车牌号排序，含部门信息）
        /// </summary>
        public async Task<List<Models.Vehicle>> GetActiveVehiclesAsync()
        {
            var me = await _currentUser.GetAsync();
            var scope = me.ScopeDepartmentId;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.Vehicles.Include(v => v.Department).AsQueryable();
            if (scope is int scopedDeptId)
                query = query.Where(v => v.DepartmentId == scopedDeptId);

            return await query
                .Where(v => v.Status == Models.VehicleStatus.Active)
                .OrderBy(v => v.LicensePlate)
                .ToListAsync();
        }

        /// <summary>
        /// 创建车辆
        /// </summary>
        public async Task<Models.Vehicle> CreateVehicleAsync(Models.Vehicle vehicle)
        {
            // 部门管理员新建车辆强制落在本部门，忽略前端传入的部门
            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                vehicle.DepartmentId = scopedDeptId;

            var created = await _vehicleRepo.AddAsync(vehicle);
            await _auditService.WriteAsync("Vehicle.Create", "Vehicle", created.Id,
                $"车牌={created.LicensePlate}，部门Id={created.DepartmentId}");
            return created;
        }

        /// <summary>
        /// 更新车辆（写前校验归属）
        /// </summary>
        public async Task<Models.Vehicle> UpdateVehicleAsync(Models.Vehicle vehicle)
        {
            var me = await _currentUser.GetAsync();
            if (!me.CanAccessDepartment(vehicle.DepartmentId))
                throw new UnauthorizedAccessException("无权操作其他部门的车辆");

            vehicle.UpdatedAt = ChinaTime.Now;
            var updated = await _vehicleRepo.UpdateAsync(vehicle);
            await _auditService.WriteAsync("Vehicle.Update", "Vehicle", updated.Id,
                $"车牌={updated.LicensePlate}，部门Id={updated.DepartmentId}");
            return updated;
        }

        /// <summary>
        /// 删除车辆（含附件清理 + 业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteVehicleAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var me = await _currentUser.GetAsync();
            var target = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
            if (target is null)
                return (false, "删除失败：车辆不存在。");
            if (!me.CanAccessDepartment(target.DepartmentId))
                return (false, "无权操作该部门数据。");

            var hasRecords = await db.VehicleUsageRecords.AnyAsync(r => r.VehicleId == id);
            if (hasRecords)
            {
                return (false, "无法删除该车辆：该车辆还有使用记录，请先删除相关使用记录。");
            }

            // 维修记录同样是 DeleteBehavior.Restrict，不先校验就会在 SaveChanges 时抛 DbUpdateException
            var hasMaintenance = await db.VehicleMaintenanceRecords.AnyAsync(r => r.VehicleId == id);
            if (hasMaintenance)
            {
                return (false, "无法删除该车辆：该车辆还有维修记录，请先删除相关维修记录。");
            }

            var vehicle = await db.Vehicles.FindAsync(id);
            if (vehicle == null) return (false, "删除失败：车辆不存在。");

            var licensePlate = vehicle.LicensePlate;
            var departmentId = vehicle.DepartmentId;
            var imagePath = vehicle.ImagePath;

            // 先落库再删文件：记录指向已删文件无法恢复，孤儿文件只是浪费磁盘
            db.Vehicles.Remove(vehicle);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // 兜住校验与提交之间的并发窗口：对方在我们校验后插入了引用记录
                _logger.LogError(ex, "删除车辆 {Id} 失败：仍存在外键引用", id);
                return (false, "删除失败：该车辆仍被其他记录引用，请先清理关联数据。");
            }

            if (!string.IsNullOrEmpty(imagePath))
            {
                try { _fileService.DeleteFile(imagePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "附件清理失败（孤儿文件）：{Path}", imagePath); }
            }

            _logger.LogInformation("车辆 {Id} 已删除", id);
            await _auditService.WriteAsync("Vehicle.Delete", "Vehicle", id,
                $"车牌={licensePlate}，部门Id={departmentId}");
            return (true, null);
        }

        /// <summary>
        /// 校验车辆是否可以下载二维码
        /// </summary>
        public async Task<(bool CanDownload, string ErrorMessage)> ValidateQrCodeDownloadAsync(Models.Vehicle vehicle)
        {
            var me = await _currentUser.GetAsync();
            if (!me.CanAccessDepartment(vehicle.DepartmentId))
            {
                return (false, "无权操作该部门数据。");
            }

            // 校验1：车辆是否已绑定部门
            if (vehicle.DepartmentId == 0 || vehicle.Department == null)
            {
                return (false, "该车辆尚未绑定部门，请先编辑车辆信息绑定部门后再下载二维码");
            }

            // 校验2：该部门下是否有在职人员
            await using var db = await _dbFactory.CreateDbContextAsync();
            var hasPersonnel = await ((IQueryable<Models.Personnel>)db.Personnel)
                .AnyAsync(p => p.DepartmentId == vehicle.DepartmentId && p.IsActive);
            if (!hasPersonnel)
            {
                return (false, "该车辆所属部门下暂无人员，请先在人员管理中添加部门人员后再下载二维码");
            }

            return (true, null);
        }

        /// <summary>
        /// 确保车辆有二维码Token，没有则生成新的
        /// </summary>
        public async Task<string> EnsureQrCodeTokenAsync(Models.Vehicle vehicle, QrCodeService qrCodeService)
        {
            if (string.IsNullOrEmpty(vehicle.QrCodeToken))
            {
                vehicle.QrCodeToken = qrCodeService.GenerateNewToken();
                await _vehicleRepo.UpdateAsync(vehicle);
            }
            return vehicle.QrCodeToken;
        }
    }
}

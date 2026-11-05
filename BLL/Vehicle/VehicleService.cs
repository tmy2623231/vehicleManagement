using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<VehicleService> _logger;

        public VehicleService(
            VehicleRepository vehicleRepo,
            DepartmentRepository deptRepo,
            IDbContextFactory<VehicleDbContext> dbFactory,
            FileUploadService fileService,
            ILogger<VehicleService> logger)
        {
            _vehicleRepo = vehicleRepo;
            _deptRepo = deptRepo;
            _dbFactory = dbFactory;
            _fileService = fileService;
            _logger = logger;
        }

        /// <summary>
        /// 根据部门ID获取在职人员列表
        /// </summary>
        public async Task<List<Models.Personnel>> GetPersonnelByDepartmentAsync(int departmentId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<Models.Personnel>)db.Personnel)
                .Where(p => p.DepartmentId == departmentId && p.IsActive)
                .ToListAsync();
        }

        /// <summary>
        /// 设置车辆借用状态
        /// </summary>
        public async Task ToggleBorrowedAsync(int vehicleId, bool isBorrowed)
        {
            var v = await _vehicleRepo.GetByIdAsync(vehicleId);
            if (v != null)
            {
                v.IsBorrowed = isBorrowed;
                await _vehicleRepo.UpdateAsync(v);
            }
        }

        /// <summary>
        /// 分页查询车辆列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Vehicle>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            IQueryable<Models.Vehicle> query = db.Vehicles.Include(v => v.Department);

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
                "plate" => sortAscending ? query.OrderBy(v => v.LicensePlate) : query.OrderByDescending(v => v.LicensePlate),
                "brand" => sortAscending
                    ? query.OrderBy(v => v.Brand).ThenBy(v => v.Model)
                    : query.OrderByDescending(v => v.Brand).ThenByDescending(v => v.Model),
                "color" => sortAscending ? query.OrderBy(v => v.Color) : query.OrderByDescending(v => v.Color),
                _ => sortAscending ? query.OrderBy(v => v.CreatedAt) : query.OrderByDescending(v => v.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            if (page > 1 && page > (int)Math.Ceiling((double)totalCount / pageSize))
                page = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Models.Vehicle>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
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
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Vehicles
                .Include(v => v.Department)
                .Where(v => v.Status == Models.VehicleStatus.Active)
                .OrderBy(v => v.LicensePlate)
                .ToListAsync();
        }

        /// <summary>
        /// 创建车辆
        /// </summary>
        public async Task<Models.Vehicle> CreateVehicleAsync(Models.Vehicle vehicle)
        {
            return await _vehicleRepo.AddAsync(vehicle);
        }

        /// <summary>
        /// 更新车辆
        /// </summary>
        public async Task<Models.Vehicle> UpdateVehicleAsync(Models.Vehicle vehicle)
        {
            vehicle.UpdatedAt = ChinaTime.Now;
            return await _vehicleRepo.UpdateAsync(vehicle);
        }

        /// <summary>
        /// 删除车辆（含附件清理 + 业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteVehicleAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var hasRecords = await db.VehicleUsageRecords.AnyAsync(r => r.VehicleId == id);
            if (hasRecords)
            {
                return (false, "无法删除该车辆：该车辆还有使用记录，请先删除相关使用记录。");
            }

            var vehicle = await db.Vehicles.FindAsync(id);
            if (vehicle == null) return (false, "删除失败：车辆不存在。");

            // 先清理车辆图片附件，失败则阻止 DB 删除
            if (!string.IsNullOrEmpty(vehicle.ImagePath))
            {
                try
                {
                    _fileService.DeleteFile(vehicle.ImagePath);
                    _logger.LogInformation("删除车辆 {Id} 时清理了图片附件", id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "删除车辆 {Id} 的图片附件失败，已阻止数据库删除", id);
                    return (false, "删除失败：图片附件被占用或无法访问，请稍后重试。");
                }
            }

            db.Vehicles.Remove(vehicle);
            await db.SaveChangesAsync();
            _logger.LogInformation("车辆 {Id} 已删除", id);
            return (true, null);
        }

        /// <summary>
        /// 校验车辆是否可以下载二维码
        /// </summary>
        public async Task<(bool CanDownload, string ErrorMessage)> ValidateQrCodeDownloadAsync(Models.Vehicle vehicle)
        {
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

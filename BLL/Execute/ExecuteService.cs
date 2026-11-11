using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Location;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Execute
{
    /// <summary>
    /// 二维码执行页面服务实现
    /// </summary>
    public class ExecuteService
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly LocationService _locationService;
        private readonly ICurrentUser _currentUser;
        private readonly AuditService _auditService;
        private readonly ILogger<ExecuteService> _logger;

        public ExecuteService(
            IDbContextFactory<VehicleDbContext> dbFactory,
            LocationService locationService,
            ICurrentUser currentUser,
            AuditService auditService,
            ILogger<ExecuteService> logger)
        {
            _dbFactory = dbFactory;
            _locationService = locationService;
            _currentUser = currentUser;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>
        /// 根据二维码Token加载页面数据
        /// </summary>
        public async Task<ExecutePageData> LoadPageDataAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            await using var db = await _dbFactory.CreateDbContextAsync();
            // 查询车辆信息（含部门）
            var vehicle = await db.Vehicles
                .Include(v => v.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.QrCodeToken == token);

            if (vehicle == null) return null;

            // 使用 / 借车由「登录人所属部门 vs 车辆所属部门」自动决定，页面不提供切换
            var me = await _currentUser.GetAsync();
            var loginDeptId = me.DepartmentId ?? vehicle.DepartmentId;
            var isBorrow = ResolveIsBorrow(me.DepartmentId, 0, vehicle.DepartmentId);

            // 人员下拉取登录人所属部门（非车辆部门）：跨部门时也不能借本部门以外的人
            var personnelList = await ((IQueryable<Models.Personnel>)db.Personnel)
                .AsNoTracking()
                .Where(p => p.DepartmentId == loginDeptId && p.IsActive)
                .ToListAsync();

            // 查询途经路径点和停车位置
            var waypoints = await _locationService.GetActiveWaypointsAsync();
            var finalLocations = await _locationService.GetActiveFinalLocationsAsync();

            // 起始里程预填：使用 / 借车两个分支共用这一份值
            var preFilledMileage = await QueryLastMileageEndAsync(db, vehicle.Id);

            return new ExecutePageData
            {
                Vehicle = vehicle,
                IsBorrow = isBorrow,
                PersonnelList = personnelList,
                Waypoints = waypoints,
                FinalLocations = finalLocations,
                PreFilledMileageStart = preFilledMileage
            };
        }

        /// <summary>
        /// 起始里程预填值：该车最近一条非空结束里程；无历史返回 null（不返回 0）。
        /// 供后台借车页 /borrow/{vehicleId} 使用，与扫码页同一口径。
        /// </summary>
        public async Task<double?> GetLastMileageEndAsync(int vehicleId)
        {
            if (vehicleId <= 0) return null;
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await QueryLastMileageEndAsync(db, vehicleId);
        }

        /// <summary>
        /// 预填口径唯一化：MileageEnd 非空过滤 + CreatedAt 降序、Id 降序（唯一列兜底）。
        /// 只取「最近一条」不够 —— 最近一条可能还没填结束里程，会把预填值一起漏掉。
        /// </summary>
        private static Task<double?> QueryLastMileageEndAsync(VehicleDbContext db, int vehicleId)
            => db.VehicleUsageRecords
                .AsNoTracking()
                .Where(r => r.VehicleId == vehicleId && r.MileageEnd != null)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Select(r => r.MileageEnd)
                .FirstOrDefaultAsync();

        /// <summary>归属部门：User/DeptAdmin 恒取自身部门；Admin 不绑部门才回落到表单值（既有约定）。</summary>
        public static int ResolveDepartmentId(int? scopeDepartmentId, int requestedDepartmentId, int vehicleDepartmentId)
            => scopeDepartmentId ?? (requestedDepartmentId != 0 ? requestedDepartmentId : vehicleDepartmentId);

        /// <summary>使用 / 借车唯一判据：归属部门与车辆所属部门相同 → 使用；不同 → 借车（不由前端传值或 URL 参数决定）。</summary>
        public static bool ResolveIsBorrow(int? scopeDepartmentId, int requestedDepartmentId, int vehicleDepartmentId)
            => ResolveDepartmentId(scopeDepartmentId, requestedDepartmentId, vehicleDepartmentId) != vehicleDepartmentId;

        /// <summary>
        /// 保存车辆使用记录
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SaveRecordAsync(SaveUsageRecordRequest request)
        {
            // ── 服务端定归属：前端传什么一律不算数 ──
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录");

            if (!me.IsEndUser && request.PersonnelId == 0)
                return (false, "请选择使用人员");

            // 使用人员：普通使用人员恒为本人，不允许代为他人登记
            var personnelId = me.IsEndUser ? (me.PersonnelId ?? 0) : request.PersonnelId;
            if (personnelId == 0)
                return (false, "当前账号未绑定车间人员，请联系管理员");

            // 业务校验
            if (request.WaypointIds == null || !request.WaypointIds.Any())
                return (false, "请选择至少一个途经路径点");
            if (request.FinalLocationId == 0)
                return (false, "请选择最终停车位置");

            try
            {
                var waypointSequence = string.Join(",", request.WaypointIds);
                await using var db = await _dbFactory.CreateDbContextAsync();

                // 车辆只用于存在性校验与取所属部门：借车只是记录标记，不写车辆状态
                var vehicle = await db.Vehicles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == request.VehicleId);
                if (vehicle == null) return (false, "车辆不存在，请重新扫码");

                // 归属部门：User/DeptAdmin 恒取自身部门；Admin 不绑部门才回落到表单值（既有约定）
                var departmentId = ResolveDepartmentId(me.ScopeDepartmentId, request.DepartmentId, vehicle.DepartmentId);

                // 使用 / 借车只看部门异同，不看前端传的开关
                var isBorrow = ResolveIsBorrow(me.ScopeDepartmentId, request.DepartmentId, vehicle.DepartmentId);

                var record = new Models.VehicleUsageRecord
                {
                    VehicleId = request.VehicleId,
                    DepartmentId = departmentId,
                    PersonnelId = personnelId,
                    IsBorrow = isBorrow,
                    FinalLocationId = request.FinalLocationId,
                    PurposeSequence = waypointSequence,
                    MileageStart = request.MileageStart,
                    MileageEnd = request.MileageEnd,
                    ImagePaths = request.UploadedFiles?.Any() == true ? string.Join(";", request.UploadedFiles) : null,
                    VideoPaths = null,
                    Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks,
                    CreatedAt = ChinaTime.Now
                };

                db.VehicleUsageRecords.Add(record);

                // 保存关联表（途经路径点按顺序）
                for (int i = 0; i < request.WaypointIds.Count; i++)
                {
                    db.VehicleUsageRecordPurposes.Add(new Models.VehicleUsageRecordPurpose
                    {
                        UsageRecord = record,
                        PurposeId = request.WaypointIds[i],
                        SequenceOrder = i + 1
                    });
                }

                await db.SaveChangesAsync();   // 记录与路径点同一次提交

                // 跨部门用车/借车是真实业务（扫到其他部门的车），允许，但必须落审计
                if (isBorrow)
                {
                    await _auditService.WriteAsync("UsageRecord.CrossDepartment", "Vehicle", vehicle.Id,
                        $"借车部门={departmentId}，车辆所属部门={vehicle.DepartmentId}");
                    await _auditService.WriteAsync("UsageRecord.Borrow", "VehicleUsageRecord", record.Id,
                        $"车辆Id={request.VehicleId}，借车部门={departmentId}，使用人员Id={personnelId}");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                // 异常原文会带出表名/列名/文件路径，不得回传给已登录的使用人员
                _logger.LogError(ex, "保存使用记录失败 VehicleId={VehicleId} PersonnelId={PersonnelId}",
                    request.VehicleId, request.PersonnelId);
                return (false, "保存失败，请稍后重试或联系管理员");
            }
        }
    }

    // ponytail: 原 IExecuteService.cs 中的 DTO，内联到实现文件
    public class ExecutePageData
    {
        public Models.Vehicle Vehicle { get; set; } = null!;

        /// <summary>是否借车登记：由「登录人所属部门 vs 车辆所属部门」服务端判定</summary>
        public bool IsBorrow { get; set; }

        public List<Models.Personnel> PersonnelList { get; set; } = new();
        public List<Models.Location> Waypoints { get; set; } = new();
        public List<Models.Location> FinalLocations { get; set; } = new();
        public double? PreFilledMileageStart { get; set; }
    }

    public class SaveUsageRecordRequest
    {
        public int VehicleId { get; set; }

        /// <summary>归属部门：仅 Admin（无部门身份）才会被采信，其余角色一律用服务端身份覆盖</summary>
        public int DepartmentId { get; set; }

        public int PersonnelId { get; set; }
        public List<int> WaypointIds { get; set; } = new();
        public int FinalLocationId { get; set; }
        public double? MileageStart { get; set; }
        public double? MileageEnd { get; set; }
        public List<string> UploadedFiles { get; set; } = new();
        public string Remarks { get; set; } = "";
    }
}

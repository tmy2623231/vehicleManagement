using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        public ExecuteService(IDbContextFactory<VehicleDbContext> dbFactory, LocationService locationService)
        {
            _dbFactory = dbFactory;
            _locationService = locationService;
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
                .FirstOrDefaultAsync(v => v.QrCodeToken == token);

            if (vehicle == null) return null;

            // 查询该部门的在职人员
            var personnelList = await ((IQueryable<Models.Personnel>)db.Personnel)
                .Where(p => p.DepartmentId == vehicle.DepartmentId && p.IsActive)
                .ToListAsync();

            // 查询途经路径点和停车位置
            var waypoints = await _locationService.GetActiveWaypointsAsync();
            var finalLocations = await _locationService.GetActiveFinalLocationsAsync();

            // 自动预填起始里程：查询该车辆最近一条使用记录的结束里程
            double? preFilledMileage = null;
            var lastRecord = await ((IQueryable<Models.VehicleUsageRecord>)db.VehicleUsageRecords)
                .Where(r => r.VehicleId == vehicle.Id)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
            if (lastRecord?.MileageEnd.HasValue == true)
            {
                preFilledMileage = lastRecord.MileageEnd.Value;
            }

            return new ExecutePageData
            {
                Vehicle = vehicle,
                PersonnelList = personnelList,
                Waypoints = waypoints,
                FinalLocations = finalLocations,
                PreFilledMileageStart = preFilledMileage
            };
        }

        /// <summary>
        /// 保存车辆使用记录
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SaveRecordAsync(SaveUsageRecordRequest request)
        {
            // 业务校验
            if (request.PersonnelId == 0)
                return (false, "请选择使用人员");
            if (request.WaypointIds == null || !request.WaypointIds.Any())
                return (false, "请选择至少一个途经路径点");
            if (request.FinalLocationId == 0)
                return (false, "请选择最终停车位置");

            try
            {
                var waypointSequence = string.Join(",", request.WaypointIds);
                await using var db = await _dbFactory.CreateDbContextAsync();

                var record = new Models.VehicleUsageRecord
                {
                    VehicleId = request.VehicleId,
                    DepartmentId = request.DepartmentId,
                    PersonnelId = request.PersonnelId,
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

                await db.SaveChangesAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"保存失败：{ex.Message}");
            }
        }
    }

    // ponytail: 原 IExecuteService.cs 中的 DTO，内联到实现文件
    public class ExecutePageData
    {
        public Models.Vehicle Vehicle { get; set; } = null!;
        public List<Models.Personnel> PersonnelList { get; set; } = new();
        public List<Models.Location> Waypoints { get; set; } = new();
        public List<Models.Location> FinalLocations { get; set; } = new();
        public double? PreFilledMileageStart { get; set; }
    }

    public class SaveUsageRecordRequest
    {
        public int VehicleId { get; set; }
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

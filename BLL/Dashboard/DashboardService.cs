using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Dashboard
{
    /// <summary>
    /// 首页仪表盘服务实现
    /// </summary>
    public class DashboardService
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly ICurrentUser _currentUser;

        public DashboardService(IDbContextFactory<VehicleDbContext> dbFactory, ICurrentUser currentUser)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
        }

        /// <summary>
        /// 获取车辆统计卡片数据
        /// </summary>
        public async Task<VehicleStatsSummary> GetVehicleStatsAsync()
        {
            // 深度防御：本页实际仅 Admin 可达，但统计口径同样不能越过部门边界
            var scope = (await _currentUser.GetAsync()).ScopeDepartmentId;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var vehicles = db.Vehicles.AsQueryable();
            if (scope is int scopedDeptId)
                vehicles = vehicles.Where(v => v.DepartmentId == scopedDeptId);

            // ponytail: 3 次 CountAsync 代替全表 ToListAsync + 内存过滤
            var total = await vehicles.CountAsync();
            var active = await vehicles.CountAsync(v => v.Status == Models.VehicleStatus.Active);
            var maintenance = await vehicles.CountAsync(v => v.Status == Models.VehicleStatus.Maintenance);
            var retired = await vehicles.CountAsync(v => v.Status == Models.VehicleStatus.Retired);
            return new VehicleStatsSummary
            {
                TotalCount = total,
                ActiveCount = active,
                MaintenanceCount = maintenance,
                RetiredCount = retired
            };
        }

        /// <summary>
        /// 获取部门车辆分布统计
        /// </summary>
        public async Task<List<DeptVehicleStat>> GetDeptVehicleStatsAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var scope = (await _currentUser.GetAsync()).ScopeDepartmentId;

            // ponytail: 单个 GroupBy 查询代替全表加载 + 内存分组
            var vehicles = db.Vehicles.AsQueryable();
            if (scope is int deptForStats)
                vehicles = vehicles.Where(v => v.DepartmentId == deptForStats);

            var stats = await vehicles
                .GroupBy(v => v.Department.Name)
                .Select(g => new DeptVehicleStat
                {
                    DeptName = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(s => s.Count)
                .ToListAsync();
            return stats;
        }

        /// <summary>
        /// 获取近6个月用车趋势
        /// </summary>
        public async Task<List<MonthlyTrendStat>> GetMonthlyTrendAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var scope = (await _currentUser.GetAsync()).ScopeDepartmentId;
            var sixMonthsAgo = ChinaTime.Now.AddMonths(-6);
            var recordQuery = ((IQueryable<Models.VehicleUsageRecord>)db.VehicleUsageRecords)
                .Where(r => r.CreatedAt >= sixMonthsAgo);
            if (scope is int deptForTrend)
                recordQuery = recordQuery.Where(r => r.DepartmentId == deptForTrend);
            var records = await recordQuery.ToListAsync();

            var monthlyGroups = records
                .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
                .Select(g => new MonthlyTrendStat
                {
                    Label = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Count = g.Count()
                })
                .OrderBy(m => m.Label)
                .ToList();

            // 补齐没有数据的月份
            var result = new List<MonthlyTrendStat>();
            for (var dt = sixMonthsAgo; dt <= ChinaTime.Now; dt = dt.AddMonths(1))
            {
                var label = $"{dt.Year}-{dt.Month:D2}";
                var existing = monthlyGroups.FirstOrDefault(m => m.Label == label);
                result.Add(new MonthlyTrendStat { Label = label, Count = existing?.Count ?? 0 });
            }
            return result;
        }

        /// <summary>
        /// 获取部门用车排行
        /// </summary>
        public async Task<List<DeptRankStat>> GetDeptRankAsync(string period)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleUsageRecords.AsQueryable();
            var scope = (await _currentUser.GetAsync()).ScopeDepartmentId;
            if (scope is int scopedDeptId)
                query = query.Where(r => r.DepartmentId == scopedDeptId);
            query = ApplyPeriodFilter(query, period);

            var depts = await ((IQueryable<Models.Department>)db.Departments).ToListAsync();
            var grouped = await query
                .GroupBy(r => r.DepartmentId)
                .Select(g => new { DeptId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            return grouped.Take(10).Select(g => new DeptRankStat
            {
                DeptName = depts.FirstOrDefault(d => d.Id == g.DeptId)?.Name ?? "未知",
                Count = g.Count
            }).ToList();
        }

        /// <summary>
        /// 获取人员用车排行
        /// </summary>
        public async Task<List<PersonRankStat>> GetPersonRankAsync(string period)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleUsageRecords.AsQueryable();
            var scope = (await _currentUser.GetAsync()).ScopeDepartmentId;
            if (scope is int scopedDeptId)
                query = query.Where(r => r.DepartmentId == scopedDeptId);
            query = ApplyPeriodFilter(query, period);

            var personnelList = await ((IQueryable<Models.Personnel>)db.Personnel).ToListAsync();
            var grouped = await query
                .GroupBy(r => r.PersonnelId)
                .Select(g => new { PersonId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            return grouped.Take(10).Select(g => new PersonRankStat
            {
                PersonName = personnelList.FirstOrDefault(p => p.Id == g.PersonId)?.Name ?? "未知",
                Count = g.Count
            }).ToList();
        }

        /// <summary>
        /// 分页查询最近使用记录
        /// </summary>
        public async Task<PagedResult<Models.VehicleUsageRecord>> GetRecentRecordsAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.VehicleUsageRecords
                .Include(r => r.Vehicle)
                .Include(r => r.Personnel)
                .Include(r => r.Department)
                .Include(r => r.FinalLocation)
                .AsQueryable();

            // 部门隔离（深度防御）
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
        /// 根据时间范围过滤使用记录查询
        /// </summary>
        private IQueryable<Models.VehicleUsageRecord> ApplyPeriodFilter(IQueryable<Models.VehicleUsageRecord> query, string period)
        {
            if (period == "month")
            {
                var start = new DateTime(ChinaTime.Now.Year, ChinaTime.Now.Month, 1);
                query = query.Where(r => r.CreatedAt >= start);
            }
            else if (period == "quarter")
            {
                var quarter = (ChinaTime.Now.Month - 1) / 3;
                var start = new DateTime(ChinaTime.Now.Year, quarter * 3 + 1, 1);
                query = query.Where(r => r.CreatedAt >= start);
            }
            return query;
        }
    }

    // ponytail: 原 IDashboardService.cs 中的 DTO，内联到实现文件
    public class VehicleStatsSummary
    {
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int MaintenanceCount { get; set; }
        public int RetiredCount { get; set; }
    }

    public class DeptVehicleStat
    {
        public string DeptName { get; set; } = "";
        public int Count { get; set; }
    }

    public class MonthlyTrendStat
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class DeptRankStat
    {
        public string DeptName { get; set; } = "";
        public int Count { get; set; }
    }

    public class PersonRankStat
    {
        public string PersonName { get; set; } = "";
        public int Count { get; set; }
    }
}

using System;
using System.Text;
using System.Threading.Tasks;
using vehicleManagement.aiServer.BLL.Enums;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Vehicle;
using vehicleManagement.BLL.Department;
using vehicleManagement.BLL.Personnel;
using vehicleManagement.BLL.UsageRecord;
using vehicleManagement.BLL.Dashboard;
using vehicleManagement.DAL.Model;
using BizModels = vehicleManagement.DAL.Model;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// 根据用户意图查询 vehicleManagement.db 并格式化结果为 LLM 可读文本
    /// </summary>
    public class DataQuerier
    {
        private readonly VehicleService _vehicleService;
        private readonly DepartmentService _departmentService;
        private readonly PersonnelService _personnelService;
        private readonly UsageRecordService _usageRecordService;
        private readonly DashboardService _dashboardService;

        private const int MaxQueryResults = 20;

        public DataQuerier(
            VehicleService vehicleService,
            DepartmentService departmentService,
            PersonnelService personnelService,
            UsageRecordService usageRecordService,
            DashboardService dashboardService)
        {
            _vehicleService = vehicleService;
            _departmentService = departmentService;
            _personnelService = personnelService;
            _usageRecordService = usageRecordService;
            _dashboardService = dashboardService;
        }

        /// <summary>
        /// 根据意图分发查询
        /// </summary>
        public async Task<string?> QueryAsync(UserIntent intent, string? keyword)
        {
            try
            {
                return intent switch
                {
                    UserIntent.VehicleQuery => await QueryVehiclesAsync(keyword),
                    UserIntent.ActiveVehicleQuery => await QueryActiveVehiclesAsync(),
                    UserIntent.VehicleStats => await QueryVehicleStatsAsync(),
                    UserIntent.DepartmentQuery => await QueryDepartmentsAsync(keyword),
                    UserIntent.PersonnelQuery => await QueryPersonnelAsync(keyword),
                    UserIntent.UsageRecordQuery => await QueryUsageRecordsAsync(keyword),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                return $"查询数据库时发生错误：{ex.Message}";
            }
        }

        private async Task<string> QueryVehiclesAsync(string? keyword)
        {
            var paged = await _vehicleService.GetPagedAsync(keyword ?? "", "createdAt", false, 1, MaxQueryResults);
            if (paged.Items.Count == 0)
                return "【车辆查询结果】\n当前没有匹配的车辆数据。";

            var sb = new StringBuilder();
            sb.AppendLine("【车辆查询结果】");
            sb.AppendLine($"共找到 {paged.TotalCount} 辆匹配的车辆，以下显示前 {paged.Items.Count} 条：");
            sb.AppendLine();

            foreach (var v in paged.Items)
            {
                var statusText = v.Status switch
                {
                    BizModels.VehicleStatus.Active => "在用",
                    BizModels.VehicleStatus.Maintenance => "维修",
                    BizModels.VehicleStatus.Retired => "退役",
                    _ => "未知"
                };
                sb.AppendLine($"- 车牌号：{v.LicensePlate} | 品牌：{v.Brand ?? "-"} | 型号：{v.Model ?? "-"} | 颜色：{v.Color ?? "-"} | 年份：{v.Year?.ToString() ?? "-"} | 状态：{statusText} | 所属部门：{v.Department?.Name ?? "未分配"}");
            }

            return sb.ToString();
        }

        private async Task<string> QueryActiveVehiclesAsync()
        {
            var vehicles = await _vehicleService.GetActiveVehiclesAsync();
            if (vehicles.Count == 0)
                return "【在用车辆查询结果】\n当前没有在用车辆。";

            var sb = new StringBuilder();
            sb.AppendLine("【在用车辆查询结果】");
            sb.AppendLine($"共找到 {vehicles.Count} 辆在用车辆：");
            sb.AppendLine();

            foreach (var v in vehicles)
            {
                sb.AppendLine($"- 车牌号：{v.LicensePlate} | 品牌：{v.Brand ?? "-"} | 型号：{v.Model ?? "-"} | 颜色：{v.Color ?? "-"} | 所属部门：{v.Department?.Name ?? "未分配"}");
            }

            return sb.ToString();
        }

        private async Task<string> QueryVehicleStatsAsync()
        {
            var stats = await _dashboardService.GetVehicleStatsAsync();
            var deptStats = await _dashboardService.GetDeptVehicleStatsAsync();
            var trend = await _dashboardService.GetMonthlyTrendAsync();

            var sb = new StringBuilder();
            sb.AppendLine("【车辆统计数据（真实数据库查询结果）】");
            sb.AppendLine();
            sb.AppendLine("▸ 车辆总览：");
            sb.AppendLine($"  - 车辆总数：{stats.TotalCount} 辆");
            sb.AppendLine($"  - 在用车辆：{stats.ActiveCount} 辆");
            sb.AppendLine($"  - 维修车辆：{stats.MaintenanceCount} 辆");
            sb.AppendLine($"  - 退役车辆：{stats.RetiredCount} 辆");
            sb.AppendLine();

            if (deptStats.Count > 0)
            {
                sb.AppendLine("▸ 部门车辆分布（车辆归属到各部门的数量统计，可用于回答「哪个部门用车最多」等问题）：");
                foreach (var ds in deptStats)
                    sb.AppendLine($"  - {ds.DeptName}：{ds.Count} 辆");
                sb.AppendLine();
            }

            sb.AppendLine("▸ 近6个月用车趋势：");
            foreach (var t in trend)
                sb.AppendLine($"  - {t.Label}：{t.Count} 次");

            return sb.ToString();
        }

        private async Task<string> QueryDepartmentsAsync(string? keyword)
        {
            var paged = await _departmentService.GetPagedAsync(keyword ?? "", "name", true, 1, MaxQueryResults);
            if (paged.Items.Count == 0)
                return "【部门查询结果】\n当前没有匹配的部门数据。";

            var sb = new StringBuilder();
            sb.AppendLine("【部门查询结果】");
            sb.AppendLine($"共找到 {paged.TotalCount} 个部门，以下显示前 {paged.Items.Count} 条：");
            sb.AppendLine();

            foreach (var d in paged.Items)
            {
                sb.AppendLine($"- 部门名称：{d.Name} | 描述：{d.Description ?? "-"} | 创建时间：{d.CreatedAt:yyyy-MM-dd}");
            }

            // 交叉查询：如果关键词暗示查询部门下的人员（如有谁/有哪些人/人员/员工）
            var hasPersonHint = keyword != null &&
                (keyword.Contains("有谁") || keyword.Contains("有哪些人") || keyword.Contains("人员") ||
                 keyword.Contains("员工") || keyword.Contains("人") || keyword.Contains("谁"));
            if (hasPersonHint)
            {
                sb.AppendLine();
                sb.AppendLine("▸ 各部门人员：");
                foreach (var d in paged.Items)
                {
                    var personnel = await _personnelService.GetPagedAsync(d.Name, "name", true, 1, MaxQueryResults);
                    if (personnel.Items.Count > 0)
                    {
                        sb.AppendLine($"  【{d.Name}】");
                        foreach (var p in personnel.Items)
                        {
                            var status = p.IsActive ? "在职" : "离职";
                            sb.AppendLine($"    - {p.Name} | 电话：{p.Phone ?? "-"} | 状态：{status}");
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private async Task<string> QueryPersonnelAsync(string? keyword)
        {
            var paged = await _personnelService.GetPagedAsync(keyword ?? "", "name", true, 1, MaxQueryResults);
            if (paged.Items.Count == 0)
                return "【人员查询结果】\n当前没有匹配的人员数据。";

            var sb = new StringBuilder();
            sb.AppendLine("【人员查询结果】");
            sb.AppendLine($"共找到 {paged.TotalCount} 名人员，以下显示前 {paged.Items.Count} 条：");
            sb.AppendLine();

            foreach (var p in paged.Items)
            {
                var status = p.IsActive ? "在职" : "离职";
                sb.AppendLine($"- 姓名：{p.Name} | 电话：{p.Phone ?? "-"} | 部门：{p.Department?.Name ?? "未分配"} | 状态：{status}");
            }

            // 交叉查询：如果关键词暗示查询用车/车辆（如开了哪些车/用过什么车）
            var hasVehicleHint = keyword != null &&
                (keyword.Contains("开了") || keyword.Contains("开过") || keyword.Contains("用过") ||
                 keyword.Contains("用") || keyword.Contains("哪些车") || keyword.Contains("什么车"));
            if (hasVehicleHint)
            {
                sb.AppendLine();
                sb.AppendLine("▸ 人员用车记录：");
                foreach (var p in paged.Items)
                {
                    var records = await _usageRecordService.GetPagedAsync(p.Name, "createdAt", false, 1, MaxQueryResults);
                    if (records.Items.Count > 0)
                    {
                        sb.AppendLine($"  【{p.Name}】");
                        foreach (var r in records.Items)
                        {
                            var plate = r.Vehicle?.LicensePlate ?? "未知";
                            sb.AppendLine($"    - {r.CreatedAt:yyyy-MM-dd HH:mm} | 车牌：{plate} | 目的：{r.PurposeSequence ?? "-"}");
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private async Task<string> QueryUsageRecordsAsync(string? keyword)
        {
            // 检测时间范围关键词，映射为天数
            var days = TryParseTimeRange(keyword);
            PagedResult<VehicleUsageRecord> paged;

            if (days > 0)
            {
                paged = await _usageRecordService.GetRecentAsync(days, 1, MaxQueryResults);
            }
            else
            {
                // 无时间关键词时尝试纯文本搜索
                paged = await _usageRecordService.GetPagedAsync(keyword ?? "", "createdAt", false, 1, MaxQueryResults);
            }

            if (paged.Items.Count == 0)
                return days > 0
                    ? $"【使用记录查询结果】\n最近 {days} 天内没有使用记录。"
                    : "【使用记录查询结果】\n当前没有匹配的使用记录。";

            var sb = new StringBuilder();
            sb.AppendLine("【使用记录查询结果】");
            sb.AppendLine(days > 0
                ? $"最近 {days} 天内共 {paged.TotalCount} 条记录，以下显示前 {paged.Items.Count} 条："
                : $"共找到 {paged.TotalCount} 条记录，以下显示前 {paged.Items.Count} 条：");
            sb.AppendLine();

            foreach (var r in paged.Items)
            {
                var licensePlate = r.Vehicle?.LicensePlate ?? "未知";
                var driverName = r.Personnel?.Name ?? "未知";
                var deptName = r.Department?.Name ?? "未知";
                sb.AppendLine($"- 时间：{r.CreatedAt:yyyy-MM-dd HH:mm} | 车牌：{licensePlate} | 驾驶员：{driverName} | 部门：{deptName} | 目的：{r.PurposeSequence ?? "-"}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从关键词中提取时间范围（天数），未匹配返回 0
        /// </summary>
        private static int TryParseTimeRange(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return 0;

            var kw = keyword.Trim();

            // ── 精确数字匹配 ──
            var digitMatch = System.Text.RegularExpressions.Regex.Match(kw, @"(\d+)\s*天");
            if (digitMatch.Success && int.TryParse(digitMatch.Groups[1].Value, out var d))
                return d;

            var monthDigitMatch = System.Text.RegularExpressions.Regex.Match(kw, @"(\d+)\s*(个)?月");
            if (monthDigitMatch.Success && int.TryParse(monthDigitMatch.Groups[1].Value, out var m))
                return m * 30;

            // 天
            if (kw.Contains("今天") || kw.Contains("今日")) return 1;
            if (kw.Contains("昨天") || kw.Contains("昨日")) return 1;
            if (kw.Contains("前天")) return 3;

            // 周
            if (kw.Contains("一周") || kw.Contains("7天") || kw.Contains("七天") || kw.Contains("本周") || kw.Contains("这周"))
                return 7;
            if (kw.Contains("两周") || kw.Contains("14天") || kw.Contains("十四天") || kw.Contains("双周"))
                return 14;
            if (kw.Contains("三周") || kw.Contains("21天"))
                return 21;

            // 月
            if (kw.Contains("上个月") || kw.Contains("上月")) return 30;
            if (kw.Contains("一个月") || kw.Contains("一月") || kw.Contains("30天") || kw.Contains("三十天") || kw.Contains("本月"))
                return 30;
            if (kw.Contains("两个月") || kw.Contains("二月") || kw.Contains("60天"))
                return 60;
            if (kw.Contains("三个月") || kw.Contains("三月") || kw.Contains("90天"))
                return 90;

            // 季度
            if (kw.Contains("一季度") || kw.Contains("一个季度") || kw.Contains("上季度") || kw.Contains("本季度"))
                return 90;
            if (kw.Contains("二季度") || kw.Contains("两个季度") || kw.Contains("半年"))
                return 180;
            if (kw.Contains("三季") || kw.Contains("三个季度"))
                return 270;

            // 年
            if (kw.Contains("去年") || kw.Contains("上一年")) return 365;
            if (kw.Contains("一年") || kw.Contains("本年") || kw.Contains("今年") || kw.Contains("十二个月") || kw.Contains("365天"))
                return 365;
            if (kw.Contains("两年") || kw.Contains("二年") || kw.Contains("2年"))
                return 730;

            // 最近 / 近期（默认一周，排在最后避免误匹配）
            if (kw.Contains("最近") || kw.Contains("近期"))
                return 7;

            return 0;
        }
    }
}

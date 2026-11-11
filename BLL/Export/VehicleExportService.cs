using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.BLL.Auth;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Export
{
    public class VehicleExportService
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly ICurrentUser _currentUser;

        public VehicleExportService(IDbContextFactory<VehicleDbContext> dbFactory, ICurrentUser currentUser)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
        }

        /// <summary>
        /// 按月导出一辆车的使用记录与维修记录。
        /// 部门隔离：先断归属再查记录，越权与其他不存在一律返回 null（调用方统一提示「未找到」），
        /// 不泄露车辆是否存在，也不导出任何字段。
        /// </summary>
        public async Task<byte[]> ExportAsync(int vehicleId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var vehicle = await db.Vehicles
                .Include(v => v.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vehicleId);
            if (vehicle == null) return null;

            var me = await _currentUser.GetAsync();
            if (!me.CanAccessDepartment(vehicle.DepartmentId)) return null;

            var usageRecords = await db.VehicleUsageRecords
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Include(r => r.FinalLocation)
                .Include(r => r.UsageRecordPurposes).ThenInclude(p => p.Purpose)
                .AsNoTracking()
                .Where(r => r.VehicleId == vehicleId)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .ToListAsync();

            // 维修人 / 维修部门与车辆部门一起取，才能算出「跨部门维修」
            var maintenanceRecords = await db.VehicleMaintenanceRecords
                .Include(r => r.Vehicle).ThenInclude(v => v.Department)
                .Include(r => r.Maintainer)
                .Include(r => r.MaintainerDepartment)
                .AsNoTracking()
                .Where(r => r.VehicleId == vehicleId)
                .OrderByDescending(r => r.MaintenanceDate)
                .ThenByDescending(r => r.Id)
                .ToListAsync();

            using var ms = new MemoryStream();
            var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook);
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();
            var wsp = wbp.AddNewPart<WorksheetPart>();
            var ws = new Worksheet();
            wsp.Worksheet = ws;

            var sheets = wbp.Workbook.AppendChild(new Sheets());
            var sheet = new Sheet { Id = wbp.GetIdOfPart(wsp), SheetId = 1, Name = "车辆信息导出" };
            sheets.Append(sheet);

            var sd = new SheetData();
            ws.Append(sd);

            uint rowIdx = 1;

            // ponytail: helper lambdas for adding rows
            Action<string, string, bool> addTitle = (label, value, isTitle) =>
            {
                var row = new Row { RowIndex = rowIdx };
                row.Append(new Cell { CellReference = $"A{rowIdx}", DataType = CellValues.InlineString,
                    InlineString = new InlineString() { Text = new Text(label ?? "") } });
                if (value != null && !isTitle)
                {
                    row.Append(new Cell { CellReference = $"B{rowIdx}", DataType = CellValues.InlineString,
                        InlineString = new InlineString() { Text = new Text(value) } });
                }
                sd.Append(row);
                rowIdx++;
            };

            Action<string[]> addRow = (values) =>
            {
                var row = new Row { RowIndex = rowIdx };
                for (int i = 0; i < values.Length; i++)
                {
                    row.Append(new Cell
                    {
                        CellReference = $"{(char)('A' + i)}{rowIdx}",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString() { Text = new Text(values[i] ?? "") }
                    });
                }
                sd.Append(row);
                rowIdx++;
            };

            // ── 表头：车辆基本信息 ──
            addTitle("车辆基本信息", null, true);
            addTitle("车牌号", vehicle.LicensePlate, false);
            addTitle("所属部门", vehicle.Department?.Name ?? "-", false);
            addTitle("品牌/型号", $"{vehicle.Brand} {vehicle.Model}", false);
            addTitle("颜色", vehicle.Color ?? "-", false);
            addTitle("状态", vehicle.Status.ToString(), false);
            addTitle("导出时间", ChinaTime.Now.ToString("yyyy-MM-dd HH:mm"), false);
            rowIdx++; // 空行

            // ── 使用记录列表 ──
            addTitle("使用记录列表", null, true);
            addRow(new[] { "序号", "类型", "部门", "人员", "使用路径", "目的地", "起始里程", "结束里程", "使用时间" });
            int seq = 1;
            foreach (var r in usageRecords)
            {
                var route = string.Join(" → ",
                    (r.UsageRecordPurposes ?? new()).OrderBy(p => p.SequenceOrder).Select(p => p.Purpose?.Name ?? ""));
                addRow(new[]
                {
                    seq++.ToString(),
                    // 类型取记录维度快照，不读车辆当前 IsBorrowed（会随时间变化）
                    r.RecordKindText,
                    r.Department?.Name ?? "-",
                    r.Personnel?.Name ?? "-",
                    route,
                    r.FinalLocation?.Name ?? "-",
                    r.MileageStart?.ToString("0.0") ?? "-",
                    r.MileageEnd?.ToString("0.0") ?? "-",
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                });
            }
            rowIdx++; // 空行

            // ── 维修记录 ──
            addTitle("维修信息记录", null, true);
            if (maintenanceRecords.Any())
            {
                addRow(new[] { "序号", "维修日期", "维修项目", "费用(元)", "维修人", "维修部门", "类型", "备注" });
                int mSeq = 1;
                foreach (var m in maintenanceRecords)
                {
                    addRow(new[]
                    {
                        mSeq++.ToString(),
                        m.MaintenanceDate.ToString("yyyy-MM-dd"),
                        m.Description ?? "-",
                        m.Cost.ToString("F2"),
                        // 老库记录无维修人快照时统一回退「未知」
                        m.MaintainerName,
                        m.MaintainerDepartmentName,
                        // 类型与两个列表同源同值：只取 MaintenanceKindText（跨部门维修 / 维修 / -）
                        m.MaintenanceKindText,
                        m.Remarks ?? "-"
                    });
                }
            }
            else
            {
                addTitle("暂无维修记录", null, false);
            }

            // ponytail: 只设最小列宽
            var cols = new Columns(new Column { Min = 1, Max = 20, Width = 18, CustomWidth = true });
            ws.InsertBefore(cols, sd);

            wbp.Workbook.Save();
            doc.Dispose();  // 必须关闭文档以将内容刷新到 MemoryStream
            return ms.ToArray();
        }
    }
}

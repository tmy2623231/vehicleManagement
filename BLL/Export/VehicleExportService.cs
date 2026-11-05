using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Export
{
    public class VehicleExportService
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public VehicleExportService(IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<byte[]> ExportAsync(int vehicleId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var vehicle = await db.Vehicles
                .Include(v => v.Department)
                .FirstOrDefaultAsync(v => v.Id == vehicleId);
            if (vehicle == null) return null;

            var usageRecords = await db.VehicleUsageRecords
                .Include(r => r.Department)
                .Include(r => r.Personnel)
                .Include(r => r.FinalLocation)
                .Include(r => r.UsageRecordPurposes).ThenInclude(p => p.Purpose)
                .Where(r => r.VehicleId == vehicleId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var maintenanceRecords = await db.VehicleMaintenanceRecords
                .Where(r => r.VehicleId == vehicleId)
                .OrderByDescending(r => r.MaintenanceDate)
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
            addTitle("借用状态", vehicle.IsBorrowed ? "是" : "否", false);
            addTitle("导出时间", ChinaTime.Now.ToString("yyyy-MM-dd HH:mm"), false);
            rowIdx++; // 空行

            // ── 使用记录列表 ──
            addTitle("使用记录列表", null, true);
            addRow(new[] { "序号", "部门", "是否借用", "人员", "使用路径", "目的地", "起始里程", "结束里程", "使用时间" });
            int seq = 1;
            foreach (var r in usageRecords)
            {
                var route = string.Join(" → ",
                    (r.UsageRecordPurposes ?? new()).OrderBy(p => p.SequenceOrder).Select(p => p.Purpose?.Name ?? ""));
                addRow(new[]
                {
                    seq++.ToString(),
                    r.Department?.Name ?? "-",
                    vehicle.IsBorrowed ? "是" : "否",
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
                addRow(new[] { "序号", "维修日期", "维修项目", "费用(元)", "备注" });
                int mSeq = 1;
                foreach (var m in maintenanceRecords)
                {
                    addRow(new[]
                    {
                        mSeq++.ToString(),
                        m.MaintenanceDate.ToString("yyyy-MM-dd"),
                        m.Description ?? "-",
                        m.Cost.ToString("F2"),
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

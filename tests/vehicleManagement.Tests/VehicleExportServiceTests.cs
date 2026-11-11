using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using vehicleManagement.BLL.Export;
using vehicleManagement.DAL.Common;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>导出 Excel 的列口径必须与 /usage-records、/maintenance-records 列表一致。</summary>
    public class VehicleExportServiceTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();

        private async Task<List<string[]>> ExportRowsAsync(int vehicleId)
        {
            var bytes = await new VehicleExportService(_factory, StubCurrentUser.Admin()).ExportAsync(vehicleId);
            Assert.NotNull(bytes);

            using var ms = new MemoryStream(bytes!);
            using var doc = SpreadsheetDocument.Open(ms, false);
            var sheetData = doc.WorkbookPart!.WorksheetParts.First()
                .Worksheet.GetFirstChild<SheetData>()!;

            return sheetData.Elements<Row>()
                .Select(r => r.Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? "").ToArray())
                .ToList();
        }

        /// <summary>取以指定列头开头的表头行及其后的连续数据行。</summary>
        private static (string[] Header, List<string[]> Rows) Section(
            List<string[]> rows, params string[] expectedHeader)
        {
            var header = rows.FirstOrDefault(r => r.SequenceEqual(expectedHeader));
            Assert.NotNull(header);

            var start = rows.IndexOf(header!);
            var data = new List<string[]>();
            for (var i = start + 1; i < rows.Count && rows[i].Length > 1; i++)
                data.Add(rows[i]);

            return (header!, data);
        }

        private static Models.VehicleUsageRecord Usage(int vehicleId, int departmentId, int personnelId, bool isBorrow)
            => new()
            {
                VehicleId = vehicleId,
                DepartmentId = departmentId,
                PersonnelId = personnelId,
                FinalLocationId = 2,
                PurposeSequence = "",
                IsBorrow = isBorrow,
                CreatedAt = ChinaTime.Now
            };

        private static Models.VehicleMaintenanceRecord Maintenance(
            int vehicleId, int? maintainerId, int? maintainerDeptId, string description)
            => new()
            {
                VehicleId = vehicleId,
                MaintenanceDate = ChinaTime.Now,
                Description = description,
                Cost = 100m,
                MaintainerPersonnelId = maintainerId,
                MaintainerDepartmentId = maintainerDeptId
            };

        [Fact]
        public async Task UsageSection_UsesRecordSnapshot_NotVehicleCurrentState()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                // 车辆 1 当前未被借用，但唯一一条记录是借车：必须按记录维度显示「借车」
                db.VehicleUsageRecords.AddRange(
                    Usage(1, TestData.DeptA, 10, isBorrow: true),
                    Usage(1, TestData.DeptA, 12, isBorrow: false));
                await db.SaveChangesAsync();
            }

            var rows = await ExportRowsAsync(1);
            var (header, data) = Section(rows, "序号", "类型", "部门", "人员", "使用路径", "目的地",
                "起始里程", "结束里程", "使用时间");

            Assert.Equal(1, Array.IndexOf(header, "类型"));
            Assert.DoesNotContain(rows, r => r.Contains("车辆当前借用状态"));
            // 车辆借用状态不再是业务概念，导出表头不得再出现
            Assert.DoesNotContain(rows, r => r.Contains("借用状态"));
            Assert.Contains(data, r => r[1] == "借车" && r[3] == "马存宝");
            Assert.Contains(data, r => r[1] == "使用" && r[3] == "马驰");
        }

        [Fact]
        public async Task MaintenanceSection_ShowsMaintainerAndKindColumn()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleMaintenanceRecords.AddRange(
                    // 车辆 2 属 DeptB：维修人属 DeptA → 跨部门
                    Maintenance(2, 10, TestData.DeptA, "更换刹车片"),
                    // 同部门 → 维修
                    Maintenance(2, 20, TestData.DeptB, "更换雨刷"),
                    // 老库补列后历史记录维修人/部门为 NULL → 类型显示「-」
                    Maintenance(2, null, null, "老旧保养"));
                await db.SaveChangesAsync();
            }

            var rows = await ExportRowsAsync(2);
            var (header, data) = Section(rows, "序号", "维修日期", "维修项目", "费用(元)",
                "维修人", "维修部门", "类型", "备注");

            Assert.Equal(4, Array.IndexOf(header, "维修人"));
            Assert.Equal(5, Array.IndexOf(header, "维修部门"));
            Assert.Equal(6, Array.IndexOf(header, "类型"));

            var cross = data.Single(r => r[2] == "更换刹车片");
            Assert.Equal("马存宝", cross[4]);
            Assert.Equal("信息安防技术室", cross[5]);
            Assert.Equal("跨部门维修", cross[6]);

            var same = data.Single(r => r[2] == "更换雨刷");
            Assert.Equal("尹辰璐", same[4]);
            Assert.Equal("信息设备维护室", same[5]);
            Assert.Equal("维修", same[6]);

            var legacy = data.Single(r => r[2] == "老旧保养");
            Assert.Equal("未知", legacy[4]);
            Assert.Equal("未知", legacy[5]);
            // 未知不得默认成「维修」
            Assert.Equal("-", legacy[6]);
        }

        public void Dispose() => _factory.Dispose();
    }
}

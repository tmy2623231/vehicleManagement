using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL
{
    /// <summary>种子数据：业务必需参考数据（任何环境）+ 演示数据（仅 Development 且开关开启）。</summary>
    public static class SeedDataInitializer
    {
        /// <summary>空库时插入的占位停车位置（扫码页必须有 Category=Purpose 的地点才能提交记录）</summary>
        private const string PlaceholderPurposeName = "停车场A";

        /// <summary>空库时插入的占位途经地点</summary>
        private const string PlaceholderLocationName = "办公楼";

        /// <summary>补全业务必需的参考数据（可重复执行）：仅当地点表完全为空时插入 2 条占位数据。</summary>
        public static async Task EnsureReferenceDataAsync(VehicleDbContext db)
        {
            if (await db.Locations.AnyAsync())
            {
                var purposeCount = await db.Locations.CountAsync(l => l.Category == LocationCategory.Purpose);
                if (purposeCount == 0)
                {
                    Log.Warning("当前库没有任何【最终停车位置】(Category=Purpose) 地点，扫码页将无法提交；请到 /locations 添加");
                }
                return;
            }

            db.Locations.AddRange(
                new Location
                {
                    Name = PlaceholderPurposeName,
                    Description = "占位数据，请替换为真实地点",
                    Category = LocationCategory.Purpose,
                    SortOrder = 1
                },
                new Location
                {
                    Name = PlaceholderLocationName,
                    Description = "占位数据，请替换为真实地点",
                    Category = LocationCategory.Location,
                    SortOrder = 1
                });

            await db.SaveChangesAsync();
            Log.Warning("空库：已插入 2 条占位地点（{Purpose} / {Location}），请在 /locations 页面替换为真实地点",
                PlaceholderPurposeName, PlaceholderLocationName);
        }

        /// <summary>插入演示数据（部门/人员/车辆/使用记录，可重复执行）：须 Development + 已开启开关，库中已有车辆则整体跳过。</summary>
        public static async Task SeedDemoDataAsync(VehicleDbContext db)
        {
            if (await db.Vehicles.AnyAsync())
            {
                Log.Information("演示种子数据跳过：数据库中已有车辆记录");
                return;
            }

            // 1. 5 个演示部门
            var departments = new[]
            {
                new Department { Id = 1, Name = "综合管理部", Description = "负责行政、财务、后勤等综合管理事务" },
                new Department { Id = 2, Name = "技术研发部", Description = "负责产品研发、技术创新与技术支持" },
                new Department { Id = 3, Name = "市场运营部", Description = "负责市场推广、销售运营与客户关系" },
                new Department { Id = 4, Name = "人力资源部", Description = "负责人事管理、招聘培训与薪酬福利" },
                new Department { Id = 5, Name = "质量管理部", Description = "负责产品质量检测、标准制定与质量改进" },
            };
            db.Departments.AddRange(departments);
            await db.SaveChangesAsync();

            // 2. 10 名演示人员
            var personnel = new[]
            {
                new Personnel { Id = 1,  DepartmentId = 1, Name = "张伟",   Phone = "13800138001", IsActive = true },
                new Personnel { Id = 2,  DepartmentId = 1, Name = "李娜",   Phone = "13800138002", IsActive = true },
                new Personnel { Id = 3,  DepartmentId = 2, Name = "王强",   Phone = "13800138003", IsActive = true },
                new Personnel { Id = 4,  DepartmentId = 2, Name = "赵敏",   Phone = "13800138004", IsActive = true },
                new Personnel { Id = 5,  DepartmentId = 3, Name = "孙磊",   Phone = "13800138005", IsActive = true },
                new Personnel { Id = 6,  DepartmentId = 3, Name = "周洁",   Phone = "13800138006", IsActive = true },
                new Personnel { Id = 7,  DepartmentId = 4, Name = "吴涛",   Phone = "13800138007", IsActive = true },
                new Personnel { Id = 8,  DepartmentId = 4, Name = "郑丽",   Phone = "13800138008", IsActive = true },
                new Personnel { Id = 9,  DepartmentId = 5, Name = "陈刚",   Phone = "13800138009", IsActive = true },
                new Personnel { Id = 10, DepartmentId = 5, Name = "刘洋",   Phone = "13800138010", IsActive = true },
            };
            db.Personnel.AddRange(personnel);
            await db.SaveChangesAsync();

            // 3. 8 辆演示车辆
            var vehicles = new[]
            {
                new Vehicle { Id = 1, DepartmentId = 1, LicensePlate = "京A88601", Brand = "丰田",   Model = "凯美瑞",   Color = "黑色", Year = 2024, Status = VehicleStatus.Active },
                new Vehicle { Id = 2, DepartmentId = 2, LicensePlate = "京B12345", Brand = "大众",   Model = "帕萨特",   Color = "白色", Year = 2024, Status = VehicleStatus.Active },
                new Vehicle { Id = 3, DepartmentId = 1, LicensePlate = "京C56789", Brand = "本田",   Model = "雅阁",     Color = "银色", Year = 2023, Status = VehicleStatus.Active },
                new Vehicle { Id = 4, DepartmentId = 3, LicensePlate = "京D33456", Brand = "别克",   Model = "GL8",      Color = "金色", Year = 2025, Status = VehicleStatus.Active },
                new Vehicle { Id = 5, DepartmentId = 2, LicensePlate = "京E99821", Brand = "比亚迪", Model = "汉EV",     Color = "红色", Year = 2025, Status = VehicleStatus.Active },
                new Vehicle { Id = 6, DepartmentId = 1, LicensePlate = "京F77654", Brand = "奥迪",   Model = "A6L",      Color = "黑色", Year = 2024, Status = VehicleStatus.Maintenance },
                new Vehicle { Id = 7, DepartmentId = 4, LicensePlate = "京G11223", Brand = "红旗",   Model = "H9",       Color = "黑色", Year = 2025, Status = VehicleStatus.Active },
                new Vehicle { Id = 8, DepartmentId = 3, LicensePlate = "京H55667", Brand = "长城",   Model = "哈弗H6",   Color = "白色", Year = 2023, Status = VehicleStatus.Active },
            };
            db.Vehicles.AddRange(vehicles);
            await db.SaveChangesAsync();

            // 4. 演示地点与示例使用记录
            await SeedDemoLocationsAsync(db);
            await SeedDemoUsageRecordsAsync(db);

            Log.Information("演示种子数据初始化完成：{DeptCount}部门 {PersonCount}人 {VehicleCount}车",
                departments.Length, personnel.Length, vehicles.Length);
        }

        /// <summary>
        /// 演示用地点的增量补充：分别为 Location / Purpose 两个分类各补 1 条演示数据。
        /// </summary>
        private static async Task SeedDemoLocationsAsync(VehicleDbContext db)
        {
            var added = 0;

            if (!await db.Locations.AnyAsync(l => l.Category == LocationCategory.Location))
            {
                db.Locations.Add(new Location
                {
                    Name = "集团总部",
                    Description = "演示数据",
                    Category = LocationCategory.Location,
                    SortOrder = 100
                });
                added++;
            }

            if (!await db.Locations.AnyAsync(l => l.Category == LocationCategory.Purpose))
            {
                db.Locations.Add(new Location
                {
                    Name = "集团总部停车场",
                    Description = "演示数据",
                    Category = LocationCategory.Purpose,
                    SortOrder = 100
                });
                added++;
            }

            if (added > 0)
            {
                await db.SaveChangesAsync();
                Log.Information("演示种子数据：插入了 {Count} 个演示地点", added);
            }
        }

        /// <summary>
        /// 演示用使用记录：仅当使用记录表为空时插入，FinalLocationId 取库中真实存在的 Purpose 地点。
        /// </summary>
        private static async Task SeedDemoUsageRecordsAsync(VehicleDbContext db)
        {
            if (await db.VehicleUsageRecords.AnyAsync()) return;

            var purposeIds = await db.Locations
                .Where(l => l.Category == LocationCategory.Purpose)
                .OrderBy(l => l.Id)
                .Select(l => l.Id)
                .ToListAsync();

            if (purposeIds.Count == 0)
            {
                Log.Warning("跳过演示使用记录：库中没有任何 Category=Purpose 的地点");
                return;
            }

            int Loc(int index) => purposeIds[index % purposeIds.Count];

            var today = ChinaTime.Now.Date;
            var usageRecords = new[]
            {
                new VehicleUsageRecord { VehicleId = 1, DepartmentId = 1, PersonnelId = 1,  FinalLocationId = Loc(0), PurposeSequence = "公车出差", MileageStart = 10000, MileageEnd = 10150, CreatedAt = today.AddDays(-0) },
                new VehicleUsageRecord { VehicleId = 2, DepartmentId = 2, PersonnelId = 3,  FinalLocationId = Loc(1), PurposeSequence = "拜访客户", MileageStart = 25000, MileageEnd = 25180, CreatedAt = today.AddDays(-1) },
                new VehicleUsageRecord { VehicleId = 4, DepartmentId = 3, PersonnelId = 5,  FinalLocationId = Loc(2), PurposeSequence = "接送领导", MileageStart = 8000,  MileageEnd = 8300,  CreatedAt = today.AddDays(-2) },
                new VehicleUsageRecord { VehicleId = 5, DepartmentId = 2, PersonnelId = 4,  FinalLocationId = Loc(0), PurposeSequence = "项目考察", MileageStart = 5000,  MileageEnd = 5200,  CreatedAt = today.AddDays(-3) },
                new VehicleUsageRecord { VehicleId = 7, DepartmentId = 4, PersonnelId = 7,  FinalLocationId = Loc(1), PurposeSequence = "社保办事", MileageStart = 15000, MileageEnd = 15120, CreatedAt = today.AddDays(-4) },
                new VehicleUsageRecord { VehicleId = 8, DepartmentId = 3, PersonnelId = 6,  FinalLocationId = Loc(0), PurposeSequence = "市场调研", MileageStart = 3000,  MileageEnd = 3200,  CreatedAt = today.AddDays(-5) },
                new VehicleUsageRecord { VehicleId = 3, DepartmentId = 1, PersonnelId = 2,  FinalLocationId = Loc(1), PurposeSequence = "取送文件", MileageStart = 20000, MileageEnd = 20080, CreatedAt = today.AddDays(-5) },
                new VehicleUsageRecord { VehicleId = 6, DepartmentId = 1, PersonnelId = 1,  FinalLocationId = Loc(0), PurposeSequence = "车辆送修", MileageStart = 50000, MileageEnd = 50100, CreatedAt = today.AddDays(-6) },
            };
            db.VehicleUsageRecords.AddRange(usageRecords);
            await db.SaveChangesAsync();
            Log.Information("演示种子数据：插入了 {Count} 条使用记录", usageRecords.Length);
        }
    }
}

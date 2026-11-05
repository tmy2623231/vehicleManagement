using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL
{
    /// <summary>
    /// 种子数据初始化器：Development 环境下插入标准部门、人员、车辆、使用记录数据
    /// </summary>
    public static class SeedDataInitializer
    {
        /// <summary>
        /// 仅在数据库为空时执行全量种子数据初始化（幂等操作）
        /// </summary>
        public static async Task InitializeAsync(VehicleDbContext db)
        {
            // 幂等性检查：已有车辆数据则跳过全量初始化，走增量补充
            if (await db.Vehicles.AnyAsync())
            {
                Log.Information("全量种子数据跳过：数据库中已有车辆记录");
                await SeedLocationsIfEmptyAsync(db);
                await SeedUsageRecordsIfEmptyAsync(db);
                return;
            }

            Log.Warning("正在重建数据库并插入标准种子数据...");

            // 1. 清空并重建所有表
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // 2. 插入 5 个标准化部门
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

            // 3. 插入 10 名标准化人员
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

            // 4. 插入 8 辆标准化车辆
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

            // 5. 插入标准地点和使用记录
            await SeedLocationsIfEmptyAsync(db);
            await SeedUsageRecordsIfEmptyAsync(db);

            Log.Information("种子数据初始化完成：{DeptCount}部门 {PersonCount}人 {VehicleCount}车",
                departments.Length, personnel.Length, vehicles.Length);
        }

        /// <summary>
        /// 增量补充：地点表为空时插入标准地点
        /// </summary>
        private static async Task SeedLocationsIfEmptyAsync(VehicleDbContext db)
        {
            if (await db.Locations.AnyAsync()) return;

            var locations = new[]
            {
                new Location { Id = 1, Name = "集团总部",       Description = "集团办公大楼",   Category = LocationCategory.Location, SortOrder = 1 },
                new Location { Id = 2, Name = "市政府",         Description = "政府办公楼",     Category = LocationCategory.Location, SortOrder = 2 },
                new Location { Id = 3, Name = "机场T3航站楼",   Description = "首都机场T3",    Category = LocationCategory.Location, SortOrder = 3 },
            };
            db.Locations.AddRange(locations);
            await db.SaveChangesAsync();
            Log.Information("增量种子数据：插入了 {Count} 个标准地点", locations.Length);
        }

        /// <summary>
        /// 增量补充：使用记录为空时插入示例数据（分布在最近7天）
        /// </summary>
        private static async Task SeedUsageRecordsIfEmptyAsync(VehicleDbContext db)
        {
            if (await db.VehicleUsageRecords.AnyAsync()) return;

            var today = ChinaTime.Now.Date;
            var usageRecords = new[]
            {
                new VehicleUsageRecord { VehicleId = 1, DepartmentId = 1, PersonnelId = 1,  FinalLocationId = 1, PurposeSequence = "公车出差", MileageStart = 10000, MileageEnd = 10150, CreatedAt = today.AddDays(-0) },
                new VehicleUsageRecord { VehicleId = 2, DepartmentId = 2, PersonnelId = 3,  FinalLocationId = 2, PurposeSequence = "拜访客户", MileageStart = 25000, MileageEnd = 25180, CreatedAt = today.AddDays(-1) },
                new VehicleUsageRecord { VehicleId = 4, DepartmentId = 3, PersonnelId = 5,  FinalLocationId = 3, PurposeSequence = "接送领导", MileageStart = 8000,  MileageEnd = 8300,  CreatedAt = today.AddDays(-2) },
                new VehicleUsageRecord { VehicleId = 5, DepartmentId = 2, PersonnelId = 4,  FinalLocationId = 1, PurposeSequence = "项目考察", MileageStart = 5000,  MileageEnd = 5200,  CreatedAt = today.AddDays(-3) },
                new VehicleUsageRecord { VehicleId = 7, DepartmentId = 4, PersonnelId = 7,  FinalLocationId = 2, PurposeSequence = "社保办事", MileageStart = 15000, MileageEnd = 15120, CreatedAt = today.AddDays(-4) },
                new VehicleUsageRecord { VehicleId = 8, DepartmentId = 3, PersonnelId = 6,  FinalLocationId = 1, PurposeSequence = "市场调研", MileageStart = 3000,  MileageEnd = 3200,  CreatedAt = today.AddDays(-5) },
                new VehicleUsageRecord { VehicleId = 3, DepartmentId = 1, PersonnelId = 2,  FinalLocationId = 2, PurposeSequence = "取送文件", MileageStart = 20000, MileageEnd = 20080, CreatedAt = today.AddDays(-5) },
                new VehicleUsageRecord { VehicleId = 6, DepartmentId = 1, PersonnelId = 1,  FinalLocationId = 1, PurposeSequence = "车辆送修", MileageStart = 50000, MileageEnd = 50100, CreatedAt = today.AddDays(-6) },
            };
            db.VehicleUsageRecords.AddRange(usageRecords);
            await db.SaveChangesAsync();
            Log.Information("增量种子数据：插入了 {Count} 条使用记录", usageRecords.Length);
        }
    }
}


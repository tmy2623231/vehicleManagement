using System.Threading.Tasks;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>播种辅助：所有用例都需要的部门/人员/车辆/地点最小集合。</summary>
    internal static class TestData
    {
        public const int DeptA = 6;
        public const int DeptB = 7;

        /// <summary>播种两个部门 + 3 名 A 部门人员 + 2 名 B 部门人员 + 2 辆车 + 途经点/停车位置各 1。</summary>
        public static async Task SeedBasicsAsync(VehicleDbContext db)
        {
            db.Departments.AddRange(
                new Department { Id = DeptA, Name = "信息安防技术室" },
                new Department { Id = DeptB, Name = "信息设备维护室" });

            db.Personnel.AddRange(
                new Personnel { Id = 10, DepartmentId = DeptA, Name = "马存宝", IsActive = true },
                new Personnel { Id = 11, DepartmentId = DeptA, Name = "徐哲", IsActive = true },
                new Personnel { Id = 12, DepartmentId = DeptA, Name = "马驰", IsActive = true },
                new Personnel { Id = 20, DepartmentId = DeptB, Name = "尹辰璐", IsActive = true },
                new Personnel { Id = 21, DepartmentId = DeptB, Name = "胡克汉", IsActive = true });

            db.Vehicles.AddRange(
                new Vehicle { Id = 1, DepartmentId = DeptA, LicensePlate = "京A88601", QrCodeToken = "token-a", Status = VehicleStatus.Active },
                new Vehicle { Id = 2, DepartmentId = DeptB, LicensePlate = "京B12345", QrCodeToken = "token-b", Status = VehicleStatus.Active });

            db.Locations.AddRange(
                new Location { Id = 1, Name = "办公楼", Category = LocationCategory.Location, SortOrder = 1, IsActive = true },
                new Location { Id = 2, Name = "停车场A", Category = LocationCategory.Purpose, SortOrder = 1, IsActive = true });

            await db.SaveChangesAsync();
        }
    }
}

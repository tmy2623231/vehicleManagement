using vehicleManagement.DAL.Model;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// 维修记录「类型」列的唯一判据：记录维度的 IsCrossDepartment。
    /// 跨部门 → 跨部门维修；同部门 → 维修；身份未知（NULL）→ "-"（不得猜成「维修」）。
    /// </summary>
    public class MaintenanceKindTextTests
    {
        private const int VehicleDept = 3;

        private static VehicleMaintenanceRecord Record(int? maintainerDeptId)
            => new()
            {
                VehicleId = 1,
                Description = "更换刹车片",
                MaintainerDepartmentId = maintainerDeptId,
                Vehicle = new Vehicle { Id = 1, DepartmentId = VehicleDept, LicensePlate = "测A00001" }
            };

        [Fact]
        public void CrossDepartment_ShowsCrossDepartmentText()
        {
            var record = Record(VehicleDept + 1);

            Assert.True(record.IsCrossDepartment);
            Assert.Equal("跨部门维修", record.MaintenanceKindText);
            Assert.Equal("badge-red", record.MaintenanceKindBadgeClass);
        }

        [Fact]
        public void SameDepartment_ShowsMaintenanceText()
        {
            var record = Record(VehicleDept);

            Assert.False(record.IsCrossDepartment);
            Assert.Equal("维修", record.MaintenanceKindText);
            Assert.Equal("badge-green", record.MaintenanceKindBadgeClass);
        }

        [Fact]
        public void UnknownMaintainerDepartment_ShowsDash_NotMaintenance()
        {
            var record = Record(null);

            Assert.Null(record.IsCrossDepartment);
            Assert.Equal("-", record.MaintenanceKindText);
            Assert.Equal("", record.MaintenanceKindBadgeClass);
        }

        [Fact]
        public void MissingVehicle_ShowsDash()
        {
            var record = new VehicleMaintenanceRecord
            {
                VehicleId = 1,
                Description = "更换雨刷",
                MaintainerDepartmentId = VehicleDept,
                Vehicle = null
            };

            Assert.Null(record.IsCrossDepartment);
            Assert.Equal("-", record.MaintenanceKindText);
        }
    }
}

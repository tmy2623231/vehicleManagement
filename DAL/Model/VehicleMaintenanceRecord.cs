using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    public class VehicleMaintenanceRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        [Required]
        public DateTime MaintenanceDate { get; set; } = ChinaTime.Now;

        [MaxLength(500)]
        public string Description { get; set; }

        public decimal Cost { get; set; }

        // 可空：与现库列定义一致
        public string? Remarks { get; set; }

        /// <summary>附件路径，分号分隔</summary>
        public string? ImagePaths { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }

        /// <summary>维修人（登记时从登录身份写入，前端不可改）；老库补列为 NULL 时读取侧回退「未知」</summary>
        public int? MaintainerPersonnelId { get; set; }

        /// <summary>维修人所属部门（登记时从登录身份写入），与车辆所属部门不同即跨部门维修</summary>
        public int? MaintainerDepartmentId { get; set; }

        [ForeignKey("VehicleId")]
        public Vehicle Vehicle { get; set; }

        [ForeignKey("MaintainerPersonnelId")]
        public Personnel? Maintainer { get; set; }

        [ForeignKey("MaintainerDepartmentId")]
        public Department? MaintainerDepartment { get; set; }

        /// <summary>维修人姓名；未记录（老库记录 / Admin 登记）回退「未知」</summary>
        [NotMapped]
        public string MaintainerName => Maintainer?.Name ?? "未知";

        /// <summary>维修人所属部门名；未记录回退「未知」</summary>
        [NotMapped]
        public string MaintainerDepartmentName => MaintainerDepartment?.Name ?? "未知";

        /// <summary>维修部门与车辆所属部门是否不同；任一侧未知时返回 null（不当作跨部门）</summary>
        [NotMapped]
        public bool? IsCrossDepartment
            => MaintainerDepartmentId is int deptId && Vehicle is not null
                ? deptId != Vehicle.DepartmentId
                : null;

        /// <summary>
        /// 「类型」列文案的唯一判据：跨部门维修 / 维修 / -（身份未知，不猜成同部门）。
        /// 三处展示（/maintenance-records、/usage-records 内嵌维修表、Excel 导出）都只能取这个属性，
        /// 不得各自用车辆状态、登录人身份或前端传值重新推导。
        /// </summary>
        [NotMapped]
        public string MaintenanceKindText
            => IsCrossDepartment is bool cross ? (cross ? "跨部门维修" : "维修") : "-";

        /// <summary>「类型」单元格的徽标样式；未知（文案为「-」）返回空串，页面按纯文本渲染。</summary>
        [NotMapped]
        public string MaintenanceKindBadgeClass
            => IsCrossDepartment is bool cross ? (cross ? "badge-red" : "badge-green") : "";
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    public class Vehicle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [Required]
        [MaxLength(50)]
        public string LicensePlate { get; set; }

        // 以下六列在现库均为 TEXT NULL；Nullable=annotations 下不标 ? 会被 EF 推断成 NOT NULL
        [MaxLength(50)]
        public string? Brand { get; set; }

        [MaxLength(50)]
        public string? Model { get; set; }

        public int? Year { get; set; }

        [MaxLength(20)]
        public string? Color { get; set; }

        public VehicleStatus Status { get; set; } = VehicleStatus.Active;

        [MaxLength(64)]
        public string? QrCodeToken { get; set; }

        public string? ImagePath { get; set; }

        public string? Remarks { get; set; }

        /// <summary>
        /// 历史遗留列：借车只是使用记录上的 IsBorrow 标记，本列不再是业务状态。
        /// 保留是因为表结构不动且老库已有该列；任何判定/展示均不得再读它（启动时由 SchemaBootstrapper 归零）。
        /// </summary>
        public bool IsBorrowed { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("DepartmentId")]
        public Department Department { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    public class VehicleUsageRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int VehicleId { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [Required]
        public int PersonnelId { get; set; }

        /// <summary>是否借车登记：true=跨部门借车，false=同部门使用。记录维度唯一判据，与车辆当前状态无关。</summary>
        public bool IsBorrow { get; set; }

        [Required]
        public int FinalLocationId { get; set; }

        [Required]
        public string PurposeSequence { get; set; }

        public double? MileageStart { get; set; }

        public double? MileageEnd { get; set; }

        // 可空：与现库列定义（TEXT NULL）一致
        public string? ImagePaths { get; set; }

        public string? VideoPaths { get; set; }

        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        [ForeignKey("VehicleId")]
        public Vehicle Vehicle { get; set; }

        [ForeignKey("DepartmentId")]
        public Department Department { get; set; }

        [ForeignKey("PersonnelId")]
        public Personnel Personnel { get; set; }

        [ForeignKey("FinalLocationId")]
        public Location FinalLocation { get; set; }

        public List<VehicleUsageRecordPurpose> UsageRecordPurposes { get; set; }

        /// <summary>列表展示用的记录类型文案</summary>
        [NotMapped]
        public string RecordKindText => IsBorrow ? "借车" : "使用";
    }
}

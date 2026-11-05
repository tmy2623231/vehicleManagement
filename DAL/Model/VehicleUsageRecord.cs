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

        [Required]
        public int FinalLocationId { get; set; }

        [Required]
        public string PurposeSequence { get; set; }

        public double? MileageStart { get; set; }

        public double? MileageEnd { get; set; }

        public string ImagePaths { get; set; }

        public string VideoPaths { get; set; }

        public string Remarks { get; set; }

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
    }
}

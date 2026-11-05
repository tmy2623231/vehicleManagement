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

        public string Remarks { get; set; }

        /// <summary>附件路径，分号分隔</summary>
        public string ImagePaths { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("VehicleId")]
        public Vehicle Vehicle { get; set; }
    }
}

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

        [MaxLength(50)]
        public string Brand { get; set; }

        [MaxLength(50)]
        public string Model { get; set; }

        public int? Year { get; set; }

        [MaxLength(20)]
        public string Color { get; set; }

        public VehicleStatus Status { get; set; } = VehicleStatus.Active;

        [MaxLength(64)]
        public string QrCodeToken { get; set; }

        public string ImagePath { get; set; }

        public string Remarks { get; set; }

        public bool IsBorrowed { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("DepartmentId")]
        public Department Department { get; set; }
    }
}

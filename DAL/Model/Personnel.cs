using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicleManagement.DAL.Model
{
    public class Personnel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; }

        [MaxLength(20)]
        public string Phone { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("DepartmentId")]
        public Department Department { get; set; }
    }
}

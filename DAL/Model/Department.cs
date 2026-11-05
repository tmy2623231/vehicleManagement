using System;
using System.ComponentModel.DataAnnotations;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        public string Description { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}

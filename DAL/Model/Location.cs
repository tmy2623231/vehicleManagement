using System;
using System.ComponentModel.DataAnnotations;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    public class Location
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        // 可空：与现库列定义（TEXT NULL）一致
        public string? Description { get; set; }

        [Required]
        public LocationCategory Category { get; set; } = LocationCategory.Location;

        public int SortOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    }
}

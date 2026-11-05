using System;
using System.ComponentModel.DataAnnotations;

namespace vehicleManagement.DAL.Model
{
    public class Location
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        public string Description { get; set; }

        [Required]
        public LocationCategory Category { get; set; } = LocationCategory.Location;

        public int SortOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}

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

        // 可空：与现库列定义（TEXT NULL）一致；Nullable=annotations 下不标 ? 会被 EF 推断成 NOT NULL
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}

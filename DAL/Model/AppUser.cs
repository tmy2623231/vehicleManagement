using System;
using System.ComponentModel.DataAnnotations;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    /// <summary>系统账号（Cookie 认证自建，角色见 <see cref="Roles"/>）；增改列须同时改 DDL 与实体。</summary>
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        /// <summary>登录名，写入前统一 ToLowerInvariant（SQLite 的 TEXT 比较区分大小写）。</summary>
        [Required]
        [MaxLength(20)]
        public string UserName { get; set; } = "";

        /// <summary>口令哈希（PasswordHasher 结果），明文永不入库。</summary>
        [Required]
        public string PasswordHash { get; set; } = "";

        [MaxLength(50)]
        // 可空：与现库列定义（TEXT NULL）一致
        public string? DisplayName { get; set; }

        /// <summary>Admin | DeptAdmin | User，取值见 <see cref="Roles"/>。</summary>
        [Required]
        [MaxLength(16)]
        public string Role { get; set; } = Roles.User;

        /// <summary>所属部门，DeptAdmin 与 User 必填；Admin 为 null（全量视野）。</summary>
        public int? DepartmentId { get; set; }

        /// <summary>绑定的车间人员，仅 User 有值（部分唯一索引保证一人一账号）。</summary>
        public int? PersonnelId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    }
}

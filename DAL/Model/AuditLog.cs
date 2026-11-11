using System;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.DAL.Model
{
    /// <summary>审计日志（只追加，绝不 UPDATE / DELETE）。表结构由 SchemaBootstrapper 幂等建立。</summary>
    public class AuditLog
    {
        public int Id { get; set; }

        /// <summary>动作名，形如 <c>Auth.LoginSuccess</c> / <c>User.ResetPassword</c> / <c>Access.CrossDepartment</c>。</summary>
        public string Action { get; set; } = "";

        /// <summary>目标对象类型，如 <c>Personnel</c> / <c>Vehicle</c> / <c>AppUser</c>。</summary>
        public string TargetType { get; set; } = "";

        public int TargetId { get; set; }

        /// <summary>补充说明。**禁止写入明文口令**。</summary>
        public string? Detail { get; set; }

        /// <summary>操作人登录名（取自登录主体，不接受前端传入）。</summary>
        public string? UserName { get; set; }

        /// <summary>操作人所属部门；不写则事后无法按部门审计。</summary>
        public int? DepartmentId { get; set; }

        public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    }
}

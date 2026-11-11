using System.Security.Claims;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>当前登录者身份快照（不可变）：数据可见范围的唯一判据。</summary>
    public sealed class UserIdentity
    {
        public static UserIdentity Anonymous { get; } = new();

        public bool IsAuthenticated { get; init; }

        public int UserId { get; init; }

        public string UserName { get; init; } = "";

        /// <summary>取值见 <see cref="Roles"/>；未认证时为空串。</summary>
        public string Role { get; init; } = "";

        /// <summary>账号所属部门；Admin 为 null。</summary>
        public int? DepartmentId { get; init; }

        /// <summary>绑定的车间人员；仅 User 有值。</summary>
        public int? PersonnelId { get; init; }

        public bool IsAdmin => Role == Roles.Admin;

        public bool IsDeptAdmin => Role == Roles.DeptAdmin;

        public bool IsEndUser => Role == Roles.User;

        /// <summary>数据可见范围：Admin → null（不限）；其余 → 自身部门。**BLL 所有查询必须用它**。</summary>
        public int? ScopeDepartmentId => IsAdmin ? null : DepartmentId;

        /// <summary>写操作前的归属断言：范围内为 true。Admin 恒为 true。</summary>
        public bool CanAccessDepartment(int departmentId)
            => ScopeDepartmentId is not int scope || scope == departmentId;

        /// <summary>从 ClaimsPrincipal 解析；任一项解析失败即按未认证处理（fail-closed）。</summary>
        public static UserIdentity FromPrincipal(ClaimsPrincipal principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return Anonymous;

            return new UserIdentity
            {
                IsAuthenticated = true,
                UserId = ParseInt(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value),
                UserName = principal.Identity.Name ?? "",
                Role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "",
                DepartmentId = ParseNullableInt(principal.FindFirst(DAL.Common.AppClaimTypes.DepartmentId)?.Value),
                PersonnelId = ParseNullableInt(principal.FindFirst(DAL.Common.AppClaimTypes.PersonnelId)?.Value),
            };
        }

        private static int ParseInt(string value) => int.TryParse(value, out var n) ? n : 0;

        private static int? ParseNullableInt(string value) => int.TryParse(value, out var n) ? n : null;
    }
}

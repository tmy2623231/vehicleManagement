using System;

namespace vehicleManagement.DAL.Common
{
    /// <summary>自定义 Claim 类型：部门与人员归属只从登录主体取，绝不读表单或 URL 参数。</summary>
    public static class AppClaimTypes
    {
        /// <summary>账号所属部门 Id（Admin 无此 Claim）。</summary>
        public const string DepartmentId = "vm:dept";

        /// <summary>绑定的车间人员 Id（仅 User 角色有）。</summary>
        public const string PersonnelId = "vm:personnel";
    }
}

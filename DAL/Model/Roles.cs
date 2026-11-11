using System;

namespace vehicleManagement.DAL.Model
{
    /// <summary>账号角色常量。必须 const string（[Authorize(Roles)] 要求编译期常量），放 Model 层供实体默认值使用。</summary>
    public static class Roles
    {
        /// <summary>全局管理员：唯一账号 admin，可进全部后台页面（含 AI 页面），不作为扫码页使用人。</summary>
        public const string Admin = "Admin";

        /// <summary>部门管理员：只能看/改本部门的人员、车辆，使用记录只读；无 AI 页面。</summary>
        public const string DeptAdmin = "DeptAdmin";

        /// <summary>普通使用人员：不进后台，只用于扫码页登录与填写使用记录。</summary>
        public const string User = "User";

        /// <summary>后台两类角色（菜单显隐与页面授权共用），值形如 <c>Admin,DeptAdmin</c>。</summary>
        public const string BackOffice = Admin + "," + DeptAdmin;
    }
}

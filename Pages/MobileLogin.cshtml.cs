using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;

namespace vehicleManagement.Pages
{
    /// <summary>移动端登录（扫码/登记专用入口）：复用后台登录的校验与落地页逻辑，只换一套页面。</summary>
    public class MobileLoginModel : LoginModel
    {
        public MobileLoginModel(AuthService authService, AuditService auditService)
            : base(authService, auditService)
        {
        }

        /// <summary>落地页一律收敛成根相对路径，否则从 /m/login 解析相对地址会变成 /m/execute/xxx。</summary>
        protected override string ResolveTarget(string role, string returnUrl)
        {
            var target = base.ResolveTarget(role, returnUrl);
            return target.StartsWith('/') ? target : "/" + target;
        }
    }
}

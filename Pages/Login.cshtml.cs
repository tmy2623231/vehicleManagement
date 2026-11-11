using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.Pages
{
    /// <summary>
    /// 登录页模型。必须走 HTTP POST：Blazor 电路里拿不到 HttpContext，无法 SignInAsync。
    /// </summary>
    public class LoginModel : PageModel
    {
        /// <summary>D2 定案：记住我 = 30 天持久 Cookie。</summary>
        private static readonly TimeSpan PersistentDays = TimeSpan.FromDays(30);

        private readonly AuthService _authService;
        private readonly AuditService _auditService;

        public LoginModel(AuthService authService, AuditService auditService)
        {
            _authService = authService;
            _auditService = auditService;
        }

        [BindProperty]
        public string UserName { get; set; } = "";

        [BindProperty]
        public string Password { get; set; } = "";

        /// <summary>「记住我」，默认勾选（D2）。</summary>
        [BindProperty]
        public bool Remember { get; set; } = true;

        [BindProperty]
        public string ReturnUrl { get; set; }

        public string ErrorMessage { get; private set; }

        public void OnGet(string returnUrl)
        {
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var typedName = AuthService.NormalizeUserName(UserName);

            if (typedName.Length == 0 || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "请输入用户名与密码";
                return Page();
            }

            var user = await _authService.VerifyAsync(typedName, Password);
            if (user is null)
            {
                // 不区分「账号不存在 / 已禁用 / 密码错」，避免账号枚举
                ErrorMessage = "用户名或密码不正确";
                await _auditService.WriteAsAsync("Auth.LoginFailed", "AppUser", 0,
                    $"登录名={typedName}", typedName, null);
                return Page();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Role, user.Role)
            };
            if (user.DepartmentId is int deptId)
                claims.Add(new Claim(AppClaimTypes.DepartmentId, deptId.ToString()));
            if (user.PersonnelId is int personnelId)
                claims.Add(new Claim(AppClaimTypes.PersonnelId, personnelId.ToString()));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = new AuthenticationProperties
            {
                IsPersistent = Remember,
                ExpiresUtc = Remember ? DateTimeOffset.UtcNow.Add(PersistentDays) : null
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties);

            await _auditService.WriteAsAsync("Auth.LoginSuccess", "AppUser", user.Id,
                $"角色={user.Role}", user.UserName, user.DepartmentId);

            return LocalRedirect(ResolveTarget(user.Role, ReturnUrl));
        }

        /// <summary>按角色决定落地页：returnUrl 只对使用人员生效，且必须过 Url.IsLocalUrl 防开放重定向。</summary>
        protected virtual string ResolveTarget(string role, string returnUrl)
        {
            if (role == Roles.User)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return returnUrl;
                return "/scan";
            }

            return role == Roles.DeptAdmin ? "/vehicles" : "/";
        }
    }
}

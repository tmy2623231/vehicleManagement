using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>ICurrentUser 的 Blazor Server 实现：认证状态读一次并缓存到 Circuit 结束（不能用 IHttpContextAccessor）。</summary>
    public class CurrentUser : ICurrentUser
    {
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly ILogger<CurrentUser> _logger;
        private Task<UserIdentity> _cached;

        public CurrentUser(AuthenticationStateProvider authStateProvider, ILogger<CurrentUser> logger)
        {
            _authStateProvider = authStateProvider;
            _logger = logger;
        }

        public Task<UserIdentity> GetAsync() => _cached ??= LoadAsync();

        private async Task<UserIdentity> LoadAsync()
        {
            try
            {
                var state = await _authStateProvider.GetAuthenticationStateAsync();
                return UserIdentity.FromPrincipal(state.User);
            }
            catch (Exception ex)
            {
                // fail-closed：读不到身份就当未登录，绝不「读失败 → 放行」
                _logger.LogWarning(ex, "读取认证状态失败，按未登录处理");
                return UserIdentity.Anonymous;
            }
        }
    }
}

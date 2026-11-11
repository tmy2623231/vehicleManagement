using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.BLL.Auth;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Common
{
    /// <summary>审计日志写入（只追加）；由各服务显式调用，保证 Action 名可见、可 grep。</summary>
    public class AuditService
    {
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            IDbContextFactory<VehicleDbContext> dbFactory,
            ICurrentUser currentUser,
            ILogger<AuditService> logger)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>以当前登录者身份写审计（Blazor 页面与 BLL 内部一律用这个）。</summary>
        public async Task WriteAsync(string action, string targetType, int targetId, string detail = null)
        {
            var me = await _currentUser.GetAsync();
            await WriteCoreAsync(action, targetType, targetId, detail,
                me.IsAuthenticated ? me.UserName : null, me.DepartmentId);
        }

        /// <summary>以指定操作人写审计：登录页是 Razor Page，此刻还没有登录态，用户名只能从表单取。</summary>
        public Task WriteAsAsync(string action, string targetType, int targetId, string detail,
            string actorUserName, int? actorDepartmentId)
            => WriteCoreAsync(action, targetType, targetId, detail, actorUserName, actorDepartmentId);

        /// <summary>审计失败不阻断业务，但必须留 Error 日志。</summary>
        private async Task WriteCoreAsync(string action, string targetType, int targetId, string detail,
            string userName, int? departmentId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                db.AuditLogs.Add(new AuditLog
                {
                    Action = action,
                    TargetType = targetType,
                    TargetId = targetId,
                    Detail = detail,
                    UserName = userName,
                    DepartmentId = departmentId,
                    CreatedAt = ChinaTime.Now
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写审计日志失败 Action={Action} Target={TargetType}#{TargetId}", action, targetType, targetId);
            }
        }
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>账号校验（Cookie 认证的凭证核对）：口令用框架自带 PasswordHasher，用户名一律小写归一化。</summary>
    public class AuthService
    {
        /// <summary>账号新建/改密时接受的最短口令长度（初始口令 123456 满足）。</summary>
        public const int MinPasswordLength = 6;

        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly IPasswordHasher<AppUser> _hasher;

        public AuthService(IDbContextFactory<VehicleDbContext> dbFactory, IPasswordHasher<AppUser> hasher)
        {
            _dbFactory = dbFactory;
            _hasher = hasher;
        }

        /// <summary>用户名归一化：去空格 + 全小写。</summary>
        public static string NormalizeUserName(string userName)
            => (userName ?? "").Trim().ToLowerInvariant();

        public string HashPassword(string password) => _hasher.HashPassword(new AppUser(), password);

        /// <summary>
        /// 校验用户名/口令。账号不存在、已禁用、口令错误一律返回 null（不区分原因，避免账号枚举）。
        /// </summary>
        public async Task<AppUser> VerifyAsync(string userName, string password)
        {
            var normalized = NormalizeUserName(userName);
            if (normalized.Length == 0 || string.IsNullOrEmpty(password))
                return null;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.AppUsers.AsNoTracking()
                              .FirstOrDefaultAsync(u => u.UserName == normalized);

            if (user is null || !user.IsActive)
                return null;

            // 绑定人员已停用（离职）时账号一并失效
            if (user.PersonnelId is int personnelId)
            {
                var personnelIsActive = await db.Personnel.AsNoTracking()
                                                .AnyAsync(p => p.Id == personnelId && p.IsActive);
                if (!personnelIsActive)
                    return null;
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return result == PasswordVerificationResult.Failed ? null : user;
        }
    }
}

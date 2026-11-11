using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>账号管理：本部门使用人员的账号与口令维护（可操作范围由 ScopeDepartmentId 判定）。</summary>
    public class AccountService
    {
        /// <summary>用户名：3-20 位、字母开头、只允许小写字母与数字。</summary>
        private static readonly Regex UserNamePattern = new("^[a-z][a-z0-9]{2,19}$", RegexOptions.Compiled);

        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly ICurrentUser _currentUser;
        private readonly AuthService _authService;
        private readonly AuditService _auditService;

        public AccountService(
            IDbContextFactory<VehicleDbContext> dbFactory,
            ICurrentUser currentUser,
            AuthService authService,
            AuditService auditService)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
            _authService = authService;
            _auditService = auditService;
        }

        /// <summary>批量取「人员 → 账号」映射（人员列表的账号列用）；只含启用中的账号，其余按「未开通」展示。</summary>
        public async Task<Dictionary<int, AppUser>> GetAccountsByPersonnelAsync(IEnumerable<int> personnelIds)
        {
            var ids = (personnelIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var result = new Dictionary<int, AppUser>();
            if (ids.Count == 0) return result;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.AppUsers.AsNoTracking()
                            .Where(u => u.IsActive && u.PersonnelId != null && ids.Contains(u.PersonnelId.Value));

            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                query = query.Where(u => u.DepartmentId == scopedDeptId);

            foreach (var user in await query.ToListAsync())
            {
                if (user.PersonnelId is int pid)
                    result[pid] = user;
            }

            return result;
        }

        /// <summary>
        /// 给本部门人员开通账号：角色固定 <see cref="Roles.User"/>，部门固定为该人员所在部门。
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> CreateAccountAsync(
            int personnelId, string userName, string password)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");

            // 与登录侧同一套归一化：否则建出来的 "ZhangSan" 永远登不上（SQLite 比较区分大小写）
            var normalized = AuthService.NormalizeUserName(userName);
            if (!UserNamePattern.IsMatch(normalized))
                return (false, "用户名需为 3-20 位小写字母或数字且以字母开头（例如 zhangsan）。");

            if (string.IsNullOrEmpty(password) || password.Length < AuthService.MinPasswordLength)
                return (false, $"初始密码至少 {AuthService.MinPasswordLength} 位。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var personnel = await db.Personnel.AsNoTracking()
                                    .FirstOrDefaultAsync(p => p.Id == personnelId);
            if (personnel == null)
                return (false, "人员不存在。");
            if (!me.CanAccessDepartment(personnel.DepartmentId))
                return (false, "无权操作该部门数据。");

            // 软删行仍在库中（同一人员唯一），需取出走「重新开通」而非再插一行
            var existing = await db.AppUsers.FirstOrDefaultAsync(u => u.PersonnelId == personnelId);
            if (existing != null && existing.IsActive)
                return (false, "该人员已开通账号，如需更换口令请使用「重置密码」。");

            var duplicateName = await db.AppUsers.AnyAsync(u => u.UserName == normalized &&
                                                               (existing == null || u.Id != existing.Id));
            if (duplicateName)
                return (false, "该用户名已被占用，请换一个。");

            if (existing != null)
            {
                // 重新开通：复用原行，保留 Id
                existing.UserName = normalized;
                existing.PasswordHash = _authService.HashPassword(password);
                existing.DisplayName = personnel.Name;
                existing.Role = Roles.User;
                existing.DepartmentId = personnel.DepartmentId;
                existing.IsActive = true;
                await db.SaveChangesAsync();

                await _auditService.WriteAsync("User.Create", "AppUser", existing.Id,
                    $"用户名={normalized}，绑定人员={personnel.Name}#{personnelId}，方式=重新开通");

                return (true, null);
            }

            var user = new AppUser
            {
                UserName = normalized,
                PasswordHash = _authService.HashPassword(password),
                DisplayName = personnel.Name,
                Role = Roles.User,                       // 部门管理员不得创建 DeptAdmin / Admin
                DepartmentId = personnel.DepartmentId,   // 部门从人员行取，不信任前端
                PersonnelId = personnelId,
                IsActive = true,
                CreatedAt = ChinaTime.Now
            };

            db.AppUsers.Add(user);
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.Create", "AppUser", user.Id,
                $"用户名={normalized}，绑定人员={personnel.Name}#{personnelId}");

            return (true, null);
        }

        /// <summary>重置本部门普通使用人员的口令。</summary>
        public async Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(int userId, string newPassword)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");

            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < AuthService.MinPasswordLength)
                return (false, $"新密码至少 {AuthService.MinPasswordLength} 位。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "账号不存在。");
            if (!CanManage(me, user))
                return (false, "只能重置本部门普通使用人员的密码。");

            user.PasswordHash = _authService.HashPassword(newPassword);
            user.IsActive = true;          // 改密同时解除禁用
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.ResetPassword", "AppUser", user.Id, $"用户名={user.UserName}");

            return (true, null);
        }

        /// <summary>停用本部门使用人员的账号（软删 + 口令置随机；硬删会被启动种子重建成弱口令）。</summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAccountAsync(int userId)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "账号不存在。");
            if (!CanManage(me, user))
                return (false, "只能删除本部门普通使用人员的账号。");

            var userName = user.UserName;
            user.IsActive = false;
            user.PasswordHash = _authService.HashPassword(Guid.NewGuid().ToString("N"));
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.Delete", "AppUser", userId,
                $"用户名={userName}，方式=停用（可用「开通账号」重新启用）");

            return (true, null);
        }

        /// <summary>可管理判定：目标是普通使用人员且部门在调用者范围内（Admin 恒为 true）。</summary>
        private static bool CanManage(UserIdentity me, AppUser user)
            => user.Role == Roles.User && me.CanAccessDepartment(user.DepartmentId ?? 0);
    }
}

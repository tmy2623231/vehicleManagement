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
using DepartmentEntity = vehicleManagement.DAL.Model.Department;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>
    /// 账号服务，两个入口职责分开：
    /// 1) 人员管理页：本部门普通使用人员（User）账号的开通/重置/停用；
    /// 2) 账号管理页：后台账号（Admin / DeptAdmin）的创建/重置/启停/信息修正，写操作一律要求 me.IsAdmin。
    /// </summary>
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

        /// <summary>
        /// 账号管理页分页列表：只包含后台账号（Admin / DeptAdmin）。
        /// 普通使用人员账号归人员管理页，不在此重复展示。
        /// </summary>
        public async Task<PagedResult<AppUser>> GetPagedAccountsAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            pageSize = PagedResult.ClampPageSize(pageSize);

            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.AppUsers.AsNoTracking().Include(u => u.Department)
                            .Where(u => u.Role == Roles.Admin || u.Role == Roles.DeptAdmin);

            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                query = query.Where(u => u.DepartmentId == scopedDeptId);

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                // 用户名入库前已归一化为小写，关键字同步归一化后再匹配
                var kw = AuthService.NormalizeUserName(searchKeyword);
                query = query.Where(u => u.UserName.Contains(kw) ||
                                         (u.DisplayName != null && u.DisplayName.Contains(kw)));
            }

            // 排序白名单 + Id 兜底，保证分页顺序稳定
            query = sortColumn switch
            {
                "username" => sortAscending
                    ? query.OrderBy(u => u.UserName).ThenBy(u => u.Id)
                    : query.OrderByDescending(u => u.UserName).ThenByDescending(u => u.Id),
                _ => sortAscending
                    ? query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                    : query.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id),
            };

            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>开号与调整部门用的部门下拉选项（按名称排序）。</summary>
        public async Task<List<DepartmentEntity>> GetDepartmentOptionsAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Departments.AsNoTracking()
                           .OrderBy(d => d.Name)
                           .ToListAsync();
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

        /// <summary>
        /// 开通后台账号（Admin / DeptAdmin）：部门管理员必选部门且一个部门只能一个启用中的管理员；
        /// 全局管理员 DepartmentId 恒为 null（全量视野）。普通使用人员账号不得从此处创建。
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> CreateBackOfficeAccountAsync(
            string userName, string password, string role, int? departmentId, string displayName)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");
            if (!me.IsAdmin)
                return (false, "仅全局管理员可创建后台账号。");

            // 角色白名单：阻挡“通过后台账号入口建出普通使用人员”或任意提权
            if (role != Roles.Admin && role != Roles.DeptAdmin)
                return (false, "只能创建全局管理员或部门管理员账号。");

            var normalized = AuthService.NormalizeUserName(userName);
            if (!UserNamePattern.IsMatch(normalized))
                return (false, "用户名需为 3-20 位小写字母或数字且以字母开头（例如 zhangsan）。");

            if (string.IsNullOrEmpty(password) || password.Length < AuthService.MinPasswordLength)
                return (false, $"初始密码至少 {AuthService.MinPasswordLength} 位。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            if (await db.AppUsers.AnyAsync(u => u.UserName == normalized))
                return (false, "该用户名已被占用，请换一个。");

            int? deptId = null;
            if (role == Roles.DeptAdmin)
            {
                if (departmentId is not int dept || dept <= 0)
                    return (false, "请选择该部门管理员所属部门。");
                if (!await db.Departments.AnyAsync(d => d.Id == dept))
                    return (false, "所选部门不存在。");
                if (await db.AppUsers.AnyAsync(u => u.Role == Roles.DeptAdmin && u.IsActive && u.DepartmentId == dept))
                    return (false, "该部门已有管理员账号，如需更换请重置密码或先停用。");
                deptId = dept;
            }

            var user = new AppUser
            {
                UserName = normalized,
                PasswordHash = _authService.HashPassword(password),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                Role = role,
                DepartmentId = deptId,
                PersonnelId = null,          // 后台账号不绑定车间人员
                IsActive = true,
                CreatedAt = ChinaTime.Now
            };

            db.AppUsers.Add(user);
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.Create", "AppUser", user.Id,
                $"用户名={normalized}，角色={role}，部门Id={(deptId?.ToString() ?? "-")}，来源=账号管理页");

            return (true, null);
        }

        /// <summary>
        /// 修正后台账号信息：显示名可改；部门仅部门管理员可调（全局管理员必须保持全量视野）。
        /// 用户名不开放修改：它是登录凭据，已被审计日志与登录取证引用。
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateBackOfficeAccountAsync(
            int userId, string displayName, int? departmentId)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");
            if (!me.IsAdmin)
                return (false, "仅全局管理员可修改后台账号。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "账号不存在。");
            if (user.Role != Roles.Admin && user.Role != Roles.DeptAdmin)
                return (false, "只能修改全局管理员或部门管理员账号。");

            if (user.Role == Roles.DeptAdmin)
            {
                if (departmentId is not int dept || dept <= 0)
                    return (false, "请选择该部门管理员所属部门。");
                if (!await db.Departments.AnyAsync(d => d.Id == dept))
                    return (false, "所选部门不存在。");

                // 目标部门不得已有其他启用中的部门管理员
                if (dept != user.DepartmentId &&
                    await db.AppUsers.AnyAsync(u => u.Role == Roles.DeptAdmin && u.IsActive &&
                                                    u.DepartmentId == dept && u.Id != user.Id))
                    return (false, "该部门已有管理员账号，如需更换请重置密码或先停用。");

                user.DepartmentId = dept;
            }

            user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.Update", "AppUser", user.Id,
                $"用户名={user.UserName}，显示名={user.DisplayName ?? "-"}，部门Id={(user.DepartmentId?.ToString() ?? "-")}");

            return (true, null);
        }

        /// <summary>重置口令：Admin 可重置任意账号（含后台账号与自己的改密）；其余角色仅限本部门普通使用人员。</summary>
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
            if (!CanResetPassword(me, user))
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

        /// <summary>
        /// 启用/停用账号（仅 Admin）：不改口令；被「删除账号」随机化过口令的账号启用后需再重置密码才能登录。
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SetActiveAsync(int userId, bool isActive)
        {
            var me = await _currentUser.GetAsync();
            if (!me.IsAuthenticated)
                return (false, "登录状态已失效，请重新登录。");
            if (!me.IsAdmin)
                return (false, "仅全局管理员可启用/停用账号。");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return (false, "账号不存在。");
            if (user.Id == me.UserId)
                return (false, "不能启用/停用当前登录的账号。");
            if (user.IsActive == isActive)
                return (true, null);

            // 防锁死：最后一个启用中的 Admin 不允许停用
            if (!isActive && user.Role == Roles.Admin)
            {
                var activeAdmins = await db.AppUsers.CountAsync(u => u.Role == Roles.Admin && u.IsActive);
                if (activeAdmins <= 1)
                    return (false, "系统必须保留至少一个启用中的全局管理员。");
            }

            user.IsActive = isActive;
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("User.SetActive", "AppUser", user.Id,
                $"用户名={user.UserName}，状态={(isActive ? "启用" : "停用")}");

            return (true, null);
        }

        /// <summary>可重置口令判定：Admin 可重置任意账号（含自己，即改密）；其余角色仅限本部门普通使用人员。</summary>
        private static bool CanResetPassword(UserIdentity me, AppUser user)
            => me.IsAdmin || CanManage(me, user);

        /// <summary>可管理判定：目标是普通使用人员且部门在调用者范围内（Admin 恒为 true）。</summary>
        private static bool CanManage(UserIdentity me, AppUser user)
            => user.Role == Roles.User && me.CanAccessDepartment(user.DepartmentId ?? 0);
    }
}

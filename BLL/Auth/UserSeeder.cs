using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>
    /// 账号初始化种子（幂等、只 INSERT 不 UPDATE）：admin → 部门管理员 → 在职人员按拼音建号。
    /// 仅作为首次部署的引导；后续新增部门/同级管理员一律走「账号管理」页（/accounts），不再靠改代码或重启。
    /// </summary>
    public static class UserSeeder
    {
        private const string AdminUserName = "admin";
        private const string AdminPassword = "P@ssw0rd";
        private const string DefaultPassword = "123456";

        /// <summary>用户名最大长度（D1 定案）；消歧后缀加完也不得超过它。</summary>
        private const int MaxUserNameLength = 20;

        /// <summary>
        /// 部门管理员固定映射，仅用于首次部署引导。
        /// <b>按部门名精确匹配，绝不硬编码 Id</b> —— 生产库是 3/6/7，开发库可能不同。
        /// 新增部门请到「账号管理」页开通，不要往这里加硬编码。
        /// </summary>
        private static readonly Dictionary<string, string> DeptAdminMap = new()
        {
            ["信息安防技术室"] = "anfang",
            ["信息设备维护室"] = "shebei",
            ["信息网络通信室"] = "wangluo",
        };

        /// <summary>
        /// 存量人员姓名 → 拼音（人工核对过，多音字按姓氏读音校正）。不引拼音库，见 D1。
        /// </summary>
        private static readonly Dictionary<string, string> PinyinMap = new()
        {
            ["马存宝"] = "macunbao",
            ["尹辰璐"] = "yinchenlu",
            ["徐哲"] = "xuzhe",
            ["刘华琢"] = "liuhuazhuo",
            ["卜忠义"] = "buzhongyi",   // 卜 作姓读 bǔ
            ["杨忠昊"] = "yangzhonghao",
            ["胡克汉"] = "hukehan",
            ["马驰"] = "machi",
            ["孟浩宇"] = "menghaoyu",
        };

        /// <summary>
        /// 幂等播种。必须在 <see cref="SchemaBootstrapper.ApplyAsync"/> 建表之后、任何登录发生之前调用。
        /// </summary>
        public static async Task EnsureSeedUsersAsync(
            IDbContextFactory<VehicleDbContext> factory,
            IPasswordHasher<AppUser> hasher,
            ILogger log)
        {
            await using var db = await factory.CreateDbContextAsync();
            var created = 0;

            // 1) 全局管理员：不存在才建，已存在绝不动口令
            if (!await db.AppUsers.AnyAsync(u => u.UserName == AdminUserName))
            {
                db.AppUsers.Add(NewUser(AdminUserName, "系统管理员", Roles.Admin, null, null, AdminPassword, hasher));
                created++;
                log.LogWarning("已创建全局管理员 {User}，初始口令 {Pwd}（D3：不强制首登改密，建议尽快人工修改）",
                    AdminUserName, AdminPassword);
            }
            else
            {
                await WarnIfDefaultAdminPasswordAsync(db, hasher, log);
            }

            // 2) 三个部门管理员：按部门名匹配
            foreach (var (deptName, userName) in DeptAdminMap)
            {
                if (await db.AppUsers.AnyAsync(u => u.UserName == userName))
                    continue;

                var dept = await db.Departments.AsNoTracking()
                                    .FirstOrDefaultAsync(d => d.Name == deptName);
                if (dept is null)
                {
                    log.LogWarning("部门「{Dept}」不存在，跳过部门管理员账号 {User}", deptName, userName);
                    continue;
                }

                db.AppUsers.Add(NewUser(userName, deptName, Roles.DeptAdmin, dept.Id, null, DefaultPassword, hasher));
                created++;
            }

            // 3) 存量在职人员 → 普通使用人员账号（已绑定、已停用的跳过）
            var personnel = await db.Personnel.AsNoTracking().ToListAsync();
            var boundIds = await db.AppUsers
                                    .Where(u => u.PersonnelId != null)
                                    .Select(u => u.PersonnelId!.Value)
                                    .ToListAsync();
            var takenNames = (await db.AppUsers.Select(u => u.UserName).ToListAsync()).ToHashSet();

            foreach (var p in personnel.Where(p => p.IsActive && !boundIds.Contains(p.Id)))
            {
                if (!PinyinMap.TryGetValue(p.Name, out var baseName))
                {
                    baseName = $"p{p.Id}";
                    log.LogWarning("人员「{Name}」缺少拼音映射，回退为用户名 {User}", p.Name, baseName);
                }

                var userName = MakeUnique(baseName.ToLowerInvariant(), takenNames);
                takenNames.Add(userName);
                db.AppUsers.Add(NewUser(userName, p.Name, Roles.User, p.DepartmentId, p.Id, DefaultPassword, hasher));
                created++;
            }

            if (created > 0)
            {
                // 单次提交：三批账号要么全成要么全不成，绝不出现「建了 admin 没建人员」的半成品
                await db.SaveChangesAsync();
                log.LogWarning("账号初始化完成：本次创建 {Count} 个账号；人员初始口令 {Pwd}，请尽快修改", created, DefaultPassword);
            }
        }

        /// <summary>
        /// D3 定案：不强制改密，但每次启动检查 admin 是否仍是出厂口令，留一条 Warning 便于事后追溯。
        /// </summary>
        private static async Task WarnIfDefaultAdminPasswordAsync(
            VehicleDbContext db, IPasswordHasher<AppUser> hasher, ILogger log)
        {
            var admin = await db.AppUsers.AsNoTracking()
                                 .FirstOrDefaultAsync(u => u.UserName == AdminUserName);
            if (admin is null)
                return;

            var result = hasher.VerifyHashedPassword(admin, admin.PasswordHash, AdminPassword);
            if (result != PasswordVerificationResult.Failed)
                log.LogWarning("全局管理员 {User} 仍在使用出厂口令，建议尽快修改", AdminUserName);
        }

        private static AppUser NewUser(
            string userName, string displayName, string role,
            int? departmentId, int? personnelId, string password, IPasswordHasher<AppUser> hasher)
        {
            var user = new AppUser
            {
                UserName = AuthService.NormalizeUserName(userName),
                DisplayName = displayName,
                Role = role,
                DepartmentId = departmentId,
                PersonnelId = personnelId,
                IsActive = true,
                CreatedAt = vehicleManagement.DAL.Common.ChinaTime.Now
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            return user;
        }

        /// <summary>同名消歧（D1）：基名被占用则试 name1、name2 …，整串长度不超过 20。</summary>
        private static string MakeUnique(string baseName, ICollection<string> taken)
        {
            if (baseName.Length > MaxUserNameLength)
                baseName = baseName[..MaxUserNameLength];

            if (!taken.Contains(baseName))
                return baseName;

            for (var i = 1; i <= 99; i++)
            {
                var suffix = i.ToString();
                var head = baseName.Length + suffix.Length > MaxUserNameLength
                           ? baseName[..(MaxUserNameLength - suffix.Length)]
                           : baseName;
                var candidate = head + suffix;
                if (!taken.Contains(candidate))
                    return candidate;
            }

            throw new InvalidOperationException($"无法为 {baseName} 生成唯一用户名（后缀已到 99）");
        }
    }
}

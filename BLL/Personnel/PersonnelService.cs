using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Personnel
{
    /// <summary>
    /// 人员服务实现
    /// </summary>
    public class PersonnelService
    {
        private readonly PersonnelRepository _personnelRepo;
        private readonly DepartmentRepository _deptRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly ICurrentUser _currentUser;
        private readonly AuditService _auditService;

        public PersonnelService(
            PersonnelRepository personnelRepo,
            DepartmentRepository deptRepo,
            IDbContextFactory<VehicleDbContext> dbFactory,
            ICurrentUser currentUser,
            AuditService auditService)
        {
            _personnelRepo = personnelRepo;
            _deptRepo = deptRepo;
            _dbFactory = dbFactory;
            _currentUser = currentUser;
            _auditService = auditService;
        }

        /// <summary>
        /// 分页查询人员列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Personnel>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            IQueryable<Models.Personnel> query = db.Personnel.Include(p => p.Department);

            // 部门隔离：Admin 为 null（不限），其余只能看本部门
            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                query = query.Where(p => p.DepartmentId == scopedDeptId);

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(p => p.Name.Contains(kw) || (p.Phone != null && p.Phone.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "name" => sortAscending
                    ? query.OrderBy(p => p.Name).ThenBy(p => p.Id)
                    : query.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id),
                "phone" => sortAscending
                    ? query.OrderBy(p => p.Phone).ThenBy(p => p.Id)
                    : query.OrderByDescending(p => p.Phone).ThenByDescending(p => p.Id),
                _ => sortAscending
                    ? query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
                    : query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            };

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 获取所有部门列表
        /// </summary>
        public async Task<List<Models.Department>> GetAllDepartmentsAsync()
        {
            return (await _deptRepo.GetAllAsync()).ToList();
        }

        /// <summary>
        /// 根据ID获取人员
        /// </summary>
        public async Task<Models.Personnel> GetByIdAsync(int id)
        {
            return await _personnelRepo.GetByIdAsync(id);
        }

        /// <summary>
        /// 创建人员
        /// </summary>
        public async Task<Models.Personnel> CreateAsync(int departmentId, string name, string phone, bool isActive)
        {
            // 部门管理员新建人员强制落在本部门，忽略前端传入的部门
            var me = await _currentUser.GetAsync();
            if (me.ScopeDepartmentId is int scopedDeptId)
                departmentId = scopedDeptId;

            var personnel = new Models.Personnel
            {
                DepartmentId = departmentId,
                Name = name.Trim(),
                Phone = phone?.Trim(),
                IsActive = isActive,
                CreatedAt = ChinaTime.Now
            };
            var created = await _personnelRepo.AddAsync(personnel);
            await _auditService.WriteAsync("Personnel.Create", "Personnel", created.Id,
                $"姓名={created.Name}，部门Id={created.DepartmentId}");
            return created;
        }

        /// <summary>
        /// 更新人员
        /// </summary>
        public async Task<Models.Personnel> UpdateAsync(int id, int departmentId, string name, string phone, bool isActive)
        {
            var personnel = await _personnelRepo.GetByIdAsync(id);
            if (personnel == null) return null;

            var me = await _currentUser.GetAsync();
            if (!me.CanAccessDepartment(personnel.DepartmentId) || !me.CanAccessDepartment(departmentId))
                return null;

            personnel.DepartmentId = me.ScopeDepartmentId ?? departmentId;
            personnel.Name = name.Trim();
            personnel.Phone = phone?.Trim();
            personnel.IsActive = isActive;
            personnel.UpdatedAt = ChinaTime.Now;

            var updated = await _personnelRepo.UpdateAsync(personnel);
            await _auditService.WriteAsync("Personnel.Update", "Personnel", id,
                $"姓名={updated.Name}，部门Id={updated.DepartmentId}");
            return updated;
        }

        /// <summary>
        /// 删除人员（含业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            var me = await _currentUser.GetAsync();
            await using var db = await _dbFactory.CreateDbContextAsync();

            var target = await db.Personnel.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (target == null) return (false, "删除失败：人员不存在。");
            if (!me.CanAccessDepartment(target.DepartmentId)) return (false, "无权操作该部门数据。");

            // 为一个布尔判断，原先拉全库使用记录 + 5 层 Include
            if (await db.VehicleUsageRecords.AnyAsync(r => r.PersonnelId == id))
                return (false, "无法删除该人员：该人员还有使用记录，请先删除相关使用记录。");

            var personnelName = target.Name;

            // 删人员 + 禁用其账号，同一 DbContext 单次 SaveChanges（同进同退）
            var personnel = await db.Personnel.FirstOrDefaultAsync(p => p.Id == id);
            if (personnel == null)
                return (false, "删除失败：人员不存在。");

            // 账号只禁用不硬删（审计日志按用户名引用）
            var accounts = await db.AppUsers.Where(u => u.PersonnelId == id).ToListAsync();
            foreach (var account in accounts)
                account.IsActive = false;

            db.Personnel.Remove(personnel);
            await db.SaveChangesAsync();

            await _auditService.WriteAsync("Personnel.Delete", "Personnel", id,
                $"姓名={personnelName}，联动禁用账号数={accounts.Count}");

            return (true, null);
        }
    }
}

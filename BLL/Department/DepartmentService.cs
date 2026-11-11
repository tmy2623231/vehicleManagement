using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.BLL.Common;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.BLL.Department
{
    /// <summary>
    /// 部门服务实现
    /// </summary>
    public class DepartmentService
    {
        private readonly DepartmentRepository _deptRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly AuditService _auditService;

        public DepartmentService(
            DepartmentRepository deptRepo,
            IDbContextFactory<VehicleDbContext> dbFactory,
            AuditService auditService)
        {
            _deptRepo = deptRepo;
            _dbFactory = dbFactory;
            _auditService = auditService;
        }

        /// <summary>
        /// 分页查询部门列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Department>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = db.Departments.AsQueryable();

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(d => d.Name.Contains(kw) || (d.Description != null && d.Description.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "name" => sortAscending
                    ? query.OrderBy(d => d.Name).ThenBy(d => d.Id)
                    : query.OrderByDescending(d => d.Name).ThenByDescending(d => d.Id),
                "desc" => sortAscending
                    ? query.OrderBy(d => d.Description).ThenBy(d => d.Id)
                    : query.OrderByDescending(d => d.Description).ThenByDescending(d => d.Id),
                _ => sortAscending
                    ? query.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
                    : query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            };

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 根据ID获取部门
        /// </summary>
        public async Task<Models.Department> GetByIdAsync(int id)
        {
            return await _deptRepo.GetByIdAsync(id);
        }

        /// <summary>
        /// 创建部门
        /// </summary>
        public async Task<Models.Department> CreateAsync(string name, string description)
        {
            var dept = new Models.Department
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                CreatedAt = ChinaTime.Now
            };
            return await _deptRepo.AddAsync(dept);
        }

        /// <summary>
        /// 更新部门
        /// </summary>
        public async Task<Models.Department> UpdateAsync(int id, string name, string description)
        {
            var dept = await _deptRepo.GetByIdAsync(id);
            if (dept == null) return null;

            dept.Name = name.Trim();
            dept.Description = description?.Trim();
            dept.UpdatedAt = ChinaTime.Now;

            return await _deptRepo.UpdateAsync(dept);
        }

        /// <summary>
        /// 删除部门（含业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            // 原先三次全表加载（人员/车辆/使用记录）只为三个布尔判断
            await using var db = await _dbFactory.CreateDbContextAsync();

            var target = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (target == null) return (false, "删除失败：部门不存在。");

            if (await db.Personnel.AnyAsync(p => p.DepartmentId == id))
                return (false, "无法删除该部门：该部门下还有人员记录，请先移除或转移相关人员。");
            if (await db.Vehicles.AnyAsync(v => v.DepartmentId == id))
                return (false, "无法删除该部门：该部门下还有车辆记录，请先移除或转移相关车辆。");
            if (await db.VehicleUsageRecords.AnyAsync(r => r.DepartmentId == id))
                return (false, "无法删除该部门：该部门下还有使用记录，请先删除相关使用记录。");

            // 执行删除
            var result = await _deptRepo.DeleteAsync(id);
            if (!result) return (false, "删除失败：部门不存在。");

            await _auditService.WriteAsync("Department.Delete", "Department", id, $"名称={target.Name}");
            return (true, null);
        }
    }
}

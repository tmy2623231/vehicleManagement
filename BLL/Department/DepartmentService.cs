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
        private readonly PersonnelRepository _personnelRepo;
        private readonly VehicleRepository _vehicleRepo;
        private readonly UsageRecordRepository _recordRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public DepartmentService(
            DepartmentRepository deptRepo,
            PersonnelRepository personnelRepo,
            VehicleRepository vehicleRepo,
            UsageRecordRepository recordRepo,
            IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _deptRepo = deptRepo;
            _personnelRepo = personnelRepo;
            _vehicleRepo = vehicleRepo;
            _recordRepo = recordRepo;
            _dbFactory = dbFactory;
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
                "name" => sortAscending ? query.OrderBy(d => d.Name) : query.OrderByDescending(d => d.Name),
                "desc" => sortAscending ? query.OrderBy(d => d.Description) : query.OrderByDescending(d => d.Description),
                _ => sortAscending ? query.OrderBy(d => d.CreatedAt) : query.OrderByDescending(d => d.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            if (page > 1 && page > (int)Math.Ceiling((double)totalCount / pageSize))
                page = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Models.Department>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
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
            dept.UpdatedAt = DateTime.Now;

            return await _deptRepo.UpdateAsync(dept);
        }

        /// <summary>
        /// 删除部门（含业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            // 校验1：该部门下是否有人员
            var personnel = await _personnelRepo.GetByDepartmentIdAsync(id);
            if (personnel.Any())
            {
                return (false, "无法删除该部门：该部门下还有人员记录，请先移除或转移相关人员。");
            }

            // 校验2：该部门下是否有车辆
            var vehicles = await _vehicleRepo.GetAllAsync();
            if (vehicles.Any(v => v.DepartmentId == id))
            {
                return (false, "无法删除该部门：该部门下还有车辆记录，请先移除或转移相关车辆。");
            }

            // 校验3：该部门下是否有使用记录
            var allRecords = await _recordRepo.GetAllAsync();
            if (allRecords.Any(r => r.DepartmentId == id))
            {
                return (false, "无法删除该部门：该部门下还有使用记录，请先删除相关使用记录。");
            }

            // 执行删除
            var result = await _deptRepo.DeleteAsync(id);
            return result ? (true, null) : (false, "删除失败：部门不存在。");
        }
    }
}

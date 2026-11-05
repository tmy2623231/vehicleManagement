using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        private readonly UsageRecordRepository _recordRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;

        public PersonnelService(
            PersonnelRepository personnelRepo,
            DepartmentRepository deptRepo,
            UsageRecordRepository recordRepo,
            IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _personnelRepo = personnelRepo;
            _deptRepo = deptRepo;
            _recordRepo = recordRepo;
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// 分页查询人员列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Personnel>> GetPagedAsync(
            string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            IQueryable<Models.Personnel> query = db.Personnel.Include(p => p.Department);

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(p => p.Name.Contains(kw) || (p.Phone != null && p.Phone.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "name" => sortAscending ? query.OrderBy(p => p.Name) : query.OrderByDescending(p => p.Name),
                "phone" => sortAscending ? query.OrderBy(p => p.Phone) : query.OrderByDescending(p => p.Phone),
                _ => sortAscending ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            if (page > 1 && page > (int)Math.Ceiling((double)totalCount / pageSize))
                page = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Models.Personnel>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
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
            var personnel = new Models.Personnel
            {
                DepartmentId = departmentId,
                Name = name.Trim(),
                Phone = phone?.Trim(),
                IsActive = isActive,
                CreatedAt = ChinaTime.Now
            };
            return await _personnelRepo.AddAsync(personnel);
        }

        /// <summary>
        /// 更新人员
        /// </summary>
        public async Task<Models.Personnel> UpdateAsync(int id, int departmentId, string name, string phone, bool isActive)
        {
            var personnel = await _personnelRepo.GetByIdAsync(id);
            if (personnel == null) return null;

            personnel.DepartmentId = departmentId;
            personnel.Name = name.Trim();
            personnel.Phone = phone?.Trim();
            personnel.IsActive = isActive;
            personnel.UpdatedAt = ChinaTime.Now;

            return await _personnelRepo.UpdateAsync(personnel);
        }

        /// <summary>
        /// 删除人员（含业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            // 校验：该人员是否有使用记录
            var allRecords = await _recordRepo.GetAllAsync();
            if (allRecords.Any(r => r.PersonnelId == id))
            {
                return (false, "无法删除该人员：该人员还有使用记录，请先删除相关使用记录。");
            }

            var result = await _personnelRepo.DeleteAsync(id);
            return result ? (true, null) : (false, "删除失败：人员不存在。");
        }
    }
}

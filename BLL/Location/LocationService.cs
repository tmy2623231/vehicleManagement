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

namespace vehicleManagement.BLL.Location
{
    /// <summary>
    /// 地点服务实现
    /// </summary>
    public class LocationService
    {
        private readonly LocationRepository _locationRepo;
        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly AuditService _auditService;

        public LocationService(LocationRepository locationRepo, IDbContextFactory<VehicleDbContext> dbFactory,
            AuditService auditService)
        {
            _locationRepo = locationRepo;
            _dbFactory = dbFactory;
            _auditService = auditService;
        }

        /// <summary>
        /// 按分类分页查询地点列表（支持搜索和排序）
        /// </summary>
        public async Task<PagedResult<Models.Location>> GetPagedByCategoryAsync(
            Models.LocationCategory category, string searchKeyword, string sortColumn, bool sortAscending, int page, int pageSize)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = ((IQueryable<Models.Location>)db.Locations).Where(l => l.Category == category);

            // 搜索过滤
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim();
                query = query.Where(l => l.Name.Contains(kw) || (l.Description != null && l.Description.Contains(kw)));
            }

            // 排序
            query = sortColumn switch
            {
                "name" => sortAscending
                    ? query.OrderBy(l => l.Name).ThenBy(l => l.Id)
                    : query.OrderByDescending(l => l.Name).ThenByDescending(l => l.Id),
                "desc" => sortAscending
                    ? query.OrderBy(l => l.Description).ThenBy(l => l.Id)
                    : query.OrderByDescending(l => l.Description).ThenByDescending(l => l.Id),
                _ => sortAscending
                    ? query.OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                    : query.OrderByDescending(l => l.SortOrder).ThenByDescending(l => l.Id)
            };

            pageSize = PagedResult.ClampPageSize(pageSize);
            var totalCount = await query.CountAsync();
            page = PagedResult.ClampPage(page, totalCount, pageSize);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return PagedResult.Of(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// 根据ID获取地点
        /// </summary>
        public async Task<Models.Location> GetByIdAsync(int id)
        {
            return await _locationRepo.GetByIdAsync(id);
        }

        /// <summary>
        /// 创建地点
        /// </summary>
        public async Task<Models.Location> CreateAsync(Models.LocationCategory category, string name, string description, int sortOrder, bool isActive)
        {
            var location = new Models.Location
            {
                Category = category,
                Name = name.Trim(),
                Description = description?.Trim(),
                SortOrder = sortOrder,
                IsActive = isActive,
                CreatedAt = ChinaTime.Now
            };
            return await _locationRepo.AddAsync(location);
        }

        /// <summary>
        /// 更新地点
        /// </summary>
        public async Task<Models.Location> UpdateAsync(int id, string name, string description, int sortOrder, bool isActive)
        {
            var location = await _locationRepo.GetByIdAsync(id);
            if (location == null) return null;

            location.Name = name.Trim();
            location.Description = description?.Trim();
            location.SortOrder = sortOrder;
            location.IsActive = isActive;

            return await _locationRepo.UpdateAsync(location);
        }

        /// <summary>
        /// 删除地点（含业务校验）
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var target = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (target == null) return (false, "删除失败：地点不存在。");

            // 校验1：是否被使用记录作为最终停车位置引用
            var hasFinalRecords = await ((IQueryable<Models.VehicleUsageRecord>)db.VehicleUsageRecords).AnyAsync(r => r.FinalLocationId == id);
            if (hasFinalRecords)
            {
                return (false, "无法删除该地点：该地点已被使用记录作为最终停车位置引用，请先删除相关使用记录。");
            }

            // 校验2：是否被使用记录作为途经路径点引用
            var hasPurposeRecords = await ((IQueryable<Models.VehicleUsageRecordPurpose>)db.VehicleUsageRecordPurposes).AnyAsync(p => p.PurposeId == id);
            if (hasPurposeRecords)
            {
                return (false, "无法删除该地点：该地点已被使用记录作为途经路径点引用，请先删除相关使用记录。");
            }

            var result = await _locationRepo.DeleteAsync(id);
            if (!result) return (false, "删除失败：地点不存在。");

            await _auditService.WriteAsync("Location.Delete", "Location", id,
                $"分类={target.Category}，名称={target.Name}");
            return (true, null);
        }

        /// <summary>
        /// 获取所有启用的途经路径点
        /// </summary>
        public async Task<List<Models.Location>> GetActiveWaypointsAsync()
        {
            // 活跃过滤下沉到 SQL，不在内存里 .Where
            return (await _locationRepo.GetByCategoryAsync(Models.LocationCategory.Location, activeOnly: true)).ToList();
        }

        /// <summary>
        /// 获取所有启用的停车位置
        /// </summary>
        public async Task<List<Models.Location>> GetActiveFinalLocationsAsync()
        {
            return (await _locationRepo.GetByCategoryAsync(Models.LocationCategory.Purpose, activeOnly: true)).ToList();
        }
    }
}

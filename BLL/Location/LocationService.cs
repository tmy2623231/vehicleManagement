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

        public LocationService(LocationRepository locationRepo, IDbContextFactory<VehicleDbContext> dbFactory)
        {
            _locationRepo = locationRepo;
            _dbFactory = dbFactory;
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
                "name" => sortAscending ? query.OrderBy(l => l.Name) : query.OrderByDescending(l => l.Name),
                "desc" => sortAscending ? query.OrderBy(l => l.Description) : query.OrderByDescending(l => l.Description),
                _ => sortAscending ? query.OrderBy(l => l.SortOrder).ThenBy(l => l.Id) : query.OrderByDescending(l => l.SortOrder).ThenByDescending(l => l.Id)
            };

            var totalCount = await query.CountAsync();
            if (page > 1 && page > (int)Math.Ceiling((double)totalCount / pageSize))
                page = (int)Math.Ceiling((double)totalCount / pageSize);
            if (page < 1) page = 1;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Models.Location>
            {
                Items = items,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = pageSize
            };
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
            return result ? (true, null) : (false, "删除失败：地点不存在。");
        }

        /// <summary>
        /// 获取所有启用的途经路径点
        /// </summary>
        public async Task<List<Models.Location>> GetActiveWaypointsAsync()
        {
            return (await _locationRepo.GetByCategoryAsync(Models.LocationCategory.Location))
                .Where(l => l.IsActive)
                .ToList();
        }

        /// <summary>
        /// 获取所有启用的停车位置
        /// </summary>
        public async Task<List<Models.Location>> GetActiveFinalLocationsAsync()
        {
            return (await _locationRepo.GetByCategoryAsync(Models.LocationCategory.Purpose))
                .Where(l => l.IsActive)
                .ToList();
        }
    }
}

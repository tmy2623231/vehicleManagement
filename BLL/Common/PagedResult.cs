using System;
using System.Collections.Generic;

namespace vehicleManagement.BLL.Common
{
    /// <summary>
    /// 通用分页结果封装
    /// </summary>
    /// <typeparam name="T">数据项类型</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// 当前页的数据列表
        /// </summary>
        public List<T> Items { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>总页数（PageSize 未设置时回 1，避免除零）。</summary>
        public int TotalPages => TotalCount > 0 && PageSize > 0
            ? (int)System.Math.Ceiling((double)TotalCount / PageSize)
            : 1;

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }
    }

    /// <summary>分页参数收敛辅助，统一各 GetPagedAsync 的页边界处理。</summary>
    public static class PagedResult
    {
        public const int MaxPageSize = 100;

        /// <summary>页大小硬上限：防止 pageSize=100000 把整库拉进内存。</summary>
        public static int ClampPageSize(int pageSize) => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, MaxPageSize);

        /// <summary>页码收敛：空结果回第 1 页，超界回最后一页；内部先收敛 pageSize 防除零。</summary>
        public static int ClampPage(int page, int totalCount, int pageSize)
        {
            pageSize = ClampPageSize(pageSize);
            return totalCount <= 0
                ? 1
                : Math.Clamp(page, 1, (int)Math.Ceiling((double)totalCount / pageSize));
        }

        public static PagedResult<T> Of<T>(List<T> items, int totalCount, int page, int pageSize)
            => new() { Items = items, TotalCount = totalCount, CurrentPage = page, PageSize = pageSize };
    }
}

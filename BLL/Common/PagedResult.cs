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

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => TotalCount > 0 ? (int)System.Math.Ceiling((double)TotalCount / PageSize) : 1;

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }
    }
}

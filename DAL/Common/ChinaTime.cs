using System;

namespace vehicleManagement.DAL.Common
{
    /// <summary>
    /// 北京时间（UTC+8）帮助类：
    /// Docker 容器/服务器时区通常为 UTC，DateTime.Now 会取到 UTC 时间，
    /// 统一通过 UTC+8 计算北京时间，保证数据库存取的时间与中国时间一致
    /// </summary>
    public static class ChinaTime
    {
        /// <summary>
        /// 当前北京时间（无时区依赖，任何服务器环境下结果一致）
        /// </summary>
        public static DateTime Now => DateTime.UtcNow.AddHours(8);
    }
}

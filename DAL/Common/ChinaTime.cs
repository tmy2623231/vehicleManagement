using System;

namespace vehicleManagement.DAL.Common
{
    /// <summary>北京时间（UTC+8）：全项目唯一的「当前时间」来源，替代 DateTime.Now。</summary>
    public static class ChinaTime
    {
        /// <summary>UTC+8 固定偏移（中国不实行夏令时）。</summary>
        public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

        /// <summary>当前北京时间：由 UTC 换算，与服务器/容器时区无关；Kind=Unspecified 以免被再次偏移。</summary>
        public static DateTime Now =>
            DateTime.SpecifyKind(DateTime.UtcNow.Add(Offset), DateTimeKind.Unspecified);
    }
}

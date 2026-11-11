using Serilog.Core;
using Serilog.Events;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.BLL.Common
{
    /// <summary>
    /// 日志时间戳转中国时间：附加 <c>ChinaTimestamp</c> 属性供模板渲染（容器时区为 UTC 时会差 8 小时）。
    /// </summary>
    public sealed class ChinaTimeEnricher : ILogEventEnricher
    {
        /// <summary>日志模板中引用的属性名。</summary>
        public const string PropertyName = "ChinaTimestamp";

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent.Properties.ContainsKey(PropertyName))
                return;

            var chinaTimestamp = logEvent.Timestamp.ToOffset(ChinaTime.Offset);
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, chinaTimestamp));
        }
    }
}

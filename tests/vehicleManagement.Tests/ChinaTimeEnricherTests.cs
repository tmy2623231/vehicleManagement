using System;
using System.Linq;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using vehicleManagement.BLL.Common;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>校验日志时间戳渲染：UTC 事件必须显示为中国时间。</summary>
    public class ChinaTimeEnricherTests
    {
        private sealed class StubPropertyFactory : ILogEventPropertyFactory
        {
            public LogEventProperty CreateProperty(string name, object value, bool destructureObjects = false)
                => new(name, new ScalarValue(value));
        }

        private static LogEvent CreateEvent(string timestampWithOffset)
            => new(DateTimeOffset.Parse(timestampWithOffset),
                   LogEventLevel.Information,
                   null,
                   new MessageTemplateParser().Parse("schema 引导完成"),
                   Enumerable.Empty<LogEventProperty>());

        [Fact]
        public void Enrich_UtcEvent_RendersAsChinaTime()
        {
            var logEvent = CreateEvent("2026-11-11T07:19:09+00:00");

            new ChinaTimeEnricher().Enrich(logEvent, new StubPropertyFactory());

            var value = (DateTimeOffset)((ScalarValue)logEvent.Properties[ChinaTimeEnricher.PropertyName]).Value;
            Assert.Equal(TimeSpan.FromHours(8), value.Offset);
            Assert.Equal(15, value.Hour);      // 07:19 UTC → 15:19
            Assert.Equal(19, value.Minute);
        }

        [Fact]
        public void Enrich_IsIdempotent_WhenCalledTwice()
        {
            var logEvent = CreateEvent("2026-11-11T07:19:09+00:00");
            var enricher = new ChinaTimeEnricher();

            enricher.Enrich(logEvent, new StubPropertyFactory());
            enricher.Enrich(logEvent, new StubPropertyFactory());

            var value = (DateTimeOffset)((ScalarValue)logEvent.Properties[ChinaTimeEnricher.PropertyName]).Value;
            Assert.Equal(15, value.Hour);      // 二次 enrich 不得累加
        }
    }
}

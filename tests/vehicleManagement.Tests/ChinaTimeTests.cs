using System;
using vehicleManagement.DAL.Common;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>校验时间源：时间必须是北京时间，且不随部署时区漂移。</summary>
    public class ChinaTimeTests
    {
        [Fact]
        public void Now_EqualsUtcPlusEight_NotProcessLocalTime()
        {
            var before = DateTime.UtcNow.AddHours(8);
            var chinaNow = ChinaTime.Now;
            var after = DateTime.UtcNow.AddHours(8);

            Assert.InRange(chinaNow, before.AddSeconds(-2), after.AddSeconds(2));
        }

        [Fact]
        public void Now_KindIsUnspecified_SoNoDownstreamDoubleShift()
            => Assert.Equal(DateTimeKind.Unspecified, ChinaTime.Now.Kind);

        [Fact]
        public void Offset_IsFixedPlusEight_NoDaylightSaving()
            => Assert.Equal(TimeSpan.FromHours(8), ChinaTime.Offset);
    }
}

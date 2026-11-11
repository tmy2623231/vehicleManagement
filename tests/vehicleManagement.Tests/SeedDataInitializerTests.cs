using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>P0-1 回归：参考数据播种必须幂等，且绝不能清库。</summary>
    public class SeedDataInitializerTests : System.IDisposable
    {
        private readonly TestDbFactory _factory = new();

        [Fact]
        public async Task EnsureReferenceDataAsync_RunTwice_DoesNotDuplicateLocations()
        {
            using (var db = _factory.CreateDbContext())
                await SeedDataInitializer.EnsureReferenceDataAsync(db);

            int afterFirst;
            using (var db = _factory.CreateDbContext())
                afterFirst = await db.Locations.CountAsync();

            Assert.Equal(2, afterFirst);   // 占位停车位置 + 占位途经地点

            using (var db = _factory.CreateDbContext())
                await SeedDataInitializer.EnsureReferenceDataAsync(db);

            using (var db = _factory.CreateDbContext())
            {
                Assert.Equal(afterFirst, await db.Locations.CountAsync());
                Assert.Equal(1, await db.Locations.CountAsync(l => l.Category == vehicleManagement.DAL.Model.LocationCategory.Purpose));
            }
        }

        [Fact]
        public async Task EnsureReferenceDataAsync_ExistingData_IsNotTouched()
        {
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                await SeedDataInitializer.EnsureReferenceDataAsync(db);
            }

            using (var db = _factory.CreateDbContext())
                Assert.Equal(2, await db.Locations.CountAsync());   // TestData 的 2 条原样保留
        }

        public void Dispose() => _factory.Dispose();
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL;

namespace vehicleManagement.Tests
{
    /// <summary>测试用 DbContext 工厂：全程持有一个打开的 :memory: 连接（连接关闭即销毁库），不用 EF InMemory provider。</summary>
    internal sealed class TestDbFactory : IDbContextFactory<VehicleDbContext>, IDisposable
    {
        private readonly SqliteConnection _conn = new("Data Source=:memory:");
        private readonly DbContextOptions<VehicleDbContext> _options;

        public TestDbFactory()
        {
            _conn.Open();
            _options = new DbContextOptionsBuilder<VehicleDbContext>().UseSqlite(_conn).Options;

            using var db = CreateDbContext();
            db.Database.EnsureCreated();
        }

        public VehicleDbContext CreateDbContext() => new(_options);

        // EF Core 的 IDbContextFactory 有同步 + 异步两个成员，缺一个就 CS0535
        public Task<VehicleDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());

        public void Dispose() => _conn.Dispose();
    }
}

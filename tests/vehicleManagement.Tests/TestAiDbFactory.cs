using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL;

namespace vehicleManagement.Tests
{
    /// <summary>AI 库测试工厂：:memory: SQLite，连接由字段持有，仅 Dispose 时释放。</summary>
    internal sealed class TestAiDbFactory : IDbContextFactory<AiServerDbContext>, IDisposable
    {
        private readonly SqliteConnection _conn = new("Data Source=:memory:");
        private readonly DbContextOptions<AiServerDbContext> _options;

        public TestAiDbFactory()
        {
            _conn.Open();
            _options = new DbContextOptionsBuilder<AiServerDbContext>().UseSqlite(_conn).Options;
            using var db = CreateDbContext();
            db.Database.EnsureCreated();
        }

        public AiServerDbContext CreateDbContext() => new(_options);

        public Task<AiServerDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());

        public void Dispose() => _conn.Dispose();
    }
}

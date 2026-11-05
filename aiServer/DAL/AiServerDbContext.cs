using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL.Model;

namespace vehicleManagement.aiServer.DAL
{
    /// <summary>
    /// AI 服务模块的独立数据库上下文
    /// </summary>
    public class AiServerDbContext : DbContext
    {
        public AiServerDbContext(DbContextOptions<AiServerDbContext> options)
            : base(options)
        {
        }

        public DbSet<ChatSession> ChatSessions { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<ChatMemory> ChatMemories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ChatSession 配置
            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ChatMessage 配置
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.CreatedAt);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.Session)
                      .WithMany(s => s.Messages)
                      .HasForeignKey(e => e.SessionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ChatMemory 配置
            modelBuilder.Entity<ChatMemory>(entity =>
            {
                entity.HasIndex(e => e.Key);
                entity.HasIndex(e => e.Category);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.Session)
                      .WithMany()
                      .HasForeignKey(e => e.SessionId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}

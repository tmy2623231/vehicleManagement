using Microsoft.EntityFrameworkCore;
using vehicleManagement.DAL.Model;

namespace vehicleManagement.DAL
{
    public class VehicleDbContext : DbContext
    {
        public VehicleDbContext(DbContextOptions<VehicleDbContext> options)
            : base(options)
        {
        }

        public DbSet<Department> Departments { get; set; }
        public DbSet<Personnel> Personnel { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<VehicleUsageRecord> VehicleUsageRecords { get; set; }
        public DbSet<VehicleUsageRecordPurpose> VehicleUsageRecordPurposes { get; set; }
        public DbSet<VehicleMaintenanceRecord> VehicleMaintenanceRecords { get; set; }

        // 账号与审计：表结构由 SchemaBootstrapper 幂等建立（新库靠模型、老库靠 DDL，两处必须同时有）
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Department 配置
            modelBuilder.Entity<Department>(entity =>
            {
            });

            // Personnel 配置
            modelBuilder.Entity<Personnel>(entity =>
            {
                entity.HasIndex(e => e.DepartmentId);
                // 列表页固定按「部门 + 启用」过滤
                entity.HasIndex(e => new { e.DepartmentId, e.IsActive });
            });

            // Vehicle 配置
            modelBuilder.Entity<Vehicle>(entity =>
            {
                entity.HasIndex(e => e.LicensePlate).IsUnique();
                entity.HasIndex(e => e.QrCodeToken).IsUnique();
                entity.HasIndex(e => e.DepartmentId);
                // 列表页固定按「状态 + 车牌」排序过滤
                entity.HasIndex(e => new { e.Status, e.LicensePlate });
            });

            // Location 配置
            modelBuilder.Entity<Location>(entity =>
            {
                entity.HasIndex(e => e.Category);
                // 按分类取启用地点时固定走「分类 + 启用 + 排序」
                entity.HasIndex(e => new { e.Category, e.IsActive, e.SortOrder });
            });

            // VehicleUsageRecord 配置
            modelBuilder.Entity<VehicleUsageRecord>(entity =>
            {
                entity.HasIndex(e => e.VehicleId);
                entity.HasIndex(e => e.DepartmentId);
                entity.HasIndex(e => e.CreatedAt);
                // 删除人员/地点前的引用校验（AnyAsync）与列表页排序
                entity.HasIndex(e => e.PersonnelId);
                entity.HasIndex(e => e.FinalLocationId);
                entity.HasIndex(e => new { e.DepartmentId, e.CreatedAt });
                entity.HasIndex(e => new { e.VehicleId, e.CreatedAt });

                entity.HasOne(e => e.Vehicle)
                      .WithMany()
                      .HasForeignKey(e => e.VehicleId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Department)
                      .WithMany()
                      .HasForeignKey(e => e.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Personnel)
                      .WithMany()
                      .HasForeignKey(e => e.PersonnelId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.FinalLocation)
                      .WithMany()
                      .HasForeignKey(e => e.FinalLocationId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // VehicleMaintenanceRecord 配置
            modelBuilder.Entity<VehicleMaintenanceRecord>(entity =>
            {
                entity.HasIndex(e => e.VehicleId);
                entity.HasIndex(e => e.MaintenanceDate);
                // 列表按「维修人所属部门」判定跨部门维修
                entity.HasIndex(e => e.MaintainerDepartmentId);

                entity.HasOne(e => e.Vehicle)
                      .WithMany()
                      .HasForeignKey(e => e.VehicleId)
                      .OnDelete(DeleteBehavior.Restrict);

                // 维修人 / 维修部门是登记时的身份快照，两者都可能为空（老库补列、Admin 登记）
                entity.HasOne(e => e.Maintainer)
                      .WithMany()
                      .HasForeignKey(e => e.MaintainerPersonnelId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.MaintainerDepartment)
                      .WithMany()
                      .HasForeignKey(e => e.MaintainerDepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // VehicleUsageRecordPurpose 配置
            modelBuilder.Entity<VehicleUsageRecordPurpose>(entity =>
            {
                entity.HasIndex(e => e.UsageRecordId);
                entity.HasIndex(e => new { e.UsageRecordId, e.SequenceOrder });

                entity.HasOne(e => e.UsageRecord)
                      .WithMany(r => r.UsageRecordPurposes)
                      .HasForeignKey(e => e.UsageRecordId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Purpose)
                      .WithMany()
                      .HasForeignKey(e => e.PurposeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // AppUser 配置：用户名唯一；人员绑定用部分唯一索引（NULL 不参与唯一约束）
            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasIndex(e => e.UserName).IsUnique();
                entity.HasIndex(e => e.PersonnelId).IsUnique().HasFilter("PersonnelId IS NOT NULL");
                entity.HasIndex(e => e.DepartmentId);
            });

            // AuditLog 配置：只追加，不加任何导航/级联
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.Action);
            });
        }
    }
}

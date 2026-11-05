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
            });

            // Vehicle 配置
            modelBuilder.Entity<Vehicle>(entity =>
            {
                entity.HasIndex(e => e.LicensePlate).IsUnique();
                entity.HasIndex(e => e.QrCodeToken).IsUnique();
                entity.HasIndex(e => e.DepartmentId);
            });

            // Location 配置
            modelBuilder.Entity<Location>(entity =>
            {
                entity.HasIndex(e => e.Category);
            });

            // VehicleUsageRecord 配置
            modelBuilder.Entity<VehicleUsageRecord>(entity =>
            {
                entity.HasIndex(e => e.VehicleId);
                entity.HasIndex(e => e.DepartmentId);
                entity.HasIndex(e => e.CreatedAt);

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

                entity.HasOne(e => e.Vehicle)
                      .WithMany()
                      .HasForeignKey(e => e.VehicleId)
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
        }
    }
}

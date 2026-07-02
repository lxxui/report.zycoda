using Microsoft.EntityFrameworkCore;
using report_zycoda.Models;
using static System.Collections.Specialized.BitVector32;

namespace report_zycoda.Data
{
    public class myDbContext : DbContext
    {
        internal object UserApiModels;

        public myDbContext(DbContextOptions<myDbContext> options) : base(options) { }

        //public DbSet<UserApiModels> Users { get; set; } = null!;
        public DbSet<UserApiModels> User { get; set; } = default!; 
        public DbSet<SectionApiModels> Section { get; set; } = null!;
        public DbSet<StatusApiModels> Statuses { get; set; } = null!;

        // ✅ 1. เอา // ออก เพื่อให้ Controller มองเห็น JobOrders
        //public DbSet<MaintenanceApiModels> JobOrders { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. ตาราง User
            modelBuilder.Entity<UserApiModels>(entity =>
            {
                entity.ToTable("User", "dbo");
            });

            // 2. ตาราง Section
            modelBuilder.Entity<SectionApiModels>(entity =>
            {
                entity.ToTable("Section", "dbo");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnType("int").ValueGeneratedOnAdd();
            });

        }
    }
}
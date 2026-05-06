using Microsoft.EntityFrameworkCore;
using report_zycoda.Models;

namespace report_zycoda.Data
{
    public class myDbContext : DbContext
    {
        public myDbContext(DbContextOptions<myDbContext> options) : base(options) { }

        public DbSet<UserApiModels> Users { get; set; } = null!;
        public DbSet<SectionApiModels> Sections { get; set; } = null!;
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

            // ✅ 2. เพิ่มการเชื่อมต่อกับตาราง job_order ในฐานข้อมูล
            //modelBuilder.Entity<MaintenanceApiModels>(entity =>
            //{
            //    entity.ToTable("job_order", "dbo"); // ชื่อตารางใน SQL ของคุณ
            //    entity.HasKey(e => e.id);           // กำหนด Primary Key
            //});
        }
    }
}
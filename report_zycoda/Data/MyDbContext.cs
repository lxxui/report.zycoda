using Microsoft.EntityFrameworkCore;
using report_zycoda.Models;

namespace report_zycoda.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<UserApiModels> Users { get; set; } = null!;
        public DbSet<SectionApiModels> Sections { get; set; } = null!;
        public DbSet<StatusApiModels> Statuses { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. ตาราง User
            modelBuilder.Entity<UserApiModels>(entity =>
            {
                entity.ToTable("User", "dbo");
                // ถ้าในตาราง User ของฝ้าย คอลัมน์ที่เก็บ "แผนก" ชื่อว่า Sections (มี s) 
                // แต่ใน Model ฝ้ายตั้งชื่อว่า Section (ไม่มี s) ให้ Map ตรงนี้ครับ
                // entity.Property(e => e.Section).HasColumnName("Sections");
            });

            // 2. ตาราง Section
            modelBuilder.Entity<SectionApiModels>(entity =>
            {
                entity.ToTable("Section", "dbo");

                // ระบุ Primary Key ให้ชัดเจน (ตามที่ฝ้ายใส่ [Key] ไว้ใน Model)
                entity.HasKey(e => e.Id);

                // ✅ เพิ่มจุดนี้: ป้องกันเรื่องข้อมูล "object" ที่เคย Error 
                // โดยการบอก Type ของ Id ให้ชัดเจนอีกทีในระดับ Fluent API
                entity.Property(e => e.Id).HasColumnType("int").ValueGeneratedOnAdd();
            });
        }
    }
}
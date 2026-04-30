using Microsoft.EntityFrameworkCore;
using report_zycoda.Models;

namespace report_zycoda.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<UserApiModels> Users { get; set; } = null!;
        public DbSet<SectionApiModels> Sections { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ ลองใส่ Schema "dbo" และครอบชื่อตารางด้วย [ ] เพื่อความชัวร์
            modelBuilder.Entity<UserApiModels>().ToTable("User", "dbo");
            modelBuilder.Entity<SectionApiModels>().ToTable("Section", "dbo");
        }
    }
}

 
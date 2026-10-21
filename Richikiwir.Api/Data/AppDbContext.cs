using Microsoft.EntityFrameworkCore;
using Richikiwir.Api.Models;

namespace Richikiwir.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<UserModel> Users => Set<UserModel>();
        public DbSet<AttendanceRecord> Attendances => Set<AttendanceRecord>();
        public DbSet<LogbookEntry> Logbooks => Set<LogbookEntry>();
        public DbSet<StudentInternSummary> Students => Set<StudentInternSummary>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User config
            modelBuilder.Entity<UserModel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Identifier).IsUnique();
            });

            // Attendance config
            modelBuilder.Entity<AttendanceRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.StudentNisn);
                entity.HasIndex(e => e.Timestamp);
            });

            // Logbook config
            modelBuilder.Entity<LogbookEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.StudentNisn);
            });

            // Student Summary config
            modelBuilder.Entity<StudentInternSummary>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Nisn).IsUnique();
            });
        }
    }
}

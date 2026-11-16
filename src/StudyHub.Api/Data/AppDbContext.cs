using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.DisplayName).HasMaxLength(100);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Material>(e =>
        {
            e.Property(m => m.Title).HasMaxLength(200);
            e.Property(m => m.CourseCode).HasMaxLength(20);
            e.HasIndex(m => m.CreatedAtUtc);

            e.HasOne(m => m.Author)
                .WithMany(u => u.Materials)
                .HasForeignKey(m => m.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);

            // Many-to-many: EF Core creates the join table for us.
            e.HasMany(m => m.Tags).WithMany(t => t.Materials);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(50);
            e.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.Property(r => r.Reason).HasMaxLength(500);

            e.HasOne(r => r.Material)
                .WithMany(m => m.Reports)
                .HasForeignKey(r => r.MaterialId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

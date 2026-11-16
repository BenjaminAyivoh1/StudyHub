using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<HelpfulVote> HelpfulVotes => Set<HelpfulVote>();
    public DbSet<Comment> Comments => Set<Comment>();

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

        modelBuilder.Entity<HelpfulVote>(e =>
        {
            // The key is the pair (note, student), so the database itself
            // stops one student voting twice on the same note.
            e.HasKey(v => new { v.MaterialId, v.UserId });

            e.HasOne(v => v.Material)
                .WithMany(m => m.HelpfulVotes)
                .HasForeignKey(v => v.MaterialId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Comment>(e =>
        {
            e.Property(c => c.Body).HasMaxLength(1000);

            // Comments are always read per note, oldest first.
            e.HasIndex(c => new { c.MaterialId, c.CreatedAtUtc });

            e.HasOne(c => c.Material)
                .WithMany(m => m.Comments)
                .HasForeignKey(c => c.MaterialId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

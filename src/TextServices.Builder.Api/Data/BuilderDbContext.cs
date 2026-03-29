using Microsoft.EntityFrameworkCore;

namespace TextServices.Builder.Api.Data;

public class BuilderDbContext(DbContextOptions<BuilderDbContext> options) : DbContext(options)
{
    public DbSet<BuilderJob> Jobs => Set<BuilderJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BuilderJob>(e =>
        {
            e.HasKey(j => j.Id);
            // Key may be up to ~500 characters (slash-delimited path).
            e.Property(j => j.Id).HasMaxLength(500);
            e.Property(j => j.Status).HasConversion<string>();
        });
    }
}

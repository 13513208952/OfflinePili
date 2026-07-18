using Microsoft.EntityFrameworkCore;

namespace BiliRestart.Core.Catalog;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogEntry> CatalogEntries => Set<CatalogEntry>();
    public DbSet<DanmakuSource> DanmakuSources => Set<DanmakuSource>();
    public DbSet<CommentSource> CommentSources => Set<CommentSource>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogEntry>(e =>
        {
            e.HasIndex(x => new { x.AvNumber, x.PartIndex }).IsUnique();
            e.Property(x => x.MetadataStatus).HasConversion<string>();
        });
        modelBuilder.Entity<DanmakuSource>(e =>
        {
            e.HasIndex(x => new { x.AvNumber, x.Cid }).IsUnique();
        });
        modelBuilder.Entity<CommentSource>(e =>
        {
            e.HasIndex(x => x.AvNumber).IsUnique();
        });
    }
}

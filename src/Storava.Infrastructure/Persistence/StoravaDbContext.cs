using Microsoft.EntityFrameworkCore;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// EF Core context for low-volume, rich data (settings, scan sessions, recommendations).
/// High-volume scan items are written through a dedicated batched writer (added in Phase 2)
/// using Microsoft.Data.Sqlite directly.
/// </summary>
public sealed class StoravaDbContext : DbContext
{
    public StoravaDbContext(DbContextOptions<StoravaDbContext> options) : base(options)
    {
    }

    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingEntity>(b =>
        {
            b.ToTable("Settings");
            b.HasKey(e => e.Key);
            b.Property(e => e.Key).HasMaxLength(128);
            b.Property(e => e.Value).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

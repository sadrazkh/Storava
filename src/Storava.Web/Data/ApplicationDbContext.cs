using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Storava.Web.Data;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();

    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    public DbSet<UsageLedgerEntry> UsageLedger => Set<UsageLedgerEntry>();

    public DbSet<DevicePairingCode> DevicePairingCodes => Set<DevicePairingCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(120);
            entity.Property(user => user.PlanCode).HasMaxLength(32);
            entity.HasIndex(user => user.CreatedAtUtc);
        });

        builder.Entity<AccountSession>(entity =>
        {
            entity.Property(session => session.ClientLabel).HasMaxLength(120);
            entity.HasIndex(session => new { session.UserId, session.RevokedAtUtc });
            entity.HasIndex(session => session.ExpiresAtUtc);
            entity.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserDevice>(entity =>
        {
            entity.Property(device => device.DisplayName).HasMaxLength(120);
            entity.Property(device => device.DeviceType).HasMaxLength(48);
            entity.Property(device => device.PublicKeyThumbprint).HasMaxLength(128);
            entity.Property(device => device.PublicKey).HasMaxLength(512);
            entity.Property(device => device.ChannelSecretProtected).HasMaxLength(1024);
            entity.Ignore(device => device.IsActive);
            entity.HasIndex(device => new { device.UserId, device.RevokedAtUtc });
            entity.HasIndex(device => device.PublicKeyThumbprint).IsUnique();
            entity.HasOne(device => device.User)
                .WithMany(user => user.Devices)
                .HasForeignKey(device => device.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DevicePairingCode>(entity =>
        {
            entity.Property(code => code.CodeHash).HasMaxLength(64);
            entity.Ignore(code => code.IsSpent);
            // Redemption looks a code up by its hash alone, before any user is known.
            entity.HasIndex(code => code.CodeHash).IsUnique();
            entity.HasIndex(code => code.ExpiresAtUtc);
            entity.HasOne(code => code.User)
                .WithMany(user => user.PairingCodes)
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsageLedgerEntry>(entity =>
        {
            entity.Property(entry => entry.Meter).HasMaxLength(64);
            entity.Property(entry => entry.Source).HasMaxLength(64);
            entity.HasIndex(entry => new { entry.UserId, entry.RecordedAtUtc });
            entity.HasOne(entry => entry.User)
                .WithMany(user => user.UsageEntries)
                .HasForeignKey(entry => entry.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.AspNetCore.Identity;

namespace Storava.Web.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public string DisplayName { get; set; } = string.Empty;

    public string PlanCode { get; set; } = "free";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public ICollection<AccountSession> Sessions { get; } = [];

    public ICollection<UserDevice> Devices { get; } = [];

    public ICollection<DevicePairingCode> PairingCodes { get; } = [];

    public ICollection<UsageLedgerEntry> UsageEntries { get; } = [];
}

namespace Storava.Web.Data;

public sealed class UserDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;

    public string DeviceType { get; set; } = "companion-agent";

    public string PublicKeyThumbprint { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}

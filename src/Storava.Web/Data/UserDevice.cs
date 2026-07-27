namespace Storava.Web.Data;

/// <summary>
/// A companion Agent paired to an account: a process running on the user's own machine that can
/// see the file system the browser cannot.
/// <para>
/// The device is identified by the public key it generated locally and never sends the private
/// half of. Separately it holds a channel secret, which is what the browser's short-lived access
/// tokens are signed with — identity and channel authentication are kept apart so rotating one
/// does not force the other.
/// </para>
/// <para>
/// Nothing about the machine's contents lives here. A device row records that an Agent exists,
/// what to call it, and whether it is still allowed — never a path, a drive, or a scan.
/// </para>
/// </summary>
public sealed class UserDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;

    public string DeviceType { get; set; } = "companion-agent";

    public string PublicKeyThumbprint { get; set; } = string.Empty;

    /// <summary>The device's public key, base64 SubjectPublicKeyInfo, as presented at pairing.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// The per-device secret used to sign the browser's access tokens, encrypted at rest with the
    /// application's data-protection key so a database dump alone cannot mint a token. Per device
    /// rather than per server: one leaked secret reaches one machine.
    /// </summary>
    public string ChannelSecretProtected { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;
}

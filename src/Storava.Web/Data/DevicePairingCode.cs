namespace Storava.Web.Data;

/// <summary>
/// A short-lived code the signed-in user reads off the account page and types into the companion
/// Agent on their own machine, so the Agent can prove which account it belongs to.
/// <para>
/// Only a hash of the code is stored. The code itself is shown once, in the response that created
/// it, and never written anywhere the server can read it back — so a database copy cannot be used
/// to pair a device.
/// </para>
/// </summary>
public sealed class DevicePairingCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>SHA-256 of the normalized code, hex encoded.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Set the moment the code is spent. A code pairs exactly one device: a code that leaks — in a
    /// screenshot, a chat, over someone's shoulder — must not be able to attach a second machine.
    /// </summary>
    public DateTimeOffset? RedeemedAtUtc { get; set; }

    /// <summary>The device this code produced, for the audit trail on the account page.</summary>
    public Guid? DeviceId { get; set; }

    public bool IsSpent => RedeemedAtUtc is not null;
}

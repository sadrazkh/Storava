using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Storava.Web.Data;

namespace Storava.Web.Services;

/// <summary>The outcome of trying to spend a pairing code, with a reason the UI can explain.</summary>
public enum PairingFailure
{
    None = 0,
    UnknownCode,
    Expired,
    AlreadyUsed,
    InvalidPublicKey,
    DuplicateDevice
}

public sealed record PairingResult(
    PairingFailure Failure,
    Guid DeviceId,
    string DisplayName,
    string ChannelSecret)
{
    public bool Succeeded => Failure == PairingFailure.None;

    public static PairingResult Failed(PairingFailure failure) =>
        new(failure, Guid.Empty, string.Empty, string.Empty);
}

public interface IDevicePairingService
{
    /// <summary>
    /// Mints a code for this account and returns it in the clear, once. Only its hash is stored.
    /// </summary>
    Task<string> IssueCodeAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Spends a code and registers the Agent that presented it.</summary>
    Task<PairingResult> RedeemAsync(
        string code,
        string publicKeyBase64,
        string deviceName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserDevice>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> RevokeAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);
}

/// <summary>
/// Issues and redeems the codes that attach a companion Agent to an account.
/// <para>
/// Pairing is the only moment the server and an Agent exchange anything secret, so the rules are
/// deliberately strict: a code lives for ten minutes, is stored only as a hash, is spent exactly
/// once, and buys nothing more than a device row and a channel secret. It grants no access to the
/// machine — the Agent decides that locally, for itself.
/// </para>
/// </summary>
public sealed class DevicePairingService(
    ApplicationDbContext database,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<DevicePairingService> logger) : IDevicePairingService
{
    /// <summary>Long enough to be unguessable, short enough to read off a screen and type.</summary>
    private const int CodeGroups = 3;
    private const int CodeGroupLength = 4;

    /// <summary>
    /// Excludes characters that are read wrong when transcribed by hand: no O/0, no I/1/L, no U
    /// against V. A pairing code is typed by a person, from a screen, into a terminal.
    /// </summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private const string ProtectorPurpose = "Storava.Web.DeviceChannelSecret.v1";

    public async Task<string> IssueCodeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // An account has one live code at a time, and generating a new one retires every earlier
        // one. Leaving an old code valid would widen the window for no benefit — the page only
        // ever shows the newest — and clearing spent ones keeps the table from growing.
        //
        // Note the filter is on the user alone: SQLite, which development and the integration
        // tests run on, cannot translate a DateTimeOffset comparison into SQL. Comparisons against
        // the clock are done in memory throughout this codebase for that reason.
        var previous = await database.DevicePairingCodes
            .Where(code => code.UserId == userId)
            .ToListAsync(cancellationToken);
        database.DevicePairingCodes.RemoveRange(previous);

        string plain = GenerateCode();
        database.DevicePairingCodes.Add(new DevicePairingCode
        {
            UserId = userId,
            CodeHash = Hash(plain),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(CodeLifetime)
        });

        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("A device pairing code was issued for user {UserId}.", userId);
        return plain;
    }

    public async Task<PairingResult> RedeemAsync(
        string code,
        string publicKeyBase64,
        string deviceName,
        CancellationToken cancellationToken)
    {
        if (!TryReadPublicKey(publicKeyBase64, out string thumbprint))
            return PairingResult.Failed(PairingFailure.InvalidPublicKey);

        string normalized = Normalize(code);
        if (normalized.Length == 0)
            return PairingResult.Failed(PairingFailure.UnknownCode);

        // Hoisted out of the expression tree so the hash is computed once, here, rather than left
        // for the query translator to reason about.
        string codeHash = Hash(normalized);
        var record = await database.DevicePairingCodes
            .SingleOrDefaultAsync(candidate => candidate.CodeHash == codeHash, cancellationToken);

        if (record is null)
            return PairingResult.Failed(PairingFailure.UnknownCode);

        // Checked in this order so a spent code never reports merely "expired" once time passes.
        if (record.IsSpent)
            return PairingResult.Failed(PairingFailure.AlreadyUsed);

        var now = timeProvider.GetUtcNow();
        if (record.ExpiresAtUtc <= now)
            return PairingResult.Failed(PairingFailure.Expired);

        bool keyInUse = await database.UserDevices
            .AnyAsync(device => device.PublicKeyThumbprint == thumbprint, cancellationToken);
        if (keyInUse)
            return PairingResult.Failed(PairingFailure.DuplicateDevice);

        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var device = new UserDevice
        {
            UserId = record.UserId,
            DisplayName = CleanName(deviceName),
            DeviceType = "companion-agent",
            PublicKey = publicKeyBase64,
            PublicKeyThumbprint = thumbprint,
            ChannelSecretProtected = Protector().Protect(Convert.ToBase64String(secret)),
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };

        database.UserDevices.Add(device);
        record.RedeemedAtUtc = now;
        record.DeviceId = device.Id;

        // Pairing is an accountable event, so it lands in the ledger the same as any other.
        database.UsageLedger.Add(new UsageLedgerEntry
        {
            UserId = record.UserId,
            Meter = "device.paired",
            Units = 1,
            Source = "companion-agent",
            RecordedAtUtc = now
        });

        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Device {DeviceId} was paired to user {UserId}.", device.Id, record.UserId);

        return new PairingResult(
            PairingFailure.None,
            device.Id,
            device.DisplayName,
            Convert.ToBase64String(secret));
    }

    public async Task<IReadOnlyList<UserDevice>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await database.UserDevices
            .AsNoTracking()
            .Where(device => device.UserId == userId && device.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        return devices.OrderByDescending(device => device.LastSeenAtUtc).ToList();
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await database.UserDevices.SingleOrDefaultAsync(
            candidate => candidate.Id == deviceId && candidate.UserId == userId,
            cancellationToken);

        if (device is null || device.RevokedAtUtc is not null)
            return false;

        device.RevokedAtUtc = timeProvider.GetUtcNow();
        // The channel secret is what signs the browser's access tokens, so destroying it is what
        // makes revocation real rather than a flag someone could later flip back.
        device.ChannelSecretProtected = string.Empty;

        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Device {DeviceId} was revoked.", deviceId);
        return true;
    }

    private IDataProtector Protector() => dataProtection.CreateProtector(ProtectorPurpose);

    private static string GenerateCode()
    {
        var builder = new StringBuilder(CodeGroups * (CodeGroupLength + 1));
        for (int group = 0; group < CodeGroups; group++)
        {
            if (group > 0)
                builder.Append('-');

            for (int position = 0; position < CodeGroupLength; position++)
                builder.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>Accepts the code however it was typed: spaced, lower case, with or without dashes.</summary>
    internal static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var builder = new StringBuilder(code.Length);
        foreach (char character in code)
        {
            char upper = char.ToUpperInvariant(character);
            if (CodeAlphabet.Contains(upper, StringComparison.Ordinal))
                builder.Append(upper);
        }

        return builder.ToString();
    }

    private static string Hash(string normalizedOrPlain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(normalizedOrPlain))));

    /// <summary>
    /// Accepts only a well-formed P-256 public key. Parsing it here means a malformed or oversized
    /// blob is refused at the door rather than stored and tripped over later.
    /// </summary>
    private static bool TryReadPublicKey(string? publicKeyBase64, out string thumbprint)
    {
        thumbprint = string.Empty;
        if (string.IsNullOrWhiteSpace(publicKeyBase64) || publicKeyBase64.Length > 512)
            return false;

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out int read);
            if (read != spki.Length)
                return false;

            var parameters = key.ExportParameters(includePrivateParameters: false);
            if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value &&
                !string.Equals(parameters.Curve.Oid.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }

        thumbprint = Convert.ToHexString(SHA256.HashData(spki));
        return true;
    }

    private static string CleanName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return "Companion Agent";

        // Control characters would let a device name break the account page's layout or its logs.
        var cleaned = new string([.. trimmed.Where(character => !char.IsControl(character))]);
        return cleaned.Length <= 120 ? cleaned : cleaned[..120];
    }
}

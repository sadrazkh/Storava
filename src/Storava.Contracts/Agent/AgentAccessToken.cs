using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storava.Contracts.Agent;

/// <summary>Why an access token was refused, so the Agent can say something specific.</summary>
public enum AgentTokenStatus
{
    Valid = 0,
    Malformed,
    BadSignature,
    Expired,
    NotYetValid,
    WrongDevice,
    WrongOrigin
}

/// <summary>What a verified token says. Only meaningful when <see cref="Status"/> is Valid.</summary>
public sealed record AgentTokenResult(AgentTokenStatus Status, Guid DeviceId, string Origin, DateTimeOffset ExpiresAt)
{
    public bool IsValid => Status == AgentTokenStatus.Valid;

    public static AgentTokenResult Refused(AgentTokenStatus status) =>
        new(status, Guid.Empty, string.Empty, DateTimeOffset.MinValue);
}

/// <summary>
/// The short-lived pass the browser presents to a companion Agent.
/// <para>
/// The account server mints it, signed with the channel secret that server and Agent agreed on at
/// pairing; the Agent verifies it without asking anyone. That keeps the server out of the path
/// between page and Agent — which is the whole point, since anything routed through the server
/// would be scan data leaving the machine — while still letting the server decide who may connect.
/// </para>
/// <para>
/// It is deliberately not a general-purpose JWT. There is one algorithm, no algorithm field to
/// confuse, no key lookup, and nothing optional: a token is a device id, an origin and a short
/// window, or it is refused.
/// </para>
/// </summary>
public static class AgentAccessToken
{
    /// <summary>Names the format and the algorithm at once. A token that does not start with it is refused.</summary>
    public const string Prefix = "storava1";

    /// <summary>
    /// Deliberately short. Revoking a device destroys the secret so no new token can be signed,
    /// and this is how long an already-issued one can outlive that.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Allows for the ordinary disagreement between two clocks on the same machine.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>A token beyond this length is refused before any parsing is attempted.</summary>
    private const int MaximumLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Mints a token for one device and one page origin.</summary>
    /// <param name="channelSecret">Base64, as agreed at pairing.</param>
    public static string Issue(
        string channelSecret,
        Guid deviceId,
        string origin,
        DateTimeOffset issuedAt,
        TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var payload = new TokenPayload
        {
            DeviceId = deviceId,
            Origin = origin,
            IssuedAt = issuedAt.ToUnixTimeSeconds(),
            ExpiresAt = issuedAt.Add(lifetime ?? DefaultLifetime).ToUnixTimeSeconds()
        };

        string encoded = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        string signed = $"{Prefix}.{encoded}";
        return $"{signed}.{Base64Url(Sign(channelSecret, signed))}";
    }

    /// <summary>
    /// Checks a token against the secret this Agent holds. Every failure returns a status rather
    /// than throwing: a bad token is an ordinary event on a port anything on the machine can reach.
    /// </summary>
    public static AgentTokenResult Verify(
        string? token,
        string channelSecret,
        Guid expectedDeviceId,
        string expectedOrigin,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaximumLength)
            return AgentTokenResult.Refused(AgentTokenStatus.Malformed);

        var parts = token.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            return AgentTokenResult.Refused(AgentTokenStatus.Malformed);

        byte[] presented;
        byte[] payloadBytes;
        try
        {
            presented = FromBase64Url(parts[2]);
            payloadBytes = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return AgentTokenResult.Refused(AgentTokenStatus.Malformed);
        }

        byte[] expected = Sign(channelSecret, $"{parts[0]}.{parts[1]}");

        // Compared in constant time: a byte-at-a-time comparison on a loopback port is a
        // measurable oracle for anything else running as this user.
        if (!CryptographicOperations.FixedTimeEquals(presented, expected))
            return AgentTokenResult.Refused(AgentTokenStatus.BadSignature);

        // Only now is the payload worth reading — before the signature check it is attacker input.
        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return AgentTokenResult.Refused(AgentTokenStatus.Malformed);
        }

        if (payload is null)
            return AgentTokenResult.Refused(AgentTokenStatus.Malformed);

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt);

        if (payload.DeviceId != expectedDeviceId)
            return AgentTokenResult.Refused(AgentTokenStatus.WrongDevice);

        if (!OriginMatches(payload.Origin, expectedOrigin))
            return AgentTokenResult.Refused(AgentTokenStatus.WrongOrigin);

        if (expiresAt + ClockSkew <= now)
            return AgentTokenResult.Refused(AgentTokenStatus.Expired);

        if (issuedAt - ClockSkew > now)
            return AgentTokenResult.Refused(AgentTokenStatus.NotYetValid);

        return new AgentTokenResult(AgentTokenStatus.Valid, payload.DeviceId, payload.Origin, expiresAt);
    }

    /// <summary>Origins are compared as written, minus a trailing slash and letter case.</summary>
    public static bool OriginMatches(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? origin) =>
        (origin ?? string.Empty).Trim().TrimEnd('/');

    private static byte[] Sign(string channelSecret, string content)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(channelSecret);
        }
        catch (FormatException)
        {
            // A secret this side cannot read can never match, and saying so here keeps the caller
            // free of a second failure mode to handle.
            key = [];
        }

        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(content));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }

    private sealed class TokenPayload
    {
        [JsonPropertyName("deviceId")]
        public Guid DeviceId { get; set; }

        [JsonPropertyName("origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("issuedAt")]
        public long IssuedAt { get; set; }

        [JsonPropertyName("expiresAt")]
        public long ExpiresAt { get; set; }
    }
}

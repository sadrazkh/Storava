using System.Text.Json;
using System.Text.Json.Serialization;
using Storava.Application.Abstractions;

namespace Storava.Agent.Identity;

/// <summary>
/// What the Agent keeps after pairing: which account server it answers to, the device id that
/// server gave it, and the channel secret the browser's access tokens are signed with.
/// </summary>
public sealed class AgentRegistration
{
    [JsonPropertyName("serverBaseUrl")]
    public string ServerBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; set; }

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Base64, and the reason the whole record is kept in the encrypted store rather than a plain
    /// settings file: anything able to read this could forge a token the Agent would accept.
    /// </summary>
    [JsonPropertyName("channelSecret")]
    public string ChannelSecret { get; set; } = string.Empty;

    [JsonPropertyName("pairedAtUtc")]
    public DateTimeOffset PairedAtUtc { get; set; }
}

/// <summary>Reads and writes the registration, encrypted at rest for the current Windows user.</summary>
public sealed class AgentRegistrationStore(ISecretStore secrets)
{
    internal const string RegistrationSecret = "agent.registration";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentRegistration? Load()
    {
        string? stored = secrets.Get(RegistrationSecret);
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        try
        {
            var registration = JsonSerializer.Deserialize<AgentRegistration>(stored, Options);
            // A record missing either half is not a usable pairing; treat it as none at all.
            return registration is { DeviceId: var id, ChannelSecret.Length: > 0 } && id != Guid.Empty
                ? registration
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(AgentRegistration registration) =>
        secrets.Set(RegistrationSecret, JsonSerializer.Serialize(registration, Options));

    public void Clear() => secrets.Set(RegistrationSecret, null);

    public bool Exists => Load() is not null;
}

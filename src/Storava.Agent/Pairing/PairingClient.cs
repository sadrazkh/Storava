using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Storava.Agent.Pairing;

public sealed record PairingRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("deviceName")] string DeviceName);

public sealed record PairingSuccess(
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("channelSecret")] string ChannelSecret,
    [property: JsonPropertyName("pairedAtUtc")] DateTimeOffset PairedAtUtc);

public sealed record PairingProblem(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

/// <summary>The one call the Agent makes to the account server, and the only one it ever needs.</summary>
public sealed class PairingClient(HttpClient http)
{
    /// <summary>
    /// Presents the code and this machine's public key. Returns the device record on success, or a
    /// problem describing why the server refused — never an exception for an ordinary refusal.
    /// </summary>
    public async Task<(PairingSuccess? Success, PairingProblem? Problem)> PairAsync(
        Uri serverBaseUrl,
        PairingRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(serverBaseUrl, "api/agent/pair");

        using var response = await http
            .PostAsJsonAsync(endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var success = await response.Content
                .ReadFromJsonAsync<PairingSuccess>(cancellationToken)
                .ConfigureAwait(false);

            return success is not null
                ? (success, null)
                : (null, new PairingProblem("malformed_response", "The server's reply could not be read."));
        }

        // A refusal carries a reason; anything else is reported by status so it is not mistaken
        // for a rejected code.
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<PairingProblem>(cancellationToken)
                .ConfigureAwait(false);

            if (problem is not null && !string.IsNullOrWhiteSpace(problem.Message))
                return (null, problem);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            // Fall through to the status-based message below.
        }

        return (null, new PairingProblem(
            "http_" + (int)response.StatusCode,
            $"The server replied {(int)response.StatusCode} {response.ReasonPhrase}."));
    }
}

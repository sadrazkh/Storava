using Serilog;
using Storava.Agent.Identity;
using Storava.Agent.Pairing;

namespace Storava.Agent.Commands;

/// <summary>
/// Attaches this machine to an account. The user reads a code off their account page and types it
/// here; the Agent sends that code and the public half of the key it generated locally.
/// </summary>
internal static class PairCommand
{
    public static async Task<int> RunAsync(
        CommandLine command,
        AgentKeyStore keys,
        AgentRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        if (!ServerAddress.TryParse(command.Option("server"), out var server, out string addressError))
        {
            Log.Error("{Error}", addressError);
            return ExitCodes.BadUsage;
        }

        if (registrations.Load() is { } existing)
        {
            Log.Error(
                "This computer is already paired to {Server} as \"{Name}\". Run 'storava-agent unpair' first.",
                existing.ServerBaseUrl,
                existing.DeviceName);
            return ExitCodes.BadUsage;
        }

        string? code = command.Option("code") ?? Prompt();
        if (string.IsNullOrWhiteSpace(code))
        {
            Log.Error("A pairing code is required. Generate one on your account page.");
            return ExitCodes.BadUsage;
        }

        string deviceName = command.Option("name") ?? Environment.MachineName;

        using var key = keys.LoadOrCreate();
        string publicKey = AgentKeyStore.PublicKeyOf(key);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new PairingClient(http);

        Log.Information("Pairing with {Server}…", server);

        var (success, problem) = await client
            .PairAsync(server, new PairingRequest(code, publicKey, deviceName), cancellationToken)
            .ConfigureAwait(false);

        if (success is null)
        {
            Log.Error("Pairing was refused: {Message}", problem?.Message ?? "the server did not say why.");

            // The key stays: it is this installation's identity, and a mistyped code is not a
            // reason to throw it away and appear as a different machine on the next attempt.
            return ExitCodes.Failed;
        }

        registrations.Save(new AgentRegistration
        {
            ServerBaseUrl = server.ToString(),
            DeviceId = success.DeviceId,
            DeviceName = success.DeviceName,
            ChannelSecret = success.ChannelSecret,
            PairedAtUtc = success.PairedAtUtc
        });

        Log.Information("Paired as \"{Name}\".", success.DeviceName);
        Log.Information("Key {Fingerprint}", AgentKeyStore.FingerprintOf(key));
        Log.Information("Check that fingerprint against the one on your account page.");
        Log.Information("Nothing on this computer has been read or sent. The agent only knows it belongs to you.");
        return ExitCodes.Success;
    }

    /// <summary>
    /// Asks for the code rather than taking it on the command line, so it does not end up in the
    /// shell history of a shared machine.
    /// </summary>
    private static string? Prompt()
    {
        if (Console.IsInputRedirected)
            return null;

        Console.Write("Pairing code: ");
        return Console.ReadLine();
    }
}

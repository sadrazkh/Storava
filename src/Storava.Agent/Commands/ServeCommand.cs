using Serilog;
using Storava.Agent.Channel;
using Storava.Agent.Identity;

namespace Storava.Agent.Commands;

/// <summary>
/// Runs the Agent so the Storava page in the browser can reach it. Refuses to start unpaired:
/// without a channel secret there is no way to tell the user's own page from anything else on the
/// machine, and a port that answers everyone would be worse than no port at all.
/// </summary>
internal static class ServeCommand
{
    public static async Task<int> RunAsync(
        AgentKeyStore keys,
        AgentRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        var registration = registrations.Load();
        if (registration is null)
        {
            Log.Error("This computer is not paired. Run 'storava-agent pair --server <url>' first.");
            return ExitCodes.BadUsage;
        }

        using var key = keys.TryLoad();
        if (key is null)
        {
            // The registration outlived the identity, which should not happen; saying so is better
            // than serving with half a pairing.
            Log.Error("The agent's identity is missing. Run 'storava-agent unpair' and pair again.");
            return ExitCodes.Failed;
        }

        var server = new AgentServer(registration, AgentKeyStore.FingerprintOf(key));

        try
        {
            return await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Stopped.");
            return ExitCodes.Success;
        }
    }
}

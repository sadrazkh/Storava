using Serilog;
using Storava.Agent.Identity;
using Storava.Agent.Tray;

namespace Storava.Agent.Commands;

/// <summary>
/// Forgets the pairing on this machine. It is deliberately local-only: the server keeps its own
/// device row until the user removes it on the account page, and this command says so rather than
/// implying the account has been cleaned up.
/// </summary>
internal static class UnpairCommand
{
    public static int Run(
        CommandLine command,
        AgentKeyStore keys,
        AgentRegistrationStore registrations,
        AutoStart autoStart)
    {
        var registration = registrations.Load();
        registrations.Clear();

        // By default the identity goes too, so a machine removed from an account cannot come back
        // presenting the same key the server still has on file.
        if (!command.HasFlag("keep-identity"))
            keys.Delete();

        // An agent with nothing to serve should not keep starting itself at every logon.
        autoStart.Disable();

        if (registration is null)
        {
            Log.Information("This computer was not paired. Local agent state has been cleared.");
            return ExitCodes.Success;
        }

        Log.Information("Forgot the pairing with {Server}.", registration.ServerBaseUrl);
        Log.Warning(
            "The device is still listed on your account. Remove \"{Name}\" there to finish.",
            registration.DeviceName);
        return ExitCodes.Success;
    }
}

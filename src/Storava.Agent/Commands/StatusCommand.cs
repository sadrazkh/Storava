using Serilog;
using Storava.Agent.Identity;
using Storava.Agent.Tray;

namespace Storava.Agent.Commands;

/// <summary>
/// Reports what this installation is, without revealing anything that would let someone else be
/// it: the fingerprint is a hash, and the channel secret is never printed.
/// </summary>
internal static class StatusCommand
{
    public static int Run(AgentKeyStore keys, AgentRegistrationStore registrations, AutoStart autoStart)
    {
        using var key = keys.TryLoad();

        if (key is null)
        {
            Log.Information("No identity yet. Run 'storava-agent pair --server <url>' to connect this computer.");
            return ExitCodes.Success;
        }

        Log.Information("Key       {Fingerprint}", AgentKeyStore.FingerprintOf(key));

        var registration = registrations.Load();
        if (registration is null)
        {
            // A key with no registration is the normal state after 'unpair --keep-identity', and
            // also what a refused pairing leaves behind.
            Log.Information("Paired    no");
            Log.Information("Run 'storava-agent pair --server <url>' to connect this computer.");
            return ExitCodes.Success;
        }

        Log.Information("Paired    yes");
        Log.Information("Account   {Server}", registration.ServerBaseUrl);
        Log.Information("Device    {Name} ({DeviceId})", registration.DeviceName, registration.DeviceId);
        Log.Information("Since     {PairedAt:g}", registration.PairedAtUtc.ToLocalTime());
        Log.Information("At logon  {AutoStart}", autoStart.IsEnabled ? "starts automatically" : "does not start");
        Log.Information("Removing this device on the account page stops the browser from reaching it.");
        return ExitCodes.Success;
    }
}

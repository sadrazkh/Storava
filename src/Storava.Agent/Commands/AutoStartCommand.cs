using Serilog;
using Storava.Agent.Tray;

namespace Storava.Agent.Commands;

/// <summary>
/// The same switch the tray menu offers, for people who would rather not use a menu — and so that
/// an installer can turn it on without poking at the registry itself.
/// </summary>
internal static class AutoStartCommand
{
    public static int Run(CommandLine command, AutoStart autoStart)
    {
        if (command.HasFlag("enable"))
        {
            autoStart.Enable();
            Log.Information("The agent will start when you sign in to Windows.");
            return ExitCodes.Success;
        }

        if (command.HasFlag("disable"))
        {
            autoStart.Disable();
            Log.Information("The agent will no longer start automatically.");
            return ExitCodes.Success;
        }

        Log.Information(
            autoStart.IsEnabled
                ? "Starts with Windows: yes"
                : "Starts with Windows: no");
        Log.Information("Use --enable or --disable to change it.");
        return ExitCodes.Success;
    }
}

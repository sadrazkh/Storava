using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Storava.Agent.Identity;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Infrastructure;
using Storava.Platform;

namespace Storava.Agent.Commands;

/// <summary>
/// Shows and changes how many scans the Agent keeps.
/// <para>
/// The Agent inherits automatic retention from the shared layer, which means it has always been
/// quietly discarding old scans with no way to see the number or change it. A setting that deletes
/// data and cannot be inspected is worse than one that does nothing: the desktop's Settings page
/// governs the desktop's database, and this is a different one on the same machine.
/// </para>
/// </summary>
internal static class RetentionCommand
{
    public static async Task<int> RunAsync(CommandLine command, CancellationToken cancellationToken)
    {
        using var provider = BuildServices();

        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (command.Option("keep") is { } requested)
        {
            if (!int.TryParse(requested, out int keep) || keep < 1 || keep > 50)
            {
                Log.Error("--keep takes a whole number between 1 and 50. Keeping none would discard the scan just taken.");
                return ExitCodes.Failed;
            }

            var updated = settings.Current.Clone();
            updated.KeepRecentScans = keep;
            await settings.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

            Log.Information("This agent will now keep its {Keep} most recent scan(s).", keep);
        }

        var sessions = provider.GetRequiredService<IScanSessionRepository>();
        var stored = await sessions.GetRecentAsync(100, cancellationToken).ConfigureAwait(false);

        Log.Information("Keeping   {Keep} most recent scan(s)", settings.Current.KeepRecentScans);
        Log.Information("Stored    {Count} scan(s) at {Path}", stored.Count, AgentPaths.ScanDatabase);
        Log.Information("Older scans are discarded automatically once a new one finishes.");
        Log.Information("Change it with 'storava-agent retention --keep <number>'.");

        return ExitCodes.Success;
    }

    /// <summary>
    /// The smallest set of services this needs: settings and the scan sessions.
    /// <para>
    /// Deliberately not the whole server. Nothing here scans, listens or touches a user file, and a
    /// command that reports a number should not be able to.
    /// </para>
    /// </summary>
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStoravaInfrastructure(AgentPaths.ScanDatabase);
        services.AddStoravaPlatform();
        services.AddSingleton<ScanRetentionService>();
        return services.BuildServiceProvider();
    }
}

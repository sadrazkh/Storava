using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Storava.Agent;
using Storava.Agent.Commands;
using Storava.Agent.Identity;
using Storava.Agent.Tray;
using Storava.Platform.Security;

var command = CommandLine.Parse(args);

// Launched from Explorer or the logon task there is no verb and no terminal; that is the ordinary
// case, and it should raise a tray icon rather than print help nobody can see.
bool wantsTray = command.Verb is "tray" || (args.Length == 0 && !Console.IsOutputRedirected && !HasTerminal());
bool attached = !wantsTray && ConsoleAttach.ToParentTerminal();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        AgentPaths.LogFile,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);

    // Identity and registration live in the same encrypted store the desktop app uses for the AI
    // key: DPAPI, scoped to this Windows account, outside any database that could be exported.
    var secrets = new DpapiSecretStore(
        AgentPaths.SecretsDirectory,
        loggerFactory.CreateLogger<DpapiSecretStore>());

    var keys = new AgentKeyStore(secrets);
    var registrations = new AgentRegistrationStore(secrets);
    var autoStart = new AutoStart();

    if (wantsTray)
        return RunTray(keys, registrations, autoStart);

    return command.Verb switch
    {
        "pair" => await PairCommand.RunAsync(command, keys, registrations, CancellationToken.None),
        "serve" => await ServeCommand.RunAsync(keys, registrations, ShutdownSignal()),
        "status" => StatusCommand.Run(keys, registrations, autoStart),
        "unpair" => UnpairCommand.Run(command, keys, registrations, autoStart),
        "autostart" => AutoStartCommand.Run(command, autoStart),
        "help" or "--help" or "-h" => HelpCommand.Run(),
        _ => HelpCommand.Unknown(command.Verb)
    };
}
catch (Exception exception)
{
    Log.Error("The agent could not complete that command: {Message}", exception.Message);
    Log.Debug(exception, "Unhandled agent failure.");
    return ExitCodes.Failed;
}
finally
{
    await Log.CloseAndFlushAsync();

    if (attached)
        ConsoleAttach.ReleaseTerminal();
}

/// <summary>
/// One tray icon per Windows account. Both the logon task and a double-click can start the Agent,
/// and two of them would fight over the same port and show the user two icons.
/// </summary>
static int RunTray(AgentKeyStore keys, AgentRegistrationStore registrations, AutoStart autoStart)
{
    using var single = new Mutex(initiallyOwned: true, @"Local\Storava.Agent.Tray", out bool first);
    if (!first)
    {
        Log.Information("The agent is already running for this account.");
        return ExitCodes.Success;
    }

    // Spelled out rather than relying on the generated ApplicationConfiguration, so what the tray
    // sets up is visible here. System-aware DPI keeps the drawn icon crisp on a scaled display.
    System.Windows.Forms.Application.EnableVisualStyles();
    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
    System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);

    using var tray = new AgentTrayApplication(keys, registrations, autoStart);
    return tray.Run();
}

/// <summary>Ctrl+C stops the listener cleanly rather than killing it mid-request.</summary>
static CancellationToken ShutdownSignal()
{
    var source = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        source.Cancel();
    };
    return source.Token;
}

/// <summary>True when a terminal is present, meaning the user typed the command themselves.</summary>
static bool HasTerminal()
{
    try
    {
        return Console.WindowHeight > 0;
    }
    catch (IOException)
    {
        return false;
    }
}

/// <summary>Exit codes, so the Agent can be driven from a script.</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int BadUsage = 2;
}

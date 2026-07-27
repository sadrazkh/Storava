using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Storava.Agent;
using Storava.Agent.Commands;
using Storava.Agent.Identity;
using Storava.Platform.Security;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var command = CommandLine.Parse(args);

    using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);

    // Identity and registration live in the same encrypted store the desktop app uses for the AI
    // key: DPAPI, scoped to this Windows account, outside any database that could be exported.
    var secrets = new DpapiSecretStore(
        AgentPaths.SecretsDirectory,
        loggerFactory.CreateLogger<DpapiSecretStore>());

    var keys = new AgentKeyStore(secrets);
    var registrations = new AgentRegistrationStore(secrets);

    return command.Verb switch
    {
        "pair" => await PairCommand.RunAsync(command, keys, registrations, CancellationToken.None),
        "serve" => await ServeCommand.RunAsync(keys, registrations, ShutdownSignal()),
        "status" => StatusCommand.Run(keys, registrations),
        "unpair" => UnpairCommand.Run(command, keys, registrations),
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

/// <summary>Exit codes, so the Agent can be driven from a script.</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int BadUsage = 2;
}

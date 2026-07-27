using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Storava.Agent.Identity;
using Storava.Agent.Scanning;
using Storava.Application.Abstractions;
using Storava.Contracts.Agent;
using Storava.Infrastructure;
using Storava.Infrastructure.Persistence;
using Storava.Platform;
using Storava.Rules;

namespace Storava.Agent.Channel;

/// <summary>
/// The loopback listener the Storava page in the browser talks to.
/// <para>
/// It exists so that scan data never has to travel through the account server to be useful. The
/// page asks the server for a short-lived token, then speaks to this process directly; the server
/// decides <em>who</em> may connect and learns nothing about <em>what</em> is on the machine.
/// </para>
/// <para>
/// Three things guard the port, and none of them is sufficient alone. It binds to
/// <c>127.0.0.1</c>, so nothing off this machine can reach it. Cross-origin reads are limited to
/// the one origin this Agent is paired with. And every endpoint that says anything real demands a
/// token signed with the channel secret only that account server holds.
/// </para>
/// </summary>
/// <param name="scanDatabasePath">
/// Where walks are stored. A parameter rather than a constant so a test never writes into the
/// real <c>%LOCALAPPDATA%\Storava\Agent</c> and two of them never share one file.
/// </param>
public sealed class AgentServer(
    AgentRegistration registration,
    string deviceFingerprint,
    string? scanDatabasePath = null)
{
    /// <summary>The scheme+authority of the account server this Agent belongs to.</summary>
    private readonly string _allowedOrigin = OriginOf(registration.ServerBaseUrl);

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private const string CorsPolicy = "storava-page";

    public int Port { get; private set; }

    /// <summary>
    /// Binds the first port that will actually take us and serves until cancelled.
    /// <para>
    /// Probing a port and then binding it are two moments, and something else can arrive in
    /// between — most easily another Agent starting at the same instant. Rather than trust the
    /// probe, a failed bind simply moves to the next port.
    /// </para>
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        foreach (int candidate in AgentEndpoints.Ports)
        {
            if (!LoopbackPort.IsFree(candidate))
                continue;

            try
            {
                return await ServeAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception) when (exception.InnerException is AddressInUseException)
            {
                Log.Debug("Port {Port} was taken between the check and the bind; trying the next.", candidate);
            }
        }

        Log.Error(
            "Every agent port is already in use ({Ports}). Another agent is probably running.",
            string.Join(", ", AgentEndpoints.Ports));
        return ExitCodes.Failed;
    }

    private async Task<int> ServeAsync(int port, CancellationToken cancellationToken)
    {
        Port = port;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger);
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.Listen(IPAddress.Loopback, Port);
            // Nothing here serves files, and a smaller ceiling is one less thing to reason about.
            options.Limits.MaxRequestBodySize = 64 * 1024;
            options.AddServerHeader = false;
        });

        builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(_allowedOrigin)
            .WithMethods("GET", "POST")
            .WithHeaders("Authorization", "Content-Type")));

        // The scanner, the rule catalog and the storage are the desktop application's, unchanged.
        // The Agent is a different caller for them, not a second implementation.
        builder.Services.AddStoravaInfrastructure(scanDatabasePath ?? AgentPaths.ScanDatabase);
        builder.Services.AddStoravaPlatform(AgentPaths.SecretsDirectory);
        builder.Services.AddStoravaRules<SqliteScanItemSinkFactory>();
        builder.Services.AddSingleton<AgentScanService>();

        var app = builder.Build();
        // Before CORS, which answers a preflight and stops: a header added afterwards would never
        // be written.
        app.Use(AnnouncePrivateNetworkAccess);
        app.UseCors(CorsPolicy);

        MapEndpoints(app);

        Log.Information("Listening on {Address} for {Origin}.", AgentEndpoints.BaseAddress(Port), _allowedOrigin);
        Log.Information("Paired as \"{Name}\" · key {Fingerprint}", registration.DeviceName, deviceFingerprint);
        Log.Information("Nothing is read from this computer until the page asks, and only over this port.");
        Log.Information("Press Ctrl+C to stop.");

        await app.RunAsync(cancellationToken).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private void MapEndpoints(WebApplication app)
    {
        // Unauthenticated on purpose: this is how the page finds which port an Agent is on, and it
        // has to answer before any token could be issued for it. It says only that an Agent is
        // here and which device — and CORS keeps even that from being read by another origin.
        app.MapGet(AgentEndpoints.HelloPath, () => Results.Json(new AgentHello(
            AgentEndpoints.Product,
            AgentEndpoints.ProtocolVersion,
            registration.DeviceId,
            Paired: true)));

        app.MapGet(AgentEndpoints.StatusPath, (HttpContext context) =>
        {
            var refusal = Authorize(context);
            if (refusal is not null)
                return refusal;

            return Results.Json(new AgentStatus(
                registration.DeviceId,
                registration.DeviceName,
                Version(),
                _startedAt));
        });

        // Everything below needs a pass. These are the endpoints that read the machine.

        app.MapGet(AgentScanPaths.Drives, (HttpContext context, IStorageInfoService storage) =>
            Authorize(context) ?? Results.Json(storage.GetDrives()
                .Select(drive => new AgentDrive(
                    drive.Name,
                    drive.VolumeLabel,
                    drive.DriveFormat,
                    drive.TotalSize.Bytes,
                    drive.FreeSpace.Bytes,
                    drive.IsReady))
                .ToList()));

        app.MapPost(AgentScanPaths.Scans, async (
            HttpContext context,
            AgentScanRequest request,
            AgentScanService scans) =>
        {
            var refusal = Authorize(context);
            if (refusal is not null)
                return refusal;

            var started = await scans.StartAsync(request);
            return started.Problem is { } problem
                ? Results.Json(problem, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(started.Progress);
        });

        app.MapGet($"{AgentScanPaths.Scans}/{{scanId}}", (
            HttpContext context,
            string scanId,
            AgentScanService scans) =>
            Authorize(context) ?? (scans.Get(scanId) is { } progress
                ? Results.Json(progress)
                : Results.NotFound()));

        app.MapPost($"{AgentScanPaths.Scans}/{{scanId}}/cancel", (
            HttpContext context,
            string scanId,
            AgentScanService scans) =>
            Authorize(context) ?? (scans.Cancel(scanId)
                ? Results.Json(scans.Get(scanId))
                : Results.NotFound()));

        app.MapGet($"{AgentScanPaths.Scans}/{{scanId}}/items", async (
            HttpContext context,
            string scanId,
            AgentScanService scans,
            CancellationToken cancellationToken,
            int limit = 100,
            bool foldersOnly = false) =>
        {
            var refusal = Authorize(context);
            if (refusal is not null)
                return refusal;

            var items = await scans.ItemsAsync(scanId, limit, foldersOnly, cancellationToken);
            return items is null ? Results.NotFound() : Results.Json(items);
        });
    }

    /// <summary>
    /// Checks the bearer token on a request, returning a refusal to send back or null to continue.
    /// </summary>
    private IResult? Authorize(HttpContext context)
    {
        string? header = context.Request.Headers.Authorization.ToString();
        string? token = header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;

        var result = AgentAccessToken.Verify(
            token,
            registration.ChannelSecret,
            registration.DeviceId,
            _allowedOrigin,
            DateTimeOffset.UtcNow);

        if (result.IsValid)
            return null;

        // Logged at debug: on a port anything on the machine can knock at, a refusal is routine
        // and logging every one at information level would be noise that hides the real events.
        Log.Debug("Refused a request to {Path}: {Status}.", context.Request.Path, result.Status);

        return Results.Json(
            new { error = "unauthorized", reason = result.Status.ToString().ToLowerInvariant() },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Chrome 142 replaced Private Network Access preflights with a permission prompt, so this
    /// header is no longer what gates the request. It is still answered for browsers on the older
    /// behaviour, where its absence would fail the preflight outright.
    /// </summary>
    private static async Task AnnouncePrivateNetworkAccess(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsOptions(context.Request.Method) &&
            context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
        {
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        await next(context);
    }

    /// <summary>
    /// Reduces a base URL to scheme and authority. Tokens are bound to an origin, and a path or a
    /// trailing slash would make two spellings of the same server look like different ones.
    /// </summary>
    internal static string OriginOf(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
            ? parsed.GetLeftPart(UriPartial.Authority)
            : (baseUrl ?? string.Empty).TrimEnd('/');

    private static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
}

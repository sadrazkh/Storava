namespace Storava.Contracts.Agent;

/// <summary>
/// Where the browser looks for a companion Agent, and what it expects to find.
/// <para>
/// A page cannot read a file, so it cannot be told which port the Agent settled on. The Agent
/// therefore takes the first free port from a short fixed list and the page tries the same list in
/// the same order. Four is enough for several Windows accounts to run one each, and short enough
/// that a page that finds nothing gives up quickly.
/// </para>
/// </summary>
public static class AgentEndpoints
{
    /// <summary>
    /// Bumped when the shape of these endpoints changes in a way an older page cannot handle, so a
    /// mismatched pair says so instead of failing field by field.
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>Identifies a Storava Agent to a page that is probing ports.</summary>
    public const string Product = "storava-agent";

    /// <summary>
    /// Loopback only. Chosen from the registered range rather than the dynamic one, where the
    /// operating system hands out ephemeral ports and would eventually collide.
    /// </summary>
    public static readonly int[] Ports = [47615, 47616, 47617, 47618];

    /// <summary>Always the literal address: "localhost" can resolve to IPv6 first and miss the listener.</summary>
    public const string Host = "127.0.0.1";

    /// <summary>Unauthenticated. Says only that an Agent is here and which device it is.</summary>
    public const string HelloPath = "/v1/hello";

    /// <summary>Requires a valid access token.</summary>
    public const string StatusPath = "/v1/status";

    public static string BaseAddress(int port) => $"http://{Host}:{port}";

    /// <summary>The origins the page's <c>connect-src</c> has to allow for any of this to work.</summary>
    public static IEnumerable<string> ConnectSources() => Ports.Select(BaseAddress);
}

/// <summary>The Agent's answer to a probe. Deliberately says nothing about the machine.</summary>
public sealed record AgentHello(string Product, int Protocol, Guid DeviceId, bool Paired);

/// <summary>What a caller holding a valid token may know.</summary>
public sealed record AgentStatus(
    Guid DeviceId,
    string DeviceName,
    string AgentVersion,
    DateTimeOffset StartedAtUtc,
    /// <summary>
    /// How many scans this agent keeps before discarding the older ones.
    /// <para>
    /// Reported because it is otherwise invisible: the agent discards old scans by itself, on its
    /// own database, and the desktop's Settings page governs a different one on the same machine.
    /// </para>
    /// </summary>
    int KeepRecentScans,
    /// <summary>How many it is holding now, so the number above can be read in context.</summary>
    int StoredScans);

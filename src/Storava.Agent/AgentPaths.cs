namespace Storava.Agent;

/// <summary>Where the Agent keeps its own state, beside the desktop app's and never inside it.</summary>
public static class AgentPaths
{
    /// <summary>
    /// <c>%LOCALAPPDATA%\Storava\Agent</c>. Local rather than roaming on purpose: the identity is
    /// tied to this machine, and DPAPI would not decrypt it on another one anyway.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Storava",
        "Agent");

    /// <summary>Encrypted identity and registration. Nothing else is written here.</summary>
    public static string SecretsDirectory => Path.Combine(Root, "secrets");

    /// <summary>
    /// Scans the Agent has run, in the same SQLite shape the desktop application uses. Kept apart
    /// from the desktop's own database so the two never contend for one file, and so removing the
    /// Agent takes its scans with it.
    /// </summary>
    public static string ScanDatabase => Path.Combine(Root, "agent-scans.db");

    /// <summary>
    /// Where the Agent writes its log. Running from the tray there is no console to print to, so
    /// without a file a failure at logon would leave nothing to look at. Rolled daily and kept for
    /// a week: enough to explain yesterday, not enough to become storage the tool complains about.
    /// </summary>
    public static string LogFile => Path.Combine(Root, "logs", "agent-.log");
}

/// <summary>
/// Checks the address the user passed for <c>--server</c>. An Agent hands its channel secret to
/// whatever this points at, so a typo must not be able to send it somewhere in the clear.
/// </summary>
public static class ServerAddress
{
    public static bool TryParse(string? value, out Uri address, out string error)
    {
        address = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A server address is required, for example --server https://storava.example.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
        {
            error = $"'{value}' is not a valid address.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            error = "The server address must be http or https.";
            return false;
        }

        // Plain HTTP is allowed only to a loopback address, which cannot leave the machine. Every
        // other host has to be HTTPS: pairing carries a secret the server will accept later.
        if (parsed.Scheme == Uri.UriSchemeHttp && !parsed.IsLoopback)
        {
            error = "Refusing to pair over plain HTTP. Use https, or a loopback address for local development.";
            return false;
        }

        // A base address needs its trailing slash or relative resolution eats the last segment.
        address = parsed.AbsolutePath.EndsWith('/')
            ? parsed
            : new Uri(parsed.GetLeftPart(UriPartial.Path) + "/");
        return true;
    }
}

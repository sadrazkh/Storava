using Microsoft.Win32;

namespace Storava.Agent.Tray;

/// <summary>
/// Whether the Agent starts with Windows, as a preference the user sets and can see.
/// <para>
/// Registered under <c>HKEY_CURRENT_USER</c>, never the machine-wide key. That is not only about
/// avoiding an administrator prompt: the Agent's identity and channel secret are encrypted with
/// DPAPI scoped to <em>this</em> Windows account, so an Agent started as anyone else — a service
/// running as SYSTEM, another user's session — could not decrypt them and would be useless. The
/// per-user key is the only place this can honestly live.
/// </para>
/// </summary>
public sealed class AutoStart(string? registryPath = null)
{
    /// <summary>Where Windows looks for per-user startup entries.</summary>
    public const string DefaultRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value name. Distinctive enough that nothing else will collide with it.</summary>
    public const string ValueName = "StoravaAgent";

    private readonly string _registryPath = registryPath ?? DefaultRegistryPath;

    /// <summary>True when this exact executable is registered to start at logon.</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            if (key?.GetValue(ValueName) is not string registered)
                return false;

            // A stale entry left by a copy that has since moved is not this installation starting;
            // reporting it as enabled would make the toggle lie.
            return PathsMatch(registered, CommandFor(ExecutablePath()));
        }
    }

    public void Enable() => Enable(ExecutablePath());

    /// <summary>Registers a specific executable. Separated so a test never edits the real key.</summary>
    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true);
        key?.SetValue(ValueName, CommandFor(executablePath), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_registryPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Quoted, and with the verb that opens the tray rather than the one that runs in a terminal,
    /// so a path containing spaces still starts the right thing.
    /// </summary>
    internal static string CommandFor(string executablePath) => $"\"{executablePath}\" tray";

    private static string ExecutablePath() => Environment.ProcessPath ?? string.Empty;

    private static bool PathsMatch(string registered, string expected) =>
        string.Equals(registered.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}

using Microsoft.Win32;
using Storava.Agent.Tray;

namespace Storava.Agent.Tests;

/// <summary>
/// Whether the Agent starts with Windows.
/// <para>
/// Every test writes under a throwaway key of its own, never the real
/// <c>…\CurrentVersion\Run</c>: a test suite must not be able to arrange for something to launch
/// on the developer's next logon.
/// </para>
/// </summary>
public sealed class AutoStartTests : IDisposable
{
    private readonly string _registryPath = $@"Software\Storava\Tests\{Guid.NewGuid():N}";

    private AutoStart Create() => new(_registryPath);

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void It_is_off_until_it_is_turned_on()
    {
        Assert.False(Create().IsEnabled);
    }

    [Fact]
    public void Turning_it_on_registers_this_executable()
    {
        var autoStart = Create();

        autoStart.Enable(@"C:\Program Files\Storava\storava-agent.exe");

        using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
        string value = (string)key!.GetValue(AutoStart.ValueName)!;

        // Quoted, because Program Files has a space in it and an unquoted path would be parsed as
        // "C:\Program" with arguments.
        Assert.Equal(@"""C:\Program Files\Storava\storava-agent.exe"" tray", value);
    }

    [Fact]
    public void It_registers_the_tray_verb_not_the_terminal_one()
    {
        // Started at logon there is no terminal, so 'serve' would print into nothing and leave the
        // user with no way to see or stop it.
        Assert.EndsWith(" tray", AutoStart.CommandFor(@"C:\x\storava-agent.exe"), StringComparison.Ordinal);
        Assert.DoesNotContain("serve", AutoStart.CommandFor(@"C:\x\storava-agent.exe"), StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_it_off_removes_the_entry()
    {
        var autoStart = Create();
        autoStart.Enable(@"C:\x\storava-agent.exe");

        autoStart.Disable();

        using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
        Assert.Null(key?.GetValue(AutoStart.ValueName));
    }

    [Fact]
    public void Turning_it_off_when_it_was_never_on_is_not_an_error()
    {
        Create().Disable();
    }

    [Fact]
    public void An_entry_pointing_at_a_different_copy_does_not_count_as_enabled()
    {
        var autoStart = Create();
        autoStart.Enable(@"C:\somewhere-else\storava-agent.exe");

        // A stale entry from a copy that has since moved is not this installation starting, and
        // reporting it as enabled would make the toggle lie about what will happen.
        Assert.False(autoStart.IsEnabled);
    }

    [Fact]
    public void It_writes_under_the_current_user_never_the_machine()
    {
        var autoStart = Create();
        autoStart.Enable(@"C:\x\storava-agent.exe");

        // Per-user is not a convenience: the Agent's secrets are DPAPI-encrypted for this Windows
        // account, so an Agent launched as anyone else could not read them.
        Assert.NotNull(Registry.CurrentUser.OpenSubKey(_registryPath));
        Assert.Null(Registry.LocalMachine.OpenSubKey(_registryPath));
    }

    [Fact]
    public void Enabling_twice_leaves_one_entry()
    {
        var autoStart = Create();

        autoStart.Enable(@"C:\x\storava-agent.exe");
        autoStart.Enable(@"C:\x\storava-agent.exe");

        using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
        Assert.Single(key!.GetValueNames());
    }
}

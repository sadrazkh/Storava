using System.Diagnostics;
using System.Windows.Forms;
using Serilog;
using Storava.Agent.Channel;
using Storava.Agent.Identity;

namespace Storava.Agent.Tray;

/// <summary>
/// The Agent as something a person can see and switch off.
/// <para>
/// It listens on a loopback port and can move folders to the Recycle Bin, so running invisibly
/// would be the wrong default: the tray icon is how the user knows it is there, what it is
/// connected to, and how to stop it. Nothing here can act on its own — the icon reflects state and
/// the menu gives the user the switches.
/// </para>
/// </summary>
internal sealed class AgentTrayApplication : IDisposable
{
    private readonly AgentKeyStore _keys;
    private readonly AgentRegistrationStore _registrations;
    private readonly AutoStart _autoStart;
    private readonly NotifyIcon _icon = new();
    private readonly CancellationTokenSource _shutdown = new();

    private AgentServer? _server;
    private Task<int>? _serving;
    private CancellationTokenSource? _serverStop;

    public AgentTrayApplication(
        AgentKeyStore keys,
        AgentRegistrationStore registrations,
        AutoStart autoStart)
    {
        _keys = keys;
        _registrations = registrations;
        _autoStart = autoStart;
    }

    public int Run()
    {
        _icon.Text = "Storava Agent";
        _icon.Icon = AgentIcon.Create(listening: false);
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => OpenStorava();

        BuildMenu();
        StartServing();

        System.Windows.Forms.Application.Run();
        return ExitCodes.Success;
    }

    /// <summary>
    /// Rebuilt on every open rather than kept in sync. The menu is small and opened rarely, so
    /// reading the current state each time is cheaper than a set of update paths that could drift.
    /// </summary>
    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            var registration = _registrations.Load();
            if (registration is null)
            {
                menu.Items.Add(new ToolStripMenuItem("Not connected to an account") { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("How to connect this computer…", null, (_, _) => ShowPairingHelp());
            }
            else
            {
                menu.Items.Add(new ToolStripMenuItem($"Connected as {registration.DeviceName}") { Enabled = false });
                menu.Items.Add(new ToolStripMenuItem(DescribeListening()) { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Open Storava", null, (_, _) => OpenStorava());
                menu.Items.Add(IsListening ? "Stop listening" : "Start listening", null, (_, _) => ToggleServing());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Disconnect this computer…", null, (_, _) => Unpair(registration.DeviceName));
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
            {
                Checked = _autoStart.IsEnabled,
                CheckOnClick = false
            });
            menu.Items.Add("Quit", null, (_, _) => Quit());
        };

        _icon.ContextMenuStrip = menu;
    }

    private bool IsListening => _serving is { IsCompleted: false } && _server?.Port > 0;

    private string DescribeListening() => IsListening
        ? $"Listening on 127.0.0.1:{_server!.Port}"
        : "Not listening";

    private void StartServing()
    {
        var registration = _registrations.Load();
        using var key = _keys.TryLoad();

        if (registration is null || key is null)
        {
            // Unpaired: no channel secret, so there is no way to tell the user's own page from
            // anything else on the machine. A port that answered everyone would be worse than none.
            _icon.Icon = AgentIcon.Create(listening: false);
            _icon.Text = "Storava Agent — not connected";
            return;
        }

        _serverStop = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _server = new AgentServer(registration, AgentKeyStore.FingerprintOf(key));
        _serving = Task.Run(() => _server.RunAsync(_serverStop.Token));

        // The port is only known once Kestrel has bound, so the label waits for it rather than
        // guessing and being wrong for the first second.
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 100 && _server.Port == 0; attempt++)
                await Task.Delay(50).ConfigureAwait(false);

            _icon.Icon = AgentIcon.Create(listening: _server.Port > 0);
            _icon.Text = _server.Port > 0
                ? $"Storava Agent — {registration.DeviceName} on 127.0.0.1:{_server.Port}"
                : "Storava Agent — could not listen";
        });
    }

    private void ToggleServing()
    {
        if (IsListening)
        {
            StopServing();
            _icon.Icon = AgentIcon.Create(listening: false);
            _icon.Text = "Storava Agent — not listening";
        }
        else
        {
            StartServing();
        }
    }

    private void StopServing()
    {
        _serverStop?.Cancel();

        try
        {
            _serving?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation surfaces here; the listener is going away either way.
        }

        _serverStop?.Dispose();
        _serverStop = null;
        _serving = null;
        _server = null;
    }

    private void ToggleAutoStart()
    {
        if (_autoStart.IsEnabled)
            _autoStart.Disable();
        else
            _autoStart.Enable();
    }

    private void OpenStorava()
    {
        var registration = _registrations.Load();
        if (registration is null)
        {
            ShowPairingHelp();
            return;
        }

        Open(new Uri(new Uri(registration.ServerBaseUrl), "scan").ToString());
    }

    /// <summary>
    /// Only ever an http(s) address from the Agent's own registration, and always handed to the
    /// shell as a URL. Nothing the page sends reaches this.
    /// </summary>
    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning("Could not open {Url}.", url);
        }
    }

    private static void ShowPairingHelp() => MessageBox.Show(
        """
        This computer is not connected to a Storava account yet.

        Open Storava in your browser, sign in, and choose "Connect a computer" on your account
        page. Then run this from a terminal on this computer:

            storava-agent pair --server https://your-storava-address

        Your files are not read or sent by connecting. The agent only learns which account it
        belongs to.
        """,
        "Storava Agent",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    private void Unpair(string deviceName)
    {
        var confirmed = MessageBox.Show(
            $"Disconnect \"{deviceName}\" from your Storava account?\n\n" +
            "This computer will stop answering the Storava page. Your files are not touched, and " +
            "scans this agent has taken stay on this computer.\n\n" +
            "Remove the device on your account page as well to finish.",
            "Storava Agent",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirmed != DialogResult.OK)
            return;

        StopServing();
        _registrations.Clear();
        _keys.Delete();

        _icon.Icon = AgentIcon.Create(listening: false);
        _icon.Text = "Storava Agent — not connected";
    }

    private void Quit()
    {
        StopServing();
        _shutdown.Cancel();
        _icon.Visible = false;
        System.Windows.Forms.Application.ExitThread();
    }

    public void Dispose()
    {
        StopServing();
        _shutdown.Dispose();
        _icon.Dispose();
    }
}

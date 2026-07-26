using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Storava.App.ViewModels;
using Storava.App.Views;
using Storava.Application.Abstractions;

namespace Storava.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    private static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Storava");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataDirectory);
        ConfigureLogging();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled non-UI exception.");

        try
        {
            _host = BuildHost();
            _host.Start();
            InitializeAndShow();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during startup.");
            MessageBox.Show(ex.Message, "Storava", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void InitializeAndShow()
    {
        var services = _host!.Services;

        // Load persisted settings and apply appearance before the window appears.
        var settings = services.GetRequiredService<ISettingsService>();
        settings.LoadAsync().GetAwaiter().GetResult();

        var localization = services.GetRequiredService<ILocalizationService>();
        localization.SetLanguage(settings.Current.Language);

        var theme = services.GetRequiredService<IThemeService>();
        theme.ApplyAccent(settings.Current.AccentColor);
        theme.ApplyTheme(settings.Current.Theme);

        var shell = services.GetRequiredService<ShellWindow>();
        var shellViewModel = services.GetRequiredService<ShellViewModel>();
        MainWindow = shell;
        shell.Show();
        shellViewModel.Start();
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateDefaultBuilder();
        builder.UseSerilog();
        builder.ConfigureServices(services => services.AddStoravaApp(AppDataDirectory));

        return builder.Build();
    }

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppDataDirectory, "logs", "storava-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
#if DEBUG
            .WriteTo.Console()
#endif
            .CreateLogger();

        Log.Information("Storava starting up.");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception.");
        MessageBox.Show(e.Exception.Message, "Storava", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Storava shutting down.");
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host?.Dispose();
        }
        finally
        {
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }
}

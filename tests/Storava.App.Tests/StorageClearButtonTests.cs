using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.App.ViewModels.Pages;
using Storava.App.Views.Pages;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Application.Settings;

namespace Storava.App.Tests;

/// <summary>
/// The Empty button on a storage row, through the real view.
/// <para>
/// The view model's own tests pass a row straight to the command, which proves the command and
/// proves nothing about whether a person can reach it. This builds the actual page, lets the
/// bindings run, finds the button in the visual tree and presses it — the report was that pressing
/// it does nothing, and nothing above the XAML could have caught that.
/// </para>
/// </summary>
public class StorageClearButtonTests
{
    [Fact]
    public void TheEmptyButtonIsReachableAndRunsTheCommand()
    {
        var storage = new CountingStorage();
        string? failure = OnStaThread(() =>
        {
            var view = BuildSettingsPage(storage, out var page);

            var buttons = FindClearButtons(view).Where(b => b.Visibility == Visibility.Visible).ToList();
            if (buttons.Count == 0)
                return "no Empty button was rendered for a clearable store";

            var first = buttons[0];
            if (!first.IsEnabled)
                return "the Empty button rendered disabled, so it cannot be pressed";

            if (first.Command is null)
                return "the Empty button has no command bound";

            if (!first.Command.CanExecute(first.CommandParameter))
                return "the command refused the parameter the button would pass it";

            // What a click does.
            first.Command.Execute(first.CommandParameter);
            DrainDispatcher();

            return storage.Cleared.Count == 0
                ? "pressing the button cleared nothing"
                : null;
        });

        Assert.True(failure is null, failure);
    }

    /// <summary>The row for a store this application will not empty must not offer the button.</summary>
    [Fact]
    public void AStoreThatIsKeptShowsNoButton()
    {
        var storage = new CountingStorage(clearable: false);
        string? failure = OnStaThread(() =>
        {
            var view = BuildSettingsPage(storage, out _);
            var shown = FindClearButtons(view).Where(b => b.Visibility == Visibility.Visible).ToList();
            return shown.Count == 0
                ? null
                : "an Empty button was offered for a store that is kept";
        });

        Assert.True(failure is null, failure);
    }

    // --- harness ---------------------------------------------------------------------------------

    private static SettingsView BuildSettingsPage(CountingStorage storage, out SettingsViewModel page)
    {
        ApplicationResourcesForTests.EnsureApplication();

        page = new SettingsViewModel(
            new StubSettings(), new StubTheme(), new StubLocalization(), new StubSecrets(),
            new StubMaintenance(), storage, new AcceptingDialogs(),
            NullLogger<SettingsViewModel>.Instance);

        var view = new SettingsView { DataContext = page };

        // Bindings only run once the element is measured and arranged.
        view.Measure(new Size(1200, 2000));
        view.Arrange(new Rect(0, 0, 1200, 2000));
        view.UpdateLayout();
        DrainDispatcher();

        return view;
    }

    /// <summary>Every button inside the storage list, found the way a person finds it: by looking.</summary>
    private static List<Button> FindClearButtons(DependencyObject root)
    {
        var found = new List<Button>();
        Walk(root, found);
        return found;

        static void Walk(DependencyObject node, List<Button> into)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);

                if (child is Button button && button.DataContext is Storava.App.Models.AppStorageItemModel)
                    into.Add(button);

                Walk(child, into);
            }
        }
    }

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static string? OnStaThread(Func<string?> work)
    {
        string? result = null;
        Exception? thrown = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        return thrown is not null
            ? $"{thrown.GetType().Name}: {thrown.Message} | {thrown.StackTrace}"
            : result;
    }

    // --- doubles ---------------------------------------------------------------------------------

    private sealed class CountingStorage : IAppStorageReport
    {
        private readonly bool _clearable;

        public CountingStorage(bool clearable = true) => _clearable = clearable;

        public List<AppStorageKind> Cleared { get; } = [];

        public IReadOnlyList<AppStorageEntry> Describe() =>
            [new AppStorageEntry(AppStorageKind.Logs, @"C:\storava\logs", 4096, 3, _clearable)];

        public Task<AppStorageClearResult> ClearAsync(
            AppStorageKind kind, CancellationToken cancellationToken = default)
        {
            Cleared.Add(kind);
            return Task.FromResult(new AppStorageClearResult(4096, 3, false));
        }
    }

    private sealed class AcceptingDialogs : IDialogService
    {
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText) =>
            Task.FromResult(true);
    }

    private sealed class StubLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public bool IsRightToLeft => false;

        public event EventHandler? LanguageChanged;

        public string this[string key] => key;

        public void SetLanguage(AppLanguage language) => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTheme : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;

        public string AccentColor => "#0FB5AE";

        public bool IsDark => true;

        public event EventHandler? ThemeChanged;

        public void ApplyTheme(AppTheme theme) => ThemeChanged?.Invoke(this, EventArgs.Empty);

        public void ApplyAccent(string hex)
        {
        }
    }

    private sealed class StubMaintenance : IDatabaseMaintenance
    {
        public long SizeOnDisk() => 0;

        public Task<long> CompactAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }

    private sealed class StubSecrets : ISecretStore
    {
        public string? Get(string name) => null;

        public void Set(string name, string? value)
        {
        }

        public bool Has(string name) => false;
    }
}

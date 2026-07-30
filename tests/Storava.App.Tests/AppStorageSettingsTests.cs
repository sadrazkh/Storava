using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.App.Models;
using Storava.App.ViewModels.Pages;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Application.Settings;

namespace Storava.App.Tests;

/// <summary>
/// The Settings page's view of what Storava itself occupies, and the button that empties it.
/// <para>
/// The behaviour worth pinning down is the asking. Discarding every scan is minutes of walking a
/// drive thrown away, so it goes through a confirmation — and answering no has to mean no.
/// </para>
/// </summary>
public class AppStorageSettingsTests
{
    private static SettingsViewModel Build(
        FakeStorage? storage = null,
        FakeDialogs? dialogs = null) =>
        new(new StubSettings(),
            new StubTheme(),
            new StubLocalization(),
            new StubSecrets(),
            new StubMaintenance(),
            storage ?? new FakeStorage(),
            dialogs ?? new FakeDialogs(),
            NullLogger<SettingsViewModel>.Instance);

    private static AppStorageEntry Entry(
        AppStorageKind kind, long bytes, bool canClear = true, int files = 1) =>
        new(kind, $@"C:\storava\{kind}", bytes, files, canClear);

    [Fact]
    public void ThePageListsEveryStoreItWasGiven()
    {
        var page = Build(new FakeStorage(Entry(AppStorageKind.Scans, 900), Entry(AppStorageKind.Logs, 100)));

        Assert.Equal(2, page.AppStorage.Count);
        Assert.Equal(AppStorageKind.Scans, page.AppStorage[0].Kind);
    }

    /// <summary>The headline number: the whole point of opening this section.</summary>
    [Fact]
    public void TheTotalIsEverythingAddedUp()
    {
        var page = Build(new FakeStorage(
            Entry(AppStorageKind.Scans, 1024 * 1024),
            Entry(AppStorageKind.Logs, 1024 * 1024)));

        Assert.Contains("2", page.TotalStorageText);
    }

    [Fact]
    public void AStoreThisApplicationWillNotTouchOffersNoButtonAndSaysWhy()
    {
        var page = Build(new FakeStorage(Entry(AppStorageKind.Secrets, 48, canClear: false)));

        var row = page.AppStorage[0];
        Assert.False(row.CanClear);
        Assert.True(row.HasWhyNot);
        Assert.False(page.ClearStorageCommand.CanExecute(row));
    }

    /// <summary>
    /// Nothing there yet is not the same as refused, and the row says so rather than showing a button
    /// that can only report having done nothing.
    /// </summary>
    [Fact]
    public void AnEmptyStoreOffersNoButtonEither()
    {
        var page = Build(new FakeStorage(Entry(AppStorageKind.Logs, 0, files: 0)));

        var row = page.AppStorage[0];
        Assert.False(row.CanClear);
        Assert.True(row.HasWhyNot);
    }

    [Fact]
    public async Task EmptyingTheLogsIsNotWorthAskingAbout()
    {
        var dialogs = new FakeDialogs();
        var storage = new FakeStorage(Entry(AppStorageKind.Logs, 500));

        await Build(storage, dialogs).ClearStorageCommand.ExecuteAsync(
            new AppStorageItemModel(Entry(AppStorageKind.Logs, 500), CultureInfo.InvariantCulture,
                new StubLocalization()));

        Assert.Equal(0, dialogs.Asked);
        Assert.Equal([AppStorageKind.Logs], storage.Cleared);
    }

    /// <summary>Scans can all be taken again, but not quickly. One click is not enough.</summary>
    [Fact]
    public async Task DiscardingEveryScanIsConfirmedFirst()
    {
        var dialogs = new FakeDialogs { Answer = true };
        var storage = new FakeStorage(Entry(AppStorageKind.Scans, 4096));
        var page = Build(storage, dialogs);

        await page.ClearStorageCommand.ExecuteAsync(page.AppStorage[0]);

        Assert.Equal(1, dialogs.Asked);
        Assert.Equal([AppStorageKind.Scans], storage.Cleared);
    }

    [Fact]
    public async Task SayingNoToDiscardingScansKeepsThem()
    {
        var dialogs = new FakeDialogs { Answer = false };
        var storage = new FakeStorage(Entry(AppStorageKind.Scans, 4096));
        var page = Build(storage, dialogs);

        await page.ClearStorageCommand.ExecuteAsync(page.AppStorage[0]);

        Assert.Equal(1, dialogs.Asked);
        Assert.Empty(storage.Cleared);
        Assert.Null(page.ClearStorageResultText);
    }

    /// <summary>
    /// The list is re-read afterwards, or the page would keep showing the size of something that is
    /// no longer there.
    /// </summary>
    [Fact]
    public async Task TheListIsReReadAfterClearing()
    {
        var storage = new FakeStorage(Entry(AppStorageKind.Logs, 500));
        var page = Build(storage);
        storage.Next = [Entry(AppStorageKind.Logs, 0, files: 0)];

        await page.ClearStorageCommand.ExecuteAsync(page.AppStorage[0]);

        Assert.False(page.AppStorage[0].CanClear);
    }

    /// <summary>
    /// After discarding scans the file is the same size, and the message has to say so — otherwise the
    /// unchanged number beside it looks like the button failed.
    /// </summary>
    [Fact]
    public async Task ClearingScansSaysCompactingIsStillNeeded()
    {
        var storage = new FakeStorage(Entry(AppStorageKind.Scans, 4096))
        {
            Result = new AppStorageClearResult(0, 3, NeedsCompacting: true)
        };
        var page = Build(storage, new FakeDialogs { Answer = true });

        await page.ClearStorageCommand.ExecuteAsync(page.AppStorage[0]);

        Assert.NotNull(page.ClearStorageResultText);
        Assert.Contains("NeedsCompacting", page.ClearStorageResultText);
    }

    [Fact]
    public async Task AFailureIsReportedOnThePageRatherThanThrown()
    {
        var storage = new FakeStorage(Entry(AppStorageKind.Logs, 500)) { Throw = true };
        var page = Build(storage);

        await page.ClearStorageCommand.ExecuteAsync(page.AppStorage[0]);

        Assert.Contains("ClearFailed", page.ClearStorageResultText);
        Assert.False(page.IsClearingStorage);
    }

    // --- doubles ---------------------------------------------------------------------------------

    private sealed class FakeStorage : IAppStorageReport
    {
        private IReadOnlyList<AppStorageEntry> _entries;

        public FakeStorage(params AppStorageEntry[] entries) => _entries = entries;

        /// <summary>What the next <see cref="Describe"/> should return, to model a store that changed.</summary>
        public IReadOnlyList<AppStorageEntry>? Next { get; set; }

        public List<AppStorageKind> Cleared { get; } = [];

        public AppStorageClearResult Result { get; set; } = new(500, 1, false);

        public bool Throw { get; set; }

        public IReadOnlyList<AppStorageEntry> Describe()
        {
            if (Next is not null)
            {
                _entries = Next;
                Next = null;
            }

            return _entries;
        }

        public Task<AppStorageClearResult> ClearAsync(
            AppStorageKind kind, CancellationToken cancellationToken = default)
        {
            if (Throw)
                throw new IOException("in use");

            Cleared.Add(kind);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public int Asked { get; private set; }

        public bool Answer { get; init; }

        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }

    private sealed class StubLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public bool IsRightToLeft => false;

        public event EventHandler? LanguageChanged;

        // The key itself, so a test can assert on which message was chosen without depending on
        // wording that is expected to be edited.
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

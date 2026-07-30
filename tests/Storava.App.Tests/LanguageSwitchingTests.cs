using System.IO;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.App.ViewModels.Pages;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Application.Settings;

namespace Storava.App.Tests;

/// <summary>
/// Choosing a language has to take effect and stay chosen.
/// <para>
/// It used to apply immediately and wait for Save, which produced a state with no way out. Pick
/// English, leave the page without saving, come back: the application is in English, the dropdown
/// says Persian — rebuilt from settings that were never written — and choosing Persian does
/// nothing at all, because the property already holds Persian so no change is raised. The only
/// escape was to pick English and then Persian again.
/// </para>
/// </summary>
public class LanguageSwitchingTests
{
    [Fact]
    public async Task ChoosingALanguageAppliesIt()
    {
        var localization = new RecordingLocalization();
        var page = Build(localization: localization);

        page.SelectedLanguage = AppLanguage.English;
        await page.AppearanceWrite;

        Assert.Equal(AppLanguage.English, localization.CurrentLanguage);
    }

    /// <summary>Without Save. A language is not a draft, and nobody expects to confirm one.</summary>
    [Fact]
    public async Task ChoosingALanguageKeepsItWithoutPressingSave()
    {
        var settings = new InMemorySettings();
        var page = Build(settings);

        page.SelectedLanguage = AppLanguage.English;
        await page.AppearanceWrite;

        Assert.Equal(AppLanguage.English, settings.Current.Language);
        Assert.Equal(1, settings.WriteCount);
    }

    /// <summary>
    /// The dead end itself: leave the page and come back, and the control must agree with what is
    /// on screen. A dropdown showing the language the application is not in offers a choice that
    /// cannot be made, because selecting it changes nothing.
    /// </summary>
    [Fact]
    public async Task ReturningToThePageShowsTheLanguageActuallyInUse()
    {
        var settings = new InMemorySettings();
        var localization = new RecordingLocalization();

        var first = Build(settings, localization);
        first.SelectedLanguage = AppLanguage.English;
        await first.AppearanceWrite;

        var second = Build(settings, localization);

        Assert.Equal(AppLanguage.English, second.SelectedLanguage);
    }

    /// <summary>And switching back has to work the same way round.</summary>
    [Fact]
    public async Task SwitchingBackWorksTheSameWay()
    {
        var settings = new InMemorySettings();
        var localization = new RecordingLocalization();

        var page = Build(settings, localization);
        page.SelectedLanguage = AppLanguage.English;
        await page.AppearanceWrite;
        page.SelectedLanguage = AppLanguage.Persian;
        await page.AppearanceWrite;

        Assert.Equal(AppLanguage.Persian, localization.CurrentLanguage);
        Assert.Equal(AppLanguage.Persian, settings.Current.Language);
    }

    [Fact]
    public async Task ChoosingAThemeKeepsItToo()
    {
        var settings = new InMemorySettings();
        var page = Build(settings);

        page.SelectedTheme = AppTheme.Light;
        await page.AppearanceWrite;

        Assert.Equal(AppTheme.Light, settings.Current.Theme);
    }

    /// <summary>
    /// A half-typed model name must not be committed just because somebody changed the theme.
    /// Those fields are a form, and a form is submitted deliberately.
    /// </summary>
    [Fact]
    public async Task AnAppearanceChangeDoesNotCommitTheAiFormBeingEdited()
    {
        var settings = new InMemorySettings();
        var page = Build(settings);

        page.AiModel = "half-typed-mod";
        page.SelectedTheme = AppTheme.Light;
        await page.AppearanceWrite;

        Assert.Equal("openrouter/free", settings.Current.Ai.ModelName);
        Assert.Equal(AppTheme.Light, settings.Current.Theme);
    }

    /// <summary>
    /// A failed write leaves the choice applied rather than throwing at the user — and the page
    /// still has to agree with the window.
    /// <para>
    /// This is the case that separates the two things the page could read from. Settings and the
    /// localization service normally say the same thing, so only a write that did not happen shows
    /// whether the dropdown follows the application or a stored value that never caught up. Read
    /// the stored value and this is the dead end all over again: English on screen, Persian in the
    /// dropdown, and selecting Persian does nothing because that is already what it holds.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFailedWriteLeavesThePageAgreeingWithTheWindow()
    {
        var settings = new InMemorySettings { FailWrites = true };
        var localization = new RecordingLocalization();
        var page = Build(settings, localization);

        page.SelectedLanguage = AppLanguage.English;
        await page.AppearanceWrite;

        Assert.Equal(AppLanguage.English, localization.CurrentLanguage);
        Assert.Equal(AppLanguage.Persian, settings.Current.Language); // the write really did fail

        Assert.Equal(AppLanguage.English, Build(settings, localization).SelectedLanguage);
    }

    // --- doubles -----------------------------------------------------------------------------

    private static SettingsViewModel Build(
        InMemorySettings? settings = null,
        RecordingLocalization? localization = null) =>
        new(settings ?? new InMemorySettings(),
            new RecordingTheme(),
            localization ?? new RecordingLocalization(),
            new NoSecrets(),
            new NoMaintenance(),
            NullLogger<SettingsViewModel>.Instance);

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public int WriteCount { get; private set; }

        public bool FailWrites { get; init; }

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
                throw new IOException("The settings file could not be written.");

            Current = settings;
            WriteCount++;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Persian;

        public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("fa-IR");

        public bool IsRightToLeft => CurrentLanguage == AppLanguage.Persian;

        public event EventHandler? LanguageChanged;

        public string this[string key] => key;

        public void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
            CurrentCulture = CultureInfo.GetCultureInfo(language == AppLanguage.Persian ? "fa-IR" : "en-US");
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingTheme : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        public string AccentColor { get; private set; } = "#0FB5AE";

        public bool IsDark => CurrentTheme != AppTheme.Light;

        public event EventHandler? ThemeChanged;

        public void ApplyTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyAccent(string hex) => AccentColor = hex;
    }

    /// <summary>Nothing here touches the database; the page only reads a size to display.</summary>
    private sealed class NoMaintenance : IDatabaseMaintenance
    {
        public long SizeOnDisk() => 0;

        public Task<long> CompactAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Get(string name) => null;

        public void Set(string name, string? value)
        {
        }

        public bool Has(string name) => false;
    }
}

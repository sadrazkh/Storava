using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly ISecretStore _secrets;
    private readonly IDatabaseMaintenance _maintenance;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private AppLanguage _selectedLanguage;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private string _accentColor = "#0FB5AE";
    [ObservableProperty] private int _keepRecentScans;

    /// <summary>How much room the scan database takes, so the button below has a number beside it.</summary>
    [ObservableProperty] private string _databaseSizeText = "—";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompactDatabaseCommand))]
    private bool _isCompacting;

    [ObservableProperty] private string? _compactResultText;
    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _aiModel = "openrouter/free";
    [ObservableProperty] private string _aiBaseUrl = "https://openrouter.ai/api/v1";
    [ObservableProperty] private double _aiTemperature;
    [ObservableProperty] private int _aiMaxTokens;
    [ObservableProperty] private int _aiTimeoutSeconds;
    [ObservableProperty] private int _aiRetryCount;
    [ObservableProperty] private bool _aiAllowUnknownFolders;
    [ObservableProperty] private bool _aiAllowReportGeneration;
    [ObservableProperty] private bool _hasApiKey;
    [ObservableProperty] private bool _isSaved;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        ILocalizationService localization,
        ISecretStore secrets,
        IDatabaseMaintenance maintenance,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _theme = theme;
        _localization = localization;
        _secrets = secrets;
        _maintenance = maintenance;
        _logger = logger;

        var current = settings.Current;

        // Read from the services that are actually driving the window, not from stored settings.
        // They are the same thing now that appearance is written the moment it changes, but this
        // page is the one place where showing a value that disagrees with what is on screen is
        // worse than useless — it offers a control that appears to do nothing.
        _selectedLanguage = localization.CurrentLanguage;
        _selectedTheme = theme.CurrentTheme;
        _accentColor = current.AccentColor;
        _keepRecentScans = current.KeepRecentScans;
        _aiEnabled = current.Ai.Enabled;
        _aiModel = current.Ai.ModelName;
        _aiBaseUrl = current.Ai.BaseUrl;
        _aiTemperature = current.Ai.Temperature;
        _aiMaxTokens = current.Ai.MaxTokens;
        _aiTimeoutSeconds = current.Ai.TimeoutSeconds;
        _aiRetryCount = current.Ai.RetryCount;
        _aiAllowUnknownFolders = current.Ai.AllowUnknownFolderAnalysis;
        _aiAllowReportGeneration = current.Ai.AllowReportGeneration;

        _hasApiKey = _secrets.Has(SecretNames.OpenRouterApiKey);
        RefreshDatabaseSize();
    }

    public IReadOnlyList<AppLanguage> Languages { get; } = [AppLanguage.Persian, AppLanguage.English];

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.Light, AppTheme.Dark, AppTheme.System];

    /// <summary>
    /// How many scans may be kept. A short list rather than a free number: the meaningful range is
    /// small, and a text box here would invite a value that silently loses a scan.
    /// </summary>
    public IReadOnlyList<int> KeepScanOptions { get; } = [1, 2, 3, 5, 10];

    /// <summary>Curated accent swatches for the picker.</summary>
    public IReadOnlyList<string> AccentPresets { get; } =
        ["#0FB5AE", "#6366F1", "#8B5CF6", "#EC4899", "#F97316", "#22C55E", "#0EA5E9", "#EF4444"];

    // Appearance takes effect and is kept, in one step.
    //
    // These used to apply immediately and wait for Save, which produced a state nobody could get
    // out of. Pick English, leave the page without saving, come back: the application is in
    // English, the dropdown says Persian — because it was rebuilt from settings that were never
    // written — and choosing Persian does nothing at all, since the property already holds
    // Persian and so nothing changes. The only escape was to pick English and then Persian again.
    //
    // Choosing a language is not a draft. It is not something a person expects to confirm, and it
    // is the one setting whose effect is visible everywhere the moment it is made, so a copy of it
    // waiting to be saved can only ever disagree with what is on screen.
    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        _localization.SetLanguage(value);
        PersistAppearance();
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _theme.ApplyTheme(value);
        PersistAppearance();
    }

    partial void OnAccentColorChanged(string value)
    {
        _theme.ApplyAccent(value);
        PersistAppearance();
    }

    /// <summary>
    /// Writes what is already on screen, leaving everything else on the page as the user left it.
    /// <para>
    /// Built from <see cref="ISettingsService.Current"/> rather than from this page's fields on
    /// purpose: a half-typed API model name or an out-of-range timeout should not be committed
    /// just because somebody changed the theme. Those still belong to Save.
    /// </para>
    /// </summary>
    private void PersistAppearance()
    {
        var updated = _settings.Current.Clone();
        updated.Language = SelectedLanguage;
        updated.Theme = SelectedTheme;
        updated.AccentColor = AccentColor;

        AppearanceWrite = WriteAsync(updated);

        async Task WriteAsync(Storava.Application.Settings.AppSettings snapshot)
        {
            try
            {
                await _settings.SaveAsync(snapshot).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // Nothing here is worth tearing the application down for: the change is applied
                // and visible, and the worst case is that it does not survive a restart.
                _logger.LogWarning(ex, "An appearance change could not be saved.");
            }
        }
    }

    /// <summary>
    /// The write started by the most recent appearance change; already completed when none has run.
    /// <para>
    /// Held so that a test can wait for it deliberately. Nothing in the page waits: choosing a
    /// language is meant to feel instant, and it does because applying it and recording it are
    /// separate things.
    /// </para>
    /// </summary>
    internal Task AppearanceWrite { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Gives the room freed by discarded scans back to the operating system.
    /// <para>
    /// A button rather than something automatic. SQLite rewrites the whole file under an exclusive
    /// lock, and measuring it showed every query issued meanwhile waiting for the entire rewrite —
    /// around half a minute on a database of the size this exists to deal with. Doing that behind
    /// somebody's back, right after a scan when they are about to read the results, is the kind of
    /// unexplained wait this release spent its time removing.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompactDatabase))]
    private async Task CompactDatabaseAsync()
    {
        IsCompacting = true;
        CompactResultText = null;

        try
        {
            long reclaimed = await _maintenance.CompactAsync().ConfigureAwait(true);

            CompactResultText = reclaimed > 0
                ? string.Format(
                    _localization.CurrentCulture,
                    _localization["Str.Settings.Storage.Compacted"],
                    new ByteSize(reclaimed).Humanize(_localization.CurrentCulture))
                : _localization["Str.Settings.Storage.CompactedNothing"];
        }
        catch (Exception ex)
        {
            // Most often there is not enough free disk to write the rewritten copy, which is a real
            // possibility for someone using this application at all.
            _logger.LogWarning(ex, "Compacting the database failed.");
            CompactResultText = _localization["Str.Settings.Storage.CompactFailed"];
        }
        finally
        {
            IsCompacting = false;
            RefreshDatabaseSize();
        }
    }

    private bool CanCompactDatabase() => !IsCompacting;

    private void RefreshDatabaseSize() =>
        DatabaseSizeText = new ByteSize(_maintenance.SizeOnDisk()).Humanize(_localization.CurrentCulture);

    partial void OnAiEnabledChanged(bool value) => IsSaved = false;
    partial void OnAiModelChanged(string value) => IsSaved = false;
    partial void OnAiBaseUrlChanged(string value) => IsSaved = false;
    partial void OnAiTemperatureChanged(double value) => IsSaved = false;
    partial void OnAiMaxTokensChanged(int value) => IsSaved = false;
    partial void OnAiTimeoutSecondsChanged(int value) => IsSaved = false;
    partial void OnAiRetryCountChanged(int value) => IsSaved = false;
    partial void OnAiAllowUnknownFoldersChanged(bool value) => IsSaved = false;
    partial void OnAiAllowReportGenerationChanged(bool value) => IsSaved = false;

    [RelayCommand]
    private void SelectAccent(string hex) => AccentColor = hex;

    /// <summary>
    /// Takes the key straight from the password box and hands it to the encrypted store. It is
    /// never held in an observable property, so it cannot surface in a binding trace or a dump.
    /// </summary>
    [RelayCommand]
    private void SaveApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        _secrets.Set(SecretNames.OpenRouterApiKey, key.Trim());
        HasApiKey = true;
    }

    [RelayCommand]
    private void RemoveApiKey()
    {
        _secrets.Set(SecretNames.OpenRouterApiKey, null);
        HasApiKey = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var updated = _settings.Current.Clone();
        updated.Language = SelectedLanguage;
        updated.Theme = SelectedTheme;
        updated.AccentColor = AccentColor;

        // One at the bottom: keeping none would mean deleting the scan the user just took. Ten at
        // the top because each one is millions of rows, which is how the database reached seven
        // gigabytes in the first place.
        updated.KeepRecentScans = Math.Clamp(KeepRecentScans, 1, 10);

        updated.Ai.Enabled = AiEnabled;
        updated.Ai.ModelName = string.IsNullOrWhiteSpace(AiModel) ? "openrouter/free" : AiModel.Trim();
        updated.Ai.BaseUrl = string.IsNullOrWhiteSpace(AiBaseUrl) ? "https://openrouter.ai/api/v1" : AiBaseUrl.Trim();

        // Clamp rather than reject: a nonsensical number in a text box should not block saving the
        // rest of the page, and the provider must never be handed an impossible request.
        updated.Ai.Temperature = Math.Clamp(AiTemperature, 0, 2);
        updated.Ai.MaxTokens = Math.Clamp(AiMaxTokens, 256, 32_000);
        updated.Ai.TimeoutSeconds = Math.Clamp(AiTimeoutSeconds, 10, 600);
        updated.Ai.RetryCount = Math.Clamp(AiRetryCount, 0, 5);
        updated.Ai.AllowUnknownFolderAnalysis = AiAllowUnknownFolders;
        updated.Ai.AllowReportGeneration = AiAllowReportGeneration;

        await _settings.SaveAsync(updated).ConfigureAwait(true);

        // Reflect whatever the clamps changed, so the page never shows a value that was not saved.
        KeepRecentScans = updated.KeepRecentScans;
        AiTemperature = updated.Ai.Temperature;
        AiMaxTokens = updated.Ai.MaxTokens;
        AiTimeoutSeconds = updated.Ai.TimeoutSeconds;
        AiRetryCount = updated.Ai.RetryCount;

        IsSaved = true;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.Application.Abstractions;
using Storava.Application.Common;

namespace Storava.App.ViewModels.Pages;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly ISecretStore _secrets;

    [ObservableProperty] private AppLanguage _selectedLanguage;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private string _accentColor = "#0FB5AE";
    [ObservableProperty] private int _keepRecentScans;
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
        ISecretStore secrets)
    {
        _settings = settings;
        _theme = theme;
        _localization = localization;
        _secrets = secrets;

        var current = settings.Current;
        _selectedLanguage = current.Language;
        _selectedTheme = current.Theme;
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

    // Live preview: appearance changes apply immediately; Save persists them.
    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        _localization.SetLanguage(value);
        IsSaved = false;
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _theme.ApplyTheme(value);
        IsSaved = false;
    }

    partial void OnAccentColorChanged(string value)
    {
        _theme.ApplyAccent(value);
        IsSaved = false;
    }

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

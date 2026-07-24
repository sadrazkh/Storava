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

    [ObservableProperty] private AppLanguage _selectedLanguage;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private string _accentColor = "#0FB5AE";
    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _aiModel = "openrouter/free";
    [ObservableProperty] private string _aiBaseUrl = "https://openrouter.ai/api/v1";
    [ObservableProperty] private bool _isSaved;

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        ILocalizationService localization)
    {
        _settings = settings;
        _theme = theme;
        _localization = localization;

        var current = settings.Current;
        _selectedLanguage = current.Language;
        _selectedTheme = current.Theme;
        _accentColor = current.AccentColor;
        _aiEnabled = current.Ai.Enabled;
        _aiModel = current.Ai.ModelName;
        _aiBaseUrl = current.Ai.BaseUrl;
    }

    public IReadOnlyList<AppLanguage> Languages { get; } = [AppLanguage.Persian, AppLanguage.English];

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.Light, AppTheme.Dark, AppTheme.System];

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

    [RelayCommand]
    private void SelectAccent(string hex) => AccentColor = hex;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var updated = _settings.Current.Clone();
        updated.Language = SelectedLanguage;
        updated.Theme = SelectedTheme;
        updated.AccentColor = AccentColor;
        updated.Ai.Enabled = AiEnabled;
        updated.Ai.ModelName = string.IsNullOrWhiteSpace(AiModel) ? "openrouter/free" : AiModel.Trim();
        updated.Ai.BaseUrl = string.IsNullOrWhiteSpace(AiBaseUrl) ? "https://openrouter.ai/api/v1" : AiBaseUrl.Trim();

        await _settings.SaveAsync(updated).ConfigureAwait(true);
        IsSaved = true;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.Application.Abstractions;
using Storava.Application.Common;

namespace Storava.App.ViewModels.Pages;

public sealed partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private bool _syncingFromServices;

    [ObservableProperty]
    private AppLanguage _selectedLanguage;

    [ObservableProperty]
    private bool _isDarkTheme;

    public WelcomeViewModel(
        ISettingsService settings,
        INavigationService navigation,
        IThemeService theme,
        ILocalizationService localization)
    {
        _settings = settings;
        _navigation = navigation;
        _theme = theme;
        _localization = localization;

        _selectedLanguage = localization.CurrentLanguage;
        _isDarkTheme = theme.CurrentTheme != AppTheme.Light;

        // The shell's top bar can also change language/theme; keep this page in sync so
        // "Get started" never persists a stale choice.
        _localization.LanguageChanged += OnLanguageChanged;
        _theme.ThemeChanged += OnThemeChanged;
    }

    public IReadOnlyList<AppLanguage> Languages { get; } = [AppLanguage.Persian, AppLanguage.English];

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        if (!_syncingFromServices)
            _localization.SetLanguage(value);
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (!_syncingFromServices)
            _theme.ApplyTheme(value ? AppTheme.Dark : AppTheme.Light);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => SyncFromServices(() => SelectedLanguage = _localization.CurrentLanguage);

    private void OnThemeChanged(object? sender, EventArgs e)
        => SyncFromServices(() => IsDarkTheme = _theme.CurrentTheme != AppTheme.Light);

    private void SyncFromServices(Action apply)
    {
        _syncingFromServices = true;
        try
        {
            apply();
        }
        finally
        {
            _syncingFromServices = false;
        }
    }

    [RelayCommand]
    private async Task GetStartedAsync()
    {
        var updated = _settings.Current.Clone();
        updated.Language = SelectedLanguage;
        updated.Theme = IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
        updated.OnboardingCompleted = true;
        await _settings.SaveAsync(updated).ConfigureAwait(true);

        _navigation.NavigateTo(NavigationKeys.Dashboard);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _theme.ThemeChanged -= OnThemeChanged;
    }
}

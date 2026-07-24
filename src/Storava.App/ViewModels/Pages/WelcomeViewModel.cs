using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.Application.Abstractions;
using Storava.Application.Common;

namespace Storava.App.ViewModels.Pages;

public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;

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

        _selectedLanguage = settings.Current.Language;
        _isDarkTheme = settings.Current.Theme != AppTheme.Light;
    }

    public IReadOnlyList<AppLanguage> Languages { get; } = [AppLanguage.Persian, AppLanguage.English];

    partial void OnSelectedLanguageChanged(AppLanguage value) => _localization.SetLanguage(value);

    partial void OnIsDarkThemeChanged(bool value) => _theme.ApplyTheme(value ? AppTheme.Dark : AppTheme.Light);

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
}

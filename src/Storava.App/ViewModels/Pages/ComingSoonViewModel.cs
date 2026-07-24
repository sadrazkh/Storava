using CommunityToolkit.Mvvm.ComponentModel;
using Storava.Application.Abstractions;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Honest placeholder for pages scheduled in later phases. It clearly states the area is
/// not yet implemented rather than pretending to be functional.
/// </summary>
public sealed partial class ComingSoonViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalizationService _localization;
    private string _titleKey = "Str.App.Name";

    [ObservableProperty]
    private string _header = string.Empty;

    public ComingSoonViewModel(ILocalizationService localization)
    {
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    public void Configure(string titleResourceKey)
    {
        _titleKey = titleResourceKey;
        Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => Header = _localization[_titleKey];

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}

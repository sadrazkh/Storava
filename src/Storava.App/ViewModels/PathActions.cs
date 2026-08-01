using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.Application.Abstractions;

namespace Storava.App.ViewModels;

/// <summary>
/// Copy a path, or open it where it lives. One object, reachable from every page.
/// <para>
/// It hangs off the shell rather than being repeated on each page's view model, so a row anywhere
/// in the window can bind through the window itself:
/// </para>
/// <code>
/// Command="{Binding DataContext.Paths.CopyCommand, RelativeSource={RelativeSource AncestorType=Window}}"
/// CommandParameter="{Binding Path}"
/// </code>
/// <para>
/// Twelve view models would otherwise each have grown the same two commands and the same
/// constructor argument to serve them, which is twelve chances for one page to be forgotten — and
/// being forgotten on some pages is exactly the complaint this answers.
/// </para>
/// </summary>
public sealed partial class PathActions : ObservableObject
{
    private readonly IPathPresenter _presenter;
    private readonly ILocalizationService _localization;

    public PathActions(IPathPresenter presenter, ILocalizationService localization)
    {
        _presenter = presenter;
        _localization = localization;
    }

    /// <summary>
    /// What just happened, for a line the user can see. Empty most of the time.
    /// <para>
    /// A copy that silently succeeds looks the same as a button that does nothing, which is the
    /// mistake this release has already made once with a different button.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _notice = string.Empty;

    [RelayCommand]
    private void Copy(string? path)
    {
        Notice = _presenter.Copy(path)
            ? _localization["Str.Common.Path.Copied"]
            : _localization["Str.Common.Path.CopyFailed"];
    }

    /// <summary>
    /// Opens the file manager with the item selected.
    /// <para>
    /// A path from a scan describes the disk as it was; the user may have moved or removed it
    /// since, and saying so is more use than an Explorer window opened on nothing.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Reveal(string? path)
    {
        if (!_presenter.CanReveal(path))
        {
            Notice = _localization["Str.Common.Path.Gone"];
            return;
        }

        Notice = _presenter.Reveal(path)
            ? string.Empty
            : _localization["Str.Common.Path.RevealFailed"];
    }
}

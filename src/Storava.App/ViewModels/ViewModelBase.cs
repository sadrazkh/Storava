using CommunityToolkit.Mvvm.ComponentModel;

namespace Storava.App.ViewModels;

/// <summary>Base for all ViewModels. Keeps view logic thin and observable.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// True while this page is fetching something the user is waiting for.
    /// <para>
    /// It lives here rather than on each page because the shell draws one indicator for whatever
    /// page is on screen. A page that forgot to declare it would simply show nothing while it
    /// worked, which is the failure this ends: once the database work moved off the UI thread the
    /// window stayed responsive, and a page reading a scan of several million rows stopped looking
    /// frozen and started looking like a page that had decided to stay empty.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>
    /// What is being waited for, as a localization key. Empty falls back to a general wording.
    /// </summary>
    [ObservableProperty] private string _loadingMessageKey = string.Empty;

    /// <summary>
    /// Marks the page busy until the returned token is disposed.
    /// <para>
    /// A <c>using</c> block rather than a pair of assignments, because the half that matters is the
    /// one that runs when something throws — and that is the half a person forgets. A page left
    /// showing a spinner over a failure it never mentions is worse than showing nothing.
    /// </para>
    /// </summary>
    protected IDisposable BeginLoading(string messageKey = "")
    {
        LoadingMessageKey = messageKey;
        IsLoading = true;
        return new LoadingScope(this);
    }

    private sealed class LoadingScope(ViewModelBase owner) : IDisposable
    {
        public void Dispose()
        {
            owner.IsLoading = false;
            owner.LoadingMessageKey = string.Empty;
        }
    }
}

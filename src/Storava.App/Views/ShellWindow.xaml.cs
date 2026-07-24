using System.Windows;
using Storava.App.ViewModels;

namespace Storava.App.Views;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public ShellWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        // Rebuild the grouped rail after a language switch so item and group text refresh.
        _viewModel.NavRefreshRequested += (_, _) => NavList.Items.Refresh();
    }
}

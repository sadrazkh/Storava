using System.Windows;
using System.Windows.Controls;
using Storava.App.ViewModels;
using Storava.App.ViewModels.Pages;

namespace Storava.App.Views.Pages;

public partial class ScanExplorerView : UserControl
{
    public ScanExplorerView() => InitializeComponent();

    // TreeView.SelectedItem is read-only, so bridge the tree selection to the ViewModel here.
    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ScanExplorerViewModel vm && e.NewValue is ScanNodeViewModel { IsPlaceholder: false } node)
            vm.SelectedItem = node.Item;
    }
}

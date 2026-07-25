using System.Windows.Controls;
using Storava.App.Controls;
using Storava.App.ViewModels.Pages;

namespace Storava.App.Views.Pages;

public partial class AnalysisView : UserControl
{
    public AnalysisView() => InitializeComponent();

    private async void Treemap_OnItemActivated(object? sender, TreemapItem item)
    {
        if (DataContext is AnalysisViewModel viewModel)
            await viewModel.DrillDownAsync(item);
    }
}

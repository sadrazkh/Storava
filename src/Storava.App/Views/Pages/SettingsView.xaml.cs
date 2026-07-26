using System.Windows;
using System.Windows.Controls;
using Storava.App.ViewModels.Pages;

namespace Storava.App.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// PasswordBox.Password is not a dependency property, and deliberately so — binding it would
    /// leave the key sitting in a WPF property store. It is read once here, handed to the
    /// encrypted store, and cleared from the box immediately.
    /// </summary>
    private void OnSaveApiKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        viewModel.SaveApiKeyCommand.Execute(ApiKeyBox.Password);
        ApiKeyBox.Clear();
    }
}

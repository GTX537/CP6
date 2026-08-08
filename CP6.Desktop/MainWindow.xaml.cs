using System.Windows;
using System.Windows.Controls;

namespace CP6.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public ShellViewModel ViewModel { get; }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        => ViewModel.Password = ((PasswordBox)sender).Password;
}

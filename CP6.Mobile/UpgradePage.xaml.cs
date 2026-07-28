namespace CP6.Mobile;

public partial class UpgradePage : ContentPage
{
    public UpgradePage(UpgradeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

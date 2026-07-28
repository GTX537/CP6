namespace CP6.Mobile;

using ZXing.Net.Maui;

public partial class MoveScanPage : ContentPage
{
    private readonly MoveScanViewModel _viewModel;
    public MoveScanPage(MoveScanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            MainThread.BeginInvokeOnMainThread(
                async () => await _viewModel.AcceptExternalScanAsync(value));
    }
}

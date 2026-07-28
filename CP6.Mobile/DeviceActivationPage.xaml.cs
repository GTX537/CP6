using ZXing.Net.Maui;

namespace CP6.Mobile;

public partial class DeviceActivationPage : ContentPage
{
    private readonly DeviceActivationViewModel _viewModel;

    public DeviceActivationPage(DeviceActivationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            MainThread.BeginInvokeOnMainThread(
                async () => await _viewModel.AcceptQrAsync(value));
    }
}

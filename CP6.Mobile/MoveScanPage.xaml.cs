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
        Dispatcher.Dispatch(() => ScanEntry.Focus());
    }

    private async void ScanEntry_OnCompleted(object? sender, EventArgs e)
    {
        if (_viewModel.HidTerminator != CP6.Client.Core.ScannerHidTerminator.Enter)
            return;
        await SubmitHidAsync();
    }

    private async void ScanEntry_OnUnfocused(object? sender, FocusEventArgs e)
    {
        if (_viewModel.HidTerminator != CP6.Client.Core.ScannerHidTerminator.Tab
            || string.IsNullOrWhiteSpace(ScanEntry.Text))
            return;
        await SubmitHidAsync();
    }

    private async Task SubmitHidAsync()
    {
        await _viewModel.AcceptHidScanAsync(ScanEntry.Text ?? string.Empty);
        Dispatcher.Dispatch(() => ScanEntry.Focus());
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            MainThread.BeginInvokeOnMainThread(
                async () => await _viewModel.AcceptExternalScanAsync(
                    value,
                    CP6.Client.Core.ScannerInputSource.Camera));
    }
}

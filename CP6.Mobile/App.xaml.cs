using CP6.Client.Core;

namespace CP6.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;
    private readonly WmsRealtimeService _realtime;

    public App(
        AppShell shell,
        ClientDeviceHeartbeatLoop heartbeat,
        WmsRealtimeService realtime)
    {
        InitializeComponent();
        _shell = shell;
        _heartbeat = heartbeat;
        _realtime = realtime;
        _heartbeat.StateChanged += HeartbeatOnStateChanged;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Activated += WindowOnActivated;
        window.Resumed += WindowOnActivated;
        window.Stopped += WindowOnStopped;
        window.Destroying += WindowOnStopped;
        return window;
    }

    private async void WindowOnActivated(object? sender, EventArgs e)
        => await _heartbeat.StartAsync();

    private async void WindowOnStopped(object? sender, EventArgs e)
        => await _heartbeat.StopAsync();

    private void HeartbeatOnStateChanged(
        object? sender,
        ClientDeviceHeartbeatStateChangedEventArgs e)
    {
        if (e.Status != ClientDeviceHeartbeatStatus.Rejected)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await _realtime.StopAsync();
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("//login");
        });
    }
}

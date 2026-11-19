using CP6.Client.Core;

namespace CP6.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;
    private readonly WmsRealtimeService _realtime;
    private readonly IClientSessionService _sessions;

    public App(
        AppShell shell,
        ClientDeviceHeartbeatLoop heartbeat,
        WmsRealtimeService realtime,
        IClientSessionService sessions)
    {
        InitializeComponent();
        _shell = shell;
        _heartbeat = heartbeat;
        _realtime = realtime;
        _sessions = sessions;
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

        var interaction = _sessions.InteractionVersion;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_sessions.Current != null || _sessions.InteractionVersion != interaction) return;
            await _realtime.StopAsync();
            if (_sessions.Current == null && _sessions.InteractionVersion == interaction && Shell.Current is not null)
                await Shell.Current.GoToAsync("//login");
        });
    }
}

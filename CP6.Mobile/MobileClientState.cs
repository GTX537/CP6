using CommunityToolkit.Mvvm.Messaging;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Mobile;

public sealed record MobileSessionChanged;

public sealed class MobileClientState
{
    private readonly WmsTaskService _tasks;
    private Selection? _selection;

    public MobileClientState(WmsTaskService tasks, IClientSessionService sessions)
    {
        _tasks = tasks;
        sessions.SessionChanged += (_, _) =>
        {
            var selected = Volatile.Read(ref _selection);
            if (selected != null && !_tasks.IsCurrent(selected.Scope))
                Interlocked.CompareExchange(ref _selection, null, selected);
            WeakReferenceMessenger.Default.Send(new MobileSessionChanged());
        };
    }

    public MobileTask? SelectedTask
    {
        get
        {
            var selection = Volatile.Read(ref _selection);
            return selection is { } value && _tasks.IsCurrent(value.Scope) ? value.Task : null;
        }
        set => Volatile.Write(ref _selection, value == null ? null : new Selection(_tasks.CaptureScope(), value));
    }

    private sealed record Selection(ClientRequestScope Scope, MobileTask Task);

    public ClientUpgradeDecision? UpgradeDecision { get; set; }
    public bool IsDeviceActivated { get; set; } =
        Preferences.Default.Get("cp6.device-activated", false);

    public void SetDeviceActivated(bool activated)
    {
        IsDeviceActivated = activated;
        Preferences.Default.Set("cp6.device-activated", activated);
    }
}

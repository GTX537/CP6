using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Mobile;

public sealed class MobileClientState
{
    public MobileTask? SelectedTask { get; set; }
    public ClientUpgradeDecision? UpgradeDecision { get; set; }
    public bool IsDeviceActivated { get; set; } =
        Preferences.Default.Get("cp6.device-activated", false);
}

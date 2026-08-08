using System.Net.NetworkInformation;
using CP6.Client.Core;

namespace CP6.Desktop;

internal sealed class DesktopDeviceHeartbeatContext
    : IClientDeviceHeartbeatContext
{
    public ValueTask<ClientDeviceHeartbeatSnapshot> CaptureAsync(
        CancellationToken ct = default)
    {
        string? network = null;
        try
        {
            network = NetworkInterface.GetIsNetworkAvailable()
                ? "Online"
                : "Offline";
        }
        catch (NetworkInformationException)
        {
        }
        return ValueTask.FromResult(new ClientDeviceHeartbeatSnapshot(
            DesktopSettings.IsDeviceActivated(),
            NetworkType: network));
    }

    public void MarkActivationRequired()
        => DesktopSettings.WriteDeviceActivation(false);
}

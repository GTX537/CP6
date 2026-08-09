using CP6.Client.Core;

namespace CP6.Mobile;

internal sealed class MobileDeviceHeartbeatContext(MobileClientState state)
    : IClientDeviceHeartbeatContext
{
    public ValueTask<ClientDeviceHeartbeatSnapshot> CaptureAsync(
        CancellationToken ct = default)
    {
        int? batteryPercent = null;
        string? networkType = null;

        try
        {
            var level = Battery.Default.ChargeLevel;
            if (!double.IsNaN(level))
                batteryPercent = Math.Clamp(
                    (int)Math.Round(level * 100),
                    0,
                    100);
        }
        catch (Exception ex) when (
            ex is FeatureNotSupportedException
                or FeatureNotEnabledException
                or PermissionException)
        {
        }

        try
        {
            networkType = string.Join(
                ",",
                Connectivity.Current.ConnectionProfiles.Select(
                    profile => profile.ToString()));
        }
        catch (Exception ex) when (
            ex is FeatureNotSupportedException
                or FeatureNotEnabledException
                or PermissionException)
        {
        }

        var taskNo = state.SelectedTask?.Status == 1
            ? state.SelectedTask.TaskNo
            : null;
        return ValueTask.FromResult(new ClientDeviceHeartbeatSnapshot(
            state.IsDeviceActivated,
            taskNo,
            batteryPercent,
            networkType));
    }

    public void MarkActivationRequired()
        => state.SetDeviceActivated(false);
}

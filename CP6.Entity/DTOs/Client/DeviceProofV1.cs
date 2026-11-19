using System.Text.Json;

namespace CP6.Entity.DTOs.Client;

public static class DeviceProofV1
{
    public const string RegisteredDeviceGate = "registered-device-v1";

    public static bool IsFresh(DateTimeOffset timestamp)
        => Math.Abs((DateTimeOffset.UtcNow - timestamp).TotalMinutes) <= 5;

    public static byte[] ActivationResult(Guid operation, string tenant, string device, DateTimeOffset timestamp)
        => JsonSerializer.SerializeToUtf8Bytes(new object[]
            { "cp6-activation-result-v1", operation, tenant, device, timestamp.ToUnixTimeSeconds() });

    public static byte[] DeviceBinding(Guid operation, string device, string platform, string appVersion, DateTimeOffset timestamp)
        => JsonSerializer.SerializeToUtf8Bytes(new object[]
            { "cp6-auth-device-binding-v1", operation, device, platform, appVersion, timestamp.ToUnixTimeSeconds() });
}

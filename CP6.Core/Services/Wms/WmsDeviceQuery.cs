using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Wms;

/// <summary>设备联动 v1 占位：返空（真接 AGV/WCS 实时流 = P3+）。架构留可注入数据点，未来换源不返工。</summary>
public class WmsDeviceQuery : IWmsDeviceQuery
{
    public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid floorId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeviceDto>>(Array.Empty<DeviceDto>());
}

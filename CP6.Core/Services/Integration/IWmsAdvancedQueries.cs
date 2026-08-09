namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 高级可视化只读查询契约族（08；消费者 Space 侧定义，WMS 接真实现）。
/// 与 <see cref="IWmsStockQuery"/> 同族：单向、纯读、join 按 LocationCode/FloorId。
/// 多租户由 CP6Context 全局过滤自动隔离（无 tenantId 参数）。
/// </summary>
public interface IWmsPickTaskQuery : ISpaceDataSourceDescriptor
{
    /// <summary>取拣货任务的有序拣货点（源=出库单 + 明细，按 LineNo 序）。未知单 → 空 Items。</summary>
    Task<PickPathDto> GetPickPathAsync(string taskNo, CancellationToken ct = default);
}

/// <summary>拣货任务有序库位序列（Space 后端再补 AbsXYZ）。</summary>
public sealed class PickPathDto
{
    public string TaskNo { get; set; } = "";
    public IReadOnlyList<PickStop> Items { get; set; } = [];
}

/// <summary>单个拣货点（有序）。</summary>
public sealed class PickStop
{
    public int     Seq          { get; set; }          // = LineNo（拣货序）
    public string  LocationCode { get; set; } = "";
    public decimal Qty          { get; set; }          // = RequiredQty
    public string? MaterialNo   { get; set; }          // = ProductCd
}

/// <summary>作业热图查询（源=库存流水按库位×时间窗计次）。</summary>
public interface IWmsWorkloadQuery : ISpaceDataSourceDescriptor
{
    Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid floorId, DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>某库位时间窗内作业次数。</summary>
public sealed class WorkloadDto
{
    public string LocationCode { get; set; } = "";
    public int    OpCount      { get; set; }
}

/// <summary>设备联动查询（v1 占位，WMS 返空/示例）。</summary>
public interface IWmsDeviceQuery : ISpaceDataSourceDescriptor
{
    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid floorId, CancellationToken ct = default);
}

/// <summary>设备图元（v1 占位；Space 后端可按 LocationCode 补 AbsXYZ）。</summary>
public sealed class DeviceDto
{
    public string  DeviceId     { get; set; } = "";
    public string  Type         { get; set; } = "";   // AGV/Stacker/Conveyor…
    public string? LocationCode { get; set; }
    public int     Status       { get; set; }          // 0闲 1忙 2故障…
}

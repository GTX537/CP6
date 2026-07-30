namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 库存只读查询契约（消费者 Space 侧定义；WMS 接真实现 <see cref="CP6.Core.Services.Wms.WmsStockQuery"/>）。
/// 单向、纯读、join 按 LocationCode。多租户由 CP6Context 全局过滤自动隔离（无 tenantId 参数）。
/// </summary>
public interface IWmsStockQuery : ISpaceDataSourceDescriptor
{
    /// <summary>批量按库位编码查库存（叠加主力）。未命中编码不在结果集。</summary>
    Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default);

    /// <summary>按物料/批次/容器反查"哪些库位有它"（D8 P2 半）。</summary>
    Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default);

    /// <summary>单库位库存量（04 停用前置校验用）。warehouseCd 给定时按 (仓,码) 锚查（§3.4 多仓防串仓）。</summary>
    Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default);
}

/// <summary>库位库存叠加 DTO（join key=LocationCode）。</summary>
public sealed class WmsStockDto
{
    public string  LocationCode { get; set; } = "";
    public int     BinStatus    { get; set; }   // 0空 1有货 2满 3锁定 4在拣
    public decimal Qty          { get; set; }   // ΣPhysicalQty
    public decimal AllocatedQty { get; set; }   // ΣAllocatedQty
    public decimal? Capacity    { get; set; }   // Location.CapacityQty（0/未设→null）
    public string?  TopMaterial { get; set; }   // 占量最大 ProductCd
    public int      ProductKinds{ get; set; }   // distinct ProductCd 数
}

/// <summary>按物料/批/容器反查条件（非空即 AND；全空→空结果）。</summary>
public sealed class StockLocateQuery
{
    public string? MaterialNo { get; set; }
    public string? Lot        { get; set; }
    public string? Container  { get; set; }
}

/// <summary>反查命中库位。</summary>
public sealed class WmsLocationHit
{
    public string  LocationCode { get; set; } = "";
    public decimal Qty          { get; set; }
    public string? Lot          { get; set; }
}

/// <summary>P1 桩：恒空/0。测试与 WMS 未接真时兜底。</summary>
public sealed class StubWmsStockQuery : IWmsStockQuery
{
    public CP6.Entity.DTOs.Space.SpaceDataSourceKind DataSourceKind =>
        CP6.Entity.DTOs.Space.SpaceDataSourceKind.Unavailable;

    public string DataSourceId => "WMS_UNCONFIGURED";

    public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WmsStockDto>>(Array.Empty<WmsStockDto>());

    public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WmsLocationHit>>(Array.Empty<WmsLocationHit>());

    public Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default)
        => Task.FromResult(0m);
}

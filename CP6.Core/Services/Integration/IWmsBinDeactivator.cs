namespace CP6.Core.Services.Integration;

/// <summary>
/// 停用同步 RPC 契约（ch04 §6 v1.1，Space 侧定义、WMS 实现，与 IWmsStockQuery 同构的单向抽象）。
/// Space 停用前同步调用，WMS 按实时库存权威判定（TOCTOU 防护）；
/// Space 据同步返回决定本地 Status——成功才 1→2，不再乐观翻转+回滚（§6.3 无孤儿库位）。
/// </summary>
public interface IWmsBinDeactivator
{
    Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default);
}

/// <summary>停用同步请求（对应契约 POST /api/wms/bins/deactivate 的进程内形态）。</summary>
public sealed class WmsDeactivateRequest
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    /// <summary>仓库维度（§3.4 映射；bin 已落库时以 bin 记录为准）</summary>
    public string? WarehouseCd { get; set; }
    /// <summary>停用后的新版本号（= Space 侧 Version+1），成功时写入 T_WmsBin.Version</summary>
    public long Version { get; set; }
    public string? User { get; set; }
}

/// <summary>停用同步返回。</summary>
public sealed class WmsDeactivateResult
{
    public bool Success { get; set; }
    /// <summary>拒绝原因（如 W-SPACE-404 库存非0）</summary>
    public string? Reason { get; set; }
}

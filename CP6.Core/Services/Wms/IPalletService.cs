using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// パレットサービス（MSBBWM240）
/// </summary>
/// <remarks>
/// 仕様書 §33。
/// 1パレット = 1製品1ロット（混載禁止原則）
/// 状态遷移：0=組成中 → 1=保管中 → 2=出荷待機 → 3=出荷済
/// </remarks>
public interface IPalletService
{
    Task<List<Pallet>> SearchAsync(PalletSearchQuery q);
    Task<Pallet?> GetAsync(string palletNo);
    Task<string> CreateAsync(PalletDto dto, string? userName);
    Task UpdateAsync(string palletNo, PalletDto dto, string? userName);

    /// <summary>状态遷移 — 組成完了 0→1</summary>
    Task CompleteBuildingAsync(string palletNo, string? userName);

    /// <summary>状态遷移 — 出荷待機ゾーンへ 1→2</summary>
    Task MoveToShippingWaitAsync(string palletNo, string toLocationCd, string? userName);

    /// <summary>出荷確定 2→3（関連出荷指示NO 紐付）</summary>
    Task MarkShippedAsync(string palletNo, string outboundNo, string? userName);

    Task DeleteAsync(string palletNo, string? userName);
}

public class PalletSearchQuery
{
    public string? PalletNo { get; set; }
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public string? WarehouseCd { get; set; }
    public int? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PalletDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public int CartonQty { get; set; }
    public decimal? WeightKg { get; set; }
    public int? HeightMm { get; set; }
    public int? MaxStackLayers { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

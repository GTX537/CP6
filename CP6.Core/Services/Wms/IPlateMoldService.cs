using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 印版・木型 管理サービス（MSBBWM220）
/// </summary>
/// <remarks>
/// 仕様書 §31。マスタ管理 + 使用ショット累計 + 寿命警報 + メンテ管理。
/// 状态遷移：0=使用可 ⇆ 1=メンテ中 ; UsedShots ≥ MaxShots → 2=寿命到達
/// </remarks>
public interface IPlateMoldService
{
    Task<List<PlateMoldStock>> SearchAsync(PlateMoldSearchQuery q);
    Task<PlateMoldStock?> GetAsync(string plateNo);
    Task<string> CreateAsync(PlateMoldDto dto, string? userName);
    Task UpdateAsync(string plateNo, PlateMoldDto dto, string? userName);

    /// <summary>使用記録 — shots を UsedShots に加算、LastUsedAt 更新。寿命超なら 0→2</summary>
    Task RecordUsageAsync(string plateNo, int shots, string? userName);

    /// <summary>メンテ開始 0→1</summary>
    Task StartMaintenanceAsync(string plateNo, DateTime? nextMaintDate, string? userName);

    /// <summary>メンテ完了 1→0</summary>
    Task CompleteMaintenanceAsync(string plateNo, string? userName);

    /// <summary>廃版 → 3</summary>
    Task DiscardAsync(string plateNo, string? userName);

    /// <summary>寿命警報リスト：UsedShots/MaxShots ≥ threshold or 寿命到達</summary>
    Task<List<PlateMoldStock>> WarningListAsync(decimal threshold = 0.9m);

    Task DeleteAsync(string plateNo, string? userName);
}

public class PlateMoldSearchQuery
{
    public string? PlateNo { get; set; }
    public string? PlateType { get; set; }
    public string? CustomerCd { get; set; }
    public string? ProductCd { get; set; }
    public int? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PlateMoldDto
{
    public string PlateType { get; set; } = "PLATE";
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public int? ColorCount { get; set; }
    public string? SizeNote { get; set; }
    public DateTime? MadeDate { get; set; }
    public decimal? MadeCost { get; set; }
    public int? MaxShots { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }
    public string? WarehouseCd { get; set; }
    public string? LocationCd { get; set; }
    public string? Remarks { get; set; }
}

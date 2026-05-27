using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 残材管理サービス（MSBBWM210）
/// </summary>
/// <remarks>
/// 仕様書 §30。
/// 登録 → 予約 → 使用 / 廃棄 の状态遷移。
/// 再利用検索：素材区分 + サイズ範囲（min/max 幅/長さ）でマッチング。
/// </remarks>
public interface IRemnantService
{
    Task<List<RemnantMaterial>> SearchAsync(RemnantSearchQuery q);
    Task<RemnantMaterial?> GetAsync(string remnantNo);
    Task<string> CreateAsync(RemnantDto dto, string? userName);
    Task UpdateAsync(string remnantNo, RemnantDto dto, string? userName);

    /// <summary>再利用候補検索（指定サイズ以上 + 素材区分一致）</summary>
    Task<List<RemnantMaterial>> MatchAsync(string materialType, int minWidthMm, int minLengthMm);

    /// <summary>予約：0→1（用途文字列を保存）</summary>
    Task ReserveAsync(string remnantNo, string reservedFor, string? userName);

    /// <summary>予約解除：1→0</summary>
    Task UnreserveAsync(string remnantNo, string? userName);

    /// <summary>使用済：0/1→2</summary>
    Task MarkUsedAsync(string remnantNo, string? userName);

    /// <summary>廃棄：0/1/2→3</summary>
    Task DisposeAsync(string remnantNo, string? userName);

    Task DeleteAsync(string remnantNo, string? userName);
}

public class RemnantSearchQuery
{
    public string? RemnantNo { get; set; }
    public string? MaterialType { get; set; }
    public string? MaterialGrade { get; set; }
    public int? Status { get; set; }
    public string? WarehouseCd { get; set; }
    public string? SourceWorkOrderNo { get; set; }
    public string? SourceRollNo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class RemnantDto
{
    public string MaterialType { get; set; } = "PAPER";
    public string? MaterialGrade { get; set; }
    public int WidthMm { get; set; }
    public int LengthMm { get; set; }
    public int? ThicknessUm { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public string UnitCd { get; set; } = "SHT";
    public string? SourceWorkOrderNo { get; set; }
    public string? SourceRollNo { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

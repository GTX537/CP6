using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 原紙ロールサービス（MSBBWM200）
/// </summary>
/// <remarks>
/// 仕様書 §29。
/// 巾割り（スリッター）：1 親ロール → N 子ロール + 残端（任意）
/// 例：W=1310 → 905 × 1 + 390 × 1（端材） + 残端 15
/// </remarks>
public interface IPaperRollService
{
    Task<List<PaperRoll>> SearchAsync(PaperRollSearchQuery q);
    Task<PaperRoll?> GetAsync(string rollNo);

    /// <summary>原紙ロール入庫（ROLLYYYYMMDD-NNNNN 採番）</summary>
    Task<string> CreateAsync(PaperRollDto dto, string? userName);

    /// <summary>消費（残米減算）。残米が 0 未満は拒否；閾値以下で自動的に Status=2 残米へ</summary>
    Task ConsumeAsync(string rollNo, decimal lengthM, string? userName);

    /// <summary>適合検索：紙質+巾+流れ+必要長 で 最も残米が少ない（=端材作らない）優先</summary>
    Task<List<PaperRoll>> MatchAsync(string paperGrade, int widthMm, string grainDirection, decimal requiredLengthM);

    /// <summary>
    /// スリッター（巾割り）：親ロール 1 本 → 子ロール N 本（巾合計 ≤ 親巾）
    /// 残端も任意で 1 ロールとして保存可能
    /// </summary>
    Task<List<string>> SlitAsync(SlitRequest req, string? userName);

    /// <summary>廃棄（Status → 3）</summary>
    Task DisposeAsync(string rollNo, string? userName);
}

public class PaperRollSearchQuery
{
    public string? RollNo { get; set; }
    public string? PaperGrade { get; set; }
    public int? WidthMm { get; set; }
    public string? GrainDirection { get; set; }
    public int? Status { get; set; }
    public string? WarehouseCd { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PaperRollDto
{
    public string PaperGrade { get; set; } = string.Empty;
    public int WidthMm { get; set; }
    public decimal BasisWeight { get; set; }
    public string GrainDirection { get; set; } = "T";
    public decimal OriginalLengthM { get; set; }
    public decimal CoreDiameterInch { get; set; } = 3m;
    public DateTime? MfgDate { get; set; }
    public string? MfgLotNo { get; set; }
    public string? SupplierRollNo { get; set; }
    public string LocationCd { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public decimal? DisposeThresholdM { get; set; }
    public string? Remarks { get; set; }
}

public class SlitRequest
{
    public string ParentRollNo { get; set; } = string.Empty;
    /// <summary>子ロールの巾 リスト（例：[905, 390]）</summary>
    public List<int> ChildWidths { get; set; } = new();
    /// <summary>端材を残ロールとして保存するか</summary>
    public bool KeepRemnant { get; set; } = true;
}

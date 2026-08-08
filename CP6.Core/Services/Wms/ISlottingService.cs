using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// スロッティング最適化サービス（MSBBWM110）
/// </summary>
/// <remarks>
/// 仕様書 §21。
/// 1. AnalyzeAsync：指定倉庫の過去 N 日 OUT トランザクションを集計
///    → 製品ごとの出庫回数・数量 → ABC ランク → 推奨ロケ算出
/// 2. 推奨ロケ命名規約：
///    A→PIK-A-*（高頻度／前棚 / 低位）
///    B→PIK-B-*（中頻度）
///    C→RES-C-*（低頻度／保管棚奥）
/// 3. SlottingPlan に推奨 JSON で保存
/// </remarks>
public interface ISlottingService
{
    Task<List<SlottingPlan>> SearchAsync(string? warehouseCd, int? status);
    Task<SlottingPlanResult?> GetAsync(string planNo);
    Task<string> AnalyzeAsync(string warehouseCd, int analysisDays, string? userName);
    Task<int> ApproveAsync(string planNo, string? userName);
    Task CancelAsync(string planNo, string? userName);
}

public class SlottingPlanResult
{
    public SlottingPlan Plan { get; set; } = default!;
    public List<SlottingRecommendation> Recommendations { get; set; } = new();
}

public class SlottingRecommendation
{
    public string ProductCd { get; set; } = string.Empty;
    public int OutCount { get; set; }
    public decimal OutQty { get; set; }
    public string AbcRank { get; set; } = "C";
    /// <summary>現在の主たる ロケCD（在庫量最大）</summary>
    public string? CurrentLocationCd { get; set; }
    /// <summary>推奨 ロケCD パターン（PIK-A-* / PIK-B-* / RES-C-*）</summary>
    public string? RecommendedLocationPattern { get; set; }
    /// <summary>推奨と異なる場合に true（移動候補）</summary>
    public bool NeedsRelocation { get; set; }
    /// <summary>承認時に選択された実移動先。</summary>
    public string? TargetLocationCd { get; set; }
    /// <summary>承認時に予約された移動数量。</summary>
    public decimal? MoveQty { get; set; }
    /// <summary>承認によって生成された v2 MOVE タスク。</summary>
    public string? MobileTaskNo { get; set; }
    /// <summary>候補をタスク化できなかった場合の安定エラーコード。</summary>
    public string? GenerationErrorCode { get; set; }
}

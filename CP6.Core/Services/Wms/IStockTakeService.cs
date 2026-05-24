using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 棚卸サービス（WM090）。
/// 4 段階フロー：計画 → カウント → 差異確認 → 承認 → 完了。
/// </summary>
public interface IStockTakeService
{
    Task<List<StockTake>> SearchAsync(StockTakeSearchQuery q);
    Task<StockTakeDto?> GetAsync(string stockTakeNo);

    /// <summary>
    /// 棚卸計画を作成し、対象範囲の Stock スナップショットを取って明細を生成。
    /// status = 0:Planned。
    /// </summary>
    Task<string> CreatePlanAsync(StockTakePlanDto dto, string? userName);

    /// <summary>カウント開始：status 0 → 1。ActualDate にセット。</summary>
    Task StartCountAsync(string stockTakeNo, string? userName);

    /// <summary>
    /// 複数明細のカウント値を一括更新。DiffQty・DiffAmount を自動計算。
    /// </summary>
    Task UpdateCountsAsync(string stockTakeNo, List<StockTakeCountInput> inputs, string? userName);

    /// <summary>
    /// カウント完了 → 差異確認状態へ：status 1 → 2 (差異あり/閾値超なら 3 へ)。
    /// 差異 0 の明細を自動承認。
    /// </summary>
    Task SubmitForReviewAsync(string stockTakeNo, string? userName);

    /// <summary>
    /// 残差異明細を承認 → ADJ トランザクション発行 → 在庫を実盤数に上書き。
    /// status → 4:Completed。
    /// </summary>
    Task ApproveAndApplyAsync(string stockTakeNo, string? userName);

    /// <summary>取消：status → 9。承認済以降は不可。</summary>
    Task CancelAsync(string stockTakeNo, string? userName);
}

// ───────── DTOs ─────────

public class StockTakeSearchQuery
{
    public string? StockTakeNo { get; set; }
    public int? Status { get; set; }
    public int? StockTakeType { get; set; }
    public string? TargetWarehouseCd { get; set; }
    public DateTime? PlannedFrom { get; set; }
    public DateTime? PlannedTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class StockTakePlanDto
{
    public int StockTakeType { get; set; } = 1;
    public DateTime PlannedDate { get; set; } = DateTime.Today;
    public string TargetWarehouseCd { get; set; } = string.Empty;
    public string? TargetLocationPrefix { get; set; }
    public string? TargetProductCd { get; set; }
    public decimal? ApprovalThresholdAmount { get; set; }
    public string? Remarks { get; set; }
}

public class StockTakeCountInput
{
    public int LineNo { get; set; }
    public decimal CountedQty { get; set; }
    public string? CountedByCd { get; set; }
    public string? DiffReasonCd { get; set; }
    public string? DiffReasonText { get; set; }
    public string? Remarks { get; set; }
}

public class StockTakeDto
{
    public string StockTakeNo { get; set; } = string.Empty;
    public int StockTakeType { get; set; }
    public DateTime PlannedDate { get; set; }
    public DateTime? ActualDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int Status { get; set; }
    public string TargetWarehouseCd { get; set; } = string.Empty;
    public string? TargetLocationPrefix { get; set; }
    public string? TargetProductCd { get; set; }
    public decimal? ApprovalThresholdAmount { get; set; }
    public string? ApproverCd { get; set; }
    public string? Remarks { get; set; }
    public List<StockTakeDetailDto> Details { get; set; } = new();
}

public class StockTakeDetailDto
{
    public int LineNo { get; set; }
    public Guid StockId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal BookQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal? DiffQty { get; set; }
    public decimal? DiffAmount { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? DiffReasonCd { get; set; }
    public string? DiffReasonText { get; set; }
    public int ApprovalStatus { get; set; }
    public string? AdjustTxnNo { get; set; }
    public string? CountedByCd { get; set; }
    public string? Remarks { get; set; }
}

using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 入荷検品サービス（MSBBWM100）。
/// </summary>
/// <remarks>
/// 仕様書 §20。
/// ワークフロー：
///   1. CreateFromInbound（入庫予定参照）or CreateDirect（直入）
///      → status=0 作成 + 明細スナップショット
///   2. StartInspection → status=1 検品中
///   3. SaveItems → 検査結果（合格/不良/保留 数量）入力
///   4. Judge → 最終判定、status=2 判定済
///      - 合格(PASS) → InboundService.ConfirmReceipt 自動呼出で在庫反映
///      - 他判定 → 在庫反映なし（業務側で個別処理）
/// </remarks>
public interface IQcInspectionService
{
    Task<List<QcInspection>> SearchAsync(QcInspectionSearchQuery q);
    Task<QcInspectionDto?> GetAsync(string inspectionNo);
    Task<string> CreateFromInboundAsync(string inboundNo, string? userName);
    Task<string> CreateDirectAsync(QcInspectionDto dto, string? userName);
    Task SaveItemsAsync(string inspectionNo, List<QcInspectionItemDto> items, string? userName);
    Task<JudgeResult> JudgeAsync(string inspectionNo, JudgeRequest req, string? userName);
    Task CancelAsync(string inspectionNo, string? userName);
}

public class QcInspectionSearchQuery
{
    public string? InspectionNo { get; set; }
    public string? InboundNo { get; set; }
    public string? SupplierCd { get; set; }
    public int? Status { get; set; }
    public string? FinalJudgement { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class QcInspectionDto
{
    public string? InspectionNo { get; set; }
    public string? InboundNo { get; set; }
    public string? SupplierCd { get; set; }
    public string? SupplierName { get; set; }
    public DateTime ArrivalDateTime { get; set; } = DateTime.Now;
    public string? InspectorCd { get; set; }
    public int Status { get; set; }
    public string? FinalJudgement { get; set; }
    public string? JudgementReason { get; set; }
    public string? GeneratedReceiptNo { get; set; }
    public string? PhotoUrls { get; set; }
    public string? Remarks { get; set; }
    public List<QcInspectionItemDto> Items { get; set; } = new();
}

public class QcInspectionItemDto
{
    public int LineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal PendingQty { get; set; }
    public string? DefectReasonCd { get; set; }
    public string? Remarks { get; set; }
}

public class JudgeRequest
{
    /// <summary>PASS / CONDITIONAL / HOLD / FAIL / RETURN</summary>
    public string FinalJudgement { get; set; } = "PASS";
    public string? Reason { get; set; }
    /// <summary>PASS の時のみ：自動入庫先の倉庫CD（既定は元入庫予定の倉庫）</summary>
    public string? AcceptWarehouseCd { get; set; }
    /// <summary>PASS の時のみ：自動入庫先ロケーション（明細順）</summary>
    public List<string>? AcceptLocations { get; set; }
}

public class JudgeResult
{
    public string FinalJudgement { get; set; } = string.Empty;
    /// <summary>PASS 時に自動生成された InboundReceipt NO</summary>
    public string? GeneratedReceiptNo { get; set; }
}

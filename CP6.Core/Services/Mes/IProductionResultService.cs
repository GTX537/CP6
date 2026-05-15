using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME040 / 050 — 製造実績 業務サービス契約
/// </summary>
public interface IProductionResultService
{
    /// <summary>ME050 — 実績一覧検索</summary>
    Task<PagedResultDto<ProductionResultDto>> SearchAsync(ProductionResultSearchQuery query);

    /// <summary>ME040 — 工程開始報告（actualStartTime=NOW、工程→1、指図→3）</summary>
    Task<string> StartAsync(ProductionResultRequest req, string? userName);

    /// <summary>ME040 — 工程中断報告（中断理由必須、工程→3、指図→5）</summary>
    Task<string> SuspendAsync(ProductionResultRequest req, string? userName);

    /// <summary>ME040 — 工程中断解除（工程→1、指図→3）</summary>
    Task<string> ResumeAsync(ProductionResultRequest req, string? userName);

    /// <summary>ME040 — 工程完了報告（良品数必須、actualEndTime=NOW、工程→2、全工程完了で指図→4）</summary>
    Task<string> CompleteAsync(ProductionResultRequest req, string? userName);

    /// <summary>ME040 — 数量報告（追加報告のみ。状態遷移なし）</summary>
    Task<string> ReportAsync(ProductionResultRequest req, string? userName);

    /// <summary>ME040 — 指図に紐づく全工程の現在状態を取得（実績入力画面の上部表示）</summary>
    Task<WorkOrderDto?> GetWorkOrderSummaryAsync(string workOrderNo);
}

using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME020 / 030 — 製造指図 業務サービス契約
/// </summary>
/// <remarks>
/// 3 表 1 次提交（T_WorkOrder / T_WorkOrderProcess / T_WorkOrderMaterial）
/// 業務 PK：WorkOrderNo（WOyyyyMMdd-NNNN）
/// </remarks>
public interface IWorkOrderService
{
    /// <summary>ME020 — 单条详细取得（含全工程+全材料）</summary>
    Task<WorkOrderDto?> GetByNoAsync(string workOrderNo);

    /// <summary>ME030 — 多条件检索 + 分页</summary>
    Task<PagedResultDto<WorkOrderDto>> SearchAsync(WorkOrderSearchQuery query);

    /// <summary>ME020 — 新建指图（一次写入 3 张表，自动采番）</summary>
    Task<string> CreateAsync(WorkOrderDto dto, string? userName);

    /// <summary>ME020 — 修正指图（仅 Status=0/1 可修正）</summary>
    Task UpdateAsync(string workOrderNo, WorkOrderDto dto, string? userName);

    /// <summary>ME030 — 论理删除（仅 Status=0/1 可删除）</summary>
    Task DeleteAsync(string workOrderNo, byte[]? rowVersion, string? userName);

    /// <summary>ME020 — 指図発行（Status → 2）</summary>
    Task IssueAsync(string workOrderNo, string? userName);

    /// <summary>
    /// ME020 Phase 6 — 受注取消連動：指図取消（Status → 7=Cancelled）
    /// </summary>
    /// <remarks>
    /// 制約：Status &gt;= InProgress(3) は取消不可（既に着手済のため）
    /// 既存材料引当は本メソッドでは解除しない。上位 IOrderCancelBridgeHook が
    /// 先に関連 OutboundOrder を取消す（OutboundService.CancelOrderAsync が自動 UNRSV）
    /// ことで在庫整合を保証する。本メソッドは Status 遷移のみ責任を持つ。
    /// </remarks>
    /// <returns>true=取消成功 / false=既に Cancelled（冪等）</returns>
    /// <exception cref="InvalidOperationException">指図不在、または Status &gt;= 3</exception>
    Task<bool> CancelAsync(string workOrderNo, string reason, string? userName);

    /// <summary>ME020 — 受注（PA070）から指図自動展開（工程ルーティング+材料構成を PA050 から展開）</summary>
    Task<List<string>> ExpandFromOrderAsync(ExpandFromOrderRequest req, string? userName);

    /// <summary>ME020 — 指図NO 採番取得</summary>
    Task<string> NextSequenceAsync();
}

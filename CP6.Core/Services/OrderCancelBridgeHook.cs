using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services;

/// <summary>
/// <see cref="IOrderCancelBridgeHook"/> 標準実装（Phase 6）。
/// </summary>
/// <remarks>
/// 取消順序（依存関係上重要）：
///   1. WebOrderNo 紐付の OutboundOrder（出荷指示）を先に取消 → CancelOrderAsync 内で RSV 自動解除
///   2. WebOrderNo 紐付の WorkOrder を取消 → 取消された WO が作った材料出庫 OutboundOrder も取消
///   3. WO 直接紐付の OutboundOrder（材料出庫）も取消
///
/// この順序により、在庫整合（AllocatedQty/PhysicalQty）が二重解除されない。
/// </remarks>
public class OrderCancelBridgeHook : IOrderCancelBridgeHook
{
    private readonly CP6Context _db;
    private readonly IWorkOrderService _woService;
    private readonly IOutboundService _outboundService;
    private readonly ILogger<OrderCancelBridgeHook> _logger;

    public OrderCancelBridgeHook(
        CP6Context db,
        IWorkOrderService woService,
        IOutboundService outboundService,
        ILogger<OrderCancelBridgeHook> logger)
    {
        _db = db;
        _woService = woService;
        _outboundService = outboundService;
        _logger = logger;
    }

    public async Task<OrderCancelHookResult> OnOrderCancelledAsync(
        string webOrderNo, bool force, string? userName, Guid correlationId)
    {
        // ───── 1. 関連 WorkOrder を探索 ─────
        var relatedWos = await _db.WorkOrders
            .Where(w => w.WebOrderNo == webOrderNo && !w.IsDeleted)
            .Select(w => new { w.WorkOrderNo, w.Status })
            .ToListAsync();

        var woProbes = relatedWos.Select(w => new WorkOrderProbe
        {
            WorkOrderNo = w.WorkOrderNo,
            Status = w.Status,
            AutoCancellable = WorkOrderStatus.IsCancellable(w.Status),
        }).ToList();

        // ───── 2. 関連 OutboundOrder を探索 ─────
        // 受注紐付（出荷指示）+ WO 紐付（材料出庫）の両方
        var woNos = relatedWos.Select(w => w.WorkOrderNo).ToList();
        var relatedOutbounds = await _db.OutboundOrders
            .Where(o => !o.IsDeleted
                && o.Status != OutboundOrderStatus.Cancelled
                && (o.WebOrderNo == webOrderNo || (o.WorkOrderNo != null && woNos.Contains(o.WorkOrderNo))))
            .Select(o => new { o.OutboundNo, o.Status, o.OutboundType })
            .ToListAsync();

        var outProbes = relatedOutbounds.Select(o => new OutboundProbe
        {
            OutboundNo = o.OutboundNo,
            Status = o.Status,
            OutboundType = o.OutboundType,
            // Picking(3) 以降は不可（既に出庫作業中）
            AutoCancellable = o.Status < OutboundOrderStatus.Picking,
        }).ToList();

        var allAutoCancellable = woProbes.All(w => w.AutoCancellable)
                              && outProbes.All(o => o.AutoCancellable);

        // ───── 探査モード ─────
        if (!force)
        {
            return new OrderCancelHookResult
            {
                Success = true,
                FullyCascaded = false,
                WorkOrders = woProbes,
                Outbounds = outProbes,
                Message = allAutoCancellable
                    ? "全関連項目が自動取消可能"
                    : "半路状態の関連項目あり — 二段確認必要",
            };
        }

        // ───── 実施モード（force=true）─────
        // Step 1: OutboundOrder を先に取消（RSV 解除を含む）
        foreach (var probe in outProbes.Where(p => p.AutoCancellable))
        {
            try
            {
                await _outboundService.CancelOrderAsync(probe.OutboundNo, userName);
                probe.Cancelled = true;
                probe.Message = "Cancelled";
                _logger.LogInformation(
                    "[OrderCancel-Bridge] Outbound {No} 取消成功 (corr={CorrId})",
                    probe.OutboundNo, correlationId);
            }
            catch (Exception ex)
            {
                probe.Cancelled = false;
                probe.Message = $"FAILED: {ex.Message}";
                _logger.LogError(ex,
                    "[OrderCancel-Bridge] Outbound {No} 取消失敗", probe.OutboundNo);
            }
        }

        // Step 2: WorkOrder を取消（RSV は OutboundOrder.CancelOrderAsync が既に解除済）
        foreach (var probe in woProbes.Where(p => p.AutoCancellable))
        {
            try
            {
                var ok = await _woService.CancelAsync(
                    probe.WorkOrderNo, "受注取消連動", userName);
                probe.Cancelled = ok;
                probe.Message = ok ? "Cancelled" : "Already cancelled (idempotent)";
                _logger.LogInformation(
                    "[OrderCancel-Bridge] WO {No} 取消結果={Ok} (corr={CorrId})",
                    probe.WorkOrderNo, ok, correlationId);
            }
            catch (Exception ex)
            {
                probe.Cancelled = false;
                probe.Message = $"FAILED: {ex.Message}";
                _logger.LogError(ex,
                    "[OrderCancel-Bridge] WO {No} 取消失敗", probe.WorkOrderNo);
            }
        }

        var fullyCascaded = woProbes.Where(w => w.AutoCancellable).All(w => w.Cancelled == true)
                         && outProbes.Where(o => o.AutoCancellable).All(o => o.Cancelled == true);

        return new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = fullyCascaded && allAutoCancellable,
            WorkOrders = woProbes,
            Outbounds = outProbes,
            Message = fullyCascaded
                ? "全 cascade 完了"
                : "一部 cascade 失敗 — ログ確認",
        };
    }
}

using CP6.Core.Services.Wf;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购订单审批终态回调（BizType="PUR_PO"，仿 A5 BudgetApprovalCallback）。
/// 与 OA 引擎共享 scoped DbContext，激活方法不自行 SaveChanges（引擎统一持久化，同事务原子）。
/// 通过→Confirmed / 驳回→Draft。用 IServiceProvider 延迟解析 IPurchaseOrderService 打破 DI 构造期循环
/// （FlowEngine→Dispatcher→IApprovalCallback→本回调→IPurchaseOrderService→IApprovalService(适配器)→FlowEngine）。
/// </summary>
public class PoApprovalCallback : IApprovalCallback
{
    private readonly IServiceProvider _sp;
    public PoApprovalCallback(IServiceProvider sp) => _sp = sp;

    public string BizType => "PUR_PO";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
        => await _sp.GetRequiredService<IPurchaseOrderService>().ConfirmFromApprovalAsync(ctx.BizId, ctx.DecidedById?.ToString() ?? "OA");

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
        => await _sp.GetRequiredService<IPurchaseOrderService>().RejectFromApprovalAsync(ctx.BizId, ctx.Reason ?? "审批驳回");
}

/// <summary>
/// 采购申请审批终态回调（BizType="PUR_PR"，仿 A5）。通过→Approved / 驳回→Draft。
/// </summary>
public class PrApprovalCallback : IApprovalCallback
{
    private readonly IServiceProvider _sp;
    public PrApprovalCallback(IServiceProvider sp) => _sp = sp;

    public string BizType => "PUR_PR";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
        => await _sp.GetRequiredService<IPurchaseRequestService>().ApproveFromApprovalAsync(ctx.BizId, ctx.DecidedById?.ToString() ?? "OA");

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
        => await _sp.GetRequiredService<IPurchaseRequestService>().RejectFromApprovalAsync(ctx.BizId, ctx.Reason ?? "审批驳回");
}

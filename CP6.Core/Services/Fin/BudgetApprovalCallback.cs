using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 预算版本审批终态回调（BizType="A5_Budget"，spec §8.3）。
/// 与引擎共享 scoped DbContext，不自行 SaveChanges（OA 引擎统一持久化，保证同事务原子）。
/// <para>
/// 通过：Status=Approved + 清旧 Active→Archived + 本版 IsActive=true（委托 ActivateFromApprovalAsync）。
/// 驳回：Status=Rejected + RejectReason（幂等：非 PendingApproval 状态直接跳过）。
/// </para>
/// <para>
/// ⚠ 用 <see cref="IServiceProvider"/> 延迟解析 <see cref="IBudgetService"/>，打破 DI 构造期循环依赖：
/// FlowEngine→ApprovalDispatcher→IApprovalCallback→BudgetApprovalCallback→IBudgetService→IApprovalService→FlowEngine。
/// 回调在引擎所在 scope 内执行，GetRequiredService 取到的是同 scope 的 BudgetService（共享 DbContext，原子性不变）。
/// </para>
/// </summary>
public class BudgetApprovalCallback : IApprovalCallback
{
    private readonly IServiceProvider _sp;

    public BudgetApprovalCallback(IServiceProvider sp) => _sp = sp;

    public string BizType => "A5_Budget";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
    {
        var budget = _sp.GetRequiredService<IBudgetService>();
        // 同事务一次性：Status=Approved + 清旧 Active→Archived + 本版 IsActive（spec §8.3）
        // ActivateFromApprovalAsync 不 SaveChanges，由 OA 引擎统一持久化
        await budget.ActivateFromApprovalAsync(Guid.Parse(ctx.BizId), ctx.DecidedById?.ToString() ?? "OA");
    }

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
    {
        var budget = _sp.GetRequiredService<IBudgetService>();
        var v = await budget.GetVersionAsync(Guid.Parse(ctx.BizId));
        if (v == null || v.Status != BudgetVersionStatus.PendingApproval) return;   // 幂等

        v.Status = BudgetVersionStatus.Rejected;
        v.RejectReason = ctx.Reason ?? "审批驳回";
        // 不 SaveChanges —— 引擎统一持久化（GetVersionAsync 返回 tracked 实体，改值由 EF 追踪）
    }
}

using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 终态分发器（OA 章05 §4）。流程实例走到终态（通过/驳回）时由 <see cref="FlowEngine"/> 调用，
/// 按实例的 BizType 在运行时注册的所有 <see cref="IApprovalCallback"/> 中找到对应业务回调同步直调。
/// <para>
/// 编译期单向（OA 不引用业务）+ 运行时多态（DI 注入业务回调集合）。OA 原生表单（BizType 空）不回调。
/// 配了 BizType 却无对应回调 → 抛异常（配置错误，宁可失败也不静默放过）。
/// </para>
/// </summary>
public class ApprovalDispatcher
{
    private readonly IEnumerable<IApprovalCallback> _callbacks;

    public ApprovalDispatcher(IEnumerable<IApprovalCallback> callbacks) => _callbacks = callbacks;

    /// <summary>
    /// 实例终态分发。<paramref name="decidedBy"/> = 末一个办理人（作业务可追溯/复核人来源）。
    /// 回调与引擎共享 DbContext，调用方须在最终 SaveChanges 前调用以保证原子（回调抛异常→整体回滚）。
    /// </summary>
    public async Task OnInstanceFinishedAsync(Wf_FlowInstance inst, bool approved, Guid? decidedBy, string? reason)
    {
        if (string.IsNullOrEmpty(inst.BizType)) return;   // OA 原生表单，无业务回调

        var cb = _callbacks.FirstOrDefault(c => c.BizType == inst.BizType)
                 ?? throw new InvalidOperationException($"未注册 BizType={inst.BizType} 的审批回调");

        var ctx = new ApprovalCallbackContext
        {
            BizType = inst.BizType,
            BizId = inst.BizId ?? string.Empty,
            InstanceId = inst.Id,
            StarterId = inst.StarterId,
            DecidedById = decidedBy,
            Reason = reason,
        };

        if (approved) await cb.OnApprovedAsync(ctx);   // 同步直调，业务在回调里走自己的状态机
        else          await cb.OnRejectedAsync(ctx);
    }
}

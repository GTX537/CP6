using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>审批节点：算审批人建待办（缺位挂起）。逐字等价旧 EnterNodeAsync approval 分支，唯一增量 = task.TokenId。
/// token 停泊（保持 Active），由 ActAsync 会签判定后 AdvanceToken 推进。</summary>
internal sealed class ApprovalNodeHandler : INodeHandler
{
    public string Type => "approval";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var node = ctx.Node;
        var rule = FlowEngine.BuildRule(node);
        if (rule is null) { eng.Suspend(inst, node, "节点未配置审批人"); return; }

        var res = await eng.Approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = inst.StarterId });
        if (!res.Resolved) { eng.Suspend(inst, node, res.UnresolvedReason ?? "审批人无法解析"); return; }

        // 重入节点（退回/循环）：先作废上一轮遗留任务，避免会签计票串台
        var stale = await eng.Db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id && t.Status != FlowTaskStatus.Cancelled)
            .ToListAsync();
        foreach (var t in stale) t.Status = FlowTaskStatus.Cancelled;

        var dueAt = FlowEngine.NodeDueAt(node);
        foreach (var uid in res.ApproverIds.Distinct())
        {
            var (assignee, delegatedFrom) = await eng.ResolveActualAssigneeAsync(uid);   // 委派期替换为代理人
            var task = new Wf_FlowTask
            {
                Id = Guid.NewGuid(),
                InstanceId = inst.Id,
                NodeId = node.Id,
                AssigneeId = assignee,
                Status = FlowTaskStatus.Pending,
                Countersign = node.Countersign,
                DueAt = dueAt,
                TokenId = ctx.Token.Id,   // ★ 唯一增量：会签计票按 token 隔离
            };
            eng.Db.Wf_FlowTasks.Add(task);
            if (delegatedFrom is Guid g)
                eng.AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");   // 双痕：代理人 + 被代理人
            await eng.Notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, inst.FlowKey);   // 推送待办（空实现=no-op）
        }
        // token 停泊（保持 Active）
    }
}

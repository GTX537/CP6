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

        // ── 单档(无 Stages):与今天逐字等价,不碰 planner/StagePlanJson ──
        if (node.Stages is null || node.Stages.Count == 0)
        {
            ctx.Token.StagePlanJson = null;   // 清陈旧串簽计划:多档节点→单档节点同 token 流转时,token 上一节点的冻结计划须清,否则 ActOnceAsync 误判本单档节点为多档
            await eng.WriteCcAsync(inst, node.Id, node.CcUsers, node.CcRoleId);
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
            var step = eng.NextStepSeq(inst.Id);   // ★ T9：同一关卡多审批人共享同一序号（foreach 外算）
            eng.WriteSnapshot(inst, node, ctx.Token, step);   // ★ T10：节点到达，存一份表单快照（一关卡一份，foreach 外）
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
                eng.WriteFormToOnSend(inst, node, ctx.Token,   // ★ T9：送签建传签履历行
                    expectedHandler: assignee, onBehalfOf: delegatedFrom, stepSeq: step);
                if (delegatedFrom is Guid g)
                    eng.AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");   // 双痕：代理人 + 被代理人
                await eng.Notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, inst.FlowKey);   // 推送待办（空实现=no-op）
            }
            // token 停泊（保持 Active）
            return;
        }

        // ── 多档串簽:冻结计划进 token.StagePlanJson,进第 0 档 ──
        await eng.WriteCcAsync(inst, node.Id, node.CcUsers, node.CcRoleId);
        var plan = await eng.Planner.BuildAsync(inst, ctx.Schema, node);
        ctx.Token.StagePlanJson = System.Text.Json.JsonSerializer.Serialize(plan);
        await eng.EnterStageAsync(inst, ctx.Schema, node, ctx.Token, plan, 0);
    }
}

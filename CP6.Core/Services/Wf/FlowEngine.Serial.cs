using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    /// <summary>进入串簽第 k 档:解析该档审批人建任务(StageRound 自推导)。解析不到 → Suspend(E-WF-013)。</summary>
    internal async Task EnterStageAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node,
        Wf_FlowToken token, IReadOnlyList<RuntimeApprovalStage> plan, int k)
    {
        var stage = plan[k];
        var res = await _approver.ResolveAsync(stage.Rule, new ApproverResolveContext { StarterUserId = inst.StarterId, VarsJson = inst.VarsJson });
        if (!res.Resolved) { Suspend(inst, node, "E-WF-013"); return; }

        int round = NextStageRound(inst.Id, node.Id, token.Id, k);
        var dueAt = NodeDueAt(node);
        var step = NextStepSeq(inst.Id);
        WriteSnapshot(inst, node, token, step);
        foreach (var uid in res.ApproverIds.Distinct())
        {
            var (assignee, delegatedFrom) = await ResolveActualAssigneeAsync(uid);
            var task = new Wf_FlowTask
            {
                Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id, AssigneeId = assignee,
                Status = FlowTaskStatus.Pending, Countersign = stage.Countersign, DueAt = dueAt,
                TokenId = token.Id, StageIndex = k, StageRound = round,
            };
            _db.Wf_FlowTasks.Add(task);
            WriteFormToOnSend(inst, node, token, assignee, delegatedFrom, step, stageIndex: k, stageRound: round);
            if (delegatedFrom is Guid g) AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");
            await _notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, inst.FlowKey);
        }
    }
}

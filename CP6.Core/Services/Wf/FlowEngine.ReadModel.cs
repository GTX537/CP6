using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    /// <summary>
    /// 本实例下一关卡序号（含并行分支共享序号空间）。
    /// 先查 Local（本轮未落盘的行），再查 DB（已落盘行），取二者最大值 +1。
    /// </summary>
    internal int NextStepSeq(Guid instanceId)
    {
        var localMax = _db.Wf_FlowFormTos.Local
            .Where(f => f.InstanceId == instanceId)
            .Select(f => (int?)f.StepSeq)
            .Max() ?? 0;
        var dbMax = _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId)
            .Select(f => (int?)f.StepSeq)
            .Max() ?? 0;
        return Math.Max(localMax, dbMax) + 1;
    }

    /// <summary>
    /// 送签：approval 节点为每个应处理人建一行 Pending 传签履历。
    /// 同一关卡多审批人（会签）共享同一 stepSeq（调用方在 foreach 外算好传入）。
    /// </summary>
    internal void WriteFormToOnSend(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token,
        Guid expectedHandler, Guid? onBehalfOf, int stepSeq)
    {
        _db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(),
            InstanceId = inst.Id,
            TokenId = token.Id,
            StepSeq = stepSeq,
            NodeId = node.Id,
            NodeCode = node.Id,
            NodeName = node.Name,
            ExpectedHandlerId = expectedHandler,
            OnBehalfOfId = onBehalfOf,
            Status = FlowFormToStatus.Pending,
            SentAt = DateTime.Now,
        });
    }

    /// <summary>
    /// 办结：更新该 (inst, node, token, expectedHandler) 的待签行为已办结状态。
    /// 若行不存在（幂等 / 加签 / 旁路路径），静默跳过。
    /// </summary>
    internal async Task UpdateFormToOnHandleAsync(Wf_FlowTask task, Guid actorId, bool approve, string? comment)
    {
        var row = await _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == task.InstanceId
                        && f.NodeId == task.NodeId
                        && f.TokenId == task.TokenId
                        && f.ExpectedHandlerId == task.AssigneeId
                        && f.Status == FlowFormToStatus.Pending)
            .FirstOrDefaultAsync();
        if (row is null) return;
        row.Status = approve ? FlowFormToStatus.Approved : FlowFormToStatus.Rejected;
        row.ActualHandlerId = actorId;
        row.HandledAt = DateTime.Now;
        row.Comment = comment;
    }

    /// <summary>存一份该关卡当时表单快照（来源 inst.VarsJson）。</summary>
    internal void WriteSnapshot(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token, int stepSeq)
    {
        _db.Wf_FlowDatas.Add(new Wf_FlowData
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = token.Id, StepSeq = stepSeq,
            NodeId = node.Id, DataJson = string.IsNullOrWhiteSpace(inst.VarsJson) ? "{}" : inst.VarsJson,
        });
    }

    /// <summary>
    /// 驳回连坐 / 退回清场：本实例全 Pending 传签履历行 → 作废。
    /// 先处理 Local（变更追踪器里的当前状态），再处理 DB 中已落盘但未加载的行，
    /// 排除已在 Local 的实体 Id 以避免通过 EF 身份映射读到旧落盘值。
    /// </summary>
    internal void VoidPendingFormTos(Guid instanceId)
    {
        // ① 变更追踪器（Local）视图——对本回合已修改但未落盘的行是权威状态
        foreach (var f in _db.Wf_FlowFormTos.Local
            .Where(f => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending)
            .ToList())
            f.Status = FlowFormToStatus.Voided;

        // ② DB 中已落盘但尚未被本回合加载到 Local 的行
        var localIds = _db.Wf_FlowFormTos.Local
            .Where(f => f.InstanceId == instanceId)
            .Select(f => f.Id)
            .ToHashSet();
        foreach (var f in _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId
                        && f.Status == FlowFormToStatus.Pending
                        && !localIds.Contains(f.Id))
            .ToList())
            f.Status = FlowFormToStatus.Voided;
    }
}

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
    /// 串簽多档路径可选传 stageIndex/stageRound；单档/旧路径不传（默认 null），行为零变化。
    /// </summary>
    internal void WriteFormToOnSend(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token,
        Guid expectedHandler, Guid? onBehalfOf, int stepSeq, int? stageIndex = null, int? stageRound = null)
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
            StageIndex = stageIndex,
            StageRound = stageRound,
        });
    }

    /// <summary>本 token·node·stage 的下一个 StageRound = 既存最大轮 +1(无 → 0)。前进档天然 0,prevStage 退回天然 +1。
    /// 仿 NextStepSeq 的 Local+DB 取 Max 模式,杜绝同回合未落盘行漏算。</summary>
    internal int NextStageRound(Guid instanceId, string nodeId, Guid tokenId, int stageIndex)
    {
        int localMax = _db.Wf_FlowTasks.Local
            .Where(t => t.InstanceId == instanceId && t.NodeId == nodeId && t.TokenId == tokenId && t.StageIndex == stageIndex)
            .Select(t => (int?)t.StageRound).Max() ?? -1;
        int dbMax = _db.Wf_FlowTasks
            .Where(t => t.InstanceId == instanceId && t.NodeId == nodeId && t.TokenId == tokenId && t.StageIndex == stageIndex)
            .Select(t => (int?)t.StageRound).Max() ?? -1;
        return Math.Max(localMax, dbMax) + 1;
    }

    /// <summary>
    /// 办结：更新该 (inst, node, token, expectedHandler) 的待签行为已办结状态。
    /// 若行不存在（幂等 / 加签 / 旁路路径），静默跳过。
    /// </summary>
    internal async Task UpdateFormToOnHandleAsync(Wf_FlowTask task, Guid actorId, bool approve, string? comment,
        Guid? onBehalfOf = null)
    {
        var row = await _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == task.InstanceId
                        && f.NodeId == task.NodeId
                        && f.TokenId == task.TokenId
                        && f.ExpectedHandlerId == task.AssigneeId
                        && f.Status == FlowFormToStatus.Pending)
            .OrderByDescending(f => f.SentAt)   // 万一存在两行 Pending，优先关最近送签那行
            .FirstOrDefaultAsync();
        if (row is null) return;
        row.Status = approve ? FlowFormToStatus.Approved : FlowFormToStatus.Rejected;
        row.ActualHandlerId = actorId;   // 实际点击办理的人（act-as 时 = 代理人 me）
        row.OnBehalfOfId = onBehalfOf;   // 代谁签（非 act-as 时 = null，既有路径零改）
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

    /// <summary>写抄送行（去重：同实例+同人+同节点不重复）。roleId 经 IApproverResolver Role 策略解析。</summary>
    internal async Task WriteCcAsync(Wf_FlowInstance inst, string? atNodeId, IEnumerable<Guid>? users, int? roleId)
    {
        var ids = new HashSet<Guid>(users ?? Enumerable.Empty<Guid>());
        if (roleId is int rid)
        {
            var res = await _approver.ResolveAsync(new ApproverRule(ApproverStrategy.Role, null, rid, null),
                new ApproverResolveContext { StarterUserId = inst.StarterId });
            foreach (var id in res.ApproverIds) ids.Add(id);
        }
        foreach (var uid in ids)
        {
            bool exists = _db.Wf_FlowCcs.Local.Any(c => c.InstanceId == inst.Id && c.RecipientId == uid && c.AtNodeId == atNodeId)
                || await _db.Wf_FlowCcs.AnyAsync(c => c.InstanceId == inst.Id && c.RecipientId == uid && c.AtNodeId == atNodeId);
            if (exists) continue;
            _db.Wf_FlowCcs.Add(new Wf_FlowCc { Id = Guid.NewGuid(), InstanceId = inst.Id, RecipientId = uid, AtNodeId = atNodeId });
        }
    }

    /// <summary>会签节点已决：把本节点本 token 下、非已办的 Pending 履历行置 Skipped（or 签未轮到/被取消的兄弟行）。</summary>
    internal void SkipPendingFormTos(Guid instanceId, string nodeId, Guid? tokenId)
    {
        foreach (var f in _db.Wf_FlowFormTos.Local
            .Where(f => f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId
                        && f.Status == FlowFormToStatus.Pending).ToList())
            f.Status = FlowFormToStatus.Skipped;
        var localIds = _db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == instanceId).Select(f => f.Id).ToHashSet();
        foreach (var f in _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId
                        && f.Status == FlowFormToStatus.Pending && !localIds.Contains(f.Id)).ToList())
            f.Status = FlowFormToStatus.Skipped;
    }

    /// <summary>转交读模型：原行 Transferred(实办=转出人)，受让人新起 Pending 行（同 Token/Node，StepSeq 递增）。</summary>
    internal async Task TransferFormToAsync(Guid instanceId, string nodeId, Guid? tokenId,
        Guid fromUserId, Guid toUserId, string? comment)
    {
        var src = await _db.Wf_FlowFormTos.FirstOrDefaultAsync(f =>
            f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId &&
            f.ExpectedHandlerId == fromUserId && f.Status == FlowFormToStatus.Pending);
        if (src is not null)
        {
            src.Status = FlowFormToStatus.Transferred;
            src.ActualHandlerId = fromUserId;
            src.HandledAt = DateTime.Now;
            src.Comment = comment;
        }
        _db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = instanceId, TokenId = tokenId,
            StepSeq = NextStepSeq(instanceId),   // sync — checks Local + DB, takes Max+1
            FromNodeId = src?.FromNodeId, NodeId = nodeId, NodeCode = src?.NodeCode, NodeName = src?.NodeName,
            ExpectedHandlerId = toUserId, Status = FlowFormToStatus.Pending, SentAt = DateTime.Now,
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

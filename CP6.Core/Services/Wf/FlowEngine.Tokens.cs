using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    // 供 handler 经 ctx.Engine 复用（InternalsVisibleTo CP6.Tests）
    internal CP6Context Db => _db;
    internal IApproverResolver Approver => _approver;
    internal IWfNotifier Notifier => _notifier;

    /// <summary>生一个 Active token 停在 node。parent/fork 串血缘（根皆 null）。不落盘。</summary>
    internal Wf_FlowToken SpawnToken(Wf_FlowInstance inst, FlowNode node, Guid? parent = null, Guid? fork = null)
    {
        var tok = new Wf_FlowToken
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id,
            Status = FlowTokenStatus.Active, ParentTokenId = parent, ForkId = fork,
            Creator = inst.StarterId.ToString(),
        };
        _db.Wf_FlowTokens.Add(tok);   // TenantId 由 StampTenant 自动盖
        return tok;
    }

    /// <summary>消费 token（正常退场）。带 Active 守卫 → 重放 no-op。</summary>
    internal void ConsumeToken(Wf_FlowToken token)
    {
        if (token.Status != FlowTokenStatus.Active) return;
        token.Status = FlowTokenStatus.Consumed;
    }

    /// <summary>驳回连坐：本实例全 Active token → Cancelled。并查 DB + EF Local。</summary>
    internal void CancelAllActiveTokens(Guid instanceId)
    {
        var actives = _db.Wf_FlowTokens.Local
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in _db.Wf_FlowTokens
                     .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active).ToList())
            if (!actives.Contains(t)) actives.Add(t);
        foreach (var t in actives) t.Status = FlowTokenStatus.Cancelled;
    }

    /// <summary>无 Active token 残留 ⇒ 实例正常通过（置 Approved；dispatch 由调用方在 SaveChanges 前做）。</summary>
    internal void FinishIfDrained(Wf_FlowInstance inst)
    {
        if (HasActiveToken(inst.Id)) return;
        if (inst.Status != FlowInstanceStatus.Running) return;   // 已驳回/撤回，不覆盖
        inst.Status = FlowInstanceStatus.Approved;
    }

    /// <summary>本实例是否仍有 Active token。变更追踪器(Local)对已加载 token 是权威态（含本回合未落盘的 consume/cancel）；
    /// DB 仅补查"未被本地追踪"的 token（如并行兄弟分支上回合落盘的），<b>排除已在 Local 的 Id 避免读到落盘旧值</b>。</summary>
    private bool HasActiveToken(Guid instanceId)
    {
        if (_db.Wf_FlowTokens.Local.Any(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active))
            return true;
        var localIds = _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        return _db.Wf_FlowTokens
            .Any(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active && !localIds.Contains(t.Id));
    }

    /// <summary>token 排他流转：沿出边取首个条件为真者，改 token.NodeId 后进新节点。不消费。
    /// 无后继 → 消费 token + drained 判定（等价旧 NextNodeAsync 兜底结束）。单 token 线性=零差异。</summary>
    internal async Task AdvanceToken(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)
    {
        foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId))
        {
            if (!ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
            var target = FindNode(schema, edge.To);
            if (target is not null) { token.NodeId = target.Id; await EnterNodeAsync(inst, schema, target, token); return; }
        }
        ConsumeToken(token);
        AddHistory(inst.Id, token.NodeId, inst.StarterId, "end", "无后继节点，自动结束");
        FinishIfDrained(inst);
    }
}

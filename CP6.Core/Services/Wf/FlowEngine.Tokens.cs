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
        var anyActive = _db.Wf_FlowTokens.Local.Any(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active)
            || _db.Wf_FlowTokens.Any(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active);
        if (anyActive) return;
        if (inst.Status != FlowInstanceStatus.Running) return;   // 已驳回/撤回，不覆盖
        inst.Status = FlowInstanceStatus.Approved;
    }
}

using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>子流程级联取消（spec §3.3）：子实例走既有撤回语义（Withdrawn + 任务/token/履历/Pending job 清场）
/// 并沿其 token 递归孙代。级联产生的 Withdrawn **不投递唤醒凭据**（不调 SubFlowResume.EnqueueIfChild）——
/// 父已终态/已死 token 的回注入口由 CheckSubFlowGroupAsync 状态闸双保险。不 SaveChanges（随调用方外壳落库）。
/// <para>★ 接缝互指（spec §3.3）：消费方=① FlowEngine.CancelAllActiveTokens（实例终止/全清场）、
/// ② FlowEngine.CancelTokenSubtree 第五清（二期 SameBranch 剥离）、③ TaskCenterService.WithdrawAsync（就地循环）。
/// 二期 FlowEngine.Prune.cs 的 PruneTokenAsync 只剪「被驳任务的 token」（停泊 subFlow token 无任务不会被直接剪），
/// 其坍缩路径落在 CancelAllActiveTokens——若未来出现绕过上述三处的新 token 清场路径，必须同步审视本接缝。
/// <br/>本文件由 B-T2 落最小体（CancelInstanceTree + CancelChildrenOfToken）；C-T1 补三处挂钩消费，逐字一致。</para></summary>
internal static class SubFlowCascade
{
    /// <summary>取消 parentTokenId 名下全部在途子实例（组级联，递归）。无子实例=零行为查询。</summary>
    internal static void CancelChildrenOfToken(CP6Context db, Guid parentTokenId)
    {
        // Local ∪ DB 惯用法（本回合刚起的子实例在 Local 未落盘）
        var local = db.Wf_FlowInstances.Local.Where(i => i.ParentTokenId == parentTokenId).ToList();
        var localIds = local.Select(i => i.Id).ToHashSet();
        var fromDb = db.Wf_FlowInstances
            .Where(i => i.ParentTokenId == parentTokenId && !localIds.Contains(i.Id)).ToList();
        foreach (var c in local.Concat(fromDb))
            if (c.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended or FlowInstanceStatus.Draft)
                CancelInstanceTree(db, c);
    }

    /// <summary>单实例撤回语义清场（镜像 TaskCenterService.WithdrawAsync 的清场块）+ 孙代递归。</summary>
    internal static void CancelInstanceTree(CP6Context db, Wf_FlowInstance inst)
    {
        inst.Status = FlowInstanceStatus.Withdrawn;
        inst.ModifyDate = DateTime.Now;

        // 在途任务 → Cancelled（Local ∪ DB）
        foreach (var t in db.Wf_FlowTasks.Local.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
        var localTaskIds = db.Wf_FlowTasks.Local.Where(t => t.InstanceId == inst.Id).Select(t => t.Id).ToHashSet();
        foreach (var t in db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)
            && !localTaskIds.Contains(t.Id)).ToList())
            t.Status = FlowTaskStatus.Cancelled;

        // Active token → Cancelled（收集 id 供孙代递归）
        var cancelledTokens = new List<Guid>();
        foreach (var tk in db.Wf_FlowTokens.Local.Where(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active).ToList())
        { tk.Status = FlowTokenStatus.Cancelled; cancelledTokens.Add(tk.Id); }
        var localTokIds = db.Wf_FlowTokens.Local.Where(t => t.InstanceId == inst.Id).Select(t => t.Id).ToHashSet();
        foreach (var tk in db.Wf_FlowTokens.Where(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active
            && !localTokIds.Contains(t.Id)).ToList())
        { tk.Status = FlowTokenStatus.Cancelled; cancelledTokens.Add(tk.Id); }

        // Pending 传签履历 → Voided
        foreach (var f in db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == inst.Id && f.Status == FlowFormToStatus.Pending).ToList())
            f.Status = FlowFormToStatus.Voided;
        var localFtIds = db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == inst.Id).Select(f => f.Id).ToHashSet();
        foreach (var f in db.Wf_FlowFormTos.Where(f => f.InstanceId == inst.Id && f.Status == FlowFormToStatus.Pending
            && !localFtIds.Contains(f.Id)).ToList())
            f.Status = FlowFormToStatus.Voided;

        // Pending 服务作业 → Cancelled
        var now = DateTime.UtcNow;
        foreach (var j in db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == inst.Id && j.Status == ServiceJobStatus.Pending).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
        var localJobIds = db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == inst.Id).Select(j => j.Id).ToHashSet();
        foreach (var j in db.Wf_ServiceJobs.Where(j => j.InstanceId == inst.Id && j.Status == ServiceJobStatus.Pending
            && !localJobIds.Contains(j.Id)).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }

        db.Wf_FlowHistories.Add(new Wf_FlowHistory
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = inst.CurrentNode,
            ActorId = inst.StarterId, Action = "subFlowCascadeCancelled", Comment = null,
        });

        foreach (var tid in cancelledTokens) CancelChildrenOfToken(db, tid);   // 孙代递归（spec §3.3 第一路径）
    }
}

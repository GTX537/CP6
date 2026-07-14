using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>驳回剪枝（hardening spec §4）。partial：与 FlowEngine 共享 scoped DbContext 与内部方法。
/// 铁律：剪枝绝不改 inst.Status（除全剪光递归到顶走既有 Rejected 路径）；不自行 SaveChanges
/// （随 ActOnceAsync 尾部统一落库）；终态分发接缝（DispatchIfFinished 在 SaveChanges 前）保持不动。</summary>
public partial class FlowEngine
{
    /// <summary>剪枝入口（ActOnceAsync 驳回分支调用）。true=已按 prune 处理；false=按 cascade（调用方走既有连坐）。
    /// 仅当 token 有 fork 血缘、且其本层 split 配置 onBranchReject=="prune" 时才剪。</summary>
    internal async Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token,
        Guid actorId, string? comment)
    {
        var all = SnapshotTokens(inst.Id);
        var split = FindSplitNode(schema, all, token);
        if (split is null || !IsPrune(split)) return false;
        await PruneTokenAsync(inst, schema, token, actorId, comment);
        return true;
    }

    /// <summary>定位生成 token.ForkId 批次的 split 节点：ForkParent(token).NodeId（§4.1 定案：ParentTokenId 上溯，零迁移）。</summary>
    internal static FlowNode? FindSplitNode(FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken token)
    {
        if (token.ForkId is null) return null;
        var parent = TokenLineage.ForkParent(all, token);
        return parent is null ? null : FindNode(schema, parent.NodeId);
    }

    internal static bool IsPrune(FlowNode split)
        => string.Equals(split.OnBranchReject?.Trim(), "prune", StringComparison.OrdinalIgnoreCase);

    private static bool IsJoinType(FlowNode n) => FlowGraph.IsJoinType(n);

    /// <summary>剪本支：token → Pruned；本支任务 Cancelled、Pending FormTo → Voided（tokenId 过滤复用）；
    /// 记 branchPruned 履历 + BranchPruned 通知；再做 join 补放行探测与全剪光递归上弹。</summary>
    // 子流程接缝（spec §3.3）：本方法只剪「被驳任务的 token」，停泊 subFlow token 无任务不会流入此处；
    // 其级联取消由 CancelAllActiveTokens / CancelTokenSubtree / WithdrawAsync 三钩子负责——见 SubFlowCascade 类注释。
    private async Task PruneTokenAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token,
        Guid actorId, string? comment)
    {
        token.Status = FlowTokenStatus.Pruned;
        CancelPendingTasksOfToken(inst.Id, token.Id);
        VoidPendingFormTos(inst.Id, tokenId: token.Id);
        AddHistory(inst.Id, token.NodeId, actorId, "branchPruned", comment);
        await _notifier.BranchPrunedAsync(inst.StarterId, inst.Id, inst.FlowKey, token.NodeId, comment);
        await ReleaseOrCollapseAsync(inst, schema, token, actorId, comment);
    }

    /// <summary>剪枝后的 fork 批次收束（spec §4.2.3/§4.2.4，顺序敏感）：
    /// ① join 补放行：同 ForkId 停在 join 型节点的 Active token 重入 OnEnterAsync（计数幂等，重入安全）；
    ///    检测到任一停泊 token 变为 Consumed（即 join 已齐批放行）→ 立即返回——续 token 已上弹属上层批次，
    ///    此时「无 Active 穿过本批次」是正常收束而非剪光，若继续判剪光会误递归驳回（计划期新发现护栏）。
    /// ② 全剪光检测（血缘感知，与 §3.3 同款判据）：不存在「穿过本 fork 批次」的在途 Active token →
    ///    视同该 fork 的续 token 被驳回，递归应用上一层 fork 的 OnBranchReject：
    ///    外层 prune → 剪外层该支（记痕+通知+递归收束）；外层 cascade / 无外层 → 实例 Rejected 走既有终态路径。</summary>
    private async Task ReleaseOrCollapseAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken deadBranchToken,
        Guid actorId, string? comment)
    {
        var forkId = deadBranchToken.ForkId!.Value;

        // ① join 补放行探测
        var all = SnapshotTokens(inst.Id);
        bool released = false;
        var parkedByNode = all.Where(t => t.ForkId == forkId && t.Status == FlowTokenStatus.Active)
            .GroupBy(t => t.NodeId).ToList();
        foreach (var g in parkedByNode)
        {
            var node = FindNode(schema, g.Key);
            if (node is null || !IsJoinType(node)) continue;
            var probe = g.First();
            await EnterNodeAsync(inst, schema, node, probe);
            if (probe.Status == FlowTokenStatus.Consumed) released = true;   // join 齐批放行了
        }
        if (released) return;   // 正常收束，绝不判剪光

        // ② 全剪光递归上弹（血缘感知：内层子树在途也算活支）
        all = SnapshotTokens(inst.Id);
        if (all.Any(t => t.Status == FlowTokenStatus.Active && TokenLineage.CrossesFork(all, t, forkId)))
            return;   // 还有活支 → 本批次继续等

        var forkParent = TokenLineage.ForkParent(all, deadBranchToken);
        var outerSplit = forkParent is null || forkParent.ForkId is null
            ? null : FindSplitNode(schema, all, forkParent);
        if (forkParent is not null && outerSplit is not null && IsPrune(outerSplit))
        {
            // 外层 prune：视同 forkParent（外层该支代表）被驳回 → 剪外层该支并继续递归收束。
            // forkParent 已 Consumed（进 split 时退场），无需改状态；其在途后代刚被判定为零。
            AddHistory(inst.Id, forkParent.NodeId, actorId, "branchPruned", comment);
            await _notifier.BranchPrunedAsync(inst.StarterId, inst.Id, inst.FlowKey, forkParent.NodeId, comment);
            await ReleaseOrCollapseAsync(inst, schema, forkParent, actorId, comment);
        }
        else
        {
            // 外层 cascade / 无外层 → 实例 Rejected（既有连坐终态；DispatchIfFinished 由 ActOnceAsync 尾部统一做）
            inst.Status = FlowInstanceStatus.Rejected;
            CancelAllActiveTokens(inst.Id);
            CancelAllPendingTasks(inst.Id);   // 递归上弹连坐：清场兄弟支遗留的孤儿待办（token 由上一行清、任务在此清）
            VoidPendingFormTos(inst.Id);
        }
    }

    /// <summary>递归上弹全剪光坍缩本实例时，把全部 Pending/Suspended 待办 → Cancelled。
    /// 剪本支的待办已由 <see cref="CancelPendingTasksOfToken"/> 逐支清；本方法补清「从未被剪、
    /// 仅因外层坍缩而连坐」的兄弟支孤儿待办（如嵌套外层 cascade 时的外层兄弟）。
    /// Local + localIds-exclusion 惯用法，镜像 CancelPendingTasksOfToken。仅走剪枝坍缩路径，
    /// 既有默认 cascade（ActOnceAsync !pruned 分支）零改。</summary>
    private void CancelAllPendingTasks(Guid instanceId)
    {
        foreach (var t in _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
        var localIds = _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        foreach (var t in _db.Wf_FlowTasks.Where(t => t.InstanceId == instanceId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)
            && !localIds.Contains(t.Id)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
    }
}

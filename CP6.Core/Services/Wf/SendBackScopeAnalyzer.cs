using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>退回作用域（hardening spec §5.1 三分）。</summary>
public enum SendBackScope
{
    /// <summary>目标在当前分支可达域内 → 只剥离本支（剥离层子树清场 + 血缘保留重生）。</summary>
    SameBranch,
    /// <summary>目标在 fork 栈全部 split 之前（含线性流无 fork）→ 既有全清场整块重来。</summary>
    BeforeSplit,
    /// <summary>目标在兄弟分支域内 → 拒绝（E-WF-019，语义永久禁止）。</summary>
    SiblingBranch,
}

/// <summary>退回作用域判定（纯函数，动手清场前调用）。§5.3 定案：
/// fork 栈由 <see cref="TokenLineage.ForkStack"/> 血缘上溯（与剪枝共用口径）；
/// 分支域由 <see cref="FlowGraph.BranchDomain"/>（配对 join = <see cref="FlowGraph.NearestCommonJoin"/>，
/// 与校验 E-WF-021 共用口径）。逐层内→外：目标与当前节点同域 → SameBranch（首个命中层即
/// 「包含目标节点的最内层分支域」，剥离层 = 该层分支代表 token）；目标只在兄弟域 → SiblingBranch；
/// 全层不命中 → BeforeSplit。配对不可判定（无公共 join，环路/直通 end 的怪异 schema）→ 抛 E-WF-012 保守拒绝
/// （现状对跨网关退回本来就拒，非收紧）。</summary>
public static class SendBackScopeAnalyzer
{
    public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
        FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
        string currentNodeId, string targetNodeId)
    {
        foreach (var (branchToken, _, splitNodeId) in TokenLineage.ForkStack(all, current))
        {
            var split = schema.Nodes.FirstOrDefault(n => n.Id == splitNodeId)
                        ?? throw new InvalidOperationException("E-WF-012");
            var join = FlowGraph.NearestCommonJoin(schema, split)
                       ?? throw new InvalidOperationException("E-WF-012");
            var domains = schema.Edges.Where(e => e.From == split.Id && e.IsError != true)
                .Select(e => FlowGraph.BranchDomain(schema, e.To, join.Id)).ToList();

            var mine = domains.Where(d => d.Contains(currentNodeId)).ToList();
            if (mine.Any(d => d.Contains(targetNodeId)))
                return (SendBackScope.SameBranch, branchToken);
            if (domains.Any(d => d.Contains(targetNodeId)))
                return (SendBackScope.SiblingBranch, null);
            // 目标在本层块外 → 上探外层
        }
        return (SendBackScope.BeforeSplit, null);
    }
}

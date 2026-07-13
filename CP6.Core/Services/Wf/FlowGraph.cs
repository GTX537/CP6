namespace CP6.Core.Services.Wf;

/// <summary>schema 图论辅助。校验（E-WF-021 配对）与退回作用域分析（SendBackScopeAnalyzer）
/// 共用同一「最近公共汇聚 join」口径（spec §5.3 单一口径要求）。全部 BFS，环路安全（visited）。</summary>
internal static class FlowGraph
{
    internal static bool IsJoinType(FlowNode n)
        => (n.Type ?? "").Trim().ToLowerInvariant() is "paralleljoin" or "inclusivejoin";

    /// <summary>从 startId 正向可达节点集（含自身）。</summary>
    public static HashSet<string> ReachableFrom(FlowSchema schema, string startId)
    {
        var seen = new HashSet<string> { startId };
        var q = new Queue<string>(); q.Enqueue(startId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
                if (seen.Add(e.To)) q.Enqueue(e.To);
        }
        return seen;
    }

    /// <summary>split 的配对 join = 各出边（IsError!=true）可达集交集中的 join 型节点里、距 split BFS 最近者。
    /// 无出边 / 无公共 join → null（校验报 E-WF-021；退回作用域分析保守拒 E-WF-012）。</summary>
    public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split)
    {
        var outs = schema.Edges.Where(e => e.From == split.Id && e.IsError != true).ToList();
        if (outs.Count == 0) return null;
        HashSet<string>? common = null;
        foreach (var e in outs)
        {
            var r = ReachableFrom(schema, e.To);
            if (common is null) common = r; else common.IntersectWith(r);
        }
        if (common is null || common.Count == 0) return null;

        // 距 split 最近（BFS 深度）且为 join 型
        var depth = new Dictionary<string, int> { [split.Id] = 0 };
        var q = new Queue<string>(); q.Enqueue(split.Id);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
                if (!depth.ContainsKey(e.To)) { depth[e.To] = depth[cur] + 1; q.Enqueue(e.To); }
        }
        FlowNode? best = null; int bestD = int.MaxValue;
        foreach (var n in schema.Nodes)
        {
            if (!common.Contains(n.Id) || !IsJoinType(n)) continue;
            if (depth.TryGetValue(n.Id, out var d) && d < bestD) { best = n; bestD = d; }
        }
        return best;
    }

    /// <summary>分支域：从 split 某出边目标出发的正向可达集（含该目标），<b>不进入、不穿过</b> 配对 join。
    /// 退回作用域分析用（SameBranch/SiblingBranch 判定）。</summary>
    public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId)
    {
        var seen = new HashSet<string>();
        if (edgeTargetId == pairedJoinId) return seen;
        seen.Add(edgeTargetId);
        var q = new Queue<string>(); q.Enqueue(edgeTargetId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
            {
                if (e.To == pairedJoinId) continue;   // 到配对 join 即分支域边界
                if (seen.Add(e.To)) q.Enqueue(e.To);
            }
        }
        return seen;
    }
}

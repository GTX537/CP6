namespace CP6.Core.Services.Plan;

/// <inheritdoc/>
public class LowLevelCodeService : ILowLevelCodeService
{
    public IReadOnlyDictionary<string, int> Compute(IEnumerable<BomEdge> edges)
    {
        // 去重：同一父子关系经不同工程链可能重复出现，逻辑上只算一条边
        // （否则 indegree 多计，Kahn 永不归零，被误判成环）。
        var edgeSet = new HashSet<BomEdge>(edges);

        var nodes = new HashSet<string>();
        var children = new Dictionary<string, List<string>>();
        var indegree = new Dictionary<string, int>();

        foreach (var e in edgeSet)
        {
            nodes.Add(e.Parent);
            nodes.Add(e.Child);
        }
        foreach (var n in nodes) indegree[n] = 0;
        foreach (var e in edgeSet)
        {
            if (!children.TryGetValue(e.Parent, out var lst))
                children[e.Parent] = lst = new List<string>();
            lst.Add(e.Child);
            indegree[e.Child]++;
        }

        // Kahn 拓扑：indegree=0 为顶层（成品）层级 0；按父全部处理完再算子，故子取到最深父+1。
        var level = nodes.ToDictionary(n => n, _ => 0);
        var queue = new Queue<string>(nodes.Where(n => indegree[n] == 0));
        int processed = 0;

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            processed++;
            if (!children.TryGetValue(u, out var cs)) continue;
            foreach (var v in cs)
            {
                if (level[u] + 1 > level[v]) level[v] = level[u] + 1;
                if (--indegree[v] == 0) queue.Enqueue(v);
            }
        }

        if (processed < nodes.Count)
            throw new InvalidOperationException("E-PLAN-循环BOM: BOM 存在循环依赖，无法计算低层码");

        return level;
    }
}

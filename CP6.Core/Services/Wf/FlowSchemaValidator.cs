namespace CP6.Core.Services.Wf;

/// <summary>流程 schema 静态校验（设计器保存前后端共用）。返回错误码列表（空=合法）。所有结构性问题统一 E-WF-010。</summary>
public static class FlowSchemaValidator
{
    private static readonly HashSet<string> KnownStrategies =
        new(new[] { "DirectManager", "DeptLeader", "Role", "Specified", "Starter" }, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(FlowSchema schema)
    {
        var errs = new List<string>();
        if (schema is null || schema.Nodes.Count == 0) { errs.Add("E-WF-010"); return errs; }

        string T(FlowNode n) => (n.Type ?? "approval").Trim().ToLowerInvariant();
        var ids = schema.Nodes.Select(n => n.Id).ToHashSet();

        // ① 恰一 start ② 至少一 end
        if (schema.Nodes.Count(n => T(n) == "start") != 1) errs.Add("E-WF-010");
        if (!schema.Nodes.Any(n => T(n) == "end")) errs.Add("E-WF-010");

        // ③ 边引用存在节点
        foreach (var e in schema.Edges)
            if (!ids.Contains(e.From) || !ids.Contains(e.To)) { errs.Add("E-WF-010"); break; }

        // ⑤ approval 须有合法策略（仅单档节点；串簽节点由⑦单独校验）
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval" && (n.Stages is null || n.Stages.Count == 0)))
            if (n.ApproverStrategy is null || !KnownStrategies.Contains(n.ApproverStrategy)) { errs.Add("E-WF-010"); break; }

        // ⑦ 串簽档配置(E-WF-011):有 Stages 时每档合法
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval" && n.Stages is { Count: > 0 }))
        {
            bool bad = false;
            foreach (var st in n.Stages!)
            {
                var kind = (st.Kind ?? "fixed").Trim().ToLowerInvariant();
                bool ruleOk = kind == "managerchain"
                    ? st.MaxLevels is int ml && ml >= 1
                    : st.ApproverStrategy is not null && KnownStrategies.Contains(st.ApproverStrategy);
                var cs = (st.Countersign ?? "all").Trim().ToLowerInvariant();
                bool csOk = cs is "all" or "any" or "veto";
                if (!ruleOk || !csOk) { bad = true; break; }
            }
            if (bad) { errs.Add("E-WF-011"); break; }
        }

        // ⑥ 并行网关入/出边数
        foreach (var n in schema.Nodes.Where(n => T(n) == "parallelsplit"))
            if (schema.Edges.Count(e => e.From == n.Id) < 2) { errs.Add("E-WF-010"); break; }
        foreach (var n in schema.Nodes.Where(n => T(n) == "paralleljoin"))
            if (schema.Edges.Count(e => e.To == n.Id) < 2) { errs.Add("E-WF-010"); break; }

        // ④ 从 start 可达某 end（BFS）
        var start = schema.Nodes.FirstOrDefault(n => T(n) == "start");
        if (start is not null)
        {
            var adj = schema.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());
            var seen = new HashSet<string> { start.Id };
            var q = new Queue<string>(); q.Enqueue(start.Id);
            bool reachedEnd = false;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                var node = schema.Nodes.FirstOrDefault(n => n.Id == cur);
                if (node is not null && T(node) == "end") { reachedEnd = true; break; }
                if (adj.TryGetValue(cur, out var outs))
                    foreach (var to in outs) if (seen.Add(to)) q.Enqueue(to);
            }
            if (!reachedEnd) errs.Add("E-WF-010");
        }

        return errs.Distinct().ToList();
    }
}

namespace CP6.Core.Services.Wf;

/// <summary>流程 schema 静态校验（设计器保存前后端共用）。返回错误码列表（空=合法）。所有结构性问题统一 E-WF-010。</summary>
public static class FlowSchemaValidator
{
    private static readonly HashSet<string> KnownStrategies =
        new(new[] { "DirectManager", "DeptLeader", "Role", "Specified", "Starter", "FormField", "DataMap", "Group" }, StringComparer.OrdinalIgnoreCase);

    // 服务任务合法 kind：引擎按 ServiceKind 常量精确匹配(ServiceTaskNodeHandler)，此处同用序数比较以对齐运行期语义。
    private static readonly HashSet<string> KnownServiceKinds =
        new(new[] { ServiceKind.DataWriteback, ServiceKind.WebApi, ServiceKind.Timer }, StringComparer.Ordinal);

    // 服务任务合法 mode（spec §6.1；timer 由 handler 规整为 async，此处只校验用户显式填值）。
    // 用序数比较对齐运行期语义（ServiceTaskNodeHandler 以 mode == ServiceMode.Sync 精确匹配，常量为小写 "sync"/"async"）。
    private static readonly HashSet<string> KnownServiceModes =
        new(new[] { ServiceMode.Sync, ServiceMode.Async }, StringComparer.Ordinal);

    /// <summary>IsError 边合法来源类型（跨 spec 单一常量：本 infra 波写全集含 subFlow；
    /// 子流程 spec 2026-07-05-wfs-subflow-design §5 只加放行测试，不重复定义——两处注释互指）。
    /// <c>T(n)</c> 输出小写序数值，故用 <see cref="StringComparer.OrdinalIgnoreCase"/> 比较令 servicetask/approval/subflow 全命中。</summary>
    internal static readonly HashSet<string> ErrorEdgeSourceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "serviceTask", "approval", "subFlow" };

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

        // ⑤b 新策略配置完整性(E-WF-014)：单档节点
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval" && (n.Stages is null || n.Stages.Count == 0)))
        {
            if (n.ApproverStrategy == "FormField" && string.IsNullOrWhiteSpace(n.ApproverFieldName)) { errs.Add("E-WF-014"); break; }
            if (n.ApproverStrategy == "DataMap" && (string.IsNullOrWhiteSpace(n.ApproverMapKey) || string.IsNullOrWhiteSpace(n.ApproverFieldName))) { errs.Add("E-WF-014"); break; }
            if (n.ApproverStrategy == "Group" && (n.ApproverMembers is null || n.ApproverMembers.Count == 0)) { errs.Add("E-WF-014"); break; }
        }

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

        // ⑦b 串簽档新策略配置完整性(E-WF-014)：fixed 档
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval" && n.Stages is { Count: > 0 }))
        {
            bool stageBad014 = false;
            foreach (var st in n.Stages!)
            {
                var kind = (st.Kind ?? "fixed").Trim().ToLowerInvariant();
                if (kind != "fixed") continue;
                if ((st.ApproverStrategy == "FormField" && string.IsNullOrWhiteSpace(st.ApproverFieldName)) ||
                    (st.ApproverStrategy == "DataMap" && (string.IsNullOrWhiteSpace(st.ApproverMapKey) || string.IsNullOrWhiteSpace(st.ApproverFieldName))) ||
                    (st.ApproverStrategy == "Group" && (st.ApproverMembers is null || st.ApproverMembers.Count == 0)))
                {
                    stageBad014 = true; break;
                }
            }
            if (stageBad014) { errs.Add("E-WF-014"); break; }
        }

        // ⑥ 并行网关入/出边数
        foreach (var n in schema.Nodes.Where(n => T(n) == "parallelsplit"))
            if (schema.Edges.Count(e => e.From == n.Id) < 2) { errs.Add("E-WF-010"); break; }
        foreach (var n in schema.Nodes.Where(n => T(n) == "paralleljoin"))
            if (schema.Edges.Count(e => e.To == n.Id) < 2) { errs.Add("E-WF-010"); break; }

        // ⑧ 服务任务节点配置完整性(E-WF-016) + 非 end 须有成功出边(P2-3)。
        //    kind 非法 / dataWriteback 缺 ActionName / webApi 缺 Connector|Path / timer 缺 DelayMode|DelayValue → E-WF-016。
        //    serviceTask 必非 end，若无任何"非错误"出边则成功路径无后继(引擎会误结 Approved) → E-WF-016。
        foreach (var n in schema.Nodes.Where(n => T(n) == "servicetask"))
        {
            var kind = (n.ServiceKind ?? string.Empty).Trim();
            bool bad =
                !KnownServiceKinds.Contains(kind)
                || (kind == ServiceKind.DataWriteback && string.IsNullOrWhiteSpace(n.ServiceActionName))
                || (kind == ServiceKind.WebApi && (string.IsNullOrWhiteSpace(n.ServiceConnectorName) || string.IsNullOrWhiteSpace(n.ServicePath)))
                || (kind == ServiceKind.Timer && (string.IsNullOrWhiteSpace(n.ServiceDelayMode) || string.IsNullOrWhiteSpace(n.ServiceDelayValue)))
                || (!string.IsNullOrWhiteSpace(n.ServiceMode) && !KnownServiceModes.Contains(n.ServiceMode.Trim()))  // 票5：ServiceMode 值域(sync|async)
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServicePath)         // 票4：路径模板不得含数组下标
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServiceParamsJson)   // 票4：参数模板不得含数组下标
                || !schema.Edges.Any(e => e.From == n.Id && e.IsError != true);   // P2-3：无非错误出边
            if (bad) { errs.Add("E-WF-016"); break; }
        }

        // ⑨ 错误出边(E-WF-017)：任一节点至多 1 条 IsError 出边；IsError 边来源类型 ∈ ErrorEdgeSourceTypes
        //    （放宽：由「仅 serviceTask」→ {serviceTask, approval, subFlow}，供 approval 超时 errorEdge / 子流程失败路由复用）。
        var errorSourceIds = schema.Nodes.Where(n => ErrorEdgeSourceTypes.Contains(T(n))).Select(n => n.Id).ToHashSet();
        if (schema.Edges.Where(e => e.IsError == true).GroupBy(e => e.From).Any(g => g.Count() > 1)) errs.Add("E-WF-017");
        if (schema.Edges.Any(e => e.IsError == true && !errorSourceIds.Contains(e.From))) errs.Add("E-WF-017");

        // E-WF-027：TimeoutAction=errorEdge 的节点必须有 IsError 出边（否则超时无处路由 → 只能 Suspend，设计期拦下）
        foreach (var n in schema.Nodes.Where(n => string.Equals(n.TimeoutAction, "errorEdge", StringComparison.OrdinalIgnoreCase)))
            if (!schema.Edges.Any(e => e.From == n.Id && e.IsError == true)) { errs.Add("E-WF-027"); break; }

        // ⑩ inclusive 网关（hardening E-WF-020/021）
        // E-WF-020：inclusiveSplit 出边（非错误边）须 ≥2 且恰好一条无条件 default 兜底边
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivesplit"))
        {
            var outs = schema.Edges.Where(e => e.From == n.Id && e.IsError != true).ToList();
            var dflt = outs.Count(e => string.IsNullOrWhiteSpace(e.Condition));
            if (outs.Count < 2 || dflt != 1) { errs.Add("E-WF-020"); break; }
        }

        // E-WF-021a：每个 inclusiveSplit 的最近公共汇聚须存在且类型为 inclusiveJoin
        var pairedJoinIds = new HashSet<string>();
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivesplit"))
        {
            var join = FlowGraph.NearestCommonJoin(schema, n);
            if (join is null || T(join) != "inclusivejoin") { errs.Add("E-WF-021"); continue; }
            pairedJoinIds.Add(join.Id);
        }
        // E-WF-021b：inclusiveJoin 入边 ≥2 且被至少一个 inclusiveSplit 配对（孤立 join 报错）
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivejoin"))
            if (schema.Edges.Count(e => e.To == n.Id) < 2 || !pairedJoinIds.Contains(n.Id))
            { errs.Add("E-WF-021"); break; }
        // E-WF-021c：onBranchReject 值域 ∈ {cascade, prune}（大小写不敏感）且仅允许写在 split 型节点上
        foreach (var n in schema.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.OnBranchReject)) continue;
            var v = n.OnBranchReject.Trim().ToLowerInvariant();
            bool onSplit = T(n) is "parallelsplit" or "inclusivesplit";
            if (!onSplit || (v != "cascade" && v != "prune")) { errs.Add("E-WF-021"); break; }
        }

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

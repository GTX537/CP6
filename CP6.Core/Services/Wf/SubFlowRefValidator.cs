using System.Text.Json;
using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Wf;

/// <summary>子流程引用校验（spec §5 E-WF-025/026，保存时 DI 层——静态 FlowSchemaValidator 无 DI 查不了 FlowKey）。
/// 环检测口径（spec §3.1）：DFS 遍历**校验时刻的当前已发布版**；保存后其他流程再发布可能引入新环，
/// 由运行时深度守卫（SubFlowNodeHandler E-WF-026）兜底。链上任何 FlowKey 重复即环；深度 ≥ MaxDepth 拦。</summary>
internal static class SubFlowRefValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>违规抛 InvalidOperationException("E-WF-025 ..."/"E-WF-026 ...")。flowKey=正在保存的流程（用保存中的 schema，不读库中旧版）。</summary>
    internal static void Validate(CP6Context db, string flowKey, FlowSchema schema)
    {
        var chain = new List<string> { flowKey };
        Walk(db, schema, chain);
    }

    private static void Walk(CP6Context db, FlowSchema schema, List<string> chain)
    {
        foreach (var n in schema.Nodes.Where(n =>
            string.Equals((n.Type ?? "").Trim(), "subFlow", StringComparison.OrdinalIgnoreCase)))
        {
            var key = n.SubFlowKey?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("E-WF-025 subFlow 节点缺 SubFlowKey");
            if (chain.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"E-WF-026 子流程引用环: {string.Join("→", chain)}→{key}");
            if (chain.Count >= SubFlowLimits.MaxDepth)
                throw new InvalidOperationException($"E-WF-026 子流程引用深度超限({SubFlowLimits.MaxDepth})");

            var def = db.Wf_FlowDefs.FirstOrDefault(d => d.FlowKey == key);
            if (def is null || !def.Enable)
                throw new InvalidOperationException($"E-WF-025 子流程引用不存在或未启用: {key}");

            var target = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();
            chain.Add(key);
            Walk(db, target, chain);
            chain.RemoveAt(chain.Count - 1);
        }
    }
}

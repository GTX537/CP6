using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容分叉网关（hardening spec §3.1，第 7 个 handler）：对全部条件出边求值取真边集 T；
/// T 非空 → 激活 T（default 不走）；T 空 → 激活唯一 default 兜底边。每激活边各生一枚同 ForkId 子 token
/// （与 parallelSplit 完全相同的血缘机制）。default 边 = 唯一无条件出边（E-WF-020 校验保证存在且唯一），
/// 不是恒真必走边。激活集为空 = 校验漏网属 bug → 抛引擎异常，不静默。</summary>
internal sealed class InclusiveSplitNodeHandler : INodeHandler
{
    public string Type => "inclusiveSplit";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        eng.ConsumeToken(ctx.Token);                       // 入 token 退场

        var outs = schema.Edges.Where(e => e.From == node.Id && e.IsError != true).ToList();
        var condEdges = outs.Where(e => !string.IsNullOrWhiteSpace(e.Condition)).ToList();
        var defaults  = outs.Where(e =>  string.IsNullOrWhiteSpace(e.Condition)).ToList();

        // 注意不能对全边直接调 Evaluate：空表达式在 ExpressionEvaluator 里恒真，必须先分组
        var truthy = condEdges.Where(e => ExpressionEvaluator.Evaluate(e.Condition, inst.VarsJson)).ToList();
        var active = truthy.Count > 0 ? truthy : defaults.Take(1).ToList();

        var forkId = Guid.NewGuid();
        // 两阶段（终审 Critical#1，与 ParallelSplitNodeHandler 同款）：先全量 SpawnToken 再逐个 EnterNodeAsync，
        // 防「激活边直连 join 先处理」时首枚子 token 看不到未生兄弟 → join 提前放行+二次放行+孤儿 Active 永泊。
        var activated = new List<string>();
        var spawned = new List<(FlowNode Target, Wf_FlowToken Child)>();
        foreach (var edge in active)
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            spawned.Add((target, eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId)));
            activated.Add(edge.To);
        }
        if (activated.Count == 0)   // 防御式兜底：校验漏网（无 default 且全假 / 激活边目标缺失）
            throw new InvalidOperationException($"E-WF-020: inclusiveSplit {node.Id} 无可激活出边");
        foreach (var (target, child) in spawned)
            await eng.EnterNodeAsync(inst, schema, target, child);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "inclusiveSplit",
            "activated: " + string.Join(",", activated));
    }
}

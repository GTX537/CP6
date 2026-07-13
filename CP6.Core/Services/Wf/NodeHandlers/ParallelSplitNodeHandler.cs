using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>并行分叉网关（WFS P1）：入 token 退场，沿每条出边各生一枚同 ForkId 子 token 并即进目标节点。
/// 忽略边 Condition（并行=全激活），ParentTokenId 串血缘供 join 上弹。</summary>
internal sealed class ParallelSplitNodeHandler : INodeHandler
{
    public string Type => "parallelSplit";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        eng.ConsumeToken(ctx.Token);                       // 入 token 退场
        var forkId = Guid.NewGuid();
        // 两阶段（终审 Critical#1）：先全量 SpawnToken 再逐个 EnterNodeAsync。单相 spawn+Enter 时若存在
        // 「split 直连 join」出边且先处理，首枚子 token 同步抵达 join 时兄弟 token 还没生出 → 动态计票
        // 看不到阻挡者 → 提前放行+兄弟后到二次放行+孤儿 Active 永泊。先全 spawn 保证首枚到场者
        // 能看到全部同批兄弟（CrossesFork 阻挡成立）而正确停泊；全直连极端形态下首个 Enter 齐批
        // 消费+放行一次、后续 Enter 因 token 已 Consumed（arrived==0）天然 no-op，幂等。
        var spawned = new List<(FlowNode Target, Wf_FlowToken Child)>();
        foreach (var edge in schema.Edges.Where(e => e.From == node.Id))   // 忽略 Condition，全激活
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            spawned.Add((target, eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId)));
        }
        foreach (var (target, child) in spawned)
            await eng.EnterNodeAsync(inst, schema, target, child);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "parallelSplit", null);
        if (spawned.Count == 0) eng.FinishIfDrained(inst);   // 误配（无可达出边）→ 别留零 token 的死 Running 实例
    }
}

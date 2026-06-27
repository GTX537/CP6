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
        foreach (var edge in schema.Edges.Where(e => e.From == node.Id))   // 忽略 Condition，全激活
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            var child = eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId);
            await eng.EnterNodeAsync(inst, schema, target, child);
        }
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "parallelSplit", null);
    }
}

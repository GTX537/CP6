using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>并行汇聚网关（WFS P1）：靠同 ForkId 认亲计数，等齐入边数才放行，否则停泊（计数本身即幂等闸）。
/// 放行=消费同批 token + 续生一枚"上弹一层"血缘的 token（恢复父 ForkId，供嵌套外层 join 认亲）沿单出边继续。</summary>
internal sealed class ParallelJoinNodeHandler : INodeHandler
{
    public string Type => "parallelJoin";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        var inEdges = schema.Edges.Count(e => e.To == node.Id);

        var arrived = AllTokens(eng).Count(t => t.InstanceId == inst.Id && t.NodeId == node.Id
            && t.ForkId == ctx.Token.ForkId && t.Status == FlowTokenStatus.Active);
        if (arrived < inEdges) return;   // 未齐，停泊等其余分支（幂等闸=计数本身）

        var batch = AllTokens(eng).Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id
            && t.ForkId == ctx.Token.ForkId && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in batch) eng.ConsumeToken(t);

        var parentTok = ctx.Token.ParentTokenId is Guid pid
            ? AllTokens(eng).FirstOrDefault(t => t.Id == pid) : null;
        var cont = eng.SpawnToken(inst, node, parent: parentTok?.ParentTokenId, fork: parentTok?.ForkId);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "parallelJoin", null);
        await eng.AdvanceToken(inst, schema, cont);   // 续 token 沿 join 单出边继续
    }

    private static IEnumerable<Wf_FlowToken> AllTokens(FlowEngine eng)
        => eng.Db.Wf_FlowTokens.Local.Concat(eng.Db.Wf_FlowTokens.AsEnumerable()).Distinct();
}

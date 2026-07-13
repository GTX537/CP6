using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>并行/包容 join 共享放行逻辑（D3 不合并 handler、抽静态辅助；D4 血缘感知动态计票）。
/// 放行判据（spec §3.3）：本 join 到场数（同 ForkId Active）≥1 且 不存在「穿过本 fork 批次」的其他在途
/// Active token（停在本 join 的到场 token 除外）。血缘感知 = 分支进入内层 split 后同 ForkId 无 Active、
/// 但子 token 祖先链穿过本批次 → 仍挡放行（防「A 到场、B 在内层子 fork 在途」误判提前放行）。
/// ForkId==null 退化（线性 token 进 join 的怪异 schema）→ 沿用旧静态入边计票，bit 级等价。
/// 放行 = 消费同批到场 token + 续生一枚「上弹一层」血缘的 token 沿单出边继续（原 ParallelJoinNodeHandler 机制原样保留）。
/// 计数本身即幂等闸：未齐重入 no-op，重入安全（剪枝补放行依赖此性质）。</summary>
internal static class GatewayJoinHelper
{
    public static async Task TryReleaseAsync(NodeContext ctx, string historyAction)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        var all = eng.SnapshotTokens(inst.Id);

        if (ctx.Token.ForkId is not Guid forkId)
        {
            // 退化路径：与旧 ParallelJoinNodeHandler 静态计票字节等价
            var inEdges = schema.Edges.Count(e => e.To == node.Id);
            var nullArrived = all.Count(t => t.NodeId == node.Id && t.ForkId == null
                && t.Status == FlowTokenStatus.Active);
            if (nullArrived < inEdges) return;
        }
        else
        {
            var arrivedCount = all.Count(t => t.NodeId == node.Id && t.ForkId == forkId
                && t.Status == FlowTokenStatus.Active);
            if (arrivedCount == 0) return;
            bool blocking = all.Any(t => t.Status == FlowTokenStatus.Active
                && !(t.NodeId == node.Id && t.ForkId == forkId)     // 停在本 join 的到场 token 除外
                && TokenLineage.CrossesFork(all, t, forkId));
            if (blocking) return;   // 还有活支（含内层子树在途）→ 停泊等
        }

        var batch = all.Where(t => t.NodeId == node.Id && t.ForkId == ctx.Token.ForkId
            && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in batch) eng.ConsumeToken(t);

        var parentTok = ctx.Token.ParentTokenId is Guid pid
            ? all.FirstOrDefault(t => t.Id == pid) : null;
        var cont = eng.SpawnToken(inst, node, parent: parentTok?.ParentTokenId, fork: parentTok?.ForkId);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, historyAction, null);
        await eng.AdvanceToken(inst, schema, cont);   // 续 token 沿 join 单出边继续
    }
}

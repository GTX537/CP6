namespace CP6.Core.Services.Wf;

/// <summary>结束节点：只消费当前 token 并记 end 履历；无 Active 残留才整体 Approved（多 token 安全）。</summary>
internal sealed class EndNodeHandler : INodeHandler
{
    public string Type => "end";
    public async Task OnEnterAsync(NodeContext ctx)
    {
        if (ctx.Node.CcUsers is { Count: > 0 })
            await ctx.Engine.WriteCcAsync(ctx.Inst, ctx.Node.Id, ctx.Node.CcUsers, ctx.Node.CcRoleId);
        ctx.Engine.ConsumeToken(ctx.Token);                       // 只消费当前 token
        ctx.Engine.AddHistory(ctx.Inst.Id, ctx.Node.Id, ctx.Inst.StarterId, "end", null);
        ctx.Engine.FinishIfDrained(ctx.Inst);                     // 无 Active 残留才整体 Approved
    }
}

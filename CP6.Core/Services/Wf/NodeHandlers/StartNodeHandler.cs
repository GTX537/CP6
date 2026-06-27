namespace CP6.Core.Services.Wf;

/// <summary>开始节点：token 沿唯一出边直穿（不消费），等价旧 EnterNodeAsync start 分支调 NextNodeAsync。</summary>
internal sealed class StartNodeHandler : INodeHandler
{
    public string Type => "start";
    public Task OnEnterAsync(NodeContext ctx)
        => ctx.Engine.AdvanceToken(ctx.Inst, ctx.Schema, ctx.Token);   // 沿单边推进，token 不消费
}

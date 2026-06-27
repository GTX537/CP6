namespace CP6.Core.Services.Wf;

using CP6.Entity.DomainModels.Wf;

/// <summary>节点处理器（WFS P1 插件架构）。每种 FlowNode.Type 一实现，封装"token 进该节点做什么"。
/// EnterNodeAsync 退化为 _handlers[node.Type].OnEnterAsync(ctx)。改库不落盘（调用方统一 SaveChanges）。</summary>
internal interface INodeHandler
{
    string Type { get; }
    Task OnEnterAsync(NodeContext ctx);
}

/// <summary>节点处理上下文。操作主语 = token（一实例可多 token）。Engine 回指以复用引擎能力。</summary>
internal sealed class NodeContext
{
    public required Wf_FlowInstance Inst { get; init; }
    public required FlowSchema Schema { get; init; }
    public required FlowNode Node { get; init; }
    public required Wf_FlowToken Token { get; init; }
    public required FlowEngine Engine { get; init; }
}

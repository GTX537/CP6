using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容汇聚网关（hardening spec §3.2，第 8 个 handler）：与 parallelJoin 同构，共用
/// <see cref="GatewayJoinHelper"/> 动态计票（活支==实际激活边数，只等真走的分支——inclusive join 标准解）。
/// D3：独立节点类型，不与 parallelJoin 合并。</summary>
internal sealed class InclusiveJoinNodeHandler : INodeHandler
{
    public string Type => "inclusiveJoin";
    public Task OnEnterAsync(NodeContext ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "inclusiveJoin");
}

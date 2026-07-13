using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>并行汇聚网关（WFS P1 → hardening D4 动态计票）：放行判据与机制见 <see cref="GatewayJoinHelper"/>。
/// 无剪枝/无嵌套时与旧静态入边计票行为全等（ParallelGatewayTests + DynamicJoinCountTests 回归锁定）。</summary>
internal sealed class ParallelJoinNodeHandler : INodeHandler
{
    public string Type => "parallelJoin";
    public Task OnEnterAsync(NodeContext ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "parallelJoin");
}

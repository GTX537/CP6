using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    internal void RequireSchedulingSimulation()
    {if(authority.Resource!=OperationsOwnerV1.Planning||authority.Action!="simulate-source"||authority.Scope!=5)throw new OrderPriceCorrectionException("SCHED_SIMULATION_SCOPE_REQUIRED",403);}
}

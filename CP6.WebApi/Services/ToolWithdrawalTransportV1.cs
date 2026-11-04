using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
namespace CP6.WebApi.Services;

public sealed class ToolWithdrawalTransportV1:IToolWithdrawalTransportV1,IDisposable
{
    private readonly ToolApprovalDeliveryTransportV1 transport;
    public ToolWithdrawalTransportV1(ToolApprovalDeliveryOptionsV1 options,CrmOidcOptions oidc,ApprovalIntakeRegistryV2 intake,bool development=false)
        =>transport=new(new ToolApprovalDeliveryRegistryV1(options,oidc,intake,development));
    internal ToolWithdrawalTransportV1(ToolApprovalDeliveryRegistryV1 registry,HttpMessageHandler handler)=>transport=new(registry,handler);
    public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>transport.Route(tenant);
    public Task<ToolApprovalDeliveryResponseV1> QueryAsync(ToolApprovalDeliveryRouteV1 route,Guid command,CancellationToken ct)=>transport.QueryWithdrawalAsync(route,command,ct);
    public Task<ToolApprovalDeliveryResponseV1> ContextAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,CancellationToken ct)=>transport.WithdrawalContextAsync(route,submission,ct);
    public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,string rowVersion,byte[] original,CancellationToken ct)
        =>transport.SendWithdrawalAsync(route,submission,rowVersion,original,ct);
    public void Dispose()=>transport.Dispose();
}

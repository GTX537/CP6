using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;
public partial class FlowEngine
{
    internal async Task<Wf_FlowInstance> StagePersonalTimeoutErrorV2(Guid environment,Guid data,Guid root,
        FlowPublicationV2 publication,Guid task,Guid service,CancellationToken ct)
    {
        PersonalContext(environment,data,root,publication);_personalFormDecision=true;
        await TimeoutErrorKernel(task,service,ct);await DrainApprovalSubflows(ct);
        return await _db.Wf_FlowInstances.SingleAsync(i=>i.Id==root,ct);
    }
    internal async Task<Wf_FlowInstance> StageAppTimeoutErrorV2(FlowPublicationV2 publication,ApprovalSubmitCommandV2 original,
        Guid task,Guid service,CancellationToken ct)
    {
        AppOperationContext(publication,original);_approvalDecisionV2=true;
        await TimeoutErrorKernel(task,service,ct);await DrainApprovalSubflows(ct);
        return await _db.Wf_FlowInstances.SingleAsync(i=>i.Id==ManagedRoot,ct);
    }
}

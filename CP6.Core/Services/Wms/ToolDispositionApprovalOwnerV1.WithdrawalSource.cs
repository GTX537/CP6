using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

public sealed partial class ToolDispositionApprovalOwnerV1
{
    public sealed record WithdrawalScopeV1(Guid EnvironmentId,Guid TenantId,Guid ActorId);
    public Task<WithdrawalScopeV1> WithdrawalScopeAsync(CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>{
            await scope.Root(t);return new WithdrawalScopeV1(Environment,db.CurrentTenantId,a.ActorId);
        },ct);
    private sealed record WithdrawalContext(Context Context,ToolDispositionApprovalStoreV1.SubmissionRow Submission,ToolApprovalControlStoreV1.Head Control);
    private async Task<WithdrawalContext> WithdrawalContextOwned(OperationsAccessV1 a,Guid tool,Guid submission,bool write,CancellationToken ct)
    {
        if(write){var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));}
        var c=await Prepare(a,tool,false,ct);a.RequireToolFieldRead("Remarks");
        var row=c.Submissions.SingleOrDefault(x=>x.Id==submission)??throw Error("RESOURCE_NOT_FOUND",404);
        if(c.Disposition.Request?.Reference.Id!=row.Request)throw Error("TOOL_APPROVAL_SOURCE_CHANGED",409);
        var controls=new ToolApprovalControlStoreV1(db,Environment);await controls.Ready(ct);
        return new(c,row,await controls.Read(submission,ct));
    }
    private async Task<(ToolDispositionApprovalStoreV1 Store,ToolDispositionApprovalStoreV1.SubmissionRow Row,ApprovalSubmitCommandV2 Envelope)> Frozen(ApprovalSubmitCommandV2 incoming,CancellationToken ct)
    {
        await scope.Root(ct);
        if(incoming.Subject.BusinessOwner!=ToolDispositionStoreV1.DispositionOwner
            ||incoming.SubmitAuthorizationRef.Owner!=ToolDispositionApprovalStoreV1.AuthorizationOwner
            ||!Guid.TryParseExact(incoming.SubmitAuthorizationRef.Id,"D",out var submission))throw Error("WITHDRAW_FACT_MISMATCH",422);
        var tool=await db.Database.SqlQuery<Guid?>($"""
            SELECT tool_id AS "Value" FROM cp6_tool_approval_v1.submissions
            WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId} AND id={submission}
            """).SingleOrDefaultAsync(ct)??throw Error("WITHDRAW_FACT_MISMATCH",422);
        var store=new ToolDispositionApprovalStoreV1(db,Environment);await store.Fence(false,ct);
        var row=(await store.Submissions(tool,ct)).Single(x=>x.Id==submission);
        var expected=ApprovalSubmissionWireV2.Parse(row.Payload);
        var parsed=ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(incoming));
        if(expected.IntentCore!=parsed.IntentCore)throw Error("WITHDRAW_FACT_MISMATCH",422);
        return(store,row,expected.Value);
    }
    private async Task ValidateParticipant(OperationsAccessV1 access,Guid actor,CancellationToken ct)
    {
        await access.ValidateFinalAsync(ct);
        foreach(var initial in access.Authorities)
        {
            var final=await Authority.ToolWithdrawalAuthorityAsync(initial.Resource,initial.Action,actor,ct);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion||final.OrganizationVersion!=initial.OrganizationVersion)
                throw Error("AUTHORITY_CHANGED",403);
        }
    }
}

using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

public sealed partial class ToolDispositionApprovalOwnerV1
{
    internal sealed record IntakeProof(Guid FrozenSubmissionId,string Evidence);
    internal async Task<IntakeProof> VerifyIntakeEvidence(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        var e=incoming.Value;await scope.Root(ct);
        if(e.Scope.EnvironmentId!=Environment.ToString()||e.Scope.TenantId!=db.CurrentTenantId
            ||e.Subject.BusinessOwner!=ToolDispositionStoreV1.DispositionOwner||e.Subject.BizType!="WMS_TOOL_DISCARD"
            ||e.Subject.SubjectKind!="TOOL_DISPOSITION_REQUEST"||e.Engineering!=null||e.RoundId!=null||e.ReplacesSubmissionId!=null)
            throw Error("OWNER_EVIDENCE_SCOPE_INVALID",422);
        if(e.SubmitAuthorizationRef.Owner!=ToolDispositionApprovalStoreV1.AuthorizationOwner||e.SubmitAuthorizationRef.Version!="1"
            ||!Guid.TryParseExact(e.SubmitAuthorizationRef.Id,"D",out var frozenId))throw Error("MISSING_EVIDENCE",422);
        var row=await db.Database.SqlQuery<ToolDispositionApprovalStoreV1.SubmissionRow>($"""
            SELECT id AS "Id",tool_id AS "Tool",request_id AS "Request",policy_id AS "Policy",actor_id AS "Actor",operation_id AS "Operation",
              intent_hash AS "Intent",payload AS "Payload",payload_digest AS "Digest",authorization_json AS "Authorization"
            FROM cp6_tool_approval_v1.submissions WHERE tenant_id={db.CurrentTenantId} AND environment_id={Environment} AND id={frozenId}
            """).SingleOrDefaultAsync(ct)??throw Error("MISSING_EVIDENCE",422);
        // Read the actual immutable Owner envelope. Transport cannot supply its own authorization ref,
        // starter, snapshot or policy and make it trusted by merely computing another digest.
        var original=ApprovalSubmissionWireV2.Parse(row.Payload);
        if((e.SubmissionId==row.Id?original.Core!=incoming.Core:original.IntentCore!=incoming.IntentCore)||original.Value.StarterId!=row.Actor||original.Full!=row.Digest)
            throw Error(e.SubmissionId==row.Id?"SUBMISSION_PAYLOAD_CONFLICT":"INTENT_PAYLOAD_CONFLICT",409);
        Task<OperationsOwnerV1.Authority> Current(string resource,string action,CancellationToken token)
            =>Authority.ToolApprovalIntakeAuthorityAsync(resource,action,row.Actor,token);
        var authority=await Current(OperationsOwnerV1.ToolInstance,"dispose-submit",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
        var c=await Prepare(access,row.Tool,false,ct);
        if(c.Disposition.Request?.Reference.Id!=row.Request||c.Disposition.IsDiscarded)throw Error("SUBJECT_SUPERSEDED",409);
        var policy=c.Policies.SingleOrDefault(p=>p.Reference.Id==row.Policy)??throw Error("MISSING_EVIDENCE",422);
        await RequirePolicy(c,policy.Reference,ct,storedSources:true);await access.RequireToolInstance(db,row.Tool,ct);
        await access.ValidateFinalAsync(ct);
        foreach(var initial in access.Authorities)
        {
            var final=await Current(initial.Resource,initial.Action,ct);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion||final.OrganizationVersion!=initial.OrganizationVersion)
                throw Error("AUTHORITY_CHANGED",403);
        }
        var tools=new ToolInstanceStoreV1(db);await tools.Fence(ct);
        if((await tools.Read(await access.SourceAsync(OperationsOwnerV1.ToolInstance,ct),row.Tool,null,ct)).Reference!=c.Disposition.Native)
            throw Error("TOOL_INSTANCE_CHANGED",412);
        return new(row.Id,JsonSerializer.Serialize(new{provider="Main.Wms.ToolDispositionApproval.v1",row.Id,row.Tool,row.Actor,row.Digest,
            OriginalAuthorization=row.Authorization,CurrentAuthorities=Witnesses(access),CurrentContext=c.Disposition.Etag,Policy=policy.Reference},ApprovalBindingOwnerV2.Json));
    }
}

using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ControlledDocumentReviewOwnerV1 DocumentReviews(Harness h,ContentStore store)
        =>new(h.Db,DocumentAuthority(h),ScopeFence(h),Documents(h,store));
    private async Task<(ContentStore Files,ControlledDocumentReviewOwnerV1 Owner,Guid Id)> DocumentReviewSetup(Harness h,bool registerScope=true)
    {
        var files=new ContentStore();var input=await DocumentInput(h,files);await Documents(h,files).CommandAsync(input);
        if(registerScope)await RegisterScope(h);
        foreach(var action in new[]{"review-policy","review-submit","review-decide"})await EngineeringAction(h,"plm-controlled-document",action);
        return(files,DocumentReviews(h,files),input.DocumentId);
    }
    private static DocumentReviewPolicyCommandV1 DocumentPolicy()=>new(Guid.NewGuid(),"ENABLED",
        [new("CONTENT","Review exact original, derivation, source and authority language",72,1)],
        ["Explicit local test requirement; no enterprise approval claim"],false,
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()),
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()),"Explicit test review policy");
    private static DocumentReviewSubmitV1 DocumentSubmit(DocumentReviewContextV1 c)=>new(Guid.NewGuid(),c.Document.Reference,
        c.Policies.Last().Reference,"Review the frozen original and derived drawing; compare source and authority language",[],Guid.NewGuid(),null,"Initial professional review");
    private static async Task<DocumentReviewContextV1> DocumentReady(ControlledDocumentReviewOwnerV1 owner,Guid id)
    {
        var first=await owner.ContextAsync(id);await owner.PolicyAsync(id,first.Etag,DocumentPolicy());return await owner.ContextAsync(id);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_submission_freezes_exact_content_policy_outbox_and_original_without_approval()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);var ready=await DocumentReady(f.Owner,f.Id);var command=DocumentSubmit(ready);
        var result=await f.Owner.SubmitAsync(f.Id,ready.Etag,command);Assert.False(result.Submission!.IsApproved);
        Assert.Equal(result,await f.Owner.SubmitAsync(f.Id,ready.Etag,command));
        Assert.Equal(result,(await f.Owner.OperationAsync(command.OperationId,false)).Result);
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var envelope=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(payload,ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(ready.Document.Reference.Digest,envelope.Subject.ContentDigest.Value);
        Assert.Equal(ready.Policies.Last().Reference.Digest,envelope.Policy.Digest.Value);
        Assert.Equal(command.ReviewRoundId.ToString(),envelope.RoundId);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        var changed=DocumentRevise(ready.Document) with{Body=ready.Document.Body with{RevisionLabel="B"}};
        await Documents(h,f.Files).CommandAsync(changed);
        Assert.Equal(payload,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(result,(await f.Owner.OperationAsync(command.OperationId,false)).Result);
        var current=await f.Owner.ContextAsync(f.Id);Assert.Equal("2",current.Document.Reference.Version);Assert.Equal("1",current.Submissions.Single().Document.Version);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_document_review_v1.submissions WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_actual_roles_current_submit_permission_and_exact_context_gate_new_effects()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);var first=await f.Owner.ContextAsync(f.Id);
        var bad=DocumentPolicy() with{Requirements=[new("CONTENT","Missing role",987654321,1)]};
        Assert.Equal("DOCUMENT_REVIEW_ROLE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PolicyAsync(f.Id,first.Etag,bad))).Message);
        Assert.Empty((await f.Owner.ContextAsync(f.Id)).Policies);
        var ready=await DocumentReady(f.Owner,f.Id);var command=DocumentSubmit(ready);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(f.Id,first.Etag,command))).Status);
        Assert.Empty((await f.Owner.ContextAsync(f.Id)).Submissions);
        var original=await f.Owner.SubmitAsync(f.Id,ready.Etag,command);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-submit").ExecuteDeleteAsync();
        Assert.Equal(original,await f.Owner.SubmitAsync(f.Id,ready.Etag,command));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(f.Id,ready.Etag,command with{OperationId=Guid.NewGuid()}))).Status);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_native_flow_intake_uses_document_evidence_and_rejects_forged_original()
    {
        await using var h=await Seed();var (binding,flows,version)=await PrepareBinding(h);
        var f=await DocumentReviewSetup(h,false);var ready=await DocumentReady(f.Owner,f.Id);
        var result=await f.Owner.SubmitAsync(f.Id,ready.Etag,DocumentSubmit(ready));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var principal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'PLM_DOCUMENT_REVIEW','DOCUMENT_REVISION','Main.Plm.ControlledDocument.v1',{Guid.NewGuid()},
              'Main.Plm.DocumentReview.snapshot/1','[]',true,{principal},'CP6.Services','/internal/plm/document-review-results/v1','DEMO')
            """);
        var at=BindingTime();await binding.SaveAsync(null,null,BindingBytes(BindingInput(version,at) with{BizType="PLM_DOCUMENT_REVIEW",SubjectKind="DOCUMENT_REVISION",DetailRoute="/plm/controlled-document/{bizId}"}));
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),flows,new(ToolApproval(h),f.Owner)){ReceiveClock=()=>at.AddSeconds(1)};
        var ack=await intake.SubmitAsync(payload);Assert.Equal(201,ack.Status);Assert.Equal(result.Submission!.SubmissionId,ack.Ack.SubmissionId);
        Assert.Equal(version,ack.Ack.FlowDefVersionId);Assert.Equal("DISPATCH_PENDING",ack.Ack.State);
        var original=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(payload,ApprovalBindingOwnerV2.Json)!;
        var forged=original with{CommandId=Guid.NewGuid(),StarterId=Guid.NewGuid()};
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>intake.SubmitAsync(ApprovalCanonicalV2.Bytes(forged)))).Status);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}

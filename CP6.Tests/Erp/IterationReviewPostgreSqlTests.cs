using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(ApprovalBindingOwnerV2 Owner,FlowAuthoringOwnerV2 Flows,Guid Version)> ExistingReviewBinding(Harness h)
    {
        var flows=Authoring(h);return(BindingOwner(h,flows),flows,(await flows.ActivationPreviewAsync("binding-flow")).Current.PublishedVersionId!.Value);
    }
    private static IterationReviewOwnerV1 IterationReviews(Harness h,ContentStore files)
    {
        var authority=CandidateAuthority(h);var iterations=Iterations(h,files);
        var checks=new EngineeringSourceCheckService(h.Db,authority,Documents(h,files),Specifications(h,files));
        return new(h.Db,authority,ScopeFence(h),iterations,new(h.Db,authority,iterations,checks,ScopeFence(h)));
    }
    private sealed record IterationReviewFixture(ContentStore Files,IterationFixture Iteration,EngineeringCheckViewV1 Check,
        IterationCandidateService Candidates,IterationCandidateCommandV1 Candidate,IterationReviewContextV1 Context,Guid Principal);
    private async Task<IterationReviewFixture> IterationReviewSetup(Harness h,int count=2)
    {
        var f=await CheckSetup(h);var checkAuthority=CandidateAuthority(h);
        var checks=new EngineeringSourceCheckService(h.Db,checkAuthority,Documents(h,f.Files),Specifications(h,f.Files));
        var revision=await new EngineeringRevisionOwnerV1(h.Db,checkAuthority,Documents(h,f.Files),Specifications(h,f.Files)).ReadAsync(f.Command.Revision.Id,null);
        var iteration=await IterationSetup(h,f.Files,revision.Draft.ProductRef,reuseBinding:true);
        foreach(var action in new[]{"candidate-write","review-policy","review-submit","review-decide"})await EngineeringAction(h,"plm-engineering-iteration",action);
        // Check only after all native inputs and permission registrations are established.
        var checkResult=await checks.CheckAsync(f.Command);var check=await checks.ReadAsync(checkResult.CheckRunId);Assert.Equal("PASS",check.Original.State);
        var iterations=Iterations(h,f.Files);var work=await IterationWrite(iterations,IterationChange(iteration.Iteration,"SAVE_TASK") with{Task=IterationTask(h,iteration.Reviewer)});
        work=await IterationWrite(iterations,IterationChange(work,"START"));iteration=iteration with{Iteration=work};
        var candidates=new IterationCandidateService(h.Db,checkAuthority,iterations,checks,ScopeFence(h));
        var c=new IterationCandidateCommandV1(Guid.NewGuid(),work.Reference.Id,Guid.NewGuid(),"CREATE",work.Reference,work.Etag,null,null,
            "准确原生方案","完整批准来源和差异","原始限制保持不变",check.Reference,null,"ADOPT_CURRENT","","Create exact review candidate");
        for(var i=0;i<count;i++)await candidates.CommandAsync(c with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid(),Title="方案 "+i});
        var o=IterationReviews(h,f.Files);var context=await o.ContextAsync(work.Reference.Id);
        var at=DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await o.PolicyAsync(work.Reference.Id,context.Etag,new(Guid.NewGuid(),"ENABLED",[new("TECHNICAL","Review complete candidate set and accurate original inputs",72,1)],"RETURN",true,at.AddHours(-1),at.AddDays(1),"Assigned independent project reviewer"));
        var principal=Guid.NewGuid();await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'PLM_ITER_REVIEW','PLM_ITER_REVIEW','Main.Plm.EngineeringIteration.v1',{Guid.NewGuid()},
              'ITER-OA-SNAPSHOT/1','[]',true,{principal},'CP6.Services','/internal/plm/iteration-review-results/v1','DEMO')
            """);
        return new(f.Files,iteration,check,candidates,c,await o.ContextAsync(work.Reference.Id),principal);
    }
    private static IterationReviewSubmitV1 IterationSubmit(IterationReviewFixture f)=>new(Guid.NewGuid(),f.Context.Iteration.Reference,f.Context.Iteration.Etag,
        f.Context.Iteration.Body.Iteration!.InputVersion,f.Context.Candidates.Select(x=>x.Reference).ToArray(),f.Context.Policies.Last().Reference,
        [f.Iteration.Reviewer],"完整候选集及原始技术依据","原生迭代送审");
    private async Task PrepareIterationFlow(Harness h,IterationReviewFixture f)
    {
        var(binding,flows,initial)=await ExistingReviewBinding(h);var head=await flows.ReadHeadAsync("binding-flow");
        var draft=await flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,initial,"iteration-next"));
        var saved=await flows.SaveAsync("binding-flow",AuthoringCommand("iteration-flow",draft.ResourceVersion,"native iteration review",StartSchema));
        var publication=await flows.PublishAsync("binding-flow",PublicationCommand(await Preview(flows,"binding-flow",saved),"iteration-publish"));
        await binding.SaveAsync(null,null,BindingBytes(BindingInput(publication.Result!.Publication!.VersionId,BindingTime()) with{BizType="PLM_ITER_REVIEW",SubjectKind="PLM_ITER_REVIEW",DetailRoute="/plm/engineering-iteration/{bizId}"}));
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_native_complete_package_atomic_stage_mapping_original_and_edit_lock()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h);var o=IterationReviews(h,f.Files);var c=IterationSubmit(f);var id=c.Iteration.Id;
        var saved=await o.SubmitAsync(id,f.Context.Etag,c);Assert.Equal(saved,await o.SubmitAsync(id,f.Context.Etag,c));Assert.Equal(saved,(await o.OperationAsync(c.OperationId,false)).Result);
        var detail=await o.SubmissionAsync(id,saved.Submission!.SubmissionId);Assert.Equal(2,detail.Package.Candidates.Length);
        Assert.Equal(f.Context.Iteration.OriginalJson,detail.Package.IterationOriginal);Assert.Equal("NOT_RECEIVED",detail.Submission.ApplicationState);
        Assert.Equal("IN_REVIEW",(await Iterations(h,f.Files).ReadAsync(id)).Body.Iteration!.Stage);
        Assert.Equal(f.Context.Candidates.Select(x=>x.OriginalJson),detail.Package.Candidates.Select(x=>x.OriginalJson));
        var envelope=ApprovalSubmissionWireV2.Parse(Encoding.UTF8.GetBytes(detail.OriginalSubmission)).Value;
        Assert.Equal(envelope.Subject.SubjectId,detail.Snapshot.Binding.RoundId);Assert.Equal(envelope.Subject.BusinessRevision,detail.Snapshot.Binding.RoundBindingId.ToString());
        Assert.Equal(IterationReviewCanonicalV1.Hash(Encoding.UTF8.GetBytes(detail.OriginalSnapshot)),envelope.Subject.ContentDigest.Value);
        Assert.NotEqual(detail.Snapshot.Binding.ReviewPackageDigest.Value,envelope.Subject.ContentDigest.Value);
        Assert.Equal(detail.Snapshot.Aggregate.AggregateId.ToString(),envelope.Engineering!.Value.GetProperty("candidateId").GetString());
        Assert.Null(detail.Snapshot.Binding.TechnicalManifestDigest);Assert.Equal("RETURN",detail.Snapshot.Binding.RejectDisposition);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND operation_id={c.OperationId}").SingleAsync());
        Assert.Equal("ITERATION_CANDIDATE_STAGE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Candidates.CommandAsync(f.Candidate))).Message);
        foreach(var sql in new[]{"UPDATE cp6_iteration_review_v1.submissions SET payload=payload","DELETE FROM cp6_iteration_review_v1.outbox","TRUNCATE cp6_iteration_review_v1.policies CASCADE"})await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [Fact]
    public void IterationReview_canonical_preserves_unicode_codepoint_order_and_rejects_duplicate_or_invalid_text()
    {
        using var value=JsonDocument.Parse("{\"𐀀\":\"😀\",\"\":\"e\\u0301\",\"a\":\"é\"}");
        Assert.Equal("{\"a\":\"é\",\"\":\"é\",\"𐀀\":\"😀\"}",Encoding.UTF8.GetString(IterationReviewCanonicalV1.Bytes(value.RootElement)));
        using var duplicates=JsonDocument.Parse("{\"a\":1,\"a\":2}");Assert.Throws<OrderPriceCorrectionException>(()=>IterationReviewCanonicalV1.Bytes(duplicates.RootElement));
        Assert.Throws<OrderPriceCorrectionException>(()=>IterationReviewCanonicalV1.Text("\ud800"));
        Assert.Throws<OrderPriceCorrectionException>(()=>IterationReviewCanonicalV1.Bytes(new{value="\ud800"}));
        var instant=DateTimeOffset.Parse("2026-10-28T12:34:56.1234567+00:00");
        Assert.Contains("12:34:56.1234567+00:00",Encoding.UTF8.GetString(IterationReviewCanonicalV1.Bytes(new{instant})));
        using var malformed=JsonDocument.Parse("{\"a\":\"\\ud800\"}");Assert.Throws<OrderPriceCorrectionException>(()=>IterationReviewCanonicalV1.Bytes(malformed.RootElement));
    }
}

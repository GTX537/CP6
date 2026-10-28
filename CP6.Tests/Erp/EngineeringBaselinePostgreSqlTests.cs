using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static void BaselineTrace(string stage) { var file=Environment.GetEnvironmentVariable("CP6_BASELINE_TRACE"); if(!string.IsNullOrEmpty(file))File.AppendAllText(file,DateTimeOffset.UtcNow.ToString("O")+" "+stage+"\n"); }
    private static EngineeringBaselineOwnerV1 Baselines(Harness h,ContentStore files)=>new(h.Db,CandidateAuthority(h),ScopeFence(h),new("DEMO"),
        new(h.Db,CandidateAuthority(h),Documents(h,files),Specifications(h,files),Outputs(h,files)),IterationReviews(h,files),Outputs(h,files));
    private sealed record BaselineFixture(IterationDecisionFixture Review,EngineeringFreezeCommandV1 Command);
    private async Task<BaselineFixture> BaselineSetup(Harness h,bool apply=true,Func<Task>? beforeCheck=null)
    {
        BaselineTrace("setup-start");var f=await IterationDecisionSetup(h,beforeCheck:async()=>{
            foreach(var action in new[]{"query","freeze","cancel","successor"})await EngineeringAction(h,"plm-engineering-baseline",action);
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-engineering-baseline",ScopeType=5});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            if(beforeCheck!=null)await beforeCheck();
        });
        BaselineTrace("setup-native-ready");await IterationVote(h,f);var receiver=await IterationReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var reviews=IterationReviews(h,f.Source.Files);var context=await reviews.ApplicationAsync(f.Source.Context.IterationId,f.Submission);
        BaselineTrace("setup-decision-received");var receipt=apply?(await reviews.ApplyAsync(f.Source.Context.IterationId,context.Etag,new(Guid.NewGuid(),f.Submission,null,"Apply actual native review before freeze"))).Receipt.ReceiptId:Guid.NewGuid();
        BaselineTrace("setup-applied");var detail=await reviews.SubmissionAsync(f.Source.Context.IterationId,f.Submission);var candidate=detail.Package.Candidates.Single();var check=f.Source.Check.Original;
        return new(f,new(Guid.NewGuid(),f.Source.Context.IterationId,candidate.Reference,f.Submission,detail.Submission.RoundId,receipt,
            check.Command.Revision,check.Command.Candidate,check.Command.ExpectedEtag,check.Input.Configuration!.Reference,f.Source.Check.Reference,
            check.Manifest!.Digest,detail.Submission.Package.Digest,"Freeze accurately reviewed engineering candidate"));
    }
    private Task<int> BaselineCount(Harness h,string table)=>h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM cp6_engineering_baseline_v1."+table+" WHERE tenant_id={0}",h.Tenant).SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_native_freeze_preserves_exact_originals_members_manifest_review_and_recovery()
    {
        await using var h=await Seed();var f=await BaselineSetup(h);var o=Baselines(h,f.Review.Source.Files);var c=f.Command;
        BaselineTrace("freeze-start");var result=await o.FreezeAsync(c);BaselineTrace("freeze-accepted");var v=await o.ReadAsync(result.Baseline.Id);
        Assert.Equal("ACCEPTED",result.State);Assert.Equal("FROZEN",v.State);Assert.Equal("CURRENT",v.SourceStatus);
        Assert.Equal(f.Review.Source.Check.OriginalJson,v.Original.CheckOriginal);Assert.Equal(f.Review.Source.Check.Original.Manifest,v.Original.Manifest);
        Assert.NotEqual(c.TechnicalManifestDigest,c.ReviewPackageDigest);Assert.Equal(c.ApplicationReceiptId,v.Original.Review.Receipt.ReceiptId);
        Assert.Equal(v.Original.Review.ReceiptOriginal,await h.Db.Database.SqlQuery<string>($"SELECT convert_from(payload,'UTF8') AS \"Value\" FROM cp6_iteration_review_application_v1.receipts WHERE tenant_id={h.Tenant} AND id={c.ApplicationReceiptId}").SingleAsync());
        Assert.Equal(v.Original.Review.ApplicationOriginal,await h.Db.Database.SqlQuery<string>($"SELECT convert_from(body,'UTF8') AS \"Value\" FROM cp6_iteration_review_application_v1.applications WHERE tenant_id={h.Tenant} AND submission_id={c.ReviewSubmissionId}").SingleAsync());
        Assert.Equal(v.Original.Review.DecisionOriginal,await h.Db.Database.SqlQuery<string>($"SELECT convert_from(payload,'UTF8') AS \"Value\" FROM cp6_iteration_review_fact_v1.facts WHERE tenant_id={h.Tenant} AND submission_id={c.ReviewSubmissionId}").SingleAsync());
        Assert.Equal("NOT_APPLICABLE_BY_APPROVED_POLICY",v.Original.OutputApplicability);Assert.Null(v.OutputBinding);
        Assert.Equal(1,await BaselineCount(h,"baselines"));Assert.Equal(v.Original.Revision.Draft.Components.Length,await BaselineCount(h,"members"));Assert.Equal(1,await BaselineCount(h,"intents"));
        Assert.Equal(result,await o.FreezeAsync(c));Assert.Equal(result,(await o.OperationAsync(c.OperationId)).Result);
        var duplicate=await o.FreezeAsync(c with{OperationId=Guid.NewGuid(),Reason="Same native candidate from another retry key"});
        Assert.Equal(result.Baseline,duplicate.Baseline);Assert.Equal(c.OperationId,duplicate.OriginalOperationId);Assert.Equal(1,await BaselineCount(h,"intents"));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.FreezeAsync(c with{Reason="Changed original body"}))).Message);
        var cancel=await o.CancelAsync(new(Guid.NewGuid(),c.OperationId,"UNKNOWN","Acknowledge committed original"));Assert.Equal(result,cancel.Outcome.Result);
        var revisions=new EngineeringRevisionOwnerV1(h.Db,CandidateAuthority(h),Documents(h,f.Review.Source.Files),Specifications(h,f.Review.Source.Files));
        var frozen=await revisions.ReadAsync(c.Revision.Id,null);Assert.Equal("FROZEN",frozen.State);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>revisions.CommandAsync(PatchRevision(frozen,new EngineeringDraftPatchV1{Op="SET_GOAL",Goal="Overwrite frozen"})));
        foreach(var sql in new[]{"UPDATE cp6_engineering_baseline_v1.baselines SET body=body","DELETE FROM cp6_engineering_baseline_v1.members","TRUNCATE cp6_engineering_baseline_v1.intents CASCADE"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
        var uses=await o.WhereUsedAsync(result.Baseline.Id);Assert.Contains(uses,x=>x.Status=="UNKNOWN");
        var command=new EngineeringBaselineSuccessorV1(Guid.NewGuid(),result.Baseline,c.Revision,c.ExpectedEtag,"Begin explicitly linked new design without inherited approvals");
        var next=await o.SuccessorAsync(command);Assert.NotEqual(c.Revision,next.Revision);Assert.NotEqual(c.EngineeringCandidate,next.Candidate);
        Assert.Equal(next,(await o.SuccessorOperationAsync(command.OperationId)).Result);Assert.Equal(next,await o.SuccessorAsync(command));
        Assert.Equal("DRAFT",(await revisions.ReadAsync(c.Revision.Id,null)).State);Assert.Equal("NOT_REQUESTED",(await revisions.ReadAsync(c.Revision.Id,null)).Freeze);
        var history=await o.ReadAsync(result.Baseline.Id);Assert.Equal(v.OriginalJson,history.OriginalJson);Assert.Equal("CHANGED",history.SourceStatus);Assert.Equal("FROZEN",history.State);
        Assert.Single((await o.WhereUsedAsync(result.Baseline.Id))[0].References);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_unapplied_review_and_wrong_technical_review_bindings_are_rejected_without_partial_baseline()
    {
        await using var h=await Seed();var f=await BaselineSetup(h,false);var o=Baselines(h,f.Review.Source.Files);
        Assert.Equal("ENGINEERING_FREEZE_APPLIED_REVIEW_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.FreezeAsync(f.Command))).Message);
        var reviews=IterationReviews(h,f.Review.Source.Files);var c=await reviews.ApplicationAsync(f.Command.IterationId,f.Command.ReviewSubmissionId);
        var applied=await reviews.ApplyAsync(f.Command.IterationId,c.Etag,new(Guid.NewGuid(),f.Command.ReviewSubmissionId,null,"Actual approval application"));
        var valid=f.Command with{ApplicationReceiptId=applied.Receipt.ReceiptId};
        foreach(var invalid in new[]{valid with{TechnicalManifestDigest=valid.ReviewPackageDigest},valid with{ReviewPackageDigest=valid.TechnicalManifestDigest},
            valid with{ReviewRoundId=Guid.NewGuid()},valid with{SourceCandidate=valid.SourceCandidate with{Id=Guid.NewGuid()}},valid with{ExpectedEtag=Guid.NewGuid()},valid with{Configuration=valid.Configuration with{Id=Guid.NewGuid()}}})
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.FreezeAsync(invalid));
        Assert.Equal(0,await BaselineCount(h,"baselines"));Assert.Equal("UNKNOWN",(await o.OperationAsync(valid.OperationId)).State);
        BaselineTrace("valid-freeze-start");Assert.Equal("ACCEPTED",(await o.FreezeAsync(valid)).State);BaselineTrace("valid-freeze-accepted");
    }
}

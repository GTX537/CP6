using System.Text;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_concurrent_submit_has_one_round_and_intake_rejects_changed_original_or_revoked_source()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h,1);var owner=IterationReviews(h,f.Files);var command=IterationSubmit(f);var id=f.Context.IterationId;
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=IterationReviews(h with{Db=db},f.Files);
        async Task<string> Submit(IterationReviewOwnerV1 service,IterationReviewSubmitV1 input)
        {try{await service.SubmitAsync(id,f.Context.Etag,input);return "COMMITTED";}catch(OrderPriceCorrectionException e){return e.Message;}}
        var results=await Task.WhenAll(Submit(owner,command),Submit(peer,command with{OperationId=Guid.NewGuid()}));
        Assert.Single(results,x=>x=="COMMITTED");Assert.Single(results,x=>x=="ITERATION_REVIEW_CONTEXT_CHANGED");
        Assert.Single((await owner.ContextAsync(id)).Submissions);
        Assert.Equal((long.Parse(f.Context.Iteration.Reference.Version)+1).ToString(),(await Iterations(h,f.Files).ReadAsync(id)).Reference.Version);
        await PrepareIterationFlow(h,f);
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var original=ApprovalSubmissionWireV2.Parse(payload).Value;
        var authority=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,f.Principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),authority,ToolApproval(h),Authoring(h),new(ToolApproval(h),null,null,owner)){ReceiveClock=()=>BindingTime().AddSeconds(2)};
        var changed=original with{Reason="Changed original review basis"};
        Assert.Equal("SUBMISSION_PAYLOAD_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>intake.SubmitAsync(ApprovalCanonicalV2.Bytes(changed)))).Message);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>intake.SubmitAsync(payload))).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant} AND owner={ApprovalBusinessProfileV2.IterationOwner}").SingleAsync());
        Assert.Equal("IN_REVIEW",(await Iterations(h,f.Files).ReadAsync(id)).Body.Iteration!.Stage);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_complete_set_beyond_page_limit_and_oversize_snapshot_never_truncates_or_freezes()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h,0);var owner=IterationReviews(h,f.Files);var id=f.Context.IterationId;
        // Each aggregate member preserves at least the complete SourceCheck original.
        // Size the actual native set beyond both the UI's old page size and the wire limit.
        var count=Math.Max(21,262144/Encoding.UTF8.GetByteCount(f.Check.OriginalJson)+1);
        Assert.InRange(count,21,1000);
        for(var i=0;i<count;i++)await f.Candidates.CommandAsync(f.Candidate with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid(),Title="Complete candidate "+i});
        var context=await owner.ContextAsync(id);Assert.Equal(count,context.Candidates.Length);
        var command=IterationSubmit(f with{Context=context});
        Assert.Equal("ITERATION_REVIEW_OA_SNAPSHOT_TOO_LARGE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,context.Etag,command))).Message);
        Assert.Equal(f.Context.Iteration.OriginalJson,(await Iterations(h,f.Files).ReadAsync(id)).OriginalJson);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        Assert.Empty((await owner.ContextAsync(id)).Submissions);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_iteration_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_complete_set_CAS_independence_and_current_policy_fail_closed()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h);var o=IterationReviews(h,f.Files);var c=IterationSubmit(f);var id=c.Iteration.Id;
        Assert.Equal("ITERATION_REVIEW_COMPLETE_CANDIDATE_SET_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,f.Context.Etag,c with{Candidates=[c.Candidates[0]]}))).Message);
        Assert.Equal("ITERATION_REVIEW_CANDIDATE_DUPLICATE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,f.Context.Etag,c with{Candidates=[c.Candidates[0],c.Candidates[0]]}))).Message);
        Assert.Equal("ITERATION_REVIEW_SEPARATION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,f.Context.Etag,c with{Reviewers=[h.User]}))).Message);
        await f.Candidates.CommandAsync(f.Candidate);
        Assert.Equal("ITERATION_REVIEW_CONTEXT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,f.Context.Etag,c))).Message);
        var current=await o.ContextAsync(id);c=c with{Candidates=current.Candidates.Select(x=>x.Reference).ToArray()};
        var p=current.Policies.Last().Fact.Body with{OperationId=Guid.NewGuid(),State="REVOKED"};await o.PolicyAsync(id,current.Etag,p);current=await o.ContextAsync(id);
        Assert.Equal("ITERATION_REVIEW_POLICY_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,current.Etag,c))).Message);
        Assert.Equal("IN_DESIGN",(await Iterations(h,f.Files).ReadAsync(id)).Body.Iteration!.Stage);Assert.Empty(current.Submissions);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_missing_outbox_rolls_back_native_stage_and_both_originals_then_same_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await IterationReviewSetup(h,1);var o=IterationReviews(h,f.Files);var c=IterationSubmit(f);var id=c.Iteration.Id;
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_iteration_review_v1.outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>o.SubmitAsync(id,f.Context.Etag,c)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);Assert.Equal(f.Context.Iteration.OriginalJson,(await Iterations(h,f.Files).ReadAsync(id)).OriginalJson);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND operation_id={c.OperationId}").SingleAsync());
        var result=await o.SubmitAsync(id,f.Context.Etag,c);var detail=await o.SubmissionAsync(id,result.Submission!.SubmissionId);Assert.NotNull(detail.Snapshot.Binding.TechnicalManifestDigest);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmissionAsync(id,result.Submission.SubmissionId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_final_reviewer_or_source_revocation_rolls_back_and_closed_unknown_cannot_submit()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await IterationReviewSetup(h,1);var o=IterationReviews(h,f.Files);var id=f.Context.IterationId;
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");
        foreach(var failure in new[]{"reviewer","source"})
        {
            var command=IterationSubmit(f);var injected=false;
            hook.Before=async c=>{
                if(!c.CommandText.Contains("INSERT INTO cp6_iteration_review_v1.submissions"))return;
                using var revoke=c.Connection!.CreateCommand();revoke.Transaction=c.Transaction;
                revoke.CommandText=failure=="reviewer"?"UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"=@who"
                    :"DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@who AND \"ActionCode\"='query' AND \"MenuId\"=@menu";
                var p=revoke.CreateParameter();p.ParameterName="who";p.Value=failure=="reviewer"?f.Iteration.Reviewer:h.Tenant;revoke.Parameters.Add(p);
                if(failure=="source"){var m=revoke.CreateParameter();m.ParameterName="menu";m.Value=menu.MenuId;revoke.Parameters.Add(m);}injected=await revoke.ExecuteNonQueryAsync()>0;
            };
            Assert.NotNull(await Record.ExceptionAsync(()=>o.SubmitAsync(id,f.Context.Etag,command)));hook.Before=null;Assert.True(injected);
            Assert.Equal("UNKNOWN",(await o.OperationAsync(command.OperationId,false)).State);Assert.Equal(f.Context.Iteration.OriginalJson,(await Iterations(h,f.Files).ReadAsync(id)).OriginalJson);
            Assert.Empty((await o.ContextAsync(id)).Submissions);await o.OperationAsync(command.OperationId,true);
            Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SubmitAsync(id,f.Context.Etag,command))).Message);
        }
        await o.SubmitAsync(id,f.Context.Etag,IterationSubmit(f));
    }
}

using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record DocumentApplicationFixture(DocumentDecisionFixture Document,DocumentReviewFactReceiverV1 Receiver,
        IntakeAuthority Authority,byte[] Payload,ApprovalDecisionEnvelopeV2 Decision);
    private async Task<DocumentApplicationFixture> DocumentApplicationSetup(Harness h,bool approved=true)
    {
        var f=await DocumentDecisionSetup(h);var first=DecisionActor(h,f.First);
        await DocumentVote(first,f.Files,await DocumentDecisionInput(first,f.Files,approved?"approve":"reject"));
        if(approved){var second=DecisionActor(h,f.Second);await DocumentVote(second,f.Files,await DocumentDecisionInput(second,f.Files));}
        return await DocumentReceiverSetup(h,f);
    }
    private async Task<DocumentApplicationFixture> DocumentReceiverSetup(Harness h,DocumentDecisionFixture f)
    {
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var decision=ApprovalDecisionWireV2.Parse(payload).Value;
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,Guid.NewGuid(),"document-workflow-producer","CP6.Services","test-registration",true,"DEMO"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_document_review_fact_v1.registrations(environment_id,tenant_id,principal_id,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_document_review_fact_v1.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{f.Submission},{Guid.NewGuid()},true,true,true);
            """);
        await EngineeringAction(h,"plm-controlled-document","review-apply");
        var receiver=new DocumentReviewFactReceiverV1(h.Db,ScopeFence(h),service,new(h.Db,ScopeFence(h)),FactCursorProtection);
        return new(f,receiver,service,payload,decision);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_application_requires_real_decision_and_exact_effect_keeps_later_revision_unapproved()
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h);var d=f.Document;var owner=DocumentReviews(h,d.Files);
        var before=await owner.SubmissionAsync(d.Document,d.Submission);Assert.Equal("NOT_RECEIVED",before.Application.State);
        Assert.False(before.Submission.IsApproved);Assert.Equal("DRAFT",before.Document.State);
        var command=new DocumentApplicationCommandV1(Guid.NewGuid(),d.Submission,null,"Apply the signed exact document revision");
        Assert.Equal("DOCUMENT_DECISION_NOT_RECEIVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(d.Document,before.Application.Etag,command))).Message);
        var ack=FactAck(await f.Receiver.ReceiveAsync(f.Payload));Assert.Equal("NOT_APPLIED",ack.Application);
        Assert.IsType<ApprovalApplicationNotAppliedV2>(await f.Receiver.ApplicationAsync(ack.ReceiptStreamId!.Value));
        var ready=await owner.SubmissionAsync(d.Document,d.Submission);
        var applied=await owner.ApplyAsync(d.Document,ready.Application.Etag,command);
        Assert.Equal("APPLIED",applied.Receipt.Application);Assert.Equal("DOCUMENT_APPROVED",applied.Receipt.OutcomeCode);
        Assert.Equal(applied.Receipt,Assert.IsType<ApprovalApplicationFoundV2>(await f.Receiver.ApplicationAsync(ack.ReceiptStreamId.Value)).Receipt);
        Assert.Equal("APPROVED",(await Documents(h,d.Files).ReadAsync(d.Document)).State);
        var latest=await Documents(h,d.Files).ReadAsync(d.Document);await Documents(h,d.Files).CommandAsync(DocumentRevise(latest));
        var next=await owner.ContextAsync(d.Document);Assert.Equal("2",next.Document.Reference.Version);Assert.Equal("DRAFT",next.Document.State);
        var exact=await owner.SubmissionAsync(d.Document,d.Submission);Assert.Equal("1",exact.Document.Reference.Version);Assert.Equal("APPROVED",exact.Document.State);
        Assert.True(exact.Submission.IsApproved);Assert.Equal(applied,(await owner.ApplicationOperationAsync(command.OperationId,false)).Result);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-apply").ExecuteDeleteAsync();
        Assert.Equal(applied,await owner.ApplyAsync(d.Document,ready.Application.Etag,command));
        var chain=Assert.IsType<ApprovalReceiptChainPageV2>(await f.Receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value));Assert.Single(chain.Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_rejected_round_must_be_applied_before_exact_replacement_and_cannot_fork()
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h,false);var d=f.Document;var owner=DocumentReviews(h,d.Files);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var ready=await owner.ContextAsync(d.Document);
        var replacement=DocumentSubmit(ready) with{ReplacesSubmissionId=d.Submission};
        Assert.Equal("DOCUMENT_REVIEW_REJECTION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(d.Document,ready.Etag,replacement))).Message);
        var detail=await owner.SubmissionAsync(d.Document,d.Submission);
        var result=await owner.ApplyAsync(d.Document,detail.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Preserve the professional rejection"));
        Assert.Equal("DOCUMENT_REJECTED",result.Receipt.OutcomeCode);Assert.False((await owner.SubmissionAsync(d.Document,d.Submission)).Submission.IsApproved);
        ready=await owner.ContextAsync(d.Document);Assert.Equal("REJECTED",ready.Submissions.Single().State);
        var next=await owner.SubmitAsync(d.Document,ready.Etag,replacement);Assert.NotEqual(d.Submission,next.Submission!.SubmissionId);
        ready=await owner.ContextAsync(d.Document);
        Assert.Equal("DOCUMENT_REVIEW_ALREADY_REPLACED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(d.Document,ready.Etag,
            replacement with{OperationId=Guid.NewGuid(),ReviewRoundId=Guid.NewGuid()}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_missing_application_effect_rolls_back_receipt_outbox_and_original_then_same_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await DocumentApplicationSetup(h);var d=f.Document;
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=DocumentReviews(h,d.Files);var ready=await owner.SubmissionAsync(d.Document,d.Submission);
        var command=new DocumentApplicationCommandV1(Guid.NewGuid(),d.Submission,null,"Atomic document approval");
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_document_review_application_v1.applications"))c.CommandText="SELECT 1";return Task.CompletedTask;};
        var failure=await Record.ExceptionAsync(()=>owner.ApplyAsync(d.Document,ready.Application.Etag,command));Assert.NotNull(failure);
        Assert.Contains("DOCUMENT_APPLICATION_EFFECT_REQUIRED",failure.ToString());hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(command.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_document_review_application_v1.receipts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_document_review_application_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        var result=await owner.ApplyAsync(d.Document,ready.Application.Etag,command);Assert.Equal("DOCUMENT_APPROVED",result.Receipt.OutcomeCode);
        Assert.Equal(result,(await owner.ApplicationOperationAsync(command.OperationId,false)).Result);
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task DocumentReview_application_rechecks_current_policy_and_file_availability_without_effects(bool unavailableFile)
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h);var d=f.Document;
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=DocumentReviews(h,d.Files);
        var ready=await owner.SubmissionAsync(d.Document,d.Submission);
        if(unavailableFile)
        {
            var original=d.Files.Bytes.Single(x=>x.Value.SequenceEqual(new byte[]{1,2,3}));d.Files.Bytes[original.Key]=[9,8,7];
            var occurrence=ready.Document.Body.Contents.First(x=>x.Role=="SOURCE").OccurrenceId;
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Documents(h,d.Files).DownloadAsync(d.Document,ready.Document.Reference.Version,occurrence));
        }
        else await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-policy").ExecuteDeleteAsync();
        var command=new DocumentApplicationCommandV1(Guid.NewGuid(),d.Submission,null,"Recheck current source and issuer authority");
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(d.Document,ready.Application.Etag,command));
        Assert.Equal(unavailableFile?409:403,error.Status);
        Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(command.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_document_review_application_v1.applications WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.False((await owner.SubmissionAsync(d.Document,d.Submission)).Submission.IsApproved);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_approval_does_not_promote_unresolved_language_authority()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);var documents=Documents(h,f.Files);
        var original=await documents.ReadAsync(f.Id);
        await documents.CommandAsync(DocumentRevise(original) with{Body=original.Body with{Source=original.Body.Source with{AuthorityBasis=""}}});
        var ready=await DocumentReady(f.Owner,f.Id);
        Assert.Contains("LANGUAGE_AUTHORITY_UNRESOLVED",ready.Document.Missing!);
        var command=DocumentSubmit(ready);
        Assert.Equal("DOCUMENT_SUBMISSION_INCOMPLETE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(f.Id,ready.Etag,command))).Message);
        Assert.Equal("UNKNOWN",(await f.Owner.OperationAsync(command.OperationId,false)).State);
        Assert.Empty((await f.Owner.ContextAsync(f.Id)).Submissions);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_engineering_observes_exact_applied_approval_without_approving_new_document_version()
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h);var d=f.Document;
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var owner=DocumentReviews(h,d.Files);var ready=await owner.SubmissionAsync(d.Document,d.Submission);
        await owner.ApplyAsync(d.Document,ready.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Apply exact professional approval"));
        var documents=Documents(h,d.Files);var revisions=new EngineeringRevisionOwnerV1(h.Db,DocumentAuthority(h),documents);
        var parent=await revisions.ReadAsync(ready.Document.Body.RevisionOrigin.Id,null);
        var member=new EngineeringComponentDraftV1(Guid.NewGuid(),"DOCUMENT","Approved drawing",0,"APPLIES","","",ready.Document.Reference);
        await revisions.CommandAsync(PatchRevision(parent,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=member}));
        var bound=await revisions.ReadAsync(parent.Reference.Id,null);
        Assert.DoesNotContain("DOCUMENT_APPROVAL_REQUIRED:"+member.OccurrenceId,bound.Missing!);
        var original=await documents.ReadAsync(d.Document);
        await documents.CommandAsync(DocumentRevise(original) with {Body=original.Body with{RevisionOrigin=bound.Reference,RevisionLabel="B"}});
        var next=await documents.ReadAsync(d.Document);Assert.Equal("DRAFT",next.State);
        Assert.True((await owner.SubmissionAsync(d.Document,d.Submission)).Submission.IsApproved);
        bound=await revisions.ReadAsync(parent.Reference.Id,null);
        Assert.Equal(ready.Document.Reference,Assert.Single(bound.Draft.Components).SourceRef);
        await revisions.CommandAsync(PatchRevision(bound,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=member with{SourceRef=next.Reference}}));
        Assert.Contains("DOCUMENT_APPROVAL_REQUIRED:"+member.OccurrenceId,(await revisions.ReadAsync(parent.Reference.Id,null)).Missing!);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_large_unicode_review_package_is_preserved_outside_bounded_flow_snapshot()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);var documents=Documents(h,f.Files);var original=await documents.ReadAsync(f.Id);
        var text=string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600),4000));
        var contents=original.Body.Contents.Select(x=>x with{Language=text,DerivationBasis=x.Role=="PREVIEW"?text:x.DerivationBasis}).ToList();
        var files=ContentOwner(h,f.Files,new ContentInspector());byte[] bytes=[1,2,3];
        for(var i=0;i<76;i++)
        {
            var upload=new CP6.Core.Services.Pub.FileUploadV1(Guid.NewGuid(),Guid.NewGuid(),original.Body.RevisionOrigin,"Unicode-boundary-"+i+".bin",bytes.Length,CP6.Core.Services.Pub.FileContentBytesV1.Digest(bytes),"Explicit isolated boundary input");
            await files.UploadAsync(upload,new MemoryStream(bytes));var file=await files.ReadAsync(upload.FileId);
            contents.Add(new(Guid.NewGuid(),"PREVIEW",file.ContentRef,contents[0].OccurrenceId,text,text));
        }
        await documents.CommandAsync(DocumentRevise(original) with{Body=original.Body with{Contents=contents.ToArray()}});
        // Existing declaration limits are 4,000 UTF-16 code units; document content fields
        // allow 8,000. Supplementary scalars consume two units and must remain intact.
        var reviewText=string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600),2000));
        var ready=await DocumentReady(f.Owner,f.Id);var relations=Enumerable.Repeat(reviewText,100).ToArray();
        var input=DocumentSubmit(ready) with{DeclaredChangeRelations=relations,ReviewBasis=reviewText};
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,await DocumentHostSettings(f.Files));
        var cookies=await Login(host,h);
        using var request=OperationsRequest(cookies,System.Net.Http.HttpMethod.Post,$"/api/controlled-document/owner-v1/{f.Id}/review-submissions",input);
        request.Headers.TryAddWithoutValidation("If-Match",ready.Etag);
        using var response=await host.Send(request);var submitted=await DocumentHttpData<DocumentReviewResultV1>(response);
        var exact=await f.Owner.SubmissionAsync(f.Id,submitted.Submission!.SubmissionId);
        Assert.Equal(relations,exact.Package.DeclaredChangeRelations);Assert.Equal(input.ReviewBasis,exact.Package.ReviewBasis);
        Assert.Equal(contents.ToArray(),exact.Package.Contents);
        Assert.InRange(ApprovalCanonicalV2.Bytes(exact.Package).Length,8388609,16777216);
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_document_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.True(payload.Length<262144);Assert.False(exact.Submission.IsApproved);
    }
}

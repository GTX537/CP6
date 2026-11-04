using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static DocumentAvailabilityCommandV1 AvailabilityCommand(ControlledDocumentSnapshotV1 value,string action)
        =>new(Guid.NewGuid(),value.Reference,action,value.Availability!.StateVersion,value.Availability.Etag,"Explicit exact-version "+action);
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_HTTP_holds_exact_old_revision_without_changing_content_or_other_versions()
    {
        await using var h=await Seed();var files=new ContentStore();var input=await DocumentInput(h,files);
        await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,files);await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);
        await owner.CommandAsync(DocumentRevise(first) with {Body=first.Body with {RevisionLabel="B"}});
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,await DocumentHostSettings(files));
        var cookies=await Login(host,h);var root="/api/controlled-document/owner-v1/";
        var command=new {OperationId=Guid.NewGuid(),Document=first.Reference,Action="HOLD",ExpectedStateVersion="1",ExpectedEtag=first.Etag,Reason="Original V1 under current technical review"};
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"availability-commands",command));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var old=await owner.ReadAsync(input.DocumentId,"1");Assert.Equal(first.OriginalJson,old.OriginalJson);
        Assert.Contains("DOCUMENT_AVAILABILITY:HELD",old.Missing!);
        var url=root+input.DocumentId+"/content/"+first.Body.Contents[0].OccurrenceId;
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"?version=1"));Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using var current=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"?version=2"));Assert.Equal(HttpStatusCode.OK,current.StatusCode);
        Assert.Equal(new byte[]{1,2,3},await current.Content.ReadAsByteArrayAsync());
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"operations/"+command.OperationId));
        var recovered=await DocumentHttpData<ControlledDocumentOperationV1>(original);Assert.Equal("HELD",recovered.Result!.Availability!.State);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="availability").ExecuteDeleteAsync();
        using var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"availability-commands",command));
        Assert.Equal(recovered.Result,await DocumentHttpData<ControlledDocumentResultV1>(replay));
        using var deniedCommand=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"availability-commands",AvailabilityCommand(old,"RELEASE")));
        Assert.Equal(HttpStatusCode.Forbidden,deniedCommand.StatusCode);
        using var csrf=OperationsRequest(cookies,HttpMethod.Post,root+"availability-commands",AvailabilityCommand(old,"WITHDRAW"));csrf.Headers.Remove("X-CSRF-Token");
        using var noCsrf=await host.Send(csrf);Assert.Equal(HttpStatusCode.Forbidden,noCsrf.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-controlled-document",FieldName="Content",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var hidden=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"operations/"+command.OperationId));Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_transitions_preserve_original_and_security_hold_prevents_release()
    {
        await using var h=await Seed();var files=new ContentStore();var input=await DocumentInput(h,files);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,files);await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);var hold=AvailabilityCommand(first,"HOLD");
        var held=await owner.AvailabilityAsync(hold);Assert.Equal(held,await owner.AvailabilityAsync(hold));
        Assert.Equal("DOCUMENT_AVAILABILITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(hold with{OperationId=Guid.NewGuid(),Action="WITHDRAW"}))).Message);
        var snapshot=await owner.ReadAsync(input.DocumentId);var release=AvailabilityCommand(snapshot,"RELEASE");
        var fileOwner=ContentOwner(h,files,new ContentInspector());var original=await fileOwner.ReadAsync(first.Body.Contents[0].File.Id);
        Assert.Equal("AVAILABLE",original.Availability);await fileOwner.CommandAsync(ContentCommand(original,"HOLD"));
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(release))).Message);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(release.OperationId,false)).State);
        await fileOwner.CommandAsync(ContentCommand(await fileOwner.ReadAsync(original.ContentRef.Id),"RELEASE"));
        await owner.AvailabilityAsync(release);snapshot=await owner.ReadAsync(input.DocumentId);
        Assert.Equal("AVAILABLE",snapshot.Availability!.State);Assert.Equal(first.Reference,snapshot.Reference);Assert.Equal(first.OriginalJson,snapshot.OriginalJson);
        Assert.Equal(new byte[]{1,2,3},(await owner.DownloadAsync(input.DocumentId,"1",first.Body.Contents[0].OccurrenceId)).Bytes);
        var withdrawal=await owner.AvailabilityAsync(AvailabilityCommand(snapshot,"WITHDRAW"));snapshot=await owner.ReadAsync(input.DocumentId);
        Assert.Equal("WITHDRAWN",snapshot.Availability!.State);
        Assert.Equal("DOCUMENT_AVAILABILITY_TRANSITION_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(AvailabilityCommand(snapshot,"RELEASE")))).Message);
        var history=await owner.AvailabilityHistoryAsync(input.DocumentId,"1");Assert.Equal(new[]{"WITHDRAW","RELEASE","HOLD","REGISTER"},history.Select(x=>x.Action));
        Assert.Equal(held,(await owner.OperationAsync(hold.OperationId,false)).Result);Assert.Equal(withdrawal.Availability,history[0]);
        var closed=AvailabilityCommand(snapshot,"HOLD");await owner.OperationAsync(closed.OperationId,true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(closed))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE cp6_controlled_document_v1.availability SET state='AVAILABLE'"));
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_two_connections_choose_one_CAS_winner_and_current_authority_controls_all_outlets()
    {
        await using var h=await Seed();var files=new ContentStore();var input=await DocumentInput(h,files);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,files);await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Documents(h,files,db);
        async Task<ControlledDocumentResultV1?> Change(ControlledDocumentOwnerV1 target,string action)
        {try{return await target.AvailabilityAsync(AvailabilityCommand(first,action));}catch(OrderPriceCorrectionException e){Assert.Equal("DOCUMENT_AVAILABILITY_CHANGED",e.Message);return null;}}
        var results=await Task.WhenAll(Change(owner,"HOLD"),Change(peer,"WITHDRAW"));Assert.Single(results,x=>x!=null);
        Assert.Equal("2",(await owner.ReadAsync(input.DocumentId)).Availability!.StateVersion);
        Assert.Equal(2,(await owner.AvailabilityHistoryAsync(input.DocumentId,"1")).Length);
        var latest=await owner.ReadAsync(input.DocumentId);
        Assert.Equal("DOCUMENT_REFERENCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(AvailabilityCommand(latest,"WITHDRAW") with{Document=latest.Reference with{Digest=new string('e',64)}}))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-controlled-document",FieldName="Metadata",Access=2});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityAsync(AvailabilityCommand(latest,"WITHDRAW")))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="plm-controlled-document").ExecuteDeleteAsync();
        await using var other=await Seed();await DocumentInput(other,new ContentStore());await EngineeringAction(other,"plm-controlled-document","availability");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Documents(other,files).AvailabilityAsync(AvailabilityCommand(latest,"WITHDRAW")))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.AvailabilityHistoryAsync(input.DocumentId,"1"))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(results.Single(x=>x!=null)!.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_failed_receipt_or_final_authority_rolls_back_state_and_disabled_guard_fails_closed()
    {
        var hook=new CommandHook();var writeHook=new StartWriteHook();await using var h=await Seed(hook,writeHook);var files=new ContentStore();var input=await DocumentInput(h,files);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,files);await owner.CommandAsync(input);var first=await owner.ReadAsync(input.DocumentId);var command=AvailabilityCommand(first,"HOLD");
        writeHook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))throw new IOException("Receipt store unavailable");return Task.CompletedTask;};
        try{Assert.NotNull(await Record.ExceptionAsync(()=>owner.AvailabilityAsync(command)));}finally{writeHook.Before=null;}
        Assert.Equal(first.Availability,(await owner.ReadAsync(input.DocumentId)).Availability);Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        var reached=false;hook.Before=async c=>{
            if(!c.CommandText.Contains("WITH field_subject"))return;
            await using var probe=c.Connection!.CreateCommand();probe.Transaction=c.Transaction;
            probe.CommandText=$"SELECT EXISTS(SELECT FROM cp6_controlled_document_v1.availability WHERE tenant_id='{h.Tenant}' AND state_version=2)";
            if((bool)(await probe.ExecuteScalarAsync())!){reached=true;throw new IOException("Final current authority unavailable");}
        };
        try{Assert.NotNull(await Record.ExceptionAsync(()=>owner.AvailabilityAsync(command)));}finally{hook.Before=null;}
        Assert.True(reached);Assert.Equal(first.Availability,(await owner.ReadAsync(input.DocumentId)).Availability);
        await owner.AvailabilityAsync(command);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_controlled_document_v1.availability DISABLE TRIGGER document_availability_immutable");
        try{Assert.Equal("DOCUMENT_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(input.DocumentId))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_controlled_document_v1.availability ENABLE TRIGGER document_availability_immutable");}
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_blocks_new_submission_and_engineering_adoption_but_keeps_exact_existing_reference()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);var review=await DocumentReady(f.Owner,f.Id);
        await EngineeringAction(h,"plm-controlled-document","availability");var owner=Documents(h,f.Files);var doc=review.Document;
        var revisions=new EngineeringRevisionOwnerV1(h.Db,DocumentAuthority(h),owner);var parent=await revisions.ReadAsync(doc.Body.RevisionOrigin.Id,null);
        var member=new EngineeringComponentDraftV1(Guid.NewGuid(),"DOCUMENT","Original drawing",0,"APPLIES","","",doc.Reference);
        await revisions.CommandAsync(PatchRevision(parent,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=member}));
        var bound=await revisions.ReadAsync(parent.Reference.Id,null);await owner.AvailabilityAsync(AvailabilityCommand(doc,"HOLD"));
        var current=await f.Owner.ContextAsync(f.Id);
        Assert.Equal("DOCUMENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(f.Id,current.Etag,DocumentSubmit(current)))).Message);
        var observed=await revisions.ReadAsync(parent.Reference.Id,null);Assert.Equal(bound.OriginalJson,observed.OriginalJson);
        Assert.Equal(doc.Reference,Assert.Single(observed.Draft.Components).SourceRef);Assert.Contains("DOCUMENT:"+member.OccurrenceId+":DOCUMENT_AVAILABILITY:HELD",observed.Missing!);
        Assert.Equal("DOCUMENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>revisions.CommandAsync(PatchRevision(observed,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=member})))).Message);
        Assert.Empty((await f.Owner.ContextAsync(f.Id)).Submissions);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentAvailability_blocks_actual_votes_and_application_then_preserves_approved_fact_after_withdrawal()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,f.Files);var actor=DecisionActor(h,f.First);var vote=await DocumentDecisionInput(actor,f.Files);
        var original=await owner.ReadAsync(f.Document);await owner.AvailabilityAsync(AvailabilityCommand(original,"HOLD"));
        Assert.Equal("DOCUMENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>DocumentVote(actor,f.Files,vote))).Message);
        await owner.AvailabilityAsync(AvailabilityCommand(await owner.ReadAsync(f.Document),"RELEASE"));
        await DocumentVote(actor,f.Files,vote);var second=DecisionActor(h,f.Second);await DocumentVote(second,f.Files,await DocumentDecisionInput(second,f.Files));
        var receiver=await DocumentReceiverSetup(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        await owner.AvailabilityAsync(AvailabilityCommand(await owner.ReadAsync(f.Document),"HOLD"));var reviews=DocumentReviews(h,f.Files);
        var detail=await reviews.SubmissionAsync(f.Document,f.Submission);var apply=new DocumentApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Apply exact original decision");
        Assert.Equal("DOCUMENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviews.ApplyAsync(f.Document,detail.Application.Etag,apply))).Message);
        await owner.AvailabilityAsync(AvailabilityCommand(await owner.ReadAsync(f.Document),"RELEASE"));detail=await reviews.SubmissionAsync(f.Document,f.Submission);
        var applied=await reviews.ApplyAsync(f.Document,detail.Application.Etag,apply);Assert.Equal("APPLIED",applied.Receipt.Application);
        var approved=await owner.ReadAsync(f.Document);Assert.Equal("APPROVED",approved.State);
        await owner.AvailabilityAsync(AvailabilityCommand(approved,"WITHDRAW"));var withdrawn=await owner.ReadAsync(f.Document);
        Assert.Equal("APPROVED",withdrawn.State);Assert.Equal("SOURCE_UNAVAILABLE",withdrawn.Readiness);Assert.Equal(approved.OriginalJson,withdrawn.OriginalJson);
        Assert.Equal(applied,(await reviews.ApplicationOperationAsync(apply.OperationId,false)).Result);
        Assert.Equal("DOCUMENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.DownloadAsync(f.Document,"1",original.Body.Contents[0].OccurrenceId))).Message);
    }
}

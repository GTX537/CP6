using System.Net;
using System.Text.Json;
using System.Text;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Dictionary<string,string> SetHostSettings()=>new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["RequirementEvaluation__EvidenceMode"]="DEMO"};
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_actual_HTTP_preserves_exact_original_and_late_approval_is_stale()
    {
        await using var h=await Seed();var f=await SetApplicationSetup(h);var d=f.Set;var id=d.Source.Set.Reference.Id;
        await EngineeringAction(h,"plm-requirement-set","review-withdraw");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());
        var cookies=await Login(host,h);var path=$"/api/requirement-set/owner-v1/{id}/review-submissions/{d.Submission}";
        using var contextResponse=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path+"/withdraw-context"));
        Assert.Equal(HttpStatusCode.OK,contextResponse.StatusCode);
        var context=await DocumentHttpData<JsonElement>(contextResponse);Assert.True(context.GetProperty("canWithdraw").GetBoolean());
        var before=context.GetProperty("controlRowVersion").GetString();var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw this exact requirement-set review");
        async Task<HttpResponseMessage> Send(object input)
        {
            using var request=OperationsRequest(cookies,HttpMethod.Post,path+"/withdraw",input);
            request.Headers.TryAddWithoutValidation("If-Match","\""+before+"\"");return await host.Send(request);
        }
        using(var forged=await Send(new{commandId=Guid.NewGuid(),reason="Cannot choose actor",actor=new{actualHandlerId=d.Source.Member.First}}))
            Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        using var response=await Send(command);var result=await DocumentHttpData<JsonElement>(response);
        Assert.Equal("WITHDRAWN",result.GetProperty("state").GetString());
        var fact=result.GetProperty("fact").Deserialize<ApprovalWithdrawalFactV2>(ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(h.User,fact.Actor.ActualHandlerId);Assert.Equal(h.User,fact.Actor.RequesterId);Assert.Null(fact.Actor.GrantId);
        Assert.Equal(d.Submission,fact.SubmissionId);Assert.Equal(before,fact.BusinessBeforeRowVersion);Assert.NotEqual(before,fact.BusinessAfterRowVersion);
        Assert.Equal(d.Source.Set.OriginalJson,(await Sets(h).ReadAsync(id)).OriginalJson);
        var original=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(fact,ApprovalWithdrawalWireV2.Parse(original).Value.Fact);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var reviews=SetReviews(h);var detail=await reviews.SubmissionAsync(id,d.Submission);
        var stale=await reviews.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Observe late decision against withdrawn set"));
        Assert.Equal("STALE",stale.Receipt.Application);Assert.Equal("DRAFT",(await Sets(h).ReadAsync(id)).State);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_requirement_set_review_application_v1.applications WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw").ExecuteDeleteAsync();
        using(var replay=await Send(command))Assert.Equal(result.GetRawText(),(await DocumentHttpData<JsonElement>(replay)).GetRawText());
        using(var query=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/requirement-set/owner-v1/review-withdrawal-operations/"+command.CommandId)))
            Assert.Equal("COMMITTED",(await DocumentHttpData<JsonElement>(query)).GetProperty("state").GetString());
        Assert.Equal(original,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    private static async Task<IntakeAuthority> GrantSetWithdrawRelay(Harness h,SetDecisionFixture f)
    {
        var principal=await h.Db.Database.SqlQuery<Guid>($"""
            SELECT service_principal AS "Value" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Plm.RequirementSet.v1'
            """).SingleAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_withdraw_v2.registrations(environment_id,tenant_id,principal_id,owner,fact_owner,revision,enabled,can_query,can_relay,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Plm.RequirementSet.v1','Main.Plm.RequirementSetWithdrawal.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_withdraw_v2.subject_grants(environment_id,tenant_id,principal_id,owner,submission_id,revision,enabled,can_query,can_relay)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Plm.RequirementSet.v1',{f.Submission},{Guid.NewGuid()},true,true,true)
            """);
        await EngineeringAction(h,"plm-requirement-set","review-withdraw");
        return new(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
    }
    private static ApprovalWithdrawalOwnerV2 SetWithdrawals(Harness h,SetDecisionFixture f,IntakeAuthority service)
        =>new(h.Db,ScopeFence(h),service,new(ToolApproval(h),null,SetReviews(h)));
    private static RequirementSetBusinessGrantCommandV1 SetGrantInput(Guid submission,Guid actual)
        =>new(Guid.NewGuid(),submission,null,null,actual,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1),["review-withdraw"],true,"Explicit exact-round document withdrawal delegation");
    private sealed class NativeSetWithdrawalTransport(Harness h,ApprovalWithdrawalOwnerV2 workflow,Guid principal):IRequirementSetWithdrawalTransportV1
    {
        public int Posts,Queries;public bool LoseFirstResponse=true,StaleFirstContext;
        public readonly List<byte[]> Sent=[];
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>tenant==h.Tenant?new(ScopeEnvironment,tenant,principal,"document-owner","https://issuer.example","https://main.example","v1","DEMO"):null;
        private void Outside(){Assert.Null(h.Db.Database.CurrentTransaction);Assert.False(h.Db.ChangeTracker.HasChanges());}
        public async Task<ToolApprovalDeliveryResponseV1> QueryAsync(ToolApprovalDeliveryRouteV1 route,Guid command,CancellationToken ct)
        {
            Outside();Queries++;
            try{var r=await workflow.OriginalAsync(command,ct);return new(r.Status==409?200:r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==404){return new(404,Encoding.UTF8.GetBytes("{\"code\":\"RESOURCE_NOT_FOUND\"}"));}
        }
        public async Task<ToolApprovalDeliveryResponseV1> ContextAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,CancellationToken ct)
        {
            Outside();var context=await workflow.ContextAsync(submission,ct);
            if(StaleFirstContext){StaleFirstContext=false;context=context with{SubmissionRowVersion=Convert.ToBase64String(new byte[8])};}
            return new(200,ApprovalWithdrawalResponseV2.Bytes(context));
        }
        public async Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 route,Guid submission,string version,byte[] original,CancellationToken ct)
        {
            Outside();Posts++;Sent.Add(original.ToArray());
            ApprovalWithdrawResultV2 r;
            try{r=await workflow.WithdrawAsync(submission,"\""+version+"\"",original,ct);}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status==412){return new(412,Encoding.UTF8.GetBytes("{\"code\":\"PRECONDITION_FAILED\"}"));}
            if(LoseFirstResponse){LoseFirstResponse=false;throw new HttpRequestException("Committed native Tx W; response lost");}
            return new(r.Status,ApprovalWithdrawalResponseV2.Bytes(r.Ack));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_Owner_grant_is_independent_from_TASK_and_BLOCKED_needs_new_explicit_fact()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var service=await GrantSetWithdrawRelay(h,f);
        await EngineeringAction(h,"plm-requirement-set","review-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"document-business-delegate"));var owner=SetReviews(h);var actual=SetReviews(actor);
        await CreateTaskGrant(h,h.User,actor.User,"binding-flow");var context=await actual.WithdrawalContextAsync(f.Source.Set.Reference.Id,f.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Delegate withdraws this exact original");
        Assert.Equal("REQUIREMENT_SET_BUSINESS_GRANT_DENIED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawAsync(f.Source.Set.Reference.Id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User))).Message);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());
        var ownerCookies=await Login(host,h);var actorCookies=await DocumentReviewerLogin(host,h,actor.User);var root="/api/requirement-set/owner-v1/"+f.Source.Set.Reference.Id;
        var input=SetGrantInput(f.Submission,actor.User);RequirementSetBusinessGrantV1 grant;
        using(var missing=OperationsRequest(ownerCookies,HttpMethod.Post,root+"/review-business-grants",input))
        {missing.Headers.Remove("X-CSRF-Token");using var denied=await host.Send(missing);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        using(var accepted=await host.Send(OperationsRequest(ownerCookies,HttpMethod.Post,root+"/review-business-grants",input)))
            grant=(await DocumentHttpData<RequirementSetBusinessGrantResultV1>(accepted)).Grant;
        var withdrawn=await actual.WithdrawAsync(f.Source.Set.Reference.Id,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User);
        Assert.Equal(h.User,withdrawn.Fact!.Actor.RequesterId);Assert.Equal(actor.User,withdrawn.Fact.Actor.ActualHandlerId);Assert.Equal(grant.GrantId,withdrawn.Fact.Actor.GrantId);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        var revoked=(await owner.GrantBusinessAsync(f.Source.Set.Reference.Id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false})).Grant;
        var workflow=SetWithdrawals(h,f,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var renewed=(await owner.GrantBusinessAsync(f.Source.Set.Reference.Id,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=revoked.Version})).Grant;
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        context=await actual.WithdrawalContextAsync(f.Source.Set.Reference.Id,f.Submission);RequirementSetWithdrawalResultV1 next;
        using(var request=OperationsRequest(actorCookies,HttpMethod.Post,root+"/review-submissions/"+f.Submission+"/withdrawals/"+withdrawn.TransportCommandId+"/reauthorize",new ApprovalUserWithdrawV2(Guid.NewGuid(),"Explicit current grant after BLOCKED")))
        {
            request.Headers.TryAddWithoutValidation("If-Match","\""+context.ControlRowVersion+"\"");request.Headers.Add("X-Acting-As",h.User.ToString("D"));
            using var response=await host.Send(request);next=await DocumentHttpData<RequirementSetWithdrawalResultV1>(response);
        }
        Assert.Equal(renewed.Revision,next.Fact!.Actor.GrantRevision);Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);
        Assert.Equal(withdrawn.Fact.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        var obsolete=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw);
        Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("REQUIREMENT_SET_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
        var transport=new NativeSetWithdrawalTransport(h,workflow,service.Identity.Principal){LoseFirstResponse=false};
        var delivery=new RequirementSetWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(nextRaw,Assert.Single(transport.Sent));
        Assert.Equal("IDLE_OR_DISABLED",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value)).Ack.State);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
        // Workflow withdrawal produces a real terminal fact; the set records it as stale against its already withdrawn control.
        var receiver=await SetReceiver(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));var detail=await owner.SubmissionAsync(f.Source.Set.Reference.Id,f.Submission);
        var stale=await owner.ApplyAsync(f.Source.Set.Reference.Id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Record actual withdrawn decision"));
        Assert.Equal("STALE",stale.Receipt.Application);Assert.Equal("DRAFT",(await Sets(h).ReadAsync(f.Source.Set.Reference.Id)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_atomic_outbox_failure_and_two_connections_choose_one_business_winner()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await SetApplicationSetup(h);var d=f.Set;
        await EngineeringAction(h,"plm-requirement-set","review-withdraw");FactAck(await f.Receiver.ReceiveAsync(f.Payload));
        var owner=SetReviews(h);var detail=await owner.SubmissionAsync(d.Source.Set.Reference.Id,d.Submission);var c=await owner.WithdrawalContextAsync(d.Source.Set.Reference.Id,d.Submission);
        var apply=new RequirementSetApplicationCommandV1(Guid.NewGuid(),d.Submission,null,"Apply exact decision atomically");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_requirement_set_review_application_v1.outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>owner.ApplyAsync(d.Source.Set.Reference.Id,detail.Application.Etag,apply)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.ApplicationOperationAsync(apply.OperationId,false)).State);
        Assert.Equal(c.ControlRowVersion,(await owner.WithdrawalContextAsync(d.Source.Set.Reference.Id,d.Submission)).ControlRowVersion);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw competing exact review");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_requirement_set_review_control_v1.withdraw_outbox"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>owner.WithdrawAsync(d.Source.Set.Reference.Id,d.Submission,"\""+c.ControlRowVersion+"\"",command)));hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.WithdrawalOperationAsync(command.CommandId,false)).State);
        Assert.Equal(c.ControlRowVersion,(await owner.WithdrawalContextAsync(d.Source.Set.Reference.Id,d.Submission)).ControlRowVersion);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=SetReviews(h with{Db=other});
        RequirementSetApplicationResultV1? applied=null;RequirementSetWithdrawalResultV1? withdrawn=null;
        var errors=await Task.WhenAll(Record.ExceptionAsync(async()=>{applied=await owner.ApplyAsync(d.Source.Set.Reference.Id,detail.Application.Etag,apply);}),
            Record.ExceptionAsync(async()=>{withdrawn=await peer.WithdrawAsync(d.Source.Set.Reference.Id,d.Submission,"\""+c.ControlRowVersion+"\"",command);}));
        Assert.True(applied?.Receipt.Application=="APPLIED" ^ withdrawn?.State=="WITHDRAWN",string.Join("\n",errors.Select(x=>x?.ToString())));
        if(applied!=null)
        {
            Assert.Equal("CONFLICT",withdrawn!.State);Assert.Null(withdrawn.Fact);
            Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
        }
        else
        {
            Assert.NotNull(errors[0]);detail=await owner.SubmissionAsync(d.Source.Set.Reference.Id,d.Submission);
            Assert.Equal("STALE",(await owner.ApplyAsync(d.Source.Set.Reference.Id,detail.Application.Etag,apply)).Receipt.Application);
        }
        Assert.NotEqual(c.ControlRowVersion,(await owner.WithdrawalContextAsync(d.Source.Set.Reference.Id,d.Submission)).ControlRowVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_delivery_lost_response_recovers_same_bytes_and_original_key_without_second_fact()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h);var service=await GrantSetWithdrawRelay(h,f);
        var owner=SetReviews(h);var c=await owner.WithdrawalContextAsync(f.Source.Set.Reference.Id,f.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),string.Concat(Enumerable.Repeat("😀",500)));
        var withdrawn=await owner.WithdrawAsync(f.Source.Set.Reference.Id,f.Submission,"\""+c.ControlRowVersion+"\"",command);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=SetWithdrawals(h,f,service);var transport=new NativeSetWithdrawalTransport(h,workflow,service.Identity.Principal){StaleFirstContext=true};
        var delivery=new RequirementSetWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_requirement_set_withdrawal_delivery_v1.delivery SET next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_requirement_set_withdrawal_delivery_v1.delivery SET next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);Assert.Equal(2,transport.Posts);Assert.Equal(3,transport.Queries);
        var observed=await owner.WithdrawalContextAsync(f.Source.Set.Reference.Id,f.Submission);
        Assert.Equal("WITHDRAWN",observed.Delivery!.State);
        Assert.All(transport.Sent,sent=>Assert.Equal(bytes,sent));
        Assert.Equal(bytes,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("WITHDRAWN",(await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value)).Ack.State);
        Assert.Equal(withdrawn,await owner.WithdrawAsync(f.Source.Set.Reference.Id,f.Submission,"\""+c.ControlRowVersion+"\"",command));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_pending_intake_and_explicit_reauthorization_keep_canonical_facts_and_new_round_cannot_fork()
    {
        await using var h=await Seed();var f=await SetDecisionSetup(h,false);var id=f.Source.Set.Reference.Id;
        await EngineeringAction(h,"plm-requirement-set","review-withdraw");var owner=SetReviews(h);var c=await owner.WithdrawalContextAsync(id,f.Submission);
        var result=await owner.WithdrawAsync(id,f.Submission,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"Withdraw while original set submission is pending"));
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        var principal=await h.Db.Database.SqlQuery<Guid>($"SELECT service_principal AS \"Value\" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Plm.RequirementSet.v1'").SingleAsync();
        var service=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        var intake=new ApprovalIntakeOwnerV2(h.Db,ScopeFence(h),service,ToolApproval(h),Authoring(h),new(ToolApproval(h),null,owner)){ReceiveClock=()=>BindingTime().AddSeconds(1)};
        var submission=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        await intake.SubmitAsync(submission);await GrantSetWithdrawRelay(h,f);
        var workflow=SetWithdrawals(h,f,service);var current=await workflow.ContextAsync(f.Submission);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var blocked=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",bytes);
        Assert.Equal("BLOCKED",blocked.Ack.State);Assert.Equal("WORKFLOW_NOT_STARTED",blocked.Ack.ErrorCode);
        c=await owner.WithdrawalContextAsync(id,f.Submission);
        var next=await owner.ReauthorizeWithdrawalAsync(id,f.Submission,result.TransportCommandId!.Value,"\""+c.ControlRowVersion+"\"",new(Guid.NewGuid(),"New explicit authorization of blocked set withdrawal"));
        await Starter(h).RunAsync(h.Tenant);current=await workflow.ContextAsync(f.Submission);
        var obsolete=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",bytes);
        Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("REQUIREMENT_SET_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
        Assert.True(await h.Db.Wf_FlowTasks.AsNoTracking().AnyAsync(t=>t.Status==0));
        var nextBytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE command_id={next.TransportCommandId}").SingleAsync();
        Assert.Equal("WITHDRAWN",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",nextBytes)).Ack.State);
        Assert.False(await h.Db.Wf_FlowTasks.AsNoTracking().AnyAsync(t=>t.Status==0));
        Assert.Equal(result,(await owner.WithdrawalOperationAsync(result.OperationId,false)).Result);
        var ready=await owner.ContextAsync(id);Assert.Equal("WITHDRAWN",ready.Submissions.Single().State);
        var replacement=SetSubmit(ready) with{ReplacesSubmissionId=f.Submission};var round=await owner.SubmitAsync(id,ready.Etag,replacement);
        Assert.NotEqual(f.Submission,round.Submission!.SubmissionId);Assert.Equal("IN_REVIEW",(await owner.WithdrawalContextAsync(id,round.Submission.SubmissionId)).State);
        ready=await owner.ContextAsync(id);
        Assert.Equal("REQUIREMENT_SET_REVIEW_ALREADY_REPLACED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.SubmitAsync(id,ready.Etag,replacement with{OperationId=Guid.NewGuid(),ReviewRoundId=Guid.NewGuid()}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementSetWithdrawal_current_fields_member_sources_scope_and_mode_gate_effects_but_applied_conflict_preserves_original()
    {
        await using var h=await Seed();var f=await SetApplicationSetup(h);var d=f.Set;var id=d.Source.Set.Reference.Id;
        await EngineeringAction(h,"plm-requirement-set","review-withdraw");var owner=SetReviews(h);var context=await owner.WithdrawalContextAsync(id,d.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Current exact set and member access are required");
        Task<RequirementSetWithdrawalResultV1> Withdraw()=>owner.WithdrawAsync(id,d.Submission,"\""+context.ControlRowVersion+"\"",command);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-requirement-set",FieldName="Members",Access=2});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.True((await owner.WithdrawalContextAsync(id,d.Submission)).CanWithdraw);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="plm-requirement-set"&&x.FieldName=="Members").ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Access,3));
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(Withdraw)).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="plm-requirement-set").ExecuteDeleteAsync();
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-requirement",FieldName="Definition",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(Withdraw)).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="plm-engineering-requirement").ExecuteDeleteAsync();
        var real=new RequirementSetReviewOwnerV1(h.Db,SetAuthority(h),ScopeFence(h),new(h.Db,SetAuthority(h),Evaluations(h,"REAL"),ScopeFence(h)));
        Assert.Equal("REQUIREMENT_REVIEW_MODE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>real.WithdrawAsync(id,d.Submission,"\""+context.ControlRowVersion+"\"",command))).Message);
        await using var other=await Seed();await SetSetup(other);await EngineeringAction(other,"plm-requirement-set","review-withdraw");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(other).WithdrawAsync(id,d.Submission,"\""+context.ControlRowVersion+"\"",command))).Status);
        Assert.Equal("UNKNOWN",(await owner.WithdrawalOperationAsync(command.CommandId,false)).State);
        FactAck(await f.Receiver.ReceiveAsync(f.Payload));var detail=await owner.SubmissionAsync(id,d.Submission);
        var applied=await owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Apply before withdrawal"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());var cookies=await Login(host,h);
        using var request=OperationsRequest(cookies,HttpMethod.Post,$"/api/requirement-set/owner-v1/{id}/review-submissions/{d.Submission}/withdraw",command);
        request.Headers.TryAddWithoutValidation("If-Match","\""+context.ControlRowVersion+"\"");using var response=await host.Send(request);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());var conflict=body.RootElement.GetProperty("data").Deserialize<RequirementSetWithdrawalResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal(applied.Receipt,conflict.AppliedReceipt);Assert.Null(conflict.Fact);Assert.Null(conflict.TransportCommandId);
        Assert.Equal(conflict,(await owner.WithdrawalOperationAsync(command.CommandId,false)).Result);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_requirement_set_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
    }
}

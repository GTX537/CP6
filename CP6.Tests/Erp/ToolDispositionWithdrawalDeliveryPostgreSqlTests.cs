using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ToolWithdrawalFixture(IntakeFixture Source,Guid Tool,Guid Submission);
    private async Task<ToolWithdrawalFixture> ToolWithdrawalSetup(Harness h)
    {
        var f=await DecisionSetup(h);return new(f.Source,await ApplicationTool(h),IntakeBody(f.Source).SubmissionId);
    }
    private static async Task<IntakeAuthority> GrantToolWithdrawRelay(Harness h,ToolWithdrawalFixture f)
    {
        var principal=await h.Db.Database.SqlQuery<Guid>($"""
            SELECT service_principal AS "Value" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner='Main.Wms.ToolDisposition.v1'
            """).SingleAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_withdraw_v2.registrations(environment_id,tenant_id,principal_id,owner,fact_owner,revision,enabled,can_query,can_relay,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.ToolDisposition.v1','Main.Wms.ToolWithdrawal.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_withdraw_v2.subject_grants(environment_id,tenant_id,principal_id,owner,submission_id,revision,enabled,can_query,can_relay)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.ToolDisposition.v1',{f.Submission},{Guid.NewGuid()},true,true,true)
            """);
        await EngineeringAction(h,"wms-plate-mold","dispose-withdraw");
        return new(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
    }
    private static ApprovalWithdrawalOwnerV2 ToolWithdrawals(Harness h,ToolWithdrawalFixture f,IntakeAuthority service)
        =>new(h.Db,ScopeFence(h),service,new(ToolApproval(h)));
    private static ToolBusinessGrantCommandV1 ToolGrantInput(Guid submission,Guid actual)
        =>new(Guid.NewGuid(),submission,null,null,actual,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1),["dispose-withdraw"],true,"Explicit exact-submission tool approval withdrawal delegation");
    private sealed class NativeToolWithdrawalTransport(Harness h,ApprovalWithdrawalOwnerV2 workflow,Guid principal):IToolWithdrawalTransportV1
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
    public async Task ToolWithdrawal_Owner_grant_is_independent_from_TASK_and_BLOCKED_needs_new_explicit_fact()
    {
        await using var h=await Seed();var f=await ToolWithdrawalSetup(h);var service=await GrantToolWithdrawRelay(h,f);
        await EngineeringAction(h,"wms-plate-mold","dispose-delegate");
        var actor=DecisionActor(h,await GrantDelegate(h,"tool-business-delegate"));var owner=ToolApproval(h);var actual=ToolApproval(actor);
        await CreateTaskGrant(h,h.User,actor.User,"binding-flow");var context=await actual.WithdrawalContextAsync(f.Tool,f.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Delegate withdraws this exact original");
        Assert.Equal("TOOL_BUSINESS_GRANT_DENIED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>actual.WithdrawAsync(f.Tool,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User))).Message);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString()});
        var ownerCookies=await Login(host,h);var actorCookies=await DocumentReviewerLogin(host,h,actor.User);var root="/api/wms/specials/v1/tools/"+f.Tool;
        var input=ToolGrantInput(f.Submission,actor.User);ToolBusinessGrantV1 grant;
        using(var missing=OperationsRequest(ownerCookies,HttpMethod.Post,root+"/approval-business-grants",input))
        {missing.Headers.Remove("X-CSRF-Token");using var denied=await host.Send(missing);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        using(var accepted=await host.Send(OperationsRequest(ownerCookies,HttpMethod.Post,root+"/approval-business-grants",input)))
            grant=(await DocumentHttpData<ToolBusinessGrantResultV1>(accepted)).Grant;
        var withdrawn=await actual.WithdrawAsync(f.Tool,f.Submission,"\""+context.ControlRowVersion+"\"",command,behalf:h.User);
        Assert.Equal(h.User,withdrawn.Fact!.Actor.RequesterId);Assert.Equal(actor.User,withdrawn.Fact.Actor.ActualHandlerId);Assert.Equal(grant.GrantId,withdrawn.Fact.Actor.GrantId);
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync();
        var revoked=(await owner.GrantBusinessAsync(f.Tool,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=grant.Version,Enabled=false})).Grant;
        var workflow=ToolWithdrawals(h,f,service);var current=await workflow.ContextAsync(f.Submission);
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        var renewed=(await owner.GrantBusinessAsync(f.Tool,input with{OperationId=Guid.NewGuid(),GrantId=grant.GrantId,ExpectedVersion=revoked.Version})).Grant;
        Assert.Equal("BLOCKED",(await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw)).Ack.State);
        context=await actual.WithdrawalContextAsync(f.Tool,f.Submission);ToolWithdrawalResultV1 next;
        using(var request=OperationsRequest(actorCookies,HttpMethod.Post,root+"/approval-submissions/"+f.Submission+"/withdrawals/"+withdrawn.TransportCommandId+"/reauthorize",new ApprovalUserWithdrawV2(Guid.NewGuid(),"Explicit current grant after BLOCKED")))
        {
            request.Headers.TryAddWithoutValidation("If-Match","\""+context.ControlRowVersion+"\"");request.Headers.Add("X-Acting-As",h.User.ToString("D"));
            using var response=await host.Send(request);next=await DocumentHttpData<ToolWithdrawalResultV1>(response);
        }
        Assert.Equal(renewed.Revision,next.Fact!.Actor.GrantRevision);Assert.Equal(withdrawn.TransportCommandId,next.PreviousCommandId);
        Assert.Equal(withdrawn.Fact.BusinessAfterRowVersion,next.Fact.BusinessBeforeRowVersion);
        var nextRaw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={next.TransportCommandId}").SingleAsync();
        var obsolete=await workflow.WithdrawAsync(f.Submission,"\""+current.SubmissionRowVersion+"\"",raw);
        Assert.Equal("BLOCKED",obsolete.Ack.State);Assert.Equal("TOOL_WITHDRAWAL_SUPERSEDED",obsolete.Ack.ErrorCode);
        var transport=new NativeToolWithdrawalTransport(h,workflow,service.Identity.Principal){LoseFirstResponse=false};
        var delivery=new ToolWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal(nextRaw,Assert.Single(transport.Sent));
        Assert.Equal("IDLE_OR_DISABLED",(await delivery.RunAsync(h.Tenant)).State);
        Assert.Equal("BLOCKED",(await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value)).Ack.State);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE command_id={withdrawn.TransportCommandId}").SingleAsync());
        Assert.Equal(next,(await actual.WithdrawalOperationAsync(next.OperationId,false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task ToolWithdrawal_delivery_lost_response_recovers_same_bytes_and_original_key_without_second_fact()
    {
        await using var h=await Seed();var f=await ToolWithdrawalSetup(h);var service=await GrantToolWithdrawRelay(h,f);
        var owner=ToolApproval(h);var c=await owner.WithdrawalContextAsync(f.Tool,f.Submission);
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),string.Concat(Enumerable.Repeat("😀",500)));
        var withdrawn=await owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",command);
        var bytes=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var workflow=ToolWithdrawals(h,f,service);var transport=new NativeToolWithdrawalTransport(h,workflow,service.Identity.Principal){StaleFirstContext=true};
        var returned=(await workflow.ContextAsync(f.Submission)) with{WorkflowState="RETURNED"};
        var envelope=ApprovalWithdrawalWireV2.Parse(bytes).Value;
        Assert.Equal(returned.SubmissionRowVersion,ToolWithdrawalDeliveryV1.Context(ApprovalWithdrawalResponseV2.Bytes(returned),envelope));
        Assert.Throws<ScopeAuthorizationExceptionV1>(()=>ToolWithdrawalDeliveryV1.Context(ApprovalWithdrawalResponseV2.Bytes(returned with{WorkflowState="UNRECOGNIZED"}),envelope));
        var delivery=new ToolWithdrawalDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands WHERE tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_withdrawal_delivery_v1.delivery SET next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("OUTCOME_UNKNOWN",(await delivery.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_withdrawal_delivery_v1.delivery SET next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("WITHDRAWN",(await delivery.RunAsync(h.Tenant)).State);Assert.Equal(2,transport.Posts);Assert.Equal(3,transport.Queries);
        var observed=await owner.WithdrawalContextAsync(f.Tool,f.Submission);
        Assert.Equal("WITHDRAWN",observed.Delivery!.State);
        Assert.All(transport.Sent,sent=>Assert.Equal(bytes,sent));
        Assert.Equal(bytes,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("WITHDRAWN",(await workflow.OriginalAsync(withdrawn.TransportCommandId!.Value)).Ack.State);
        Assert.Equal(withdrawn,await owner.WithdrawAsync(f.Tool,f.Submission,"\""+c.ControlRowVersion+"\"",command));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_control_v1.withdrawals WHERE tenant_id={h.Tenant}").SingleAsync());
    }

}

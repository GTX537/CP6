using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Dictionary<string,string> RuleWithdrawalHostSettings()=>new()
    { ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["LocationRuleReview__EvidenceMode"]="DEMO" };

    [CurrentPermissionPostgresFact]
    public async Task LocationRuleWithdrawal_actual_HTTP_changes_native_control_and_late_approval_is_stale()
    {
        await using var h=await Seed();var f=await RuleDecisionSetup(h);var id=f.Source.Candidate.Reference.Id;
        await EngineeringAction(h,"wms-location-rule","review-withdraw");
        await RuleVote(DecisionActor(h,f.Source.First));await RuleVote(DecisionActor(h,f.Source.Second));
        var receiver=await RuleReceiver(h,f);var owner=RuleReviews(h);
        var original=await ReferenceRules(h).ReadAsync(f.Source.Location,id);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,RuleWithdrawalHostSettings());
        var cookies=await Login(host,h);var path=$"/api/location-rule/owner-v1/{id}/review-submissions/{f.Submission}";
        using var contextResponse=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path+"/withdraw-context"));
        Assert.Equal(HttpStatusCode.OK,contextResponse.StatusCode);
        var context=await DocumentHttpData<JsonElement>(contextResponse);Assert.True(context.GetProperty("canWithdraw").GetBoolean());
        var before=context.GetProperty("controlRowVersion").GetString();
        var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw this exact location rule review");
        async Task<HttpResponseMessage> Send(object input)
        {
            using var request=OperationsRequest(cookies,HttpMethod.Post,path+"/withdraw",input);
            request.Headers.TryAddWithoutValidation("If-Match","\""+before+"\"");return await host.Send(request);
        }
        using(var forged=await Send(new{commandId=Guid.NewGuid(),reason="Worker cannot choose actor",actor=new{actualHandlerId=f.Source.First}}))
            Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        using var response=await Send(command);var result=await DocumentHttpData<JsonElement>(response);
        Assert.Equal("WITHDRAWN",result.GetProperty("state").GetString());
        var fact=result.GetProperty("fact").Deserialize<ApprovalWithdrawalFactV2>(ApprovalBindingOwnerV2.Json)!;
        Assert.Equal(h.User,fact.Actor.ActualHandlerId);Assert.Equal(h.User,fact.Actor.RequesterId);Assert.Null(fact.Actor.GrantId);
        Assert.Equal(before,fact.BusinessBeforeRowVersion);Assert.NotEqual(before,fact.BusinessAfterRowVersion);
        var after=await ReferenceRules(h).ReadAsync(f.Source.Location,id);
        Assert.Equal("WITHDRAWN",after.State);Assert.NotEqual(original.Etag,after.Etag);Assert.Equal(original.OriginalJson,after.OriginalJson);
        Assert.Equal(fact.BusinessAfterRowVersion,await h.Db.Database.SqlQuery<string>($"SELECT encode(int8send(revision),'base64') AS \"Value\" FROM cp6_location_rule_v1.proposals WHERE tenant_id={h.Tenant} AND id={id}").SingleAsync());
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(fact,ApprovalWithdrawalWireV2.Parse(raw).Value.Fact);
        FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var detail=await owner.SubmissionAsync(id,f.Submission);
        var stale=await owner.ApplyAsync(id,detail.Application.Etag,new(Guid.NewGuid(),f.Submission,null,"Observe approved fact after business withdrawal"));
        Assert.Equal("STALE",stale.Receipt.Application);Assert.False((await owner.SubmissionAsync(id,f.Submission)).Submission.IsApproved);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_location_rule_review_application_v1.applications WHERE tenant_id={h.Tenant}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw").ExecuteDeleteAsync();
        using(var replay=await Send(command))Assert.Equal(result.GetRawText(),(await DocumentHttpData<JsonElement>(replay)).GetRawText());
        using(var query=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/location-rule/owner-v1/review-withdrawal-operations/"+command.CommandId)))
            Assert.Equal("COMMITTED",(await DocumentHttpData<JsonElement>(query)).GetProperty("state").GetString());
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_location_rule_review_control_v1.withdraw_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}

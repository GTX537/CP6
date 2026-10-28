using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task IterationActions_actual_Program_HTTP_current_issues_withdrawal_CSRF_closed_body_and_original_recovery()
    {
        await using var h=await Seed();var f=await IterationDecisionSetup(h);var id=f.Source.Context.IterationId;
        var settings=await DocumentHostSettings(f.Source.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);
        var root="/api/engineering-iterations/owner-v1";var url=root+"/"+id;var submission=url+"/review-submissions/"+f.Submission;
        async Task<T> Read<T>(string route){using var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route));return await DocumentHttpData<T>(r);}
        async Task<HttpResponseMessage> Post(string route,object body,string? etag=null,bool csrf=true)
        {using var r=OperationsRequest(cookies,HttpMethod.Post,route,body);if(etag!=null)r.Headers.TryAddWithoutValidation("If-Match",etag);if(!csrf)r.Headers.Remove("X-CSRF-Token");return await host.Send(r);}
        var issues=await Read<IterationReviewIssueContextV1>(url+"/review-issues");
        var issue=new IterationReviewIssueCommandV1(Guid.NewGuid(),f.Submission,Guid.NewGuid(),"CREATE",null,"Actual HTTP review question",new("PROCESS",null,null),h.User,new(2026,11,2),null,"Original review concern");
        using(var csrf=await Post(url+"/review-issues",issue,issues.Etag,false))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        var forged=JsonSerializer.SerializeToNode(issue,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();forged["verifiedBy"]=h.User;
        using(var malformed=await Post(url+"/review-issues",forged,issues.Etag))Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        using(var response=await Post(url+"/review-issues",issue,issues.Etag))
        {Assert.True(response.Headers.CacheControl?.NoStore);Assert.Equal(issue.IssueId,(await DocumentHttpData<IterationReviewIssueResultV1>(response)).Issue.Id);}
        Assert.Equal("COMMITTED",(await Read<IterationReviewIssueOperationV1>(root+"/review-issue-operations/"+issue.OperationId)).State);
        var c=await Read<IterationApplicationContextV1>(submission+"/application");var command=new ApprovalUserWithdrawV2(Guid.NewGuid(),"Withdraw actual current round");
        using(var csrf=await Post(submission+"/withdraw",command,"\""+c.ControlRowVersion+"\"",false))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        var invalid=JsonSerializer.SerializeToNode(command,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();invalid["actualHandlerId"]=Guid.NewGuid();
        using(var malformed=await Post(submission+"/withdraw",invalid,"\""+c.ControlRowVersion+"\""))Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        using(var withdrawn=await Post(submission+"/withdraw",command,"\""+c.ControlRowVersion+"\""))
        {var r=await DocumentHttpData<IterationWithdrawalResultV1>(withdrawn);Assert.Equal("WITHDRAWN",r.State);Assert.Equal(h.User,r.Fact!.Actor.ActualHandlerId);}
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-withdraw"||x.ActionCode=="review-issue").ExecuteDeleteAsync();
        Assert.Equal("COMMITTED",(await Read<IterationWithdrawalOperationV1>(root+"/review-withdrawal-operations/"+command.CommandId)).State);
        Assert.Equal("COMMITTED",(await Read<IterationReviewIssueOperationV1>(root+"/review-issue-operations/"+issue.OperationId)).State);
        c=await Read<IterationApplicationContextV1>(submission+"/application");Assert.Equal("IN_DESIGN",c.BusinessStage);Assert.Equal("PENDING",c.WithdrawalDelivery!.State);
        using(var denied=await Post(submission+"/withdraw",new ApprovalUserWithdrawV2(Guid.NewGuid(),"No longer authorized"),"\""+c.ControlRowVersion+"\""))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var unreadable=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/review-withdrawal-operations/"+command.CommandId));Assert.Equal(HttpStatusCode.Forbidden,unreadable.StatusCode);
    }
}

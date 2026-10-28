using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<T> DocumentHttpData<T>(HttpResponseMessage response)
    {
        var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,text);
        using var json=JsonDocument.Parse(text);var root=json.RootElement;
        return (root.ValueKind==JsonValueKind.Object && root.TryGetProperty("data",out var data)?data:root).Deserialize<T>(ApprovalBindingOwnerV2.Json)!;
    }
    private static async Task<Dictionary<string,string>> DocumentReviewerLogin(AuthenticationApiProcess host,Harness h,Guid actor)
    {
        var hash=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==actor).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,hash));
        var name=await h.Db.Sys_Users.Where(x=>x.Id==actor).Select(x=>x.UserName).SingleAsync();
        using var response=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login"){
            Content=JsonContent.Create(new{UserName=name,Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);
    }
    private static async Task<Dictionary<string,string>> DocumentHostSettings(ContentStore files)
    {
        var root=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,"document-review-http-files",Guid.NewGuid().ToString("N"));
        var store=new LocalFileStore(root);
        foreach(var entry in files.Bytes)await store.SaveAsync(new MemoryStream(entry.Value),entry.Key);
        return new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["Storage__Provider"]="Local",["Storage__LocalRoot"]=root};
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_actual_Program_HTTP_policy_submission_CAS_CSRF_and_original_recovery()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,await DocumentHostSettings(f.Files));
        var cookies=await Login(host,h);var url=$"/api/controlled-document/owner-v1/{f.Id}";
        async Task<DocumentReviewContextV1> Context(){using var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/review-context"));return await DocumentHttpData<DocumentReviewContextV1>(r);}
        async Task<HttpResponseMessage> Post(string path,object input,string etag,bool csrf=true)
        {
            using var r=OperationsRequest(cookies,HttpMethod.Post,path,input);r.Headers.TryAddWithoutValidation("If-Match",etag);
            if(!csrf)r.Headers.Remove("X-CSRF-Token");return await host.Send(r);
        }
        var first=await Context();var policy=DocumentPolicy();
        using(var denied=await Post(url+"/review-policies",policy,first.Etag,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var saved=await Post(url+"/review-policies",policy,first.Etag))Assert.Equal("POLICY",(await DocumentHttpData<DocumentReviewResultV1>(saved)).Kind);
        var ready=await Context();var command=DocumentSubmit(ready);
        using(var stale=await Post(url+"/review-submissions",command,first.Etag))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        using(var submitted=await Post(url+"/review-submissions",command,ready.Etag))Assert.False((await DocumentHttpData<DocumentReviewResultV1>(submitted)).Submission!.IsApproved);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-submit").ExecuteDeleteAsync();
        using(var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/controlled-document/owner-v1/review-operations/"+command.OperationId)))
            Assert.Equal("COMMITTED",(await DocumentHttpData<DocumentReviewOperationV1>(original)).State);
        Assert.Single((await Context()).Submissions);
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_actual_Program_HTTP_professional_votes_exact_approval_and_read_only_recovery()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,await DocumentHostSettings(f.Files));
        var author=await Login(host,h);var url=$"/api/controlled-document/owner-v1/{f.Document}/review-submissions/{f.Submission}";
        async Task<DocumentReviewDetailV1> Detail(){using var r=await host.Send(OperationsRequest(author,HttpMethod.Get,url));return await DocumentHttpData<DocumentReviewDetailV1>(r);}
        var before=await Detail();Assert.False(before.Submission.IsApproved);Assert.Null(before.Application.Decision);
        foreach(var actor in new[]{f.First,f.Second})
        {
            var cookies=await DocumentReviewerLogin(host,h,actor);
            using var list=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/oa/tasks/document-review/"+f.Document));
            var task=Assert.Single(await DocumentHttpData<DocumentReviewTaskV1[]>(list));Assert.Equal(f.Submission,task.SubmissionId);
            using var get=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/oa/tasks/{task.Task.TaskId}/decision-context"));
            var current=await DocumentHttpData<ApprovalTaskDecisionViewV2>(get);
            var command=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","Reviewed exact original, derivations and language authority",current.TaskRowVersion,current.InstanceRowVersion);
            using var request=OperationsRequest(cookies,HttpMethod.Post,$"/api/oa/tasks/{task.Task.TaskId}/decision",command);
            request.Headers.TryAddWithoutValidation("If-Match",get.Headers.ETag!.ToString());
            using var decision=await host.Send(request);var saved=await DocumentHttpData<ApprovalTaskDecisionResultV2>(decision);
            Assert.Equal("COMPLETED",saved.State);
            using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/oa/tasks/decision-commands/"+command.CommandId));
            Assert.Equal(saved,await DocumentHttpData<ApprovalTaskDecisionResultV2>(original));
        }
        Assert.Null((await Detail()).Application.Decision);
        var receiver=await DocumentReceiverSetup(h,f);FactAck(await receiver.Receiver.ReceiveAsync(receiver.Payload));
        var ready=await Detail();Assert.Equal("APPROVED",ready.Application.Decision);Assert.False(ready.Submission.IsApproved);
        var apply=new DocumentApplicationCommandV1(Guid.NewGuid(),f.Submission,null,"Apply actual professional signatures to this exact document version");
        using var applicationRequest=OperationsRequest(author,HttpMethod.Post,$"/api/controlled-document/owner-v1/{f.Document}/review-application",apply);
        applicationRequest.Headers.TryAddWithoutValidation("If-Match",ready.Application.Etag);
        using var applicationResponse=await host.Send(applicationRequest);var applied=await DocumentHttpData<DocumentApplicationResultV1>(applicationResponse);
        Assert.Equal("APPLIED",applied.Receipt.Application);Assert.True((await Detail()).Submission.IsApproved);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-apply").ExecuteDeleteAsync();
        using var recovered=await host.Send(OperationsRequest(author,HttpMethod.Get,"/api/controlled-document/owner-v1/review-application-operations/"+apply.OperationId));
        Assert.Equal(applied,(await DocumentHttpData<DocumentApplicationOperationV1>(recovered)).Result);
    }
}

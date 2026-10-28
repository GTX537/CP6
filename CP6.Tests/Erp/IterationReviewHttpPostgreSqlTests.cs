using System.Net;
using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task IterationReview_actual_Program_HTTP_exact_submission_CSRF_strict_body_and_original_recovery()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h,0);var settings=await DocumentHostSettings(f.Files);settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);var id=f.Context.IterationId;var url=$"/api/engineering-iterations/owner-v1/{id}";
        async Task<IterationReviewContextV1> Context(){using var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/review-context"));return await DocumentHttpData<IterationReviewContextV1>(r);}
        async Task<HttpResponseMessage> Post(string route,object body,string? etag=null,bool csrf=true)
        {using var r=OperationsRequest(cookies,HttpMethod.Post,route,body);if(etag!=null)r.Headers.TryAddWithoutValidation("If-Match",etag);if(!csrf)r.Headers.Remove("X-CSRF-Token");return await host.Send(r);}
        using var check=await Post("/api/engineering-source-checks/owner-v1/commands",f.Check.Original.Command with{OperationId=Guid.NewGuid()});
        var checkedResult=await DocumentHttpData<EngineeringSourceCheckResultV1>(check);
        var checkRef=new EngineeringSourceRefV1(EngineeringSourceCheckValuesV1.Owner,checkedResult.CheckRunId,"1",checkedResult.Digest);
        using var candidate=await Post("/api/iteration-candidates/owner-v1/commands",f.Candidate with{SourceCheck=checkRef});await DocumentHttpData<IterationCandidateResultV1>(candidate);
        var ready=await Context();Assert.All(ready.Candidates,v=>Assert.True(v.Findings.Length==0,JsonSerializer.Serialize(new{v.SourceDrift,v.Findings,v.Original.Definition.TechnicalSource.Items})));var c=IterationSubmit(f with{Context=ready});
        using(var csrf=await Post(url+"/review-submissions",c,ready.Etag,false))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        var json=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();json["owner"]="Untrusted";
        using(var malformed=await Post(url+"/review-submissions",json,ready.Etag))Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        using var response=await Post(url+"/review-submissions",c,ready.Etag);var result=await DocumentHttpData<IterationReviewResultV1>(response);Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("IN_REVIEW",result.Submission!.BusinessStage);Assert.Equal("NOT_RECEIVED",result.Submission.ApplicationState);
        using(var replay=await Post(url+"/review-submissions",c,ready.Etag))Assert.Equal(result,await DocumentHttpData<IterationReviewResultV1>(replay));
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-submit").ExecuteDeleteAsync();
        using(var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/engineering-iterations/owner-v1/review-operations/"+c.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<IterationReviewOperationV1>(original)).State);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/review-submissions/"+result.Submission.SubmissionId));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}

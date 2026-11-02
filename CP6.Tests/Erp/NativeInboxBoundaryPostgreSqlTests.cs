using System.Net;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_two_connections_keep_one_first_read_with_separate_original_commands_and_no_legacy_writer()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var inbox=NativeInbox(h,f.Owner);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=db};
        var second=NativeInbox(peer);var results=await Task.WhenAll(inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"one")),second.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"two")));
        Assert.Equal(results[0].Result,results[1].Result);Assert.Equal(1,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_read_v2.facts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(results[0],await inbox.ReadOriginalAsync("TASK",f.Task,"one"));Assert.Equal(results[1],await second.ReadOriginalAsync("TASK",f.Task,"two"));
        var old=new InboxService(h.Db,Moq.Mock.Of<IFlowEngine>(Moq.MockBehavior.Strict),Moq.Mock.Of<IForecastService>(Moq.MockBehavior.Strict));
        Assert.Equal("CURRENT_READ_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>old.MarkTaskReadAsync(h.User,f.Task))).Message);
        var cc=(await h.Db.Wf_FlowCcs.AsNoTracking().SingleAsync()).Id;
        Assert.Equal("CURRENT_READ_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>old.MarkCcReadAsync(h.User,cc))).Message);
        Assert.Empty(await old.PendingAsync(h.User));Assert.Empty(await old.PendingCcAsync(h.User));
        Assert.Empty((await old.BatchTransferPreviewAsync(h.User)).Sample);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_HTTP_cookie_CSRF_explicit_read_original_and_current_scope_are_real()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await EngineeringAction(h,"oa-inbox","read");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string root="/api/oa/v2/inbox";
        using var pending=await host.Send(BoundRequest(cookies,root+"/pending"));var page=await PersonalHttpResult<NativeInboxPageV2>(pending);Assert.Equal(1,page.TotalItems);Assert.True(pending.Headers.CacheControl?.NoStore);
        using var before=await host.Send(BoundRequest(cookies,root+"/instances/"+f.Instance));var detail=await PersonalHttpResult<NativeInstanceReadV2>(before);Assert.Null(Assert.Single(detail.Tasks).Read);
        var raw=PersonalCommand(new{},"HTTP.Read");var path=root+"/tasks/"+f.Task+"/read";
        using var csrf=await host.Send(PersonalHttp(cookies,path,raw,false));Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var accepted=await host.Send(PersonalHttp(cookies,path,raw));var fact=await PersonalHttpResult<NativeReadCommandResultV2>(accepted);Assert.Equal("READ",fact.State);
        using var original=await host.Send(BoundRequest(cookies,fact.QueryRef));Assert.Equal(fact,await PersonalHttpResult<NativeReadCommandResultV2>(original));
        using var after=await host.Send(BoundRequest(cookies,root+"/instances/"+f.Instance));Assert.Equal(detail.Tasks[0].TaskRowVersion,(await PersonalHttpResult<NativeInstanceReadV2>(after)).Tasks[0].TaskRowVersion);
        using var legacy=await host.Send(BoundRequest(cookies,"/api/oa/inbox/task/read",new{id=f.Task}));Assert.Equal(HttpStatusCode.Conflict,legacy.StatusCode);
        using var acted=BoundRequest(cookies,root+"/pending");acted.Headers.Add("X-Acting-As",Guid.NewGuid().ToString("D"));
        using var denied=await host.Send(acted);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var policy=(await f.Owner.PolicyAsync("personal"))!;await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[],policy.RetentionPolicyRef,30,true,"revoke current resource"));
        using var hidden=await host.Send(BoundRequest(cookies,root+"/counts"));Assert.Equal(0,(await PersonalHttpResult<NativeInboxCountsV2>(hidden)).Pending);
        using var forbidden=await host.Send(BoundRequest(cookies,fact.QueryRef));Assert.Equal(HttpStatusCode.NotFound,forbidden.StatusCode);
    }
}

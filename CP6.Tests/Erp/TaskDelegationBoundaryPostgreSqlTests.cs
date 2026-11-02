using System.Net;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_HTTP_cookie_CSRF_exact_actor_original_and_revocation_reach_actual_native_read()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await GrantManagement(h);var actual=await GrantDelegate(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var owner=await Login(host,h);var peer=await DocumentReviewerLogin(host,h,actual);const string root="/api/oa/v2/delegations";
        using var targets=await host.Send(BoundRequest(owner,root+"/targets?search=delegate-v2"));
        Assert.Equal(actual,Assert.Single(await PersonalHttpResult<TaskDelegationTargetV2[]>(targets)).Id);
        var raw=PersonalCommand(GrantInput(actual),"http-grant");
        using var csrf=await host.Send(PersonalHttp(owner,root,raw,false));Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var created=await host.Send(PersonalHttp(owner,root,raw));var result=await PersonalHttpResult<TaskDelegationCommandV2>(created);var grant=result.Result!;
        Assert.True(created.Headers.CacheControl?.NoStore);
        using var original=await host.Send(BoundRequest(owner,result.QueryRef));Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result),System.Text.Json.JsonSerializer.Serialize(await PersonalHttpResult<TaskDelegationCommandV2>(original)));
        using var received=await host.Send(BoundRequest(peer,root+"/received"));Assert.Equal(h.User,Assert.Single(await PersonalHttpResult<TaskDelegationPrincipalV2[]>(received)).UserId);
        using var request=BoundRequest(peer,"/api/oa/v2/inbox/pending");request.Headers.Add("X-Acting-As",h.User.ToString("D"));
        using var read=await host.Send(request);var page=await PersonalHttpResult<NativeInboxPageV2>(read);Assert.Single(page.Items);Assert.Equal(actual,page.ActualUserId);
        using var overrideRequest=PersonalHttp(peer,root,PersonalCommand(GrantInput(h.User),"not-own"));overrideRequest.Headers.Add("X-Acting-As",h.User.ToString("D"));
        using var denied=await host.Send(overrideRequest);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using var revoke=await host.Send(PersonalHttp(owner,root+"/"+grant.GrantId+"/revoke",PersonalCommand(new TaskDelegationRevokeV2("explicit stop"),"http-revoke",grant.Version.ToString("D"))));
        Assert.False((await PersonalHttpResult<TaskDelegationCommandV2>(revoke)).Result!.Enabled);
        using var afterRequest=BoundRequest(peer,"/api/oa/v2/inbox/pending");afterRequest.Headers.Add("X-Acting-As",h.User.ToString("D"));
        using var after=await host.Send(afterRequest);Assert.Equal(HttpStatusCode.Forbidden,after.StatusCode);
        using var history=await host.Send(BoundRequest(owner,root+"/"+grant.GrantId+"/history"));Assert.Equal(2,(await PersonalHttpResult<TaskDelegationRevisionV2[]>(history)).Length);
        Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.SingleAsync(x=>x.Id==f.Task)).AssigneeId);
    }

    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_writer_coverage_direct_mutation_and_deferred_rejection_leave_no_false_authority()
    {
        await using var h=await Seed();await NativeFormSetup(h);await GrantManagement(h);var actual=await GrantDelegate(h);var owner=TaskGrants(h);
        var raw=PersonalCommand(GrantInput(actual),"guarded");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_task_delegation_v2.commands DISABLE TRIGGER grant_command_complete");
        try{Assert.Equal("GRANT_AUTHORITY_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,raw))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_task_delegation_v2.commands ENABLE TRIGGER grant_command_complete");}
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync("CREATE","new","guarded")).State);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.grant_test_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'grant_test_commit_reject' USING ERRCODE='23514';END $$;
            CREATE CONSTRAINT TRIGGER grant_test_reject AFTER INSERT ON cp6_task_delegation_v2.heads DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.grant_test_reject();
            """);
        try{Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.WriteAsync("CREATE",null,raw))).SqlState);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER grant_test_reject ON cp6_task_delegation_v2.heads;DROP FUNCTION public.grant_test_reject()");}
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync("CREATE","new","guarded")).State);Assert.Empty((await owner.ListAsync()).Granted);
        var grant=(await owner.WriteAsync("CREATE",null,raw)).Result!;
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_task_delegation_v2.heads SET revision=revision+1 WHERE id={grant.GrantId}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_task_delegation_v2.revisions SET body='{{}}' WHERE grant_id={grant.GrantId}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_task_delegation_v2.heads WHERE id={grant.GrantId}"));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(grant),System.Text.Json.JsonSerializer.Serialize(Assert.Single((await owner.ListAsync()).Granted)));
    }
}

using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task PrivacyAuthority(Harness h)
    {
        await RegisterScope(h);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,true));
        ((ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("is_platform_admin","true"));
        foreach(var action in new[]{"read","export","download","revoke","scope-identity","scope-security","scope-roles"})await EngineeringAction(h,"privacy-export",action);
    }
    private static PrivacyExportOwnerV1 PrivacyOwner(Harness h)=>new(h.Db,new OperationsHttp{HttpContext=h.Http},
        new CurrentPermissionContext(new OperationsHttp{HttpContext=h.Http},CurrentPermissionContextTests.Cache(),h.Db,new PermissionAggregator(h.Db)),Options.Create(new ScopeAuthorizationOptionsV1{EnvironmentId=ScopeEnvironment}));
    private static TenantManagementSessionV1 PrivacySession(Harness h,Func<CancellationToken,Task>? complete=null)=>new(_=>Task.FromResult(h.User),complete??(_=>Task.CompletedTask));
    private static PrivacyExportCommandV1 PrivacyCommand(Harness h)=>new(Guid.NewGuid(),Guid.NewGuid(),h.Tenant,h.User,
        "SUBJECT_ACCESS_REQUEST",["identity","security","roles"],h.User,PrivacyExportOwnerV1.Policy,60);
    private static Task<int> PrivacyCount(Harness h,string table)=>h.Db.Database.SqlQueryRaw<int>(
        $"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.{table} WHERE actor_tenant='"+h.Tenant+"'").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_exact_original_survives_export_revocation_and_close_fences_delayed_generation()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var o=PrivacyOwner(h);var session=PrivacySession(h);var command=PrivacyCommand(h);
        var first=await o.GenerateAsync(command,session);Assert.Equal("COMMITTED",first.State);Assert.False(first.Receipt!.Manifest.Complete);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NickName,"changed-after-capture"));
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="export").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(await o.GenerateAsync(command,session)));
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(await o.OperationAsync(command.OperationId,session)));
        Assert.Equal("PRIVACY_OPERATION_CONFLICT",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateAsync(command with{Purpose="ACCOUNT_SECURITY_REVIEW"},session))).Message);
        Assert.Equal(1,await PrivacyCount(h,"commands"));Assert.Equal(1,await PrivacyCount(h,"artifacts"));
        var late=PrivacyCommand(h);Assert.Equal("NOT_FOUND",(await o.OperationAsync(late.OperationId,session)).State);
        Assert.Equal("CLOSED",(await o.CloseAsync(late.OperationId,session)).State);
        await EngineeringAction(h,"privacy-export","export");Assert.Equal("CLOSED",(await o.GenerateAsync(late,session)).State);
        Assert.Equal(1,await PrivacyCount(h,"artifacts"));
        var download=await o.DownloadAsync(first.Receipt.ArtifactId,session);Assert.DoesNotContain("changed-after-capture",System.Text.Encoding.UTF8.GetString(download.Bytes));
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="read").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.OperationAsync(command.OperationId,session))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_selected_sources_and_target_binding_are_exact_and_current_download_scope_is_required()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var o=PrivacyOwner(h);var s=PrivacySession(h);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="scope-security"||x.ActionCode=="scope-roles").ExecuteDeleteAsync();
        var all=PrivacyCommand(h);Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateAsync(all,s))).Status);
        Assert.Equal(0,await PrivacyCount(h,"artifacts"));
        Assert.Equal("PRIVACY_TARGET_BINDING_CHANGED",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateAsync(all with{Sources=["identity"],TargetTenantId=Guid.NewGuid()},s))).Message);
        var one=await o.GenerateAsync(all with{Sources=["identity"]},s);var file=await o.DownloadAsync(one.Receipt!.ArtifactId,s);
        using var doc=JsonDocument.Parse(file.Bytes);Assert.Empty(doc.RootElement.GetProperty("securityLogs").EnumerateArray());
        Assert.Equal("OUTSIDE_REQUESTED_SCOPE",one.Receipt.Manifest.Sources.Single(x=>x.Source=="Sys_SecurityLog").Reason);
        Assert.Null(one.Receipt.Manifest.Sources.Single(x=>x.Source=="Sys_SecurityLog").RecordCount);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="scope-identity").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadAsync(one.Receipt.ArtifactId,s))).Status);
        Assert.Equal("COMMITTED",(await o.OperationAsync(all.OperationId,s)).State);
        await EngineeringAction(h,"privacy-export","scope-security");
        var securityOnly=await o.GenerateAsync(all with{OperationId=Guid.NewGuid(),Sources=["security"]},s);
        using var securityDoc=JsonDocument.Parse((await o.DownloadAsync(securityOnly.Receipt!.ArtifactId,s)).Bytes);
        Assert.Equal(JsonValueKind.Null,securityDoc.RootElement.GetProperty("user").ValueKind);
        Assert.Equal("OUTSIDE_REQUESTED_SCOPE",securityOnly.Receipt.Manifest.Sources.Single(x=>x.Source=="Sys_User").Reason);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_distinct_recipient_cross_tenant_denial_and_revocation_are_enforced_at_download()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var recipient=Guid.NewGuid();
        h.Db.Sys_Users.Add(new(){Id=recipient,UserName="privacy-recipient",Password="unused-simulation",RoleId=72,IsPlatformAdmin=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var o=PrivacyOwner(h);var original=await o.GenerateAsync(PrivacyCommand(h) with{RecipientId=recipient},PrivacySession(h));var id=original.Receipt!.ArtifactId;
        Assert.Equal("PRIVACY_RECIPIENT_REQUIRED",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadAsync(id,PrivacySession(h)))).Message);
        await using var recipientDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var principal=CurrentPermissionContextTests.Principal(recipient,h.Tenant);((ClaimsIdentity)principal.Identity!).AddClaim(new("is_platform_admin","true"));
        var rh=h with{Db=recipientDb,User=recipient,Http=new DefaultHttpContext{User=principal}};
        Assert.NotEmpty((await PrivacyOwner(rh).DownloadAsync(id,PrivacySession(rh))).Bytes);
        Assert.Equal("NOT_FOUND",(await PrivacyOwner(rh).OperationAsync(original.OperationId,PrivacySession(rh))).State);
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(rh).RevokeAsync(id,PrivacySession(rh)))).Status);
        await using var other=await Seed();await PrivacyAuthority(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(other).DownloadAsync(id,PrivacySession(other)))).Status);
        await o.RevokeAsync(id,PrivacySession(h));await o.RevokeAsync(id,PrivacySession(h));
        Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(rh).DownloadAsync(id,PrivacySession(rh)))).Status);
        Assert.Equal("REVOKED",(await o.ReadArtifactAsync(id,PrivacySession(h))).Availability);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_lost_commit_is_recovered_without_regeneration_and_final_session_failure_rolls_back_every_effect()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await PrivacyAuthority(h);var command=PrivacyCommand(h);fault.Armed=true;
        Assert.Equal("PRIVACY_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(h).GenerateAsync(command,PrivacySession(h)))).Message);
        await using var fresh=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var recovered=h with{Db=fresh};var o=PrivacyOwner(recovered);
        var saved=await o.OperationAsync(command.OperationId,PrivacySession(recovered));Assert.Equal("COMMITTED",saved.State);
        Assert.Equal(saved.Receipt!.ArtifactId,(await o.GenerateAsync(command,PrivacySession(recovered))).Receipt!.ArtifactId);
        Assert.Equal(1,await PrivacyCount(recovered,"audit"));
        var fail=PrivacySession(recovered,_=>throw new InvalidOperationException("simulated logout at completion"));var next=PrivacyCommand(recovered);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.GenerateAsync(next,fail));
        Assert.Equal("NOT_FOUND",(await o.OperationAsync(next.OperationId,PrivacySession(recovered))).State);
        Assert.Equal(1,await PrivacyCount(recovered,"artifacts"));Assert.Equal(1,await PrivacyCount(recovered,"audit"));
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_generation_and_download_share_native_permission_revocation_gate()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var s=PrivacySession(h,async _=>{entered.SetResult();await release.Task;});var pending=PrivacyOwner(h).GenerateAsync(PrivacyCommand(h),s);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var revoke=writer.Sys_RoleActions.Where(x=>x.ActionCode=="download").ExecuteDeleteAsync();
        try
        {
            await AssertNativeWait(writer,revoke);release.TrySetResult();var saved=await pending;await revoke;
            Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(h).DownloadAsync(saved.Receipt!.ArtifactId,PrivacySession(h)))).Status);
        }
        finally{release.TrySetResult();}
    }
    private async Task AssertNativeWait(CP6.Core.EFDbContext.CP6Context waiting,Task pending)
    {
        await using var observer=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});
        var connection=(Npgsql.NpgsqlConnection)waiting.Database.GetDbConnection();
        for(var i=0;i<100;i++)
        {
            if(pending.IsCompleted)throw new InvalidOperationException("native writer did not wait for held authority");
            if(connection.State==System.Data.ConnectionState.Open && await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={connection.ProcessID} AND wait_event_type='Lock') AS \"Value\"").SingleAsync())return;
            await Task.Delay(20);
        }
        throw new TimeoutException("native writer lock not observed");
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyExport_actual_HTTP_generates_once_downloads_current_bytes_and_denies_revoked_download()
    {
        await using var h=await Seed();await PrivacyAuthority(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());
        var cookies=await Login(host,h);var command=PrivacyCommand(h);
        using var generated=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/platform/privacy-exports-v1/commands",command));
        Assert.Equal(HttpStatusCode.OK,generated.StatusCode);Assert.True(generated.Headers.CacheControl!.NoStore);
        var saved=JsonSerializer.Deserialize<PrivacyExportOperationV1>(await generated.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using(var download=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-exports-v1/artifacts/{saved.Receipt!.ArtifactId}/download")))
        {Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.True(download.Headers.CacheControl!.NoStore);using var doc=JsonDocument.Parse(await download.Content.ReadAsStringAsync());Assert.Equal(h.User,doc.RootElement.GetProperty("manifest").GetProperty("subjectId").GetGuid());}
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="download").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-exports-v1/artifacts/{saved.Receipt!.ArtifactId}/download"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("UserName",await denied.Content.ReadAsStringAsync());
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/platform/privacy-exports-v1/operations/{command.OperationId}"));
        Assert.Equal(HttpStatusCode.OK,original.StatusCode);
    }
}

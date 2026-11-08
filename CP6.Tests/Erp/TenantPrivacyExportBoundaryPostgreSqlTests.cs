using System.Data.Common;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<Harness> TenantPrivacyRecipient(Harness h)
    {
        var setup=await PrivacyDispositionSetup(h);var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-export");
        foreach(var action in TenantPrivacyActions){await EngineeringAction(h,"privacy-export",action);h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return setup.Approver;
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_current_exact_recipient_tenant_source_authority_and_revoke_fence_every_download_without_deleting_payload()
    {
        await using var h=await Seed();var recipient=await TenantPrivacyRecipient(h);var s=PrivacySession(h);var o=PrivacyOwner(h);
        var command=TenantPrivacyCommand(h) with{RecipientId=recipient.User};var result=await o.GenerateTenantAsync(command,s);var id=result.Receipt!.ArtifactId;
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(id,s))).Status);
        var ro=PrivacyOwner(recipient);var rs=PrivacySession(recipient);Assert.NotEmpty((await ro.DownloadTenantAsync(id,rs)).Bytes);Assert.Equal("NOT_FOUND",(await ro.TenantOperationAsync(command.OperationId,false,rs)).State);
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>ro.RevokeTenantAsync(id,rs))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="tenant-scope-security").ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>ro.DownloadTenantAsync(id,rs))).Status);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-export");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="tenant-scope-security"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(x=>x.Id==recipient.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,false));await Assert.ThrowsAsync<CP6.Core.Auth.PlatformAdminAuthorizationException>(()=>ro.DownloadTenantAsync(id,rs));
        await h.Db.Sys_Users.Where(x=>x.Id==recipient.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,true));Assert.NotEmpty((await ro.DownloadTenantAsync(id,rs)).Bytes);
        await using var other=await Seed();await TenantPrivacyAuthority(other);Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(other).DownloadTenantAsync(id,PrivacySession(other)))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.GenerateTenantAsync(TenantPrivacyCommand(h) with{RecipientId=other.User},s))).Status);
        await o.RevokeTenantAsync(id,s);await o.RevokeTenantAsync(id,s);Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>ro.DownloadTenantAsync(id,rs))).Status);Assert.Equal("REVOKED",(await o.TenantArtifactAsync(id,s)).Availability);
        Assert.Equal(1,await TenantPrivacyCount(h,"payloads p JOIN cp6_privacy_tenant_export_v1.artifacts a ON a.id=p.artifact_id"));Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.audit WHERE artifact_id={id} AND kind='REVOKED'").SingleAsync());
    }
    private static async Task<Guid> HistoricalTenantPrivacyArtifact(Harness h,TenantPrivacyExportReceiptV1 receipt,byte[] bytes,DateTimeOffset expiry,bool includePayload=true)
    {
        var id=Guid.NewGuid();var copy=receipt with{ArtifactId=id,ExpiresAt=expiry};await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_tenant_export_v1.artifacts(id,environment_id,actor_tenant,actor_id,recipient_id,target_tenant,policy,expires_at,receipt_bytes)
            VALUES({id},{ScopeEnvironment},{h.Tenant},{h.User},{h.User},{receipt.TargetTenantId},{PrivacyExportOwnerV1.TenantPolicy},{expiry},{JsonSerializer.SerializeToUtf8Bytes(copy)})
            """);if(includePayload)await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({id},{bytes})");return id;
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_expiry_at_entry_or_final_session_and_missing_bytes_fail_before_any_disclosure_and_native_rewrite_is_rejected()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var o=PrivacyOwner(h);var s=PrivacySession(h);var receipt=(await o.GenerateTenantAsync(TenantPrivacyCommand(h),s)).Receipt!;var bytes=(await o.DownloadTenantAsync(receipt.ArtifactId,s)).Bytes;
        var expired=await HistoricalTenantPrivacyArtifact(h,receipt,bytes,PrivacyReviewTime(-60));Assert.Equal("EXPIRED",(await o.TenantArtifactAsync(expired,s)).Availability);Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(expired,s))).Status);
        var shortly=await HistoricalTenantPrivacyArtifact(h,receipt,bytes,PrivacyReviewTime(2));Assert.Equal("PRIVACY_ARTIFACT_EXPIRED",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(shortly,PrivacySession(h,async _=>await Task.Delay(2200))))).Message);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.audit WHERE artifact_id={shortly} AND kind='DOWNLOAD_AUTHORIZED'").SingleAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_tenant_export_v1.payloads SET bytes={new byte[]{1}} WHERE artifact_id={receipt.ArtifactId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_tenant_export_v1.artifacts SET recipient_id={Guid.NewGuid()} WHERE id={receipt.ArtifactId}"))).SqlState);
        var missing=await HistoricalTenantPrivacyArtifact(h,receipt,bytes,PrivacyReviewTime(60),false);Assert.Equal("PRIVACY_TENANT_PAYLOAD_UNAVAILABLE",(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>o.DownloadTenantAsync(missing,s))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_final_session_audit_and_requested_source_failures_roll_back_artifact_original_and_minimal_audit()
    {
        var readFault=new TenantPrivacyReadFailure();await using var h=await Seed(readFault);await TenantPrivacyAuthority(h);var o=PrivacyOwner(h);var s=PrivacySession(h);var input=TenantPrivacyCommand(h);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.GenerateTenantAsync(input,PrivacySession(h,_=>throw new InvalidOperationException("simulated logout before export commit"))));
        readFault.Armed=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>o.GenerateTenantAsync(input,s));Assert.False(readFault.Armed);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w322_fail_export_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'SIMULATED_TENANT_AUDIT_FAILURE';END $$;
            CREATE TRIGGER w322_fail_export_audit BEFORE INSERT ON cp6_privacy_tenant_export_v1.audit FOR EACH ROW EXECUTE FUNCTION public.w322_fail_export_audit();
            """);
        try{await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>o.GenerateTenantAsync(input,s));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w322_fail_export_audit ON cp6_privacy_tenant_export_v1.audit;DROP FUNCTION public.w322_fail_export_audit()");}
        Assert.Equal(0,await TenantPrivacyCount(h,"artifacts"));Assert.Equal(0,await TenantPrivacyCount(h,"commands"));Assert.Equal(0,await TenantPrivacyCount(h,"audit"));Assert.Equal("NOT_FOUND",(await o.TenantOperationAsync(input.OperationId,false,s)).State);
    }
    private sealed class TenantPrivacyReadFailure:DbCommandInterceptor
    {
        internal bool Armed;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {if(Armed&&command.CommandText.Contains("FROM \"Sys_SecurityLogs\"",StringComparison.Ordinal)){Armed=false;throw new InvalidOperationException("simulated classified tenant source unavailable");}return ValueTask.FromResult(result);}
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPrivacyExport_actual_COMMIT_reply_loss_keeps_exact_original_after_new_export_authority_is_removed()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var input=TenantPrivacyCommand(h);await using var wire=new PrivacyRestoreCommitReplyProxyFixture(h.Db.Database.GetConnectionString()!);
        await using var db=PrivacyRestoreNativeV1.Context(wire.Connection,h.Tenant);var actor=h with{Db=db};var error=await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(actor).GenerateTenantAsync(input,PrivacySession(actor,_=>{wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_OUTCOME_UNKNOWN",error.Message);await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-export").ExecuteDeleteAsync();
        var o=PrivacyOwner(h);var s=PrivacySession(h);var original=await o.TenantOperationAsync(input.OperationId,false,s);Assert.Equal("COMMITTED",original.State);Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await o.GenerateTenantAsync(input,s)));Assert.Equal(1,await TenantPrivacyCount(h,"artifacts"));
        Assert.NotEmpty((await o.DownloadTenantAsync(original.Receipt!.ArtifactId,s)).Bytes);
    }
}

using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] TenantPayloadActions=["tenant-payload-read","tenant-payload-policy","tenant-payload-prepare","tenant-payload-approve","tenant-payload-revoke","tenant-payload-execute"];
    private static async Task<(Guid Artifact,Guid Policy,Harness Approver)> TenantPayloadSetup(Harness h,bool revoke=true)
    {
        var approver=await TenantPrivacyRecipient(h);var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");
        foreach(var action in TenantPayloadActions){await EngineeringAction(h,"privacy-disposition",action);h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var artifact=(await PrivacyOwner(h).GenerateTenantAsync(TenantPrivacyCommand(h),PrivacySession(h))).Receipt!.ArtifactId;
        if(revoke)await PrivacyOwner(h).RevokeTenantAsync(artifact,PrivacySession(h));
        var policy=Guid.NewGuid();await PrivacyDispositionOwner(h).ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),policy,h.Tenant,null,Guid.NewGuid(),true,0,true),PrivacySession(h));
        return(artifact,policy,approver);
    }
    private static async Task<TenantPayloadDispositionPlanV1> TenantPayloadApproved(Harness h,(Guid Artifact,Guid Policy,Harness Approver) setup)
    {
        var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var target=new TenantPayloadDispositionTargetV1(setup.Artifact,setup.Policy);var preview=await o.TenantPayloadPreviewAsync(target,s);Assert.Empty(preview.Blockers);
        var requested=await o.RequestTenantPayloadDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),s);var id=requested.PlanId!.Value;
        var input=new TenantPayloadDispositionDecisionV1(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.DecideTenantPayloadDispositionAsync(id,input,s))).Status);
        await PrivacyDispositionOwner(setup.Approver).DecideTenantPayloadDispositionAsync(id,input,PrivacySession(setup.Approver));return await o.TenantPayloadPlanAsync(id,s);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_native_unowned_delete_is_rejected_without_losing_original_tenant_bytes()
    {
        await using var h=await Seed();await TenantPrivacyAuthority(h);var receipt=(await PrivacyOwner(h).GenerateTenantAsync(TenantPrivacyCommand(h),PrivacySession(h))).Receipt!;
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={receipt.ArtifactId}"))).SqlState);
        Assert.Equal(receipt.Length,(await PrivacyOwner(h).DownloadTenantAsync(receipt.ArtifactId,PrivacySession(h))).Bytes.LongLength);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_actual_delete_original_tomb_and_access_close_are_atomic_without_subject_identity_and_native_reinsert_is_fenced()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);
        var original=(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s)).Receipt;var input=new TenantPayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());var result=await o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,s);
        Assert.Equal(original.Length,result.Execution!.RemovedBytes);Assert.Equal("Main.Privacy.TenantExportPayload.v1",result.Execution.Owner);Assert.Equal("PARTIAL",result.Execution.AggregateState);Assert.DoesNotContain("SubjectId",JsonSerializer.Serialize(result));
        var after=await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s);Assert.Equal("DISPOSED",after.Availability);Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(after.Receipt));Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(h).DownloadTenantAsync(setup.Artifact,s))).Status);Assert.Equal("DISPOSED",(await PrivacyOwner(h).RevokeTenantAsync(setup.Artifact,s)).Availability);
        Assert.Equal(0,await TenantPrivacyCount(h,"payloads p JOIN cp6_privacy_tenant_export_v1.artifacts a ON a.id=p.artifact_id"));Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-payload-execute").ExecuteDeleteAsync();Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,s)));Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await o.TenantPayloadOperationAsync(input.OperationId,false,s)));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input with{ConfirmationRef=Guid.NewGuid()},s))).Status);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_privacy_tenant_export_v1.payloads"))).SqlState);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_current_policy_retention_access_and_independent_approver_authority_are_enforced()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h,false);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var target=new TenantPayloadDispositionTargetV1(setup.Artifact,setup.Policy);
        Assert.Contains("ACCESS_MUST_BE_CLOSED",(await o.TenantPayloadPreviewAsync(target,s)).Blockers);await PrivacyOwner(h).RevokeTenantAsync(setup.Artifact,s);
        var policy=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;var later=await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,1,true),s);Assert.Contains("MINIMUM_RETENTION_NOT_REACHED",(await o.TenantPayloadPreviewAsync(target,s)).Blockers);
        await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,later.Version,Guid.NewGuid(),true,0,true),s);var plan=await TenantPayloadApproved(h,setup);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="tenant-payload-approve").ExecuteDeleteAsync();var invalid=await o.TenantPayloadPlanAsync(plan.PlanId,s);Assert.Contains("APPROVER_AUTHORITY_CHANGED",invalid.CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s))).Status);
        var late=Guid.NewGuid();Assert.Equal("CLOSED",(await o.TenantPayloadOperationAsync(late,true,s)).State);var before=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;Assert.Equal("CLOSED",(await o.ConfigureTenantPayloadPolicyAsync(new(late,setup.Policy,h.Tenant,before.Version,Guid.NewGuid(),false,0,true),s)).State);Assert.True((await o.TenantPayloadPolicyAsync(setup.Policy,s))!.Enabled);
        await using var other=await Seed();await PrivacyAuthority(other);await EngineeringAction(other,"privacy-disposition","tenant-payload-read");Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(other).TenantPayloadPreviewAsync(target,PrivacySession(other)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_actual_COMMIT_reply_drop_recovers_exact_original_after_execution_authority_is_withdrawn()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var input=new TenantPayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(h.Db.Database.GetConnectionString()!);await using var db=PrivacyRestoreNativeV1.Context(wire.Connection,h.Tenant);var actor=h with{Db=db};
        var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(actor).ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,PrivacySession(actor,_=>{wire.Arm();return Task.CompletedTask;})));await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",error.Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="tenant-payload-execute").ExecuteDeleteAsync();var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var original=await o.TenantPayloadOperationAsync(input.OperationId,false,s);Assert.Equal("COMMITTED",original.State);Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,s)));Assert.Equal("DISPOSED",(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s)).Availability);
    }
}

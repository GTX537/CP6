using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_all_subject_holds_are_current_per_subject_cross_operator_and_overdue_and_ABA_invalidates_old_approval()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);
        await using var cross=await Seed();await PrivacyAuthority(cross);foreach(var action in new[]{"read","hold"})await EngineeringAction(cross,"privacy-disposition",action);var co=PrivacyDispositionOwner(cross);var cs=PrivacySession(cross);
        var held=await co.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,null,true,Guid.NewGuid(),PrivacyReviewTime(3)),cs);await Task.Delay(3300);
        var another=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Approver.User,null,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        var aggregate=await o.TenantPayloadHoldAsync(setup.Artifact,s);Assert.Equal(2,aggregate.ActiveCount);Assert.Equal(1,aggregate.OverdueCount);Assert.Equal(another.Version,aggregate.Version);
        var preview=await o.TenantPayloadPreviewAsync(new(setup.Artifact,setup.Policy),s);Assert.Contains("ACTIVE_HOLD",preview.Blockers);Assert.True(preview.HoldReviewOverdue);Assert.Contains("HOLD_VERSION_CHANGED",(await o.TenantPayloadPlanAsync(plan.PlanId,s)).CurrentBlockers);
        var raw=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}"));Assert.Equal("PRIVACY_TENANT_PAYLOAD_ACTIVE_HOLD",raw.MessageText);
        await co.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,held.Version,false,Guid.NewGuid(),null),cs);await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Approver.User,another.Version,false,Guid.NewGuid(),null),s);
        Assert.Equal(0,(await o.TenantPayloadHoldAsync(setup.Artifact,s)).ActiveCount);Assert.Contains("HOLD_VERSION_CHANGED",(await o.TenantPayloadPlanAsync(plan.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s))).Status);
        var crossHeld=await co.SetHoldAsync(new(Guid.NewGuid(),cross.Tenant,cross.User,null,true,Guid.NewGuid(),PrivacyReviewTime()),cs);Assert.Equal(0,(await o.TenantPayloadHoldAsync(setup.Artifact,s)).ActiveCount);
        var next=await TenantPayloadApproved(h,setup);await o.ExecuteTenantPayloadDispositionAsync(next.PlanId,new(Guid.NewGuid(),next.Version,next.ScopeDigest,Guid.NewGuid()),s);Assert.Equal("DISPOSED",(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s)).Availability);
        await co.SetHoldAsync(new(Guid.NewGuid(),cross.Tenant,cross.User,crossHeld.Version,false,Guid.NewGuid(),null),cs);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_policy_ABA_and_target_change_do_not_restore_old_approval_or_rewrite_historical_scope()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var policy=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;
        var off=await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),false,0,true),s);await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,off.Version,policy.BasisRef,true,0,true),s);
        Assert.Contains("POLICY_VERSION_CHANGED",(await o.TenantPayloadPlanAsync(plan.PlanId,s)).CurrentBlockers);Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s))).Status);
        await using var other=await Seed();var now=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,other.Tenant,now.Version,Guid.NewGuid(),true,0,true),s);var view=await o.TenantPayloadPlanAsync(plan.PlanId,s);Assert.Contains("POLICY_TARGET_CHANGED",view.CurrentBlockers);Assert.Equal(h.Tenant,view.Scope.TargetTenantId);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.TenantPayloadPreviewAsync(new(setup.Artifact,setup.Policy),s))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_final_session_or_original_audit_failure_rolls_back_bytes_tomb_execution_and_restore_epoch()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var input=new TenantPayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());var epoch=await PrivacyRestoreNativeV1.Epoch(h.Db,default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,PrivacySession(h,_=>throw new InvalidOperationException("simulated logout before deletion commit"))));
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w323_fail_original() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'SIMULATED_TENANT_PAYLOAD_ORIGINAL_FAILURE';END $$;
            CREATE TRIGGER w323_fail_original BEFORE INSERT ON cp6_main_owner_v1.operations FOR EACH ROW EXECUTE FUNCTION public.w323_fail_original();
            """);
        try{await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,s));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w323_fail_original ON cp6_main_owner_v1.operations;DROP FUNCTION public.w323_fail_original()");}
        Assert.Equal(epoch,await PrivacyRestoreNativeV1.Epoch(h.Db,default));Assert.Null((await o.TenantPayloadPlanAsync(plan.PlanId,s)).Execution);Assert.Equal("NOT_FOUND",(await o.TenantPayloadOperationAsync(input.OperationId,false,s)).State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_native_orphan_execution_or_tomb_cannot_commit_without_actual_bytes_and_original()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);var plan=await TenantPayloadApproved(h,setup);var scope=plan.Scope;var operation=Guid.NewGuid();
        var receipt=new TenantPayloadDispositionExecutionV1(plan.PlanId,operation,Guid.NewGuid(),"Main.Privacy.TenantExportPayload.v1","APPLIED","PARTIAL",ScopeEnvironment,h.Tenant,h.User,h.User,setup.Approver.User,setup.Artifact,h.Tenant,scope.PolicyId,scope.PolicyVersion,scope.PayloadVersion!.Value,scope.PayloadLength!.Value,plan.ScopeDigest,PrivacyReviewTime(0),["WAL_AND_BACKUPS"]);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_payload_disposition_v1.executions(plan_id,approval_version,artifact_id,environment_id,actor_tenant,operator_id,operation_id,scope_json,receipt_json)
                VALUES({plan.PlanId},{plan.Version},{setup.Artifact},{ScopeEnvironment},{h.Tenant},{h.User},{operation},{JsonSerializer.Serialize(scope)},{JsonSerializer.Serialize(receipt)})
                """);Assert.Equal("PRIVACY_TENANT_PAYLOAD_EFFECT_AND_RECEIPT_REQUIRED",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync())).MessageText);
        }
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_tenant_payload_disposition_v1.tombstones(artifact_id,source_plan_id,environment_id,operator_tenant,target_tenant,policy_id,scope_json,receipt_json)
            VALUES({setup.Artifact},{plan.PlanId},{ScopeEnvironment},{h.Tenant},{h.Tenant},{scope.PolicyId},{JsonSerializer.Serialize(scope)},{JsonSerializer.Serialize(receipt)})
            """))).SqlState);Assert.Null((await PrivacyDispositionOwner(h).TenantPayloadPlanAsync(plan.PlanId,PrivacySession(h))).Execution);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadDisposition_silently_skipped_native_delete_is_known_commit_rejection_and_restores_access_without_false_original()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h,false);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var policy=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;
        await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,0,false),s);var plan=await TenantPayloadApproved(h,setup);var input=new TenantPayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w323_skip_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL;END $$;
            CREATE TRIGGER a_w323_skip_delete BEFORE DELETE ON cp6_privacy_tenant_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION public.w323_skip_delete();
            """);
        try{var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteTenantPayloadDispositionAsync(plan.PlanId,input,s));Assert.Equal(409,error.Status);Assert.Equal("PRIVACY_TENANT_PAYLOAD_EFFECT_AND_RECEIPT_REQUIRED",error.Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER a_w323_skip_delete ON cp6_privacy_tenant_export_v1.payloads;DROP FUNCTION public.w323_skip_delete()");}
        Assert.Equal("AVAILABLE",(await PrivacyOwner(h).TenantArtifactAsync(setup.Artifact,s)).Availability);Assert.NotEmpty((await PrivacyOwner(h).DownloadTenantAsync(setup.Artifact,s)).Bytes);Assert.Null((await o.TenantPayloadPlanAsync(plan.PlanId,s)).Execution);Assert.Equal("NOT_FOUND",(await o.TenantPayloadOperationAsync(input.OperationId,false,s)).State);
    }
}

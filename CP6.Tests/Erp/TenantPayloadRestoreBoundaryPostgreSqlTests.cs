using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task TenantPayloadDispose(Harness h,(Guid Artifact,Guid Policy,Harness Approver) setup)
    {var plan=await TenantPayloadApproved(h,setup);await PrivacyDispositionOwner(h).ExecuteTenantPayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),PrivacySession(h));}
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_original_receipt_target_identity_corruption_is_rejected_even_when_payload_hash_and_native_row_match()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_export_v1.artifacts DISABLE TRIGGER artifact_change");
        try{await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_tenant_export_v1.artifacts SET receipt_bytes=convert_to(jsonb_set(convert_from(receipt_bytes,'UTF8')::jsonb,ARRAY['TargetTenantId'],to_jsonb({Guid.NewGuid()}))::text,'UTF8') WHERE id={setup.Artifact}");}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_export_v1.artifacts ENABLE TRIGGER artifact_change");}
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var restore=PrivacyRestorer(h,options,runtime);
        Assert.Equal("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h)))).Message);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());Assert.Equal("CLOSED",(await restore.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(h))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_same_revision_and_length_but_corrupt_native_bytes_are_rejected_before_effect_or_admission()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_export_v1.payloads DISABLE TRIGGER payload_no_rewrite");
        try{await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_tenant_export_v1.payloads SET bytes=set_byte(bytes,0,CASE get_byte(bytes,0) WHEN 123 THEN 124 ELSE 123 END) WHERE artifact_id={setup.Artifact}");}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_tenant_export_v1.payloads ENABLE TRIGGER payload_no_rewrite");}
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var restore=PrivacyRestorer(h,options,runtime);
        Assert.Equal("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h)))).Message);
        Assert.Equal("PRIVACY_RESTORE_QUARANTINED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Message);
        Assert.Equal("CLOSED",(await restore.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(h))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_orphan_bytes_without_metadata_cannot_be_reported_as_absent_or_zero_removed()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await target.Database.OpenConnectionAsync();await target.Database.ExecuteSqlRawAsync("SET session_replication_role=replica");
        try{await target.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_tenant_export_v1.artifacts WHERE id={setup.Artifact}");}
        finally{await target.Database.ExecuteSqlRawAsync("SET session_replication_role=origin");}
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var restore=PrivacyRestorer(h,options,runtime);
        Assert.Equal("PRIVACY_TENANT_PAYLOAD_RESTORE_IDENTITY_CHANGED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h)))).Message);Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("CLOSED",(await restore.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(h))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_existing_metadata_without_bytes_records_payload_absent_zero_and_installs_same_origin_fence()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await target.Database.OpenConnectionAsync();await target.Database.ExecuteSqlRawAsync("SET session_replication_role=replica");
        try{await target.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}");}
        finally{await target.Database.ExecuteSqlRawAsync("SET session_replication_role=origin");}
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var result=await PrivacyRestorer(h,options,runtime).ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),PrivacySession(h));var unit=Assert.Single(result.TenantPayloadUnits!,x=>x.ArtifactId==setup.Artifact);
        Assert.Equal("ABSENT_FENCED",unit.Outcome);Assert.Equal("PAYLOAD_ABSENT",unit.BeforeState);Assert.Equal(0,unit.RemovedBytes);Assert.Null(unit.BeforeVersion);Assert.True(await target.Database.SqlQuery<bool>($"SELECT revoked_at IS NOT NULL AS \"Value\" FROM cp6_privacy_tenant_export_v1.artifacts WHERE id={setup.Artifact}").SingleAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})"))).SqlState);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_missing_candidate_migration_or_disabled_native_effect_fence_fails_503_before_approval()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);var restore=PrivacyRestorer(h,RestoreRegistration(candidate),await RestoreRuntime(h));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await target.Database.ExecuteSqlRawAsync("ALTER FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_ready() RENAME TO w324_unavailable_ready");
        try{var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.RestorePreviewAsync(candidate.Id,PrivacySession(h)));Assert.Equal(503,error.Status);Assert.Equal("PRIVACY_TENANT_PAYLOAD_RESTORE_BOUNDARY_UNAVAILABLE",error.Message);}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER FUNCTION cp6_privacy_tenant_payload_disposition_v1.w324_unavailable_ready() RENAME TO restore_ready");}
        await target.Database.ExecuteSqlRawAsync("ALTER FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_owns(uuid,uuid,uuid,bigint) RENAME TO w324_unavailable_origin");
        try{Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.RestorePreviewAsync(candidate.Id,PrivacySession(h)))).Status);}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER FUNCTION cp6_privacy_tenant_payload_disposition_v1.w324_unavailable_origin(uuid,uuid,uuid,bigint) RENAME TO restore_owns");}
        await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.restore_receipts DISABLE TRIGGER tenant_payload_restore_complete");
        try{Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>restore.RestorePreviewAsync(candidate.Id,PrivacySession(h)))).Status);}
        finally{await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.restore_receipts ENABLE TRIGGER tenant_payload_restore_complete");}
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_current_source_policy_and_all_subject_hold_block_and_ABA_stales_approval_while_candidate_old_hold_is_not_authority()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var held=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,null,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);var released=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,held.Version,false,Guid.NewGuid(),null),s);await TenantPayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var restore=PrivacyRestorer(h,options,runtime);var policy=(await o.TenantPayloadPolicyAsync(setup.Policy,s))!;
        var disabled=await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),false,0,true),s);Assert.Contains((await restore.RestorePreviewAsync(candidate.Id,s)).Blockers,x=>x.StartsWith("CURRENT_TENANT_PAYLOAD_POLICY_DISABLED:"));
        var later=await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,disabled.Version,Guid.NewGuid(),true,1,true),s);Assert.Contains((await restore.RestorePreviewAsync(candidate.Id,s)).Blockers,x=>x.StartsWith("CURRENT_TENANT_PAYLOAD_MINIMUM_RETENTION_NOT_REACHED:"));
        await o.ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,later.Version,Guid.NewGuid(),true,0,true),s);var old=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var again=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,released.Version,true,Guid.NewGuid(),PrivacyReviewTime()),s);Assert.Contains((await restore.RestorePreviewAsync(candidate.Id,s)).Blockers,x=>x.StartsWith("CURRENT_TENANT_PAYLOAD_HOLD_REQUIRES_RESTORE_DECISION:"));await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,h.User,again.Version,false,Guid.NewGuid(),null),s);
        Assert.Contains("SOURCE_VERSION_CHANGED",(await restore.RestorePlanAsync(old.PlanId,s)).CurrentBlockers);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var result=await restore.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s);Assert.Equal("REAPPLIED",Assert.Single(result.TenantPayloadUnits!,x=>x.ArtifactId==setup.Artifact).Outcome);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);Assert.True(await target.Database.SqlQuery<bool>($"SELECT active AS \"Value\" FROM cp6_privacy_disposition_v1.holds WHERE target_tenant={h.Tenant} AND subject_id={h.User} ORDER BY sequence DESC LIMIT 1").SingleAsync());
    }
}

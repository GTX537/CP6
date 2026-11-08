using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_actual_source_COMMIT_reply_drop_reads_already_saved_original_without_reapplying_candidate_effect()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(h.Db.Database.GetConnectionString()!);await using var db=PrivacyRestoreNativeV1.Context(wire.Connection,h.Tenant,runtime);var actor=h with{Db=db};var calls=0;
        var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestorer(actor,options,runtime).ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor,_=>{if(++calls==2)wire.Arm();return Task.CompletedTask;})));await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",error.Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();var owner=PrivacyRestorer(h,options,runtime);var original=await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(h));Assert.Equal("APPLIED",original.State);Assert.Equal("REAPPLIED",Assert.Single(original.TenantPayloadUnits!,x=>x.ArtifactId==setup.Artifact).Outcome);
        Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await owner.RestoreOperationAsync(input.OperationId,"RECOVER",PrivacySession(h))));await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        await using(var admission=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admission);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_actual_candidate_COMMIT_reply_loss_keeps_both_database_responsibilities_and_recovers_original()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(candidate.Connection);var options=RestoreRegistration(candidate);options.Candidates[candidate.Id].ConnectionString=wire.Connection;
        var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_RESTORE_CANDIDATE_OUTCOME_UNKNOWN",unknown.Message);
        await AssertTenantPayloadCandidateOriginal(h,candidate,setup.Artifact,input.OperationId,owner,session);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_source_commit_unknown_after_candidate_commit_keeps_quarantine_until_source_original_is_saved()
    {
        var fault=new MissingRestoreSourceCommit();await using var h=await Seed(fault);var setup=await TenantPayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)fault.Armed=true;return Task.CompletedTask;})));
        Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",unknown.Message);await AssertTenantPayloadCandidateOriginal(h,candidate,setup.Artifact,input.OperationId,owner,session);
    }
    private static async Task AssertTenantPayloadCandidateOriginal(Harness h,PrivacyRestoreCandidateFixture candidate,Guid artifact,Guid operation,PrivacyDispositionOwnerV1 owner,TenantManagementSessionV1 session)
    {
        var pending=await owner.RestoreOperationAsync(operation,"READ",session);Assert.Equal("AWAITING_SOURCE_RESULT",pending.State);
        var unit=Assert.Single(pending.TenantPayloadUnits!,x=>x.ArtifactId==artifact);Assert.Equal("REAPPLIED",unit.Outcome);Assert.True(unit.RemovedBytes>0);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=h.Db.Database.GetConnectionString()!};
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={artifact}").SingleAsync());
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={artifact}").SingleAsync());
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        var recovered=await owner.RestoreOperationAsync(operation,"RECOVER",session);Assert.Equal(JsonSerializer.Serialize(pending with{State="APPLIED"}),JsonSerializer.Serialize(recovered));
        Assert.Equal(JsonSerializer.Serialize(recovered),JsonSerializer.Serialize(await owner.RestoreOperationAsync(operation,"RECOVER",session)));
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(admitted);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_final_session_failure_rolls_back_actual_payload_effects_and_parent_receipt_before_close()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h,false);var policy=(await PrivacyDispositionOwner(h).TenantPayloadPolicyAsync(setup.Policy,PrivacySession(h)))!;
        await PrivacyDispositionOwner(h).ConfigureTenantPayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,0,false),PrivacySession(h));
        await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)throw new InvalidOperationException("simulated logout after candidate effects");return Task.CompletedTask;})));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.False(await target.Database.SqlQuery<bool>($"SELECT revoked_at IS NOT NULL AS \"Value\" FROM cp6_privacy_tenant_export_v1.artifacts WHERE id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",session)).State);Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",session)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TenantPayloadRestore_silently_skipped_native_delete_is_rejected_at_actual_candidate_commit_without_publishing_source_success()
    {
        await using var h=await Seed();var setup=await TenantPayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await TenantPayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await target.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w324_skip_restore_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL;END $$;
            CREATE TRIGGER zz_w324_skip_restore_delete BEFORE DELETE ON cp6_privacy_tenant_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION public.w324_skip_restore_delete();
            """);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        try
        {
            var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,session));
            Assert.Equal("PRIVACY_TENANT_PAYLOAD_RESTORE_EFFECT_REQUIRED",error.Message);Assert.Equal(409,error.Status);
        }
        finally{await target.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_w324_skip_restore_delete ON cp6_privacy_tenant_export_v1.payloads;DROP FUNCTION public.w324_skip_restore_delete()");}
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",session)).State);Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",session)).State);
    }}

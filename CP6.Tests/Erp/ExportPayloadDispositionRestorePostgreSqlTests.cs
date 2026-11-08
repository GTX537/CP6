using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<PayloadDispositionOperationV1> PayloadDispose(Harness h,(Guid Artifact,Guid Policy,Harness Approver) setup)
    {
        var plan=await PayloadApproved(h,setup);return await PrivacyDispositionOwner(h).ExecutePayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),PrivacySession(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_candidate_same_revision_and_length_but_corrupt_bytes_is_rejected_before_effect_or_admission()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await using(var fault=await target.Database.BeginTransactionAsync())
        {
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads DISABLE TRIGGER payload_no_rewrite");
            await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_export_v1.payloads SET bytes=convert_to(repeat('x',octet_length(bytes)),'UTF8') WHERE artifact_id={setup.Artifact}");
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads ENABLE TRIGGER payload_no_rewrite");await fault.CommitAsync();
        }
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);
        var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        Assert.Equal("PRIVACY_EXPORT_PAYLOAD_RESTORE_IDENTITY_CHANGED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,session))).Message);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",session)).State);
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Status);
        Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",session)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_actual_candidate_COMMIT_reply_loss_keeps_both_database_responsibilities_and_recovers_original()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(candidate.Connection);var options=RestoreRegistration(candidate);options.Candidates[candidate.Id].ConnectionString=wire.Connection;
        var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_RESTORE_CANDIDATE_OUTCOME_UNKNOWN",unknown.Message);
        await AssertPayloadCandidateOriginal(h,candidate,setup.Artifact,input.OperationId,owner,session);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_source_commit_unknown_after_candidate_commit_keeps_quarantine_until_source_original_is_saved()
    {
        var fault=new MissingRestoreSourceCommit();await using var h=await Seed(fault);var setup=await PayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)fault.Armed=true;return Task.CompletedTask;})));
        Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",unknown.Message);await AssertPayloadCandidateOriginal(h,candidate,setup.Artifact,input.OperationId,owner,session);
    }
    private static async Task AssertPayloadCandidateOriginal(Harness h,PrivacyRestoreCandidateFixture candidate,Guid artifact,Guid operation,PrivacyDispositionOwnerV1 owner,TenantManagementSessionV1 session)
    {
        var pending=await owner.RestoreOperationAsync(operation,"READ",session);Assert.Equal("AWAITING_SOURCE_RESULT",pending.State);
        var unit=Assert.Single(pending.Units!,x=>x.ArtifactId==artifact);Assert.Equal("REAPPLIED",unit.Outcome);Assert.True(unit.RemovedBytes>0);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=h.Db.Database.GetConnectionString()!};
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={artifact}").SingleAsync());
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={artifact}").SingleAsync());
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        var recovered=await owner.RestoreOperationAsync(operation,"RECOVER",session);Assert.Equal(JsonSerializer.Serialize(pending with{State="APPLIED"}),JsonSerializer.Serialize(recovered));
        Assert.Equal(JsonSerializer.Serialize(recovered),JsonSerializer.Serialize(await owner.RestoreOperationAsync(operation,"RECOVER",session)));
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(admitted);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_final_session_failure_rolls_back_actual_payload_effects_and_parent_receipt_before_close()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h,false);var policy=(await PrivacyDispositionOwner(h).PayloadPolicyAsync(setup.Policy,PrivacySession(h)))!;
        await PrivacyDispositionOwner(h).ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,0,false),PrivacySession(h));
        await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)throw new InvalidOperationException("simulated logout after candidate effects");return Task.CompletedTask;})));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.False(await target.Database.SqlQuery<bool>($"SELECT revoked_at IS NOT NULL AS \"Value\" FROM cp6_privacy_export_v1.artifacts WHERE id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",session)).State);Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",session)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_existing_artifact_with_missing_payload_records_zero_removed_bytes_and_installs_the_same_tomb()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await using(var fault=await target.Database.BeginTransactionAsync())
        {
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads DISABLE TRIGGER payload_owned_delete");
            await target.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}");
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads ENABLE TRIGGER payload_owned_delete");await fault.CommitAsync();
        }
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var result=await owner.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),session);var unit=Assert.Single(result.Units!,x=>x.ArtifactId==setup.Artifact);
        Assert.Equal("ABSENT_FENCED",unit.Outcome);Assert.Equal(0,unit.RemovedBytes);Assert.Null(unit.BeforeVersion);
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admitted);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_silently_skipped_native_delete_is_rejected_at_actual_candidate_commit_without_publishing_source_success()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await PayloadDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await target.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w320_skip_restore_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL;END $$;
            CREATE TRIGGER zz_w320_skip_restore_delete BEFORE DELETE ON cp6_privacy_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION public.w320_skip_restore_delete();
            """);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var session=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        try
        {
            var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,session));
            Assert.Equal("PRIVACY_EXPORT_PAYLOAD_RESTORE_EFFECT_REQUIRED",error.Message);Assert.Equal(409,error.Status);
        }
        finally{await target.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_w320_skip_restore_delete ON cp6_privacy_export_v1.payloads;DROP FUNCTION public.w320_skip_restore_delete()");}
        Assert.Equal(1,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",session)).State);Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",session)).State);
    }
}

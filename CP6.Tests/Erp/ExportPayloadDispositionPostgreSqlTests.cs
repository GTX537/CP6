using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] PayloadActions=["payload-read","payload-policy","payload-prepare","payload-approve","payload-revoke","payload-execute"];
    private static async Task<(Guid Artifact,Guid Policy,Harness Approver)> PayloadSetup(Harness h,bool revoke=true)
    {
        var setup=await PrivacyDispositionSetup(h);var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");
        foreach(var action in PayloadActions)
        {await EngineeringAction(h,"privacy-disposition",action);h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var artifact=(await PrivacyOwner(h).GenerateAsync(PrivacyCommand(h) with{SubjectId=setup.Target,Sources=["identity"]},PrivacySession(h))).Receipt!.ArtifactId;
        if(revoke)await PrivacyOwner(h).RevokeAsync(artifact,PrivacySession(h));
        var policy=Guid.NewGuid();await PrivacyDispositionOwner(h).ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),policy,h.Tenant,null,Guid.NewGuid(),true,0,true),PrivacySession(h));
        return(artifact,policy,setup.Approver);
    }
    private static async Task<PayloadDispositionPlanV1> PayloadApproved(Harness h,(Guid Artifact,Guid Policy,Harness Approver) setup)
    {
        var owner=PrivacyDispositionOwner(h);var session=PrivacySession(h);var target=new PayloadDispositionTargetV1(setup.Artifact,setup.Policy);
        var preview=await owner.PayloadPreviewAsync(target,session);Assert.Empty(preview.Blockers);
        var requested=await owner.RequestPayloadDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),session);
        var id=requested.PlanId!.Value;var decision=new PayloadDispositionDecisionV1(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.DecidePayloadDispositionAsync(id,decision,session))).Status);
        await PrivacyDispositionOwner(setup.Approver).DecidePayloadDispositionAsync(id,decision,PrivacySession(setup.Approver));
        return await owner.PayloadPlanAsync(id,session);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_actual_delete_receipt_tomb_and_original_commit_together_and_raw_reinsert_is_fenced()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var owner=PrivacyDispositionOwner(h);var session=PrivacySession(h);var plan=await PayloadApproved(h,setup);
        var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        var result=await owner.ExecutePayloadDispositionAsync(plan.PlanId,input,session);Assert.Equal("APPLIED",result.Execution!.OwnerOutcome);Assert.True(result.Execution.RemovedBytes>0);
        Assert.Equal("PARTIAL",result.Execution.AggregateState);Assert.Equal(setup.Artifact,result.Execution.ArtifactId);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal("DISPOSED",(await PrivacyOwner(h).ReadArtifactAsync(setup.Artifact,session)).Availability);
        var current=await owner.PayloadPlanAsync(plan.PlanId,session);Assert.DoesNotContain("PAYLOAD_MISSING",current.CurrentBlockers);Assert.DoesNotContain("PAYLOAD_VERSION_CHANGED",current.CurrentBlockers);
        var duplicate=input with{OperationId=Guid.NewGuid()};Assert.Equal("PRIVACY_EXPORT_PAYLOAD_ALREADY_APPLIED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecutePayloadDispositionAsync(plan.PlanId,duplicate,session))).Message);
        Assert.Equal("NOT_FOUND",(await owner.PayloadOperationAsync(duplicate.OperationId,false,session)).State);
        Assert.Equal(410,(await Assert.ThrowsAsync<PrivacyExportExceptionV1>(()=>PrivacyOwner(h).DownloadAsync(setup.Artifact,session))).Status);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="payload-execute").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await owner.ExecutePayloadDispositionAsync(plan.PlanId,input,session)));
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await owner.PayloadOperationAsync(input.OperationId,false,session)));
        await using var other=await Seed();var policy=(await owner.PayloadPolicyAsync(setup.Policy,session))!;
        var moved=await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,other.Tenant,policy.Version,Guid.NewGuid(),true,0,true),session);
        var history=await owner.PayloadPlanAsync(plan.PlanId,session);Assert.Contains("POLICY_TARGET_CHANGED",history.CurrentBlockers);Assert.Equal(JsonSerializer.Serialize(result.Execution),JsonSerializer.Serialize(history.Execution));Assert.Equal(h.Tenant,history.Scope.TargetTenantId);
        await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,moved.Version,Guid.NewGuid(),true,0,true),session);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})"));
        Assert.Equal("23514",error.SqlState);Assert.Contains("DISPOSED_IDENTITY",error.MessageText);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_restore_reapplies_native_backup_payload_and_fences_artifacts_absent_from_backup()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h,false);var owner=PrivacyDispositionOwner(h);var session=PrivacySession(h);
        var policy=(await owner.PayloadPolicyAsync(setup.Policy,session))!;
        await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true,0,false),session);
        await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var first=await PayloadApproved(h,setup);await owner.ExecutePayloadDispositionAsync(first.PlanId,new(Guid.NewGuid(),first.Version,first.ScopeDigest,Guid.NewGuid()),session);
        var lateArtifact=(await PrivacyOwner(h).GenerateAsync(PrivacyCommand(h) with{SubjectId=first.Scope.SubjectId,Sources=["identity"]},session)).Receipt!.ArtifactId;
        var second=await PayloadApproved(h,(lateArtifact,setup.Policy,setup.Approver));
        await owner.ExecutePayloadDispositionAsync(second.PlanId,new(Guid.NewGuid(),second.Version,second.ScopeDigest,Guid.NewGuid()),session);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var restore=PrivacyRestorer(h,options,runtime);
        var preview=await restore.RestorePreviewAsync(candidate.Id,session);Assert.True(preview.Scope.ExportPayloadTombstoneCount>=2);
        var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var result=await restore.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),session);
        var actual=Assert.Single(result.Units!,x=>x.ArtifactId==setup.Artifact);Assert.Equal("Main.Privacy.ExportPayload.v1",actual.Owner);Assert.Equal("REAPPLIED",actual.Outcome);Assert.True(actual.RemovedBytes>0);
        Assert.Equal("ABSENT_FENCED",Assert.Single(result.Units!,x=>x.ArtifactId==lateArtifact).Outcome);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.True(await target.Database.SqlQuery<bool>($"SELECT revoked_at IS NOT NULL AS \"Value\" FROM cp6_privacy_export_v1.artifacts WHERE id={setup.Artifact}").SingleAsync());
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admitted);
        var sourceVersion=(await restore.RestoreContextAsync(session)).SourceVersion;
        var afterAdmission=(await PrivacyOwner(h).GenerateAsync(PrivacyCommand(h) with{SubjectId=first.Scope.SubjectId,Sources=["identity"]},session)).Receipt!.ArtifactId;
        Assert.Equal(sourceVersion,(await restore.RestoreContextAsync(session)).SourceVersion);
        await PayloadDispose(h,(afterAdmission,setup.Policy,setup.Approver));Assert.NotEqual(sourceVersion,(await restore.RestoreContextAsync(session)).SourceVersion);
        Assert.Equal("PRIVACY_RESTORE_SOURCE_PROOF_NOT_CURRENT",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Message);
        var lateReceipt=(await PrivacyOwner(h).ReadArtifactAsync(lateArtifact,session)).Receipt;var raw=JsonSerializer.SerializeToUtf8Bytes(lateReceipt);
        await target.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_export_v1.artifacts(id,environment_id,actor_tenant,actor_id,recipient_id,target_tenant,subject_id,policy,expires_at,receipt_bytes)
            VALUES({lateArtifact},{ScopeEnvironment},{h.Tenant},{h.User},{h.User},{h.Tenant},{first.Scope.SubjectId},{PrivacyExportOwnerV1.Policy},{lateReceipt.ExpiresAt},{raw})
            """);
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_export_v1.payloads(artifact_id,bytes) VALUES({lateArtifact},{new byte[]{1}})"));Assert.Contains("DISPOSED_IDENTITY",error.MessageText);
        var held=await owner.SetPayloadHoldAsync(setup.Artifact,new(Guid.NewGuid(),null,true,Guid.NewGuid(),PrivacyReviewTime()),session);
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Status);
        Assert.Contains("CURRENT_PAYLOAD_HOLD_REQUIRES_RESTORE_DECISION:"+first.PlanId.ToString("D"),(await restore.RestorePreviewAsync(candidate.Id,session)).Blockers);
        await owner.SetPayloadHoldAsync(setup.Artifact,new(Guid.NewGuid(),held.Version,false,Guid.NewGuid(),null),session);
        var currentPolicy=(await owner.PayloadPolicyAsync(setup.Policy,session))!;
        var disabled=await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,currentPolicy.Version,Guid.NewGuid(),false,0,false),session);
        Assert.Contains("CURRENT_PAYLOAD_POLICY_DISABLED:"+first.PlanId.ToString("D"),(await restore.RestorePreviewAsync(candidate.Id,session)).Blockers);
        var retained=await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,disabled.Version,Guid.NewGuid(),true,7,false),session);
        Assert.Contains("CURRENT_PAYLOAD_MINIMUM_RETENTION_NOT_REACHED:"+first.PlanId.ToString("D"),(await restore.RestorePreviewAsync(candidate.Id,session)).Blockers);
        await using var other=await Seed();var retargeted=await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,other.Tenant,retained.Version,Guid.NewGuid(),true,0,false),session);
        Assert.Contains("CURRENT_PAYLOAD_POLICY_TARGET_CHANGED:"+first.PlanId.ToString("D"),(await restore.RestorePreviewAsync(candidate.Id,session)).Blockers);
        await owner.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,retargeted.Version,Guid.NewGuid(),true,0,false),session);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_explicit_UTC_retention_access_policy_and_policy_CAS_prevent_premature_delete()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h,false);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var target=new PayloadDispositionTargetV1(setup.Artifact,setup.Policy);
        var p=(await o.PayloadPolicyAsync(setup.Policy,s))!;
        Assert.Equal(400,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,p.Version,Guid.NewGuid(),true,36501,true),s))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,null,Guid.NewGuid(),true,0,true),s))).Status);
        var updated=await o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,p.Version,Guid.NewGuid(),true,7,true),s);
        var preview=await o.PayloadPreviewAsync(target,s);Assert.Equal(TimeSpan.FromSeconds(7*86400),preview.Scope.EarliestDispositionAt-preview.Scope.CapturedAt);
        Assert.Contains("MINIMUM_RETENTION_NOT_REACHED",preview.Blockers);Assert.Contains("ACCESS_MUST_BE_CLOSED",preview.Blockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.RequestPayloadDispositionAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),s))).Status);
        await o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,updated.Version,Guid.NewGuid(),true,0,false),s);
        var plan=await PayloadApproved(h,setup);Assert.Equal("AVAILABLE",plan.AccessState);var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,(await o.PayloadPolicyAsync(setup.Policy,s))!.Version,Guid.NewGuid(),true,0,true),s);
        Assert.Contains("POLICY_VERSION_CHANGED",(await o.PayloadPlanAsync(plan.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecutePayloadDispositionAsync(plan.PlanId,input,s))).Status);
        Assert.Equal("NOT_FOUND",(await o.PayloadOperationAsync(input.OperationId,false,s)).State);
        await PrivacyOwner(h).RevokeAsync(setup.Artifact,s);var fresh=await PayloadApproved(h,setup);
        await o.ExecutePayloadDispositionAsync(fresh.PlanId,new(Guid.NewGuid(),fresh.Version,fresh.ScopeDigest,Guid.NewGuid()),s);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_global_hold_ABA_approver_revocation_and_deleted_subject_are_current_authoritative_boundaries()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var plan=await PayloadApproved(h,setup);
        await using var other=await Seed();await PrivacyDispositionSetup(other);var holdOwner=PrivacyDispositionOwner(other);var hs=PrivacySession(other);
        var held=await holdOwner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,plan.Scope.SubjectId,null,true,Guid.NewGuid(),PrivacyReviewTime(1)),hs);
        await Task.Delay(1200);var preview=await o.PayloadPreviewAsync(new(setup.Artifact,setup.Policy),s);Assert.True(preview.HoldReviewOverdue);Assert.Equal(other.Tenant,preview.Hold!.OwnerTenantId);Assert.Contains("ACTIVE_HOLD",preview.Blockers);
        await holdOwner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,plan.Scope.SubjectId,held.Version,false,Guid.NewGuid(),null),hs);
        Assert.Contains("HOLD_VERSION_CHANGED",(await o.PayloadPlanAsync(plan.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecutePayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s))).Status);
        var next=await PayloadApproved(h,setup);await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="payload-approve").ExecuteDeleteAsync();
        Assert.Contains("APPROVER_AUTHORITY_CHANGED",(await o.PayloadPlanAsync(next.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecutePayloadDispositionAsync(next.PlanId,new(Guid.NewGuid(),next.Version,next.ScopeDigest,Guid.NewGuid()),s))).Status);
        await h.Db.Sys_Users.Where(x=>x.Id==plan.Scope.SubjectId).ExecuteDeleteAsync();
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,plan.Scope.SubjectId,preview.Hold.Version,true,Guid.NewGuid(),PrivacyReviewTime()),s))).Status);
        var previous=await o.PayloadHoldAsync(setup.Artifact,s);var current=await o.SetPayloadHoldAsync(setup.Artifact,new(Guid.NewGuid(),previous!.Version,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        Assert.Contains("ACTIVE_HOLD",(await o.PayloadPreviewAsync(new(setup.Artifact,setup.Policy),s)).Blockers);
        await o.SetPayloadHoldAsync(setup.Artifact,new(Guid.NewGuid(),current.Version,false,Guid.NewGuid(),null),s);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_final_session_and_journal_failure_roll_back_bytes_access_tomb_and_receipt_and_close_fences_late_execute()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h,false);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var p=(await o.PayloadPolicyAsync(setup.Policy,s))!;
        await o.ConfigurePayloadPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,p.Version,Guid.NewGuid(),true,0,false),s);var plan=await PayloadApproved(h,setup);var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.ExecutePayloadDispositionAsync(plan.PlanId,input,PrivacySession(h,_=>throw new InvalidOperationException("simulated logout"))));
        Assert.Equal("AVAILABLE",(await PrivacyOwner(h).ReadArtifactAsync(setup.Artifact,s)).Availability);Assert.Equal("NOT_FOUND",(await o.PayloadOperationAsync(input.OperationId,false,s)).State);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w320_fail_payload_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.owner LIKE 'Main.Privacy.ExportPayload.v1/%' THEN RAISE EXCEPTION 'SIMULATED_PAYLOAD_JOURNAL_FAILURE';END IF;RETURN NEW;END $$;
            CREATE TRIGGER w320_fail_payload_journal BEFORE INSERT ON cp6_main_owner_v1.operations FOR EACH ROW EXECUTE FUNCTION public.w320_fail_payload_journal();
            """);
        try{await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>o.ExecutePayloadDispositionAsync(plan.PlanId,input,s));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w320_fail_payload_journal ON cp6_main_owner_v1.operations;DROP FUNCTION public.w320_fail_payload_journal()");}
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Null((await o.PayloadPlanAsync(plan.PlanId,s)).Execution);Assert.Equal("AVAILABLE",(await PrivacyOwner(h).ReadArtifactAsync(setup.Artifact,s)).Availability);
        Assert.Equal("CLOSED",(await o.PayloadOperationAsync(input.OperationId,true,s)).State);Assert.Equal("CLOSED",(await o.ExecutePayloadDispositionAsync(plan.PlanId,input,s)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_actual_deferred_constraint_rejection_is_known_rollback_and_never_reported_as_transport_unknown()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var plan=await PayloadApproved(h,setup);var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w320_skip_payload_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.owner LIKE 'Main.Privacy.ExportPayload.v1/%' THEN RETURN NULL;END IF;RETURN NEW;END $$;
            CREATE TRIGGER w320_skip_payload_journal BEFORE INSERT ON cp6_main_owner_v1.operations FOR EACH ROW EXECUTE FUNCTION public.w320_skip_payload_journal();
            """);
        try
        {
            var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).ExecutePayloadDispositionAsync(plan.PlanId,input,PrivacySession(h)));
            Assert.Equal("PRIVACY_EXPORT_PAYLOAD_EFFECT_AND_RECEIPT_REQUIRED",error.Message);Assert.Equal(409,error.Status);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w320_skip_payload_journal ON cp6_main_owner_v1.operations;DROP FUNCTION public.w320_skip_payload_journal()");}
        Assert.Equal("NOT_FOUND",(await PrivacyDispositionOwner(h).PayloadOperationAsync(input.OperationId,false,PrivacySession(h))).State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id={setup.Artifact}").SingleAsync());
        Assert.Null((await PrivacyDispositionOwner(h).PayloadPlanAsync(plan.PlanId,PrivacySession(h))).Execution);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_actual_COMMIT_reply_drop_recovers_exact_original_after_execution_authority_is_withdrawn()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var plan=await PayloadApproved(h,setup);var input=new PayloadDispositionExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(h.Db.Database.GetConnectionString()!);
        await using var db=PrivacyRestoreNativeV1.Context(wire.Connection,h.Tenant);var actor=h with{Db=db};
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(actor).ExecutePayloadDispositionAsync(plan.PlanId,input,PrivacySession(actor,_=>{wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",unknown.Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="payload-execute").ExecuteDeleteAsync();
        var result=await PrivacyDispositionOwner(h).PayloadOperationAsync(input.OperationId,false,PrivacySession(h));Assert.Equal("COMMITTED",result.State);Assert.Equal("APPLIED",result.Execution!.OwnerOutcome);
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await PrivacyDispositionOwner(h).ExecutePayloadDispositionAsync(plan.PlanId,input,PrivacySession(h))));
        Assert.Equal("DISPOSED",(await PrivacyOwner(h).ReadArtifactAsync(setup.Artifact,PrivacySession(h))).Availability);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_native_orphan_execution_and_restore_receipts_cannot_commit_and_missing_writer_fence_fails_fast()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var plan=await PayloadApproved(h,setup);var scope=plan.Scope;var op=Guid.NewGuid();
        var receipt=new PayloadDispositionExecutionV1(plan.PlanId,op,Guid.NewGuid(),"Main.Privacy.ExportPayload.v1","APPLIED","PARTIAL",ScopeEnvironment,h.Tenant,h.User,h.User,setup.Approver.User,
            setup.Artifact,scope.TargetTenantId,scope.SubjectId,scope.PolicyId,scope.PolicyVersion,scope.PayloadVersion!.Value,scope.PayloadLength!.Value,plan.ScopeDigest,PrivacyReviewTime(0),["WAL_AND_BACKUPS"]);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_export_disposition_v1.executions(plan_id,approval_version,artifact_id,environment_id,actor_tenant,operator_id,operation_id,scope_json,receipt_json)
                VALUES({plan.PlanId},{plan.Version},{setup.Artifact},{ScopeEnvironment},{h.Tenant},{h.User},{op},{JsonSerializer.Serialize(scope)},{JsonSerializer.Serialize(receipt)})
                """);
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync());Assert.Contains("EFFECT_AND_RECEIPT_REQUIRED",error.MessageText);
        }
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            var fake=new PrivacyRestoreResultV1(Guid.NewGuid(),"APPLIED",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"fake",[new(plan.PlanId,h.Tenant,scope.SubjectId,"REAPPLIED",scope.PayloadVersion,null,"Main.Privacy.ExportPayload.v1",setup.Artifact,scope.PayloadLength)],[],PrivacyReviewTime(0));
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_disposition_v1.restore_receipts(environment_id,actor_tenant,actor_id,operation_id,result_json) VALUES({ScopeEnvironment},{h.Tenant},{h.User},{fake.OperationId},{JsonSerializer.Serialize(fake)})");
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync());Assert.Contains("RESTORE_EFFECT_REQUIRED",error.MessageText);
        }
        Assert.Null((await PrivacyDispositionOwner(h).PayloadPlanAsync(plan.PlanId,PrivacySession(h))).Execution);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads DISABLE TRIGGER payload_no_resurrection");
        try{Assert.Equal("PRIVACY_EXPORT_PAYLOAD_BOUNDARY_UNAVAILABLE",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).PayloadPreviewAsync(new(setup.Artifact,setup.Policy),PrivacySession(h)))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_export_v1.payloads ENABLE TRIGGER payload_no_resurrection");}
        var truncate=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_privacy_export_v1.payloads"));Assert.Contains("DISPOSITION_REQUIRED",truncate.MessageText);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_execution_and_native_resurrection_or_global_hold_share_one_root_in_both_orders()
    {
        await using var h=await Seed();var setup=await PayloadSetup(h);var o=PrivacyDispositionOwner(h);var plan=await PayloadApproved(h,setup);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executing=o.ExecutePayloadDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var native=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var insert=native.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_export_v1.payloads(artifact_id,bytes) VALUES({setup.Artifact},{new byte[]{1}})");
        try{await AssertNativeWait(native,insert);release.TrySetResult();await executing;var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(async()=>await insert);Assert.Contains("DISPOSED_IDENTITY",error.MessageText);}
        finally{release.TrySetResult();}
        var next=(await PrivacyOwner(h).GenerateAsync(PrivacyCommand(h) with{SubjectId=plan.Scope.SubjectId,Sources=["identity"]},PrivacySession(h))).Receipt!.ArtifactId;
        await PrivacyOwner(h).RevokeAsync(next,PrivacySession(h));var second=await PayloadApproved(h,(next,setup.Policy,setup.Approver));
        await using var holdDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var holder=h with{Db=holdDb};entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding=PrivacyDispositionOwner(holder).SetPayloadHoldAsync(next,new(Guid.NewGuid(),null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(holder,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waiting=o.ExecutePayloadDispositionAsync(second.PlanId,new(Guid.NewGuid(),second.Version,second.ScopeDigest,Guid.NewGuid()),PrivacySession(h));
        PayloadDispositionOperationV1 held;
        try{await AssertNativeWait(h.Db,waiting);release.TrySetResult();held=await holding;Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(async()=>await waiting)).Status);}
        finally{release.TrySetResult();}
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={next}").SingleAsync());
        await o.SetPayloadHoldAsync(next,new(Guid.NewGuid(),held.Version,false,Guid.NewGuid(),null),PrivacySession(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_actual_HTTP_read_is_independent_of_old_export_and_does_not_grant_execution()
    {
        await using var h=await Seed();await PrivacyAuthority(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        const string path="/api/platform/privacy-dispositions-v1/export-payloads/context";
        using var denied=await host.Send(BoundRequest(cookies,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await EngineeringAction(h,"privacy-disposition","payload-read");
        using var allowed=await host.Send(BoundRequest(cookies,path));Assert.True(allowed.StatusCode==HttpStatusCode.OK,await allowed.Content.ReadAsStringAsync());
        using var context=JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());Assert.Equal("Main.Privacy.ExportPayload.v1",context.RootElement.GetProperty("owner").GetString());
        var actions=context.RootElement.GetProperty("actions").EnumerateArray().Select(x=>x.GetString()).ToArray();Assert.Contains("payload-read",actions);Assert.DoesNotContain("payload-execute",actions);Assert.DoesNotContain("payload-approve",actions);
    }
    [CurrentPermissionPostgresFact]
    public async Task ExportPayloadDisposition_native_unowned_delete_is_rejected_and_access_revoke_retains_payload()
    {
        await using var h=await Seed();await PrivacyAuthority(h);var owner=PrivacyOwner(h);var session=PrivacySession(h);
        var receipt=(await owner.GenerateAsync(PrivacyCommand(h),session)).Receipt!;Assert.Equal("REVOKED",(await owner.RevokeAsync(receipt.ArtifactId,session)).Availability);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={receipt.ArtifactId}").SingleAsync());
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_privacy_export_v1.payloads WHERE artifact_id={receipt.ArtifactId}"));Assert.Equal("23514",error.SqlState);Assert.Contains("PRIVACY_EXPORT_PAYLOAD_DISPOSITION_REQUIRED",error.MessageText);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={receipt.ArtifactId}").SingleAsync());
    }
}

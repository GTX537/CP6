using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_candidate_without_new_owner_boundary_fails_fast_without_admission_or_source_success()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);await target.Database.ExecuteSqlRawAsync("DROP SCHEMA cp6_privacy_security_fields_v1 CASCADE");
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteRestoreAsync(plan.PlanId,input,s));Assert.Equal("PRIVACY_SECURITY_FIELDS_BOUNDARY_UNAVAILABLE",error.Message);Assert.Equal(503,error.Status);
        Assert.Equal("PENDING",(await o.RestoreOperationAsync(input.OperationId,"READ",s)).State);Assert.Equal("CLOSED",(await o.RestoreOperationAsync(input.OperationId,"CLOSE",s)).State);
        Assert.Equal("simulated-sensitive-name",(await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).UserName);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_source_current_hold_decision_supersedes_old_candidate_hold_without_overwriting_candidate_history()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);var live=PrivacyDispositionOwner(h);var s=PrivacySession(h);
        var held=await live.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),null,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        await live.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),held.Version,false,Guid.NewGuid(),null),s);await SecurityFieldDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var result=await o.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s);Assert.Equal(2,Assert.Single(result.Units!,x=>x.LogId==setup.Log).ClearedValueCount);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Null((await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).ClientIp);
        Assert.True(await target.Database.SqlQuery<bool>($"SELECT active AS \"Value\" FROM cp6_privacy_disposition_v1.holds WHERE version={held.Version!.Value}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_same_revision_but_corrupt_classified_value_is_rejected_before_effect_or_admission()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await using(var fault=await target.Database.BeginTransactionAsync())
        {
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_SecurityLogs\" DISABLE TRIGGER privacy_fields_row");
            await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_SecurityLogs\" SET \"UserName\"='simulated-sensitive-NAME' WHERE \"Id\"={setup.Log}");
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_SecurityLogs\" ENABLE TRIGGER privacy_fields_row");await fault.CommitAsync();
        }
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        Assert.Equal("PRIVACY_SECURITY_FIELDS_RESTORE_IDENTITY_CHANGED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteRestoreAsync(plan.PlanId,input,s))).Message);
        Assert.Equal("simulated-sensitive-NAME",(await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).UserName);
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id={setup.Log}").SingleAsync());
        Assert.Equal("PENDING",(await o.RestoreOperationAsync(input.OperationId,"READ",s)).State);
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))).Status);
        Assert.Equal("CLOSED",(await o.RestoreOperationAsync(input.OperationId,"CLOSE",s)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_already_empty_fields_install_fence_without_claiming_new_clear_or_destroying_unknown_content()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        await using(var fault=await target.Database.BeginTransactionAsync())
        {
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_SecurityLogs\" DISABLE TRIGGER privacy_fields_row");
            await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_SecurityLogs\" SET \"UserName\"=NULL,\"ClientIp\"=NULL WHERE \"Id\"={setup.Log}");
            await target.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_SecurityLogs\" ENABLE TRIGGER privacy_fields_row");await fault.CommitAsync();
        }
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var result=await o.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s);var unit=Assert.Single(result.Units!,x=>x.LogId==setup.Log);Assert.Equal("ALREADY_CLEARED_FENCED",unit.Outcome);Assert.Equal(0,unit.ClearedValueCount);Assert.NotEqual(unit.BeforeVersion,unit.AfterVersion);
        var row=await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Contains("unclassified-context-remains",row.ContextJson!);Assert.Null(row.ClientIp);
        Assert.Contains("DISPOSED_IDENTITY",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.ClientIp,"198.51.100.1")))).MessageText);
        await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=h.Db.Database.GetConnectionString()!},default))Assert.NotNull(admitted);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_candidate_and_source_commit_unknown_each_keep_quarantine_until_exact_original_recovery()
    {
        foreach(var candidateReplyLost in new[]{true,false})
        {
            var fault=new MissingRestoreSourceCommit();await using var h=await Seed(fault);var setup=await SecurityFieldSetup(h);await RestorePermissions(h);
            await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);
            await using var wire=new PrivacyRestoreCommitReplyProxyFixture(candidate.Connection);var options=RestoreRegistration(candidate);
            if(candidateReplyLost)options.Candidates[candidate.Id].ConnectionString=wire.Connection;
            var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
            var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2){if(candidateReplyLost)wire.Arm();else fault.Armed=true;}return Task.CompletedTask;})));
            if(candidateReplyLost)await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(candidateReplyLost?"PRIVACY_RESTORE_CANDIDATE_OUTCOME_UNKNOWN":"PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",unknown.Message);
            var pending=await o.RestoreOperationAsync(input.OperationId,"READ",s);Assert.Equal("AWAITING_SOURCE_RESULT",pending.State);var unit=Assert.Single(pending.Units!,x=>x.LogId==setup.Log);Assert.Equal(2,unit.ClearedValueCount);
            await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=h.Db.Database.GetConnectionString()!};
            Assert.Null((await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).UserName);Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
            await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
            var recovered=await o.RestoreOperationAsync(input.OperationId,"RECOVER",s);Assert.Equal(JsonSerializer.Serialize(pending with{State="APPLIED"}),JsonSerializer.Serialize(recovered));Assert.Equal(JsonSerializer.Serialize(recovered),JsonSerializer.Serialize(await o.RestoreOperationAsync(input.OperationId,"RECOVER",s)));
            await using(var admitted=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(admitted);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_final_session_and_silent_update_skip_roll_back_fields_parent_and_admission_before_close()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);await SecurityFieldDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)throw new InvalidOperationException("simulated logout after candidate effects");return Task.CompletedTask;})));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);Assert.Equal("simulated-sensitive-name",(await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).UserName);
        await target.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w321_skip_restore_update() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL;END $$;
            CREATE TRIGGER zz_w321_skip_restore_update BEFORE UPDATE ON public."Sys_SecurityLogs" FOR EACH ROW EXECUTE FUNCTION public.w321_skip_restore_update();
            """);
        try{var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteRestoreAsync(plan.PlanId,input,s));Assert.Equal("PRIVACY_SECURITY_FIELDS_RESTORE_EFFECT_REQUIRED",error.Message);Assert.Equal(409,error.Status);}
        finally{await target.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_w321_skip_restore_update ON public.\"Sys_SecurityLogs\";DROP FUNCTION public.w321_skip_restore_update()");}
        var row=await target.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log);Assert.Equal("simulated-sensitive-name",row.UserName);Assert.Equal("198.51.100.7",row.ClientIp);
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id={setup.Log}").SingleAsync());
        Assert.Equal(0,await target.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.restore_receipts WHERE operation_id={input.OperationId}").SingleAsync());
        Assert.Equal("PENDING",(await o.RestoreOperationAsync(input.OperationId,"READ",s)).State);Assert.Equal("CLOSED",(await o.RestoreOperationAsync(input.OperationId,"CLOSE",s)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_restore_current_policy_and_hold_advance_source_epoch_and_invalidate_old_approved_restore()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);await RestorePermissions(h);await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);var disposed=await SecurityFieldDispose(h,setup);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var o=PrivacyRestorer(h,options,runtime);var s=PrivacySession(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var live=PrivacyDispositionOwner(h);var p=(await live.SecurityPolicyAsync(setup.Policy,s))!;var changed=await live.ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,p.Version,Guid.NewGuid(),true,PrivacyReviewTime()),s);
        Assert.Contains("SOURCE_VERSION_CHANGED",(await o.RestorePlanAsync(plan.PlanId,s)).CurrentBlockers);Assert.Contains((await o.RestorePreviewAsync(candidate.Id,s)).Blockers,x=>x.StartsWith("CURRENT_SECURITY_NOT_BEFORE_NOT_REACHED:"));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),s))).Status);
        await live.ConfigureSecurityPolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,changed.Version,Guid.NewGuid(),true,PrivacyReviewTime(-60)),s);
        var held=await live.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),null,true,Guid.NewGuid(),PrivacyReviewTime()),s);Assert.Contains((await o.RestorePreviewAsync(candidate.Id,s)).Blockers,x=>x.StartsWith("CURRENT_SECURITY_HOLD_REQUIRES_RESTORE_DECISION:"));
        await live.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),held.Version,false,Guid.NewGuid(),null),s);Assert.Empty((await o.RestorePreviewAsync(candidate.Id,s)).Blockers);
        Assert.Equal(disposed.Execution!.OperationId,(await live.SecurityPlanAsync(disposed.PlanId!.Value,s)).Execution!.OperationId);
    }
}

using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_global_hold_blocks_native_delete_and_truncate_and_ABA_never_revives_approval_even_after_review_or_user_deletion()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var plan=await SecurityFieldApproved(h,setup);
        await using var other=await Seed();await PrivacyDispositionSetup(other);var holdOwner=PrivacyDispositionOwner(other);var hs=PrivacySession(other);
        var held=await holdOwner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,plan.Scope.SubjectId,null,true,Guid.NewGuid(),PrivacyReviewTime(1)),hs);
        await Task.Delay(1200);var preview=await o.SecurityPreviewAsync(new(setup.Log,setup.Policy),s);Assert.True(preview.HoldReviewOverdue);Assert.Equal(other.Tenant,preview.Hold!.OwnerTenantId);Assert.Contains("ACTIVE_HOLD",preview.Blockers);
        Assert.Contains("ACTIVE_HOLD",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteDeleteAsync())).MessageText);
        Assert.Contains("ACTIVE_HOLD",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"Sys_SecurityLogs\""))).MessageText);
        Assert.Contains("ACTIVE_HOLD",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserId,(Guid?)null)))).MessageText);
        Assert.Contains("ACTIVE_HOLD",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,"replacement-under-hold")))).MessageText);
        await holdOwner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,plan.Scope.SubjectId,held.Version,false,Guid.NewGuid(),null),hs);
        Assert.Contains("HOLD_VERSION_CHANGED",(await o.SecurityPlanAsync(plan.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteSecurityDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),s))).Status);
        await h.Db.Sys_Users.Where(x=>x.Id==plan.Scope.SubjectId).ExecuteDeleteAsync();
        var previous=await o.SecurityHoldAsync(setup.Log,s);var current=await o.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),previous!.Version,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        Assert.Contains("ACTIVE_HOLD",(await o.SecurityPreviewAsync(new(setup.Log,setup.Policy),s)).Blockers);
        await o.SetSecurityHoldAsync(setup.Log,new(Guid.NewGuid(),current.Version,false,Guid.NewGuid(),null),s);
        var fresh=await SecurityFieldApproved(h,setup);await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="security-approve").ExecuteDeleteAsync();
        Assert.Contains("APPROVER_AUTHORITY_CHANGED",(await o.SecurityPlanAsync(fresh.PlanId,s)).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteSecurityDispositionAsync(fresh.PlanId,new(Guid.NewGuid(),fresh.Version,fresh.ScopeDigest,Guid.NewGuid()),s))).Status);
        await o.DecideSecurityDispositionAsync(fresh.PlanId,new(Guid.NewGuid(),fresh.Version,"REVOKE",Guid.NewGuid()),s);Assert.Equal("REVOKED",(await o.SecurityPlanAsync(fresh.PlanId,s)).State);
        Assert.Equal(1,await h.Db.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteDeleteAsync());
    }
    private static async Task AssertSecurityNotApplied(Harness h,Guid log,Guid operation)
    {
        var row=await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==log);Assert.Equal("simulated-sensitive-name",row.UserName);Assert.Equal("198.51.100.7",row.ClientIp);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id={log}").SingleAsync());
        Assert.Equal("NOT_FOUND",(await PrivacyDispositionOwner(h).SecurityOperationAsync(operation,false,PrivacySession(h))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_final_session_journal_and_deferred_constraint_failures_roll_back_real_fields_and_close_blocks_late_execute()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var plan=await SecurityFieldApproved(h,setup);var input=new SecurityFieldExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.ExecuteSecurityDispositionAsync(plan.PlanId,input,PrivacySession(h,_=>throw new InvalidOperationException("simulated logout"))));await AssertSecurityNotApplied(h,setup.Log,input.OperationId);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w321_fail_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.owner LIKE 'Main.SecurityLog.Fields.v1/%' THEN RAISE EXCEPTION 'SIMULATED_SECURITY_JOURNAL_FAILURE';END IF;RETURN NEW;END $$;
            CREATE TRIGGER w321_fail_journal BEFORE INSERT ON cp6_main_owner_v1.operations FOR EACH ROW EXECUTE FUNCTION public.w321_fail_journal();
            """);
        try{await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w321_fail_journal ON cp6_main_owner_v1.operations;DROP FUNCTION public.w321_fail_journal()");}
        await AssertSecurityNotApplied(h,setup.Log,input.OperationId);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.w321_skip_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.owner LIKE 'Main.SecurityLog.Fields.v1/%' THEN RETURN NULL;END IF;RETURN NEW;END $$;
            CREATE TRIGGER w321_skip_journal BEFORE INSERT ON cp6_main_owner_v1.operations FOR EACH ROW EXECUTE FUNCTION public.w321_skip_journal();
            """);
        try{var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s));Assert.Equal(409,error.Status);Assert.Equal("PRIVACY_SECURITY_FIELDS_EFFECT_AND_RECEIPT_REQUIRED",error.Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w321_skip_journal ON cp6_main_owner_v1.operations;DROP FUNCTION public.w321_skip_journal()");}
        await AssertSecurityNotApplied(h,setup.Log,input.OperationId);Assert.Null((await o.SecurityPlanAsync(plan.PlanId,s)).Execution);
        Assert.Equal("CLOSED",(await o.SecurityOperationAsync(input.OperationId,true,s)).State);Assert.Equal("CLOSED",(await o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_actual_COMMIT_reply_loss_recovers_exact_original_after_execute_withdrawal()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var plan=await SecurityFieldApproved(h,setup);var input=new SecurityFieldExecuteV1(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
        await using var wire=new PrivacyRestoreCommitReplyProxyFixture(h.Db.Database.GetConnectionString()!);await using var db=PrivacyRestoreNativeV1.Context(wire.Connection,h.Tenant);var actor=h with{Db=db};
        var error=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(actor).ExecuteSecurityDispositionAsync(plan.PlanId,input,PrivacySession(actor,_=>{wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",error.Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="security-execute").ExecuteDeleteAsync();var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var original=await o.SecurityOperationAsync(input.OperationId,false,s);
        Assert.Equal("COMMITTED",original.State);Assert.Equal(2,original.Execution!.ClearedValueCount);Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await o.ExecuteSecurityDispositionAsync(plan.PlanId,input,s)));
        Assert.Null((await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==setup.Log)).UserName);
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_native_orphan_restore_receipt_cannot_commit_and_disabled_writer_or_immutable_fence_fails_fast()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var plan=await SecurityFieldApproved(h,setup);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            var scope=plan.Scope;var op=Guid.NewGuid();var receipt=new SecurityFieldExecutionV1(plan.PlanId,op,Guid.NewGuid(),"Main.SecurityLog.Fields.v1","CLEAR_CLASSIFIED_SECURITY_LOG_FIELDS","APPLIED","PARTIAL",ScopeEnvironment,h.Tenant,h.User,h.User,setup.Approver.User,
                setup.Log,h.Tenant,scope.SubjectId,scope.PolicyId,scope.PolicyVersion,scope.TargetVersion,Guid.NewGuid(),plan.ScopeDigest,PrivacyReviewTime(0),2,["UserName","ClientIp"],[]);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_security_fields_v1.executions(plan_id,approval_version,log_id,environment_id,actor_tenant,operator_id,operation_id,scope_json,receipt_json)
                VALUES({plan.PlanId},{plan.Version},{setup.Log},{ScopeEnvironment},{h.Tenant},{h.User},{op},{JsonSerializer.Serialize(scope)},{JsonSerializer.Serialize(receipt)})
                """);
            Assert.Contains("EFFECT_AND_RECEIPT_REQUIRED",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync())).MessageText);
        }
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            var fake=new PrivacyRestoreResultV1(Guid.NewGuid(),"APPLIED",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"fake",[new(plan.PlanId,h.Tenant,plan.Scope.SubjectId,"REAPPLIED",plan.Scope.TargetVersion,Guid.NewGuid(),"Main.SecurityLog.Fields.v1",LogId:setup.Log,ClearedValueCount:2)],[],PrivacyReviewTime(0));
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_disposition_v1.restore_receipts(environment_id,actor_tenant,actor_id,operation_id,result_json) VALUES({ScopeEnvironment},{h.Tenant},{h.User},{fake.OperationId},{JsonSerializer.Serialize(fake)})");
            Assert.Contains("RESTORE_EFFECT_REQUIRED",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync())).MessageText);
        }
        foreach(var table in new[]{"public.\"Sys_SecurityLogs\"","cp6_privacy_security_fields_v1.tombstones"})
        {
            var trigger=table.StartsWith("public")?"privacy_fields_row":"immutable";
            await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DISABLE TRIGGER {trigger}");
            try{Assert.Equal("PRIVACY_SECURITY_FIELDS_BOUNDARY_UNAVAILABLE",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).SecurityPreviewAsync(new(setup.Log,setup.Policy),PrivacySession(h)))).Message);}
            finally{await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ENABLE TRIGGER {trigger}");}
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task SecurityFieldDisposition_native_update_and_global_hold_serialize_with_execution_on_actual_root_in_both_orders()
    {
        await using var h=await Seed();var setup=await SecurityFieldSetup(h);var o=PrivacyDispositionOwner(h);var plan=await SecurityFieldApproved(h,setup);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executing=o.ExecuteSecurityDispositionAsync(plan.PlanId,new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid()),PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var native=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var update=native.Sys_SecurityLogs.Where(x=>x.Id==setup.Log).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.UserName,"must-not-reappear"));
        try{await AssertNativeWait(native,update);release.TrySetResult();await executing;Assert.Contains("DISPOSED_IDENTITY",(await Assert.ThrowsAsync<Npgsql.PostgresException>(async()=>await update)).MessageText);}
        finally{release.TrySetResult();}
        var next=await SecurityFieldLog(h,plan.Scope.SubjectId);var second=await SecurityFieldApproved(h,(next,setup.Policy,setup.Approver));
        await using var holdDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var holder=h with{Db=holdDb};entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding=PrivacyDispositionOwner(holder).SetSecurityHoldAsync(next,new(Guid.NewGuid(),null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(holder,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var waiting=o.ExecuteSecurityDispositionAsync(second.PlanId,new(Guid.NewGuid(),second.Version,second.ScopeDigest,Guid.NewGuid()),PrivacySession(h));SecurityFieldOperationV1 held;
        try{await AssertNativeWait(h.Db,waiting);release.TrySetResult();held=await holding;Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(async()=>await waiting)).Status);}
        finally{release.TrySetResult();}
        Assert.NotNull((await h.Db.Sys_SecurityLogs.AsNoTracking().SingleAsync(x=>x.Id==next)).ClientIp);
        await o.SetSecurityHoldAsync(next,new(Guid.NewGuid(),held.Version,false,Guid.NewGuid(),null),PrivacySession(h));
    }
}

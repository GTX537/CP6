using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static CrmIdentityRuntime PrivacyIdentityRuntime(Guid tenant)
    {
        const string issuer="https://iam-writer.example.test";
        var options=new CrmIdentityOptions{Enabled=true,Issuer=issuer,ProjectionReaderClientIds=["fixture-reader"]};options.Tenants.Add(tenant,"us");
        return new(options,new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),issuer));
    }
    private static PrivacyDispositionExecuteV1 PrivacyExecuteCommand((Guid Plan,Guid Version,string Digest) plan)
        =>new(Guid.NewGuid(),plan.Version,plan.Digest,Guid.NewGuid());
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_exact_identity_effect_receipt_and_authentication_invalidation_are_atomic_without_old_PII_audit()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var family=Guid.NewGuid();h.Db.Sys_BrowserSessions.Add(new(){Id=family,UserId=setup.Target,AuthenticationVersion="fixture-original"});
        h.Db.Sys_RefreshTokens.Add(new(){UserId=setup.Target,BrowserSessionId=family,TokenHash=Guid.NewGuid().ToString("N"),ExpiresAt=DateTime.Now.AddDays(1)});
        var source=await h.Db.Sys_Users.SingleAsync(x=>x.Id==setup.Target);source.BadgeNo="subject-badge";source.QuickPinHash="old-private-hash";
        source.ExternalSubject="subject-external";source.ExternalProvider="https://old.example.invalid";source.TwoFactorEnabled=true;source.TwoFactorSecret="old-private-secret";
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var grant=Guid.NewGuid();var codeHash=Guid.NewGuid().ToString("N");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."CrmOidcGrant"("Id","CodeHash","ClientId","RedirectUri","Challenge","Nonce","SubjectId","OrganizationId",
              "SourceJti","SourceRefreshHash","SecurityStamp","ExpiresAtUtc","SourceExpiresAtUtc","AccessExpiresAtUtc","Consumed","Revoked")
            VALUES({grant},{codeHash},'simulated-client','https://fixture.invalid/callback','fixture-challenge','fixture-nonce',{setup.Target},{h.Tenant},
              'fixture-jti','fixture-refresh','fixture-stamp',LOCALTIMESTAMP+interval '1 hour',LOCALTIMESTAMP+interval '1 hour',LOCALTIMESTAMP+interval '1 hour',true,false)
            """);
        var audit=await h.Db.Sys_FieldAuditLogs.CountAsync();var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));var worker=h with{Db=db};
        var input=PrivacyExecuteCommand(plan);var result=await PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,input,PrivacySession(worker));
        Assert.Equal("COMMITTED",result.State);Assert.Equal("PARTIAL",result.Execution!.State);Assert.Equal("APPLIED",result.Execution.OwnerOutcome);
        var user=await db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==setup.Target);
        Assert.Equal("anon_"+setup.Target.ToString("N"),user.UserName);Assert.False(user.Enable);Assert.Null(user.Email);Assert.Null(user.NickName);Assert.Null(user.BadgeNo);Assert.Null(user.LastLoginIp);
        Assert.Null(user.ExternalSubject);Assert.Null(user.ExternalProvider);Assert.Null(user.TwoFactorSecret);Assert.False(user.TwoFactorEnabled);Assert.Null(user.QuickPinHash);Assert.False(user.AllowPasswordFallback);
        Assert.NotEqual(result.Execution.BeforeVersion,result.Execution.AfterVersion);Assert.NotEqual(Guid.Empty,user.AuthenticationEpoch);Assert.Equal(72,user.RoleId);
        Assert.NotNull((await db.Sys_RefreshTokens.SingleAsync(x=>x.UserId==setup.Target)).RevokedAt);
        Assert.NotNull((await db.Sys_BrowserSessions.SingleAsync(x=>x.Id==family)).LoggedOutAtUtc);
        var snapshot=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId=="user:"+setup.Target.ToString("D"));Assert.False(JsonDocument.Parse(snapshot.PayloadJson).RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(await db.Database.SqlQuery<bool>($"SELECT \"Revoked\" AS \"Value\" FROM public.\"CrmOidcGrant\" WHERE \"Id\"={grant}").SingleAsync());
        Assert.True(await db.CrmIdentitySnapshots.AsNoTracking().AnyAsync(x=>x.EventType.Contains("token.revoked")));
        Assert.Equal(audit,await db.Sys_FieldAuditLogs.CountAsync());Assert.DoesNotContain("old-private",JsonSerializer.Serialize(result));Assert.DoesNotContain("simulated@example.invalid",JsonSerializer.Serialize(result));
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,input,PrivacySession(worker))));
        Assert.Equal(JsonSerializer.Serialize(result.Execution),JsonSerializer.Serialize(await PrivacyDispositionOwner(worker).ExecutionAsync(plan.Plan,PrivacySession(worker))));
        var current=await PrivacyDispositionOwner(worker).PlanAsync(plan.Plan,PrivacySession(worker));Assert.NotNull(current.Execution);Assert.False(current.ApprovalCurrent);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,PrivacyExecuteCommand(plan),PrivacySession(worker)))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(worker).DecideAsync(plan.Plan,new(Guid.NewGuid(),plan.Version,"REVOKE",Guid.NewGuid()),PrivacySession(worker)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_hold_and_approver_changes_block_before_any_effect_and_missing_writer_fails_fast()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);var command=PrivacyExecuteCommand(plan);
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).ExecuteAsync(plan.Plan,command,PrivacySession(h)))).Status);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));var worker=h with{Db=db};var owner=PrivacyDispositionOwner(worker);var session=PrivacySession(worker);
        var held=await owner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),session);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteAsync(plan.Plan,command,session))).Status);
        await owner.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),session);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteAsync(plan.Plan,command,session))).Status);
        plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);command=PrivacyExecuteCommand(plan);
        await db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="approve").ExecuteDeleteAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteAsync(plan.Plan,command,session))).Status);
        Assert.Equal("simulated@example.invalid",await db.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        Assert.Null(await owner.ExecutionAsync(plan.Plan,session));Assert.Equal("NOT_FOUND",(await owner.OperationAsync(command.OperationId,false,session)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_commit_reply_loss_recovers_and_final_session_failure_rolls_back_source_receipt_and_identity_outbox()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        var fault=new CommitFault();await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant),fault);var worker=h with{Db=db};var input=PrivacyExecuteCommand(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,input,PrivacySession(worker,_=>throw new InvalidOperationException("simulated logout"))));
        Assert.Equal("simulated@example.invalid",await db.Sys_Users.AsNoTracking().Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());Assert.Null(await PrivacyDispositionOwner(worker).ExecutionAsync(plan.Plan,PrivacySession(worker)));
        Assert.False(await db.CrmIdentitySnapshots.AsNoTracking().AnyAsync(x=>x.AggregateId=="user:"+setup.Target.ToString("D")));
        db.ChangeTracker.Clear();fault.Armed=true;
        Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,input,PrivacySession(worker)))).Message);
        await using var fresh=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));worker=h with{Db=fresh};
        var original=await PrivacyDispositionOwner(worker).OperationAsync(input.OperationId,false,PrivacySession(worker));Assert.Equal("COMMITTED",original.State);Assert.Equal("APPLIED",original.Execution!.OwnerOutcome);
        await fresh.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="execute").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(original),JsonSerializer.Serialize(await PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,input,PrivacySession(worker))));
        var late=input with{OperationId=Guid.NewGuid()};await PrivacyDispositionOwner(worker).OperationAsync(late.OperationId,true,PrivacySession(worker));Assert.Equal("CLOSED",(await PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,late,PrivacySession(worker))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_native_hold_and_execution_competition_never_applies_after_an_earlier_hold_or_undoes_committed_effect()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        await using var workerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));var worker=h with{Db=workerDb};
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding=PrivacyDispositionOwner(h).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(h,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var execution=PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,PrivacyExecuteCommand(plan),PrivacySession(worker));
        PrivacyDispositionOperationV1 held;
        try{await AssertNativeWait(workerDb,execution);release.TrySetResult();held=await holding;await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>execution);}
        finally{release.TrySetResult();}
        Assert.Equal("simulated@example.invalid",await workerDb.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        var lifted=await PrivacyDispositionOwner(h).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),PrivacySession(h));
        plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);entered=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution=PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,PrivacyExecuteCommand(plan),PrivacySession(worker,async _=>{entered.TrySetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        holding=PrivacyDispositionOwner(h).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,lifted.Version,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(h));
        try{await AssertNativeWait(h.Db,holding);release.TrySetResult();var applied=await execution;await holding;Assert.Equal("APPLIED",applied.Execution!.OwnerOutcome);}
        finally{release.TrySetResult();}
        Assert.Null(await workerDb.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        Assert.Equal("APPLIED",(await PrivacyDispositionOwner(worker).ExecutionAsync(plan.Plan,PrivacySession(worker)))!.OwnerOutcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_scope_confirmation_policy_and_target_versions_are_checked_at_actual_effect()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));var worker=h with{Db=db};var o=PrivacyDispositionOwner(worker);var s=PrivacySession(worker);
        var input=PrivacyExecuteCommand(plan);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteAsync(plan.Plan,input with{ExpectedVersion=Guid.NewGuid()},s))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteAsync(plan.Plan,input with{ExpectedScopeDigest="different scope"},s))).Status);
        var policy=(await o.PolicyAsync(setup.Policy,s))!;
        await o.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true),s);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteAsync(plan.Plan,input,s))).Status);
        plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);input=PrivacyExecuteCommand(plan);
        await db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"changed@example.invalid"));
        await db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"simulated@example.invalid"));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ExecuteAsync(plan.Plan,input,s))).Status);
        Assert.Null(await o.ExecutionAsync(plan.Plan,s));Assert.Equal("simulated@example.invalid",await db.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyExecution_explicit_cross_tenant_target_uses_its_own_identity_writer_and_preserves_unselected_subject()
    {
        await using var h=await Seed();var a=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        await using var other=await Seed();var b=await PrivacyDispositionSetup(other);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);
        var policy=Guid.NewGuid();await o.ConfigurePolicyAsync(new(Guid.NewGuid(),policy,other.Tenant,null,Guid.NewGuid(),true),s);
        var target=new PrivacyDispositionTargetV1(other.Tenant,b.Target,policy);var preview=await o.PreviewAsync(target,s);
        var requested=await o.RequestAsync(new(Guid.NewGuid(),Guid.NewGuid(),target,preview.ScopeDigest),s);
        await PrivacyDispositionOwner(a.Approver).DecideAsync(requested.PlanId!.Value,new(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(a.Approver));
        var approved=await o.PlanAsync(requested.PlanId!.Value,s);var command=PrivacyExecuteCommand((approved.PlanId,approved.Version,approved.ScopeDigest));
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(other.Tenant));var worker=h with{Db=db};
        var result=await PrivacyDispositionOwner(worker).ExecuteAsync(approved.PlanId,command,PrivacySession(worker));Assert.Equal(other.Tenant,result.Execution!.TargetTenantId);Assert.Equal(h.Tenant,result.Execution.OperatorTenantId);
        Assert.Null(await other.Db.Sys_Users.AsNoTracking().Where(x=>x.Id==b.Target).Select(x=>x.Email).SingleAsync());
        Assert.Equal("simulated@example.invalid",await h.Db.Sys_Users.AsNoTracking().Where(x=>x.Id==a.Target).Select(x=>x.Email).SingleAsync());
        Assert.Null(await PrivacyDispositionOwner(other).ExecutionAsync(approved.PlanId,PrivacySession(other)));
    }
}

using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static PrivacyDispositionOwnerV1 PrivacyRestorer(Harness h,PrivacyRestoreOptionsV1 options,CrmIdentityRuntime runtime)
        =>new(h.Db,new OperationsHttp{HttpContext=h.Http},new CurrentPermissionContext(new OperationsHttp{HttpContext=h.Http},CurrentPermissionContextTests.Cache(),h.Db,new PermissionAggregator(h.Db)),
            Options.Create(new ScopeAuthorizationOptionsV1{EnvironmentId=ScopeEnvironment}),new BCryptPasswordHasher(4),Options.Create(options),runtime);
    private static PrivacyRestoreOptionsV1 RestoreRegistration(PrivacyRestoreCandidateFixture candidate)=>new(){Candidates=new(){[candidate.Id]=new(){ConnectionString=candidate.Connection,RegistrationVersion=Guid.NewGuid(),BackupSha256=candidate.Sha256,DisplayName="模拟恢复候选"}}};
    private static async Task<CrmIdentityRuntime> RestoreRuntime(Harness h)
    {
        var runtime=PrivacyIdentityRuntime(h.Tenant);
        foreach(var tenant in await h.Db.Database.SqlQueryRaw<Guid>("SELECT DISTINCT target_tenant AS \"Value\" FROM cp6_privacy_disposition_v1.tombstones").ToArrayAsync())runtime.Options.Tenants.TryAdd(tenant,"us");
        return runtime;
    }
    private static async Task RestorePermissions(Harness h)
    {
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");
        foreach(var action in new[]{"restore-prepare","restore-approve","restore-execute"})
        {await EngineeringAction(h,"privacy-disposition",action);h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static async Task<PrivacyRestorePlanV1> ApprovedRestore(Harness actor,Harness approver,PrivacyRestoreOptionsV1 options,CrmIdentityRuntime runtime,Guid candidate)
    {
        var owner=PrivacyRestorer(actor,options,runtime);var preview=await owner.RestorePreviewAsync(candidate,PrivacySession(actor));
        var request=await owner.RequestRestoreAsync(new(Guid.NewGuid(),candidate,Guid.NewGuid(),preview.ScopeDigest),PrivacySession(actor));
        await PrivacyRestorer(approver,options,runtime).DecideRestoreAsync(request.PlanId!.Value,new(Guid.NewGuid(),request.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(approver));
        return await owner.RestorePlanAsync(request.PlanId!.Value,PrivacySession(actor));
    }
    private static PrivacyRestoreExecuteV1 RestoreCommand(PrivacyRestorePlanV1 plan)=>new(Guid.NewGuid(),plan.Version,plan.ScopeDigest,Guid.NewGuid());
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_native_archive_reapplies_exact_tombstone_before_opening_and_stale_source_closes_it_again()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);
        await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        var absent=Guid.NewGuid();db.Sys_Users.Add(new(){Id=absent,UserName="after-archive-subject",Email="absent@example.invalid",Password="simulation",RoleId=72});await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var absentPlan=await PrivacyApprovedPlan(actor,absent,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(absentPlan.Plan,PrivacyExecuteCommand(absentPlan),PrivacySession(actor));
        var options=RestoreRegistration(candidate);var plan=await ApprovedRestore(actor,approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var owner=PrivacyRestorer(actor,options,runtime);
        var result=await owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor));Assert.Equal("APPLIED",result.State);Assert.Equal("PARTIAL",result.AggregateState);Assert.Equal("REAPPLIED",Assert.Single(result.Units!,x=>x.SubjectId==setup.Target).Outcome);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant,runtime);
        Assert.Null(await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());Assert.False(await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Enable).SingleAsync());
        Assert.Equal("ABSENT_FENCED",Assert.Single(result.Units!,x=>x.SubjectId==absent).Outcome);Assert.False(await target.Sys_Users.AnyAsync(x=>x.Id==absent));
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."Sys_Users"("Id","TenantId","UserName","Password","Email","Enable","RoleId")
            VALUES({absent},{h.Tenant},'restored-later','simulation','must-not-reappear@example.invalid',true,72)
            """))).SqlState);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>target.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"resurrected@example.invalid")));
        var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=db.Database.GetConnectionString()!};
        await using(var lease=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(lease);
        await db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor))));
        var held=await PrivacyDispositionOwner(actor).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(actor));
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
        await PrivacyDispositionOwner(actor).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),PrivacySession(actor));
        var policies=PrivacyDispositionOwner(actor);var currentPolicy=(await policies.PolicyAsync(setup.Policy,PrivacySession(actor)))!;
        var disabled=await policies.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,currentPolicy.Version,Guid.NewGuid(),false),PrivacySession(actor));
        Assert.Contains("CURRENT_POLICY_DISABLED:"+privacy.Plan.ToString("D"),(await owner.RestorePreviewAsync(candidate.Id,PrivacySession(actor))).Blockers);
        await using var other=await Seed();var retargeted=await policies.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,other.Tenant,disabled.Version,Guid.NewGuid(),true),PrivacySession(actor));
        Assert.Contains("CURRENT_POLICY_TARGET_CHANGED:"+privacy.Plan.ToString("D"),(await owner.RestorePreviewAsync(candidate.Id,PrivacySession(actor))).Blockers);
        await policies.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,retargeted.Version,Guid.NewGuid(),true),PrivacySession(actor));
    }
    private sealed class MissingRestoreSourceCommit:DbTransactionInterceptor
    {
        internal bool Armed;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData eventData,InterceptionResult result,CancellationToken ct=default)
        {if(Armed){Armed=false;throw new IOException("simulated source commit unavailable after actual candidate commit");}return ValueTask.FromResult(result);}
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_candidate_committed_source_unknown_stays_quarantined_until_read_authorized_original_recovery()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);var fault=new MissingRestoreSourceCommit();await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime,fault);
        var actor=h with{Db=db};var approver=setup.Approver with{Db=db};var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);
        await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));var options=RestoreRegistration(candidate);
        var plan=await ApprovedRestore(actor,approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var owner=PrivacyRestorer(actor,options,runtime);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor,_=>{if(++calls==2)fault.Armed=true;return Task.CompletedTask;})));
        Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",unknown.Message);
        var pending=await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(actor));Assert.Equal("AWAITING_SOURCE_RESULT",pending.State);Assert.Equal("REAPPLIED",Assert.Single(pending.Units!,x=>x.SubjectId==setup.Target).Outcome);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant,runtime);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=db.Database.GetConnectionString()!};
        await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default));
        await db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        var recovered=await owner.RestoreOperationAsync(input.OperationId,"RECOVER",PrivacySession(actor));Assert.Equal("APPLIED",recovered.State);
        await using(var lease=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(lease);
        Assert.Equal(JsonSerializer.Serialize(recovered),JsonSerializer.Serialize(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(actor))));
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_final_session_failure_rolls_back_candidate_and_close_fences_late_execution_without_fake_empty_success()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        var options=RestoreRegistration(candidate);var plan=await ApprovedRestore(actor,approver,options,runtime,candidate.Id);var owner=PrivacyRestorer(actor,options,runtime);var input=RestoreCommand(plan);var calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor,_=>{if(++calls==2)throw new InvalidOperationException("simulated logout at final source fence");return Task.CompletedTask;})));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant,runtime);
        Assert.Equal("simulated@example.invalid",await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        var pending=await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(actor));Assert.Equal("PENDING",pending.State);Assert.Null(pending.Units);
        Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(actor))).State);
        await db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        Assert.Equal("CLOSED",(await owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_actual_candidate_COMMIT_reply_drop_is_unknown_and_recovers_the_exact_committed_original()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        await using var wire= new PrivacyRestoreCommitReplyProxyFixture(candidate.Connection);var options=RestoreRegistration(candidate);options.Candidates[candidate.Id].ConnectionString=wire.Connection;
        var plan=await ApprovedRestore(actor,approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);var owner=PrivacyRestorer(actor,options,runtime);var calls=0;
        var unknown=await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor,_=>{if(++calls==2)wire.Arm();return Task.CompletedTask;})));
        await wire.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal("PRIVACY_RESTORE_CANDIDATE_OUTCOME_UNKNOWN",unknown.Message);
        var awaiting=await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(actor));Assert.Equal("AWAITING_SOURCE_RESULT",awaiting.State);Assert.Null(await db.Database.SqlQuery<string>($"SELECT result_json AS \"Value\" FROM cp6_privacy_disposition_v1.restore_outcomes WHERE operation_id={input.OperationId}").SingleOrDefaultAsync());
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=db.Database.GetConnectionString()!};
        Assert.Null(await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
        await db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="restore-execute").ExecuteDeleteAsync();
        var recovered=await owner.RestoreOperationAsync(input.OperationId,"RECOVER",PrivacySession(actor));Assert.Equal(JsonSerializer.Serialize(awaiting with{State="APPLIED"}),JsonSerializer.Serialize(recovered));
        await using(var lease=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))Assert.NotNull(lease);
    }
}

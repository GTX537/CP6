using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using CP6.Tests.Platform;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_metadata_original_stays_in_its_operator_tenant_when_the_same_actor_changes_tenant()
    {
        await using var h=await Seed();await PrivacyDispositionSetup(h);var operation=Guid.NewGuid();var options=new PrivacyRestoreOptionsV1();var runtime=PrivacyIdentityRuntime(h.Tenant);
        Assert.Equal("CLOSED",(await PrivacyRestorer(h,options,runtime).RestoreMetadataOperationAsync(operation,true,PrivacySession(h)))!.State);
        await using var other=await Seed();await PrivacyDispositionSetup(other);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.UserName,"moved-privacy-actor").SetProperty(u=>u.TenantId,other.Tenant));
        var principal=CurrentPermissionContextTests.Principal(h.User,other.Tenant);((ClaimsIdentity)principal.Identity!).AddClaim(new("is_platform_admin","true"));
        var moved=h with{Db=other.Db,Tenant=other.Tenant,Http=new DefaultHttpContext{User=principal}};
        Assert.Null(await PrivacyRestorer(moved,options,runtime).RestoreMetadataOperationAsync(operation,false,PrivacySession(moved)));
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_independent_current_approval_scope_registration_and_original_actor_are_enforced()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var owner=PrivacyRestorer(h,options,runtime);var approver=PrivacyRestorer(setup.Approver,options,runtime);
        var preview=await owner.RestorePreviewAsync(candidate.Id,PrivacySession(h));
        var request=new PrivacyRestorePlanRequestV1(Guid.NewGuid(),candidate.Id,Guid.NewGuid(),preview.ScopeDigest);
        var requested=await owner.RequestRestoreAsync(request,PrivacySession(h));var id=requested.PlanId!.Value;
        var decision=new PrivacyDispositionDecisionV1(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.DecideRestoreAsync(id,decision,PrivacySession(h)))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>approver.DecideRestoreAsync(id,decision with{ExpectedVersion=Guid.NewGuid()},PrivacySession(setup.Approver)))).Status);
        await approver.DecideRestoreAsync(id,decision,PrivacySession(setup.Approver));
        var plan=await owner.RestorePlanAsync(id,PrivacySession(h));var input=RestoreCommand(plan);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(id,input with{ExpectedScopeDigest="different scope"},PrivacySession(h)))).Status);
        Assert.Equal("NOT_FOUND",(await approver.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(setup.Approver))).State);
        Assert.Null(await approver.RestoreMetadataOperationAsync(request.OperationId,false,PrivacySession(setup.Approver)));
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.RequestRestoreAsync(request with{RequestRef=Guid.NewGuid()},PrivacySession(h)))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="restore-approve").ExecuteDeleteAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(id,input,PrivacySession(h)))).Status);
        Assert.Contains("RESTORE_APPROVER_AUTHORITY_CHANGED",(await owner.RestorePlanAsync(id,PrivacySession(h))).CurrentBlockers);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode="restore-approve"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var registration=options.Candidates[candidate.Id];options.Candidates[candidate.Id]=new(){ConnectionString=registration.ConnectionString,RegistrationVersion=Guid.NewGuid(),BackupSha256=registration.BackupSha256,DisplayName=registration.DisplayName};
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(id,input,PrivacySession(h)))).Status);
        Assert.Contains("CANDIDATE_REGISTRATION_CHANGED",(await owner.RestorePlanAsync(id,PrivacySession(h))).CurrentBlockers);options.Candidates[candidate.Id]=registration;
        var held=await PrivacyDispositionOwner(h).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(h));
        Assert.Contains("SOURCE_VERSION_CHANGED",(await owner.RestorePlanAsync(id,PrivacySession(h))).CurrentBlockers);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(id,input,PrivacySession(h)))).Status);
        await PrivacyDispositionOwner(h).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),PrivacySession(h));
        Assert.Equal("NOT_FOUND",(await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(h))).State);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        Assert.Equal("simulated@example.invalid",await target.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        var late=request with{OperationId=Guid.NewGuid()};await owner.RestoreMetadataOperationAsync(late.OperationId,true,PrivacySession(h));
        Assert.Equal("CLOSED",(await owner.RequestRestoreAsync(late,PrivacySession(h))).State);
        options.Candidates[candidate.Id]=new(){ConnectionString=h.Db.Database.GetConnectionString()!,RegistrationVersion=registration.RegistrationVersion,BackupSha256=registration.BackupSha256,DisplayName="invalid source alias"};
        Assert.Equal("PRIVACY_RESTORE_SOURCE_CANNOT_BE_TARGET",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.RestorePreviewAsync(candidate.Id,PrivacySession(h)))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_unresolved_original_blocks_another_actor_on_the_same_physical_candidate_until_explicit_close()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        var owner=PrivacyRestorer(h,options,runtime);var input=RestoreCommand(plan);var calls=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h,_=>{if(++calls==2)throw new InvalidOperationException("simulated final logout");return Task.CompletedTask;})));
        var otherPlan=await ApprovedRestore(setup.Approver,h,options,runtime,candidate.Id);var otherOwner=PrivacyRestorer(setup.Approver,options,runtime);var otherInput=RestoreCommand(otherPlan);
        Assert.Equal("PRIVACY_RESTORE_PENDING_RESPONSIBILITY_MUST_BE_RESOLVED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>otherOwner.ExecuteRestoreAsync(otherPlan.PlanId,otherInput,PrivacySession(setup.Approver)))).Message);
        Assert.Equal("PRIVACY_RESTORE_OPERATION_CONFLICT",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input with{ConfirmationRef=Guid.NewGuid()},PrivacySession(h)))).Message);
        Assert.Equal("NOT_FOUND",(await otherOwner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(setup.Approver))).State);
        Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(h))).State);
        Assert.Equal("APPLIED",(await otherOwner.ExecuteRestoreAsync(otherPlan.PlanId,otherInput,PrivacySession(setup.Approver))).State);
        Assert.Equal("CLOSED",(await owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(h))).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_admitted_request_holds_the_source_fence_and_a_later_hold_closes_the_next_request()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var options=RestoreRegistration(candidate);var runtime=await RestoreRuntime(h);var plan=await ApprovedRestore(h,setup.Approver,options,runtime,candidate.Id);
        await PrivacyRestorer(h,options,runtime).ExecuteRestoreAsync(plan.PlanId,RestoreCommand(plan),PrivacySession(h));
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);var guard=new PrivacyRestoreOptionsV1{ControlConnectionString=h.Db.Database.GetConnectionString()!};
        var lease=await PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default);Assert.NotNull(lease);
        await using var holdingDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var holder=h with{Db=holdingDb};
        var holding=PrivacyDispositionOwner(holder).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(holder));
        try{await AssertNativeWait(holdingDb,holding);}finally{await lease!.DisposeAsync();}
        var held=await holding;
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,guard,default))).Status);
        await PrivacyDispositionOwner(holder).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),PrivacySession(holder));
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_old_platform_authority_requires_IAM_recovery_and_cannot_be_silently_revoked_by_privacy()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,true));
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,false));
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        var options=RestoreRegistration(candidate);var owner=PrivacyRestorer(actor,options,runtime);var plan=await ApprovedRestore(actor,approver,options,runtime,candidate.Id);var input=RestoreCommand(plan);
        Assert.Equal("PRIVACY_RESTORE_IAM_AUTHORITY_RECOVERY_REQUIRED",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.ExecuteRestoreAsync(plan.PlanId,input,PrivacySession(actor)))).Message);
        await using var target=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant);
        var old=await target.Sys_Users.SingleAsync(x=>x.Id==setup.Target);Assert.True(old.IsPlatformAdmin);Assert.True(old.Enable);Assert.Equal("simulated@example.invalid",old.Email);
        Assert.Equal("PENDING",(await owner.RestoreOperationAsync(input.OperationId,"READ",PrivacySession(actor))).State);
        Assert.Equal(503,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyRestoreAdmissionV1.AcquireAsync(target,new(){ControlConnectionString=db.Database.GetConnectionString()!},default))).Status);
        Assert.Equal("CLOSED",(await owner.RestoreOperationAsync(input.OperationId,"CLOSE",PrivacySession(actor))).State);
    }
}

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static PrivacyDispositionOwnerV1 PrivacyDispositionOwner(Harness h)=>new(h.Db,new OperationsHttp{HttpContext=h.Http},
        new CurrentPermissionContext(new OperationsHttp{HttpContext=h.Http},CurrentPermissionContextTests.Cache(),h.Db,new PermissionAggregator(h.Db)),Options.Create(new ScopeAuthorizationOptionsV1{EnvironmentId=ScopeEnvironment}),new BCryptPasswordHasher(4));
    private static async Task<(Guid Target,Guid Policy,Harness Approver)> PrivacyDispositionSetup(Harness h)
    {
        await PrivacyAuthority(h);
        foreach(var action in new[]{"read","policy","prepare","approve","revoke","hold"})await EngineeringAction(h,"privacy-disposition",action);
        var target=Guid.NewGuid();var approver=Guid.NewGuid();var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="privacy-disposition");
        h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="privacy-independent-approver"});
        foreach(var action in new[]{"read","policy","prepare","approve","revoke","hold"})h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=menu.MenuId,ActionCode=action});
        h.Db.Sys_Users.AddRange(new(){Id=target,UserName="simulated-private-subject",NickName="original profile remains",Email="simulated@example.invalid",RoleId=72},
            new(){Id=approver,UserName="privacy-approver",Password=new BCryptPasswordHasher(4).Hash("Price1!Password"),RoleId=73,IsPlatformAdmin=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var principal=CurrentPermissionContextTests.Principal(approver,h.Tenant);((ClaimsIdentity)principal.Identity!).AddClaim(new("is_platform_admin","true"));
        var policy=Guid.NewGuid();await PrivacyDispositionOwner(h).ConfigurePolicyAsync(new(Guid.NewGuid(),policy,h.Tenant,null,Guid.NewGuid(),true),PrivacySession(h));
        return(target,policy,h with{User=approver,Http=new DefaultHttpContext{User=principal}});
    }
    private static async Task<PrivacyDispositionPlanCommandV1> PrivacyDispositionCommand(Harness h,Guid target,Guid policy)
    {
        var input=new PrivacyDispositionTargetV1(h.Tenant,target,policy);var preview=await PrivacyDispositionOwner(h).PreviewAsync(input,PrivacySession(h));
        return new(Guid.NewGuid(),Guid.NewGuid(),input,preview.ScopeDigest);
    }
    private static DateTimeOffset PrivacyReviewTime(int seconds=3600)
    {var date=DateTimeOffset.UtcNow.AddSeconds(seconds);return new(date.Ticks/10*10,TimeSpan.Zero);}

    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_exact_plan_independent_approval_and_original_receipt_never_change_subject_data()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);var owner=PrivacyDispositionOwner(h);var session=PrivacySession(h);
        var input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);var result=await owner.RequestAsync(input,session);var view=await owner.PlanAsync(result.PlanId!.Value,session);
        Assert.Equal("REQUESTED",view.State);Assert.NotEmpty(view.Scope.UncoveredOwners);Assert.Contains("Email",view.Scope.Fields);
        Assert.DoesNotContain("simulated-private-subject",JsonSerializer.Serialize(view));Assert.DoesNotContain("simulated@example.invalid",JsonSerializer.Serialize(view));
        var decision=new PrivacyDispositionDecisionV1(Guid.NewGuid(),view.Version,"APPROVE",Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>owner.DecideAsync(view.PlanId,decision,session))).Status);
        var approval=await PrivacyDispositionOwner(setup.Approver).DecideAsync(view.PlanId,decision,PrivacySession(setup.Approver));
        var approved=await owner.PlanAsync(view.PlanId,session);Assert.Equal("APPROVED",approved.State);Assert.True(approved.ApprovalCurrent);Assert.Equal(setup.Approver.User,approved.ApproverId);
        Assert.Equal(result,await owner.RequestAsync(input,session));Assert.Equal(approval,await PrivacyDispositionOwner(setup.Approver).OperationAsync(decision.OperationId,false,PrivacySession(setup.Approver)));
        var user=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==setup.Target);Assert.Equal("original profile remains",user.NickName);Assert.True(user.Enable);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_target_ABA_policy_change_and_hold_ABA_invalidate_old_preview_or_approval()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);
        await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"temporary@example.invalid"));
        await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,"simulated@example.invalid"));
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.RequestAsync(input,s))).Status);
        input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);var plan=await o.RequestAsync(input,s);var policy=(await o.PolicyAsync(setup.Policy,s))!;
        await o.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,policy.Version,Guid.NewGuid(),true),s);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(setup.Approver).DecideAsync(plan.PlanId!.Value,new(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(setup.Approver)))).Status);
        input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);plan=await o.RequestAsync(input,s);
        var held=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        Assert.Contains("ACTIVE_HOLD",(await o.PreviewAsync(input.Target,s)).Blockers);
        await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),s);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(setup.Approver).DecideAsync(plan.PlanId!.Value,new(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(setup.Approver)))).Status);
        Assert.Contains("HOLD_VERSION_CHANGED",(await o.PlanAsync(plan.PlanId!.Value,s)).CurrentBlockers);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_cross_operator_tenant_hold_and_overdue_review_remain_binding_and_platform_targets_stay_protected()
    {
        await using var h=await Seed();var a=await PrivacyDispositionSetup(h);await using var other=await Seed();var b=await PrivacyDispositionSetup(other);
        var original=await PrivacyDispositionCommand(h,a.Target,a.Policy);
        await PrivacyDispositionOwner(other).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,a.Target,null,true,Guid.NewGuid(),PrivacyReviewTime(1)),PrivacySession(other));
        await Task.Delay(1200);var view=await PrivacyDispositionOwner(h).PreviewAsync(original.Target,PrivacySession(h));
        Assert.True(view.HoldReviewOverdue);Assert.True(view.Hold!.Active);Assert.Equal(other.Tenant,view.Hold.OwnerTenantId);Assert.Contains("ACTIVE_HOLD",view.Blockers);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).RequestAsync(original,PrivacySession(h)))).Status);
        Assert.Contains("PLATFORM_AUTHORITY_MUST_BE_REVOKED_SEPARATELY",(await PrivacyDispositionOwner(h).PreviewAsync(new(h.Tenant,h.User,a.Policy),PrivacySession(h))).Blockers);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_read_recovery_survives_prepare_revocation_close_blocks_late_request_and_approver_revocation_is_visible()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);var input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);var result=await o.RequestAsync(input,s);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="prepare").ExecuteDeleteAsync();Assert.Equal(result,await o.RequestAsync(input,s));
        var late=input with{OperationId=Guid.NewGuid()};Assert.Equal("CLOSED",(await o.OperationAsync(late.OperationId,true,s)).State);Assert.Equal("CLOSED",(await o.RequestAsync(late,s)).State);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.RequestAsync(input with{RequestRef=Guid.NewGuid()},s))).Status);
        await PrivacyDispositionOwner(setup.Approver).DecideAsync(result.PlanId!.Value,new(Guid.NewGuid(),result.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(setup.Approver));
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.ActionCode=="approve").ExecuteDeleteAsync();
        var current=await o.PlanAsync(result.PlanId!.Value,s);Assert.False(current.ApprovalCurrent);Assert.Contains("APPROVER_AUTHORITY_CHANGED",current.CurrentBlockers);
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_unknown_commit_recovers_original_and_completion_failure_rolls_back_plan_and_journal()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var setup=await PrivacyDispositionSetup(h);var input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);fault.Armed=true;
        Assert.Equal("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(h).RequestAsync(input,PrivacySession(h)))).Message);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var fresh=h with{Db=db};var original=await PrivacyDispositionOwner(fresh).OperationAsync(input.OperationId,false,PrivacySession(fresh));Assert.Equal("COMMITTED",original.State);
        var next=await PrivacyDispositionCommand(fresh,setup.Target,setup.Policy);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PrivacyDispositionOwner(fresh).RequestAsync(next,PrivacySession(fresh,_=>throw new InvalidOperationException("simulated logout"))));
        Assert.Equal("NOT_FOUND",(await PrivacyDispositionOwner(fresh).OperationAsync(next.OperationId,false,PrivacySession(fresh))).State);
        Assert.Equal(1,await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_privacy_disposition_v1.plans WHERE actor_tenant={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_approval_and_global_hold_use_same_actual_authority_gate()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);var plan=await PrivacyDispositionOwner(h).RequestAsync(await PrivacyDispositionCommand(h,setup.Target,setup.Policy),PrivacySession(h));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approval=PrivacyDispositionOwner(setup.Approver).DecideAsync(plan.PlanId!.Value,new(Guid.NewGuid(),plan.Version!.Value,"APPROVE",Guid.NewGuid()),PrivacySession(setup.Approver,async _=>{entered.SetResult();await release.Task;}));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var holder=h with{Db=db};
        var hold=PrivacyDispositionOwner(holder).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(holder));
        try{await AssertNativeWait(db,hold);release.TrySetResult();await approval;await hold;var view=await PrivacyDispositionOwner(h).PlanAsync(plan.PlanId!.Value,PrivacySession(h));Assert.False(view.ApprovalCurrent);Assert.Contains("ACTIVE_HOLD",view.CurrentBlockers);}
        finally{release.TrySetResult();}
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_actual_HTTP_preview_and_plan_use_current_platform_session_and_do_not_disclose_profile_values()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,SetHostSettings());var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/platform/privacy-dispositions-v1/preview",new PrivacyDispositionTargetV1(h.Tenant,setup.Target,setup.Policy)));
        var text=await response.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);Assert.DoesNotContain("simulated-private-subject",text);
        var preview=JsonSerializer.Deserialize<PrivacyDispositionPreviewV1>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var saved=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/platform/privacy-dispositions-v1/plans",new PrivacyDispositionPlanCommandV1(Guid.NewGuid(),Guid.NewGuid(),new(h.Tenant,setup.Target,setup.Policy),preview.ScopeDigest)));
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.Contains("COMMITTED",await saved.Content.ReadAsStringAsync());
        Assert.Equal("simulated@example.invalid",await h.Db.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyDisposition_tenant_actor_isolation_policy_and_hold_CAS_rejection_and_revocation_are_authoritative()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);var o=PrivacyDispositionOwner(h);var s=PrivacySession(h);
        var input=await PrivacyDispositionCommand(h,setup.Target,setup.Policy);var result=await o.RequestAsync(input,s);
        Assert.Equal("NOT_FOUND",(await PrivacyDispositionOwner(setup.Approver).OperationAsync(input.OperationId,false,PrivacySession(setup.Approver))).State);
        await using var other=await Seed();await PrivacyDispositionSetup(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(other).PlanAsync(result.PlanId!.Value,PrivacySession(other)))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.PreviewAsync(new(other.Tenant,setup.Target,setup.Policy),s))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.ConfigurePolicyAsync(new(Guid.NewGuid(),setup.Policy,h.Tenant,null,Guid.NewGuid(),true),s))).Status);
        var held=await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),s);
        Assert.Equal(412,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,false,Guid.NewGuid(),null),s))).Status);
        await PrivacyDispositionOwner(setup.Approver).DecideAsync(result.PlanId!.Value,new(Guid.NewGuid(),result.Version!.Value,"REJECT",Guid.NewGuid()),PrivacySession(setup.Approver));
        var rejected=await o.PlanAsync(result.PlanId!.Value,s);Assert.Equal("REJECTED",rejected.State);
        Assert.Equal(409,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>PrivacyDispositionOwner(setup.Approver).DecideAsync(result.PlanId!.Value,new(Guid.NewGuid(),rejected.Version,"APPROVE",Guid.NewGuid()),PrivacySession(setup.Approver)))).Status);
        await o.SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),s);
        var next=await o.RequestAsync(await PrivacyDispositionCommand(h,setup.Target,setup.Policy),s);
        await o.DecideAsync(next.PlanId!.Value,new(Guid.NewGuid(),next.Version!.Value,"REVOKE",Guid.NewGuid()),s);Assert.Equal("REVOKED",(await o.PlanAsync(next.PlanId!.Value,s)).State);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="read").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<PrivacyDispositionExceptionV1>(()=>o.OperationAsync(input.OperationId,false,s))).Status);
    }
}

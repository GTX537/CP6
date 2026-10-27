using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ApprovalTaskAuthorityV2 TaskAuthority(Harness h) => new(h.Db,
        new OperationsHttp { HttpContext = h.Http }, ScopeFence(h), _ => Task.FromResult(
            Guid.Parse(h.Http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)));
    private static async Task TaskPermission(Harness h, Guid reviewer)
    {
        await EngineeringAction(h, "oa-inbox", "approve");
        var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "oa-inbox");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 72 && x.MenuId == menu.MenuId))
            h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
        h.Db.Sys_UserRoles.Add(new() { UserId = reviewer, RoleId = 72 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static void TaskActor(Harness h, Guid actor) => h.Http.User = CurrentPermissionContextTests.Principal(actor, h.Tenant);

    [CurrentPermissionPostgresFact]
    public async Task App_assignment_keeps_original_principal_with_active_delegate_and_freezes_real_rule_candidates()
    {
        await using var h = await Seed(); var (f, reviewer) = await StartSetup(h); await TaskPermission(h, reviewer);
        var delegateUser = Guid.NewGuid();
        h.Db.Sys_Users.Add(new() { Id = delegateUser, UserName = "actual-delegate", Password = "unused", RoleId = 72 });
        var grant = new Wf_FlowDelegate { GrantorId = reviewer, DelegateId = delegateUser, Scope = "binding-flow",
            ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) };
        h.Db.Wf_FlowDelegates.Add(grant); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Starter(h).RunAsync(h.Tenant); var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        Assert.Equal(reviewer, task.AssigneeId);
        var fact = await ApprovalTaskAssignmentsV2.Read(h.Db, ScopeEnvironment, task.Id, default);
        Assert.Equal(reviewer, fact.PrincipalId); Assert.Equal(new[] { reviewer }, fact.Candidates);
        Assert.Equal(ApproverStrategy.Role, fact.Rule.Strategy); Assert.Equal(73, fact.Rule.RoleId);
        Assert.Equal(f.Version, fact.FlowVersionId); Assert.Equal(IntakeBody(f).SubmissionId, fact.SubmissionId);
        Assert.Equal(reviewer, (await h.Db.Wf_FlowFormTos.SingleAsync()).ExpectedHandlerId);
        Assert.DoesNotContain(await h.Db.Wf_FlowHistories.ToArrayAsync(), x => x.Action == "delegate");
        TaskActor(h, delegateUser);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var authority = TaskAuthority(h); var proof = await authority.AcquireAsync(task.Id, reviewer);
        Assert.Equal(reviewer, proof.Evidence.PrincipalId); Assert.Equal(delegateUser, proof.Evidence.ActualHandlerId);
        Assert.Equal(reviewer, proof.Evidence.OnBehalfOfId); Assert.Equal(grant.Id, proof.Evidence.GrantId);
        Assert.NotNull(proof.Evidence.GrantRevision); Assert.Contains("binding-flow", proof.Evidence.GrantBody!);
        await authority.ValidateAsync(proof); await tx.CommitAsync();
        Assert.Equal(0, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status); // Authority is not a vote.
        Assert.Equal(ApprovalCanonicalV2.Bytes(fact), ApprovalCanonicalV2.Bytes(await ApprovalTaskAssignmentsV2.Read(h.Db, ScopeEnvironment, task.Id, default)));
        Assert.Equal((delegateUser, (Guid?)reviewer), await new FlowEngine(h.Db, new ApproverResolver(h.Db)).ResolveActualAssigneeAsync(reviewer)); // Legacy unchanged.
    }

    [CurrentPermissionPostgresFact]
    public async Task App_assignment_delegate_scope_expiry_principal_and_actual_sod_are_current_and_cannot_be_bypassed()
    {
        await using var h = await Seed(); await StartSetup(h); var taskOwner = (await h.Db.Sys_Users.SingleAsync(x => x.RoleId == 73)).Id;
        await TaskPermission(h, taskOwner); await Starter(h).RunAsync(h.Tenant); var task = await h.Db.Wf_FlowTasks.SingleAsync();
        var actor = Guid.NewGuid(); h.Db.Sys_Users.Add(new() { Id = actor, UserName = "delegate", Password = "unused", RoleId = 72 });
        var grant = new Wf_FlowDelegate { GrantorId = taskOwner, DelegateId = actor, Scope = "Binding-flow",
            ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) };
        h.Db.Wf_FlowDelegates.Add(grant); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); TaskActor(h, actor);
        async Task<string> Denied(Guid? behalf)
        {
            await using var tx = await h.Db.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => TaskAuthority(h).AcquireAsync(task.Id, behalf));
            await tx.RollbackAsync(); return error.Message;
        }
        Assert.Equal("RESOURCE_NOT_FOUND", await Denied(null));
        Assert.Equal("DELEGATION_EXPIRED", await Denied(taskOwner)); // Exact case-sensitive Scope.
        await h.Db.Wf_FlowDelegates.Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.Scope, "binding-flow")
            .SetProperty(g => g.ValidTo, DateTime.Now.AddMinutes(-1)));
        Assert.Equal("DELEGATION_EXPIRED", await Denied(taskOwner));
        TaskActor(h, h.User); // Starter cannot act through somebody else's legitimate duty.
        Assert.Equal("SOD_VIOLATION", await Denied(taskOwner));
        TaskActor(h, taskOwner);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var proof = await TaskAuthority(h).AcquireAsync(task.Id); Assert.Null(proof.Evidence.GrantId);
            Assert.Null(proof.Evidence.OnBehalfOfId); await tx.RollbackAsync();
        }
        await h.Db.Sys_UserRoles.Where(x => x.UserId == taskOwner && x.RoleId == 72).ExecuteDeleteAsync();
        Assert.Equal("COMMAND_FORBIDDEN", await Denied(null));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_assignment_authority_is_same_transaction_and_grant_revocation_cannot_commit_old_proof()
    {
        await using var h = await Seed(); var (_, reviewer) = await StartSetup(h); await TaskPermission(h, reviewer);
        var actor = Guid.NewGuid(); h.Db.Sys_Users.Add(new() { Id = actor, UserName = "delegate-race", Password = "unused", RoleId = 72 });
        var grant = new Wf_FlowDelegate { GrantorId = reviewer, DelegateId = actor, Scope = "binding-flow",
            ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) };
        h.Db.Wf_FlowDelegates.Add(grant); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Starter(h).RunAsync(h.Tenant); var task = await h.Db.Wf_FlowTasks.SingleAsync(); TaskActor(h, actor);
        Assert.Equal("SCOPE_TRANSACTION_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => TaskAuthority(h).AcquireAsync(task.Id, reviewer))).Message);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var authority = TaskAuthority(h); var proof = await authority.AcquireAsync(task.Id, reviewer);
            await h.Db.Wf_FlowDelegates.Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => authority.ValidateAsync(proof))).Message);
            await tx.RollbackAsync();
        }
        Assert.True(await h.Db.Wf_FlowDelegates.AnyAsync(x => x.Id == grant.Id && x.Enable));
        await using var peer = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var authority = TaskAuthority(h); var proof = await authority.AcquireAsync(task.Id, reviewer);
            var revoke = peer.Wf_FlowDelegates.Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
            await Task.Delay(150); Assert.False(revoke.IsCompleted);
            await authority.ValidateAsync(proof); await tx.CommitAsync(); await revoke;
        }
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            Assert.Equal("DELEGATION_EXPIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => TaskAuthority(h).AcquireAsync(task.Id, reviewer))).Message);
            await tx.RollbackAsync();
        }
        Assert.Equal(0, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_assignment_failure_after_engine_save_rolls_back_all_and_missing_or_changed_fact_is_rejected()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); await StartSetup(h);
        hook.Before = command => { if (command.CommandText.Contains("INSERT INTO cp6_approval_tasks_v2.assignments"))
            throw new InvalidOperationException("assignment write fault"); return Task.CompletedTask; };
        Assert.Equal("assignment write fault", (await Assert.ThrowsAsync<InvalidOperationException>(() => Starter(h).RunAsync(h.Tenant))).Message);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        hook.Before = command => { if (command.CommandText.Contains("INSERT INTO cp6_approval_tasks_v2.assignments"))
            command.Parameters[7].Value = h.User; return Task.CompletedTask; };
        var mismatch = await Assert.ThrowsAnyAsync<Exception>(() => Starter(h).RunAsync(h.Tenant));
        Assert.Contains("APP_TASK_ASSIGNMENT_MISMATCH", mismatch.ToString());
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        hook.Before = command => {
            if (!command.CommandText.Contains("INSERT INTO cp6_approval_tasks_v2.assignments")) return Task.CompletedTask;
            var body = System.Text.Json.Nodes.JsonNode.Parse((string)command.Parameters[8].Value!)!.AsObject(); body["candidates"] = null;
            var bytes = ApprovalCanonicalV2.Bytes(body); command.Parameters[8].Value = System.Text.Encoding.UTF8.GetString(bytes);
            command.Parameters[9].Value = ApprovalCanonicalV2.Hash(bytes); return Task.CompletedTask;
        };
        var missing = await Assert.ThrowsAnyAsync<Exception>(() => Starter(h).RunAsync(h.Tenant));
        Assert.Contains("APP_TASK_ASSIGNMENT_MISMATCH", missing.ToString()); Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        hook.Before = null; await Starter(h).RunAsync(h.Tenant); var task = await h.Db.Wf_FlowTasks.SingleAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_tasks_v2.assignments WHERE task_id={task.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_tasks_v2.assignments SET principal_id={h.User} WHERE task_id={task.Id}"));
        Assert.Equal(task.AssigneeId, (await ApprovalTaskAssignmentsV2.Read(h.Db, ScopeEnvironment, task.Id, default)).PrincipalId);
        Assert.Equal("APP_TASK_EVIDENCE_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ApprovalTaskAssignmentsV2.Read(h.Db, Guid.NewGuid(), task.Id, default))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_assignment_sod_checks_original_principal_and_current_role_removal_does_not_rewrite_assignment()
    {
        await using var h = await Seed(); var (_, reviewer) = await StartSetup(h, StartSchema.Replace("73", "72"));
        await TaskPermission(h, reviewer);
        h.Db.Wf_FlowDelegates.Add(new() { GrantorId = h.User, DelegateId = reviewer,
            ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddHours(1) });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); await Starter(h).RunAsync(h.Tenant);
        var task = await h.Db.Wf_FlowTasks.SingleAsync(); Assert.Equal(h.User, task.AssigneeId); TaskActor(h, reviewer);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            Assert.Equal("SOD_VIOLATION", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => TaskAuthority(h).AcquireAsync(task.Id, h.User))).Message);
            await tx.RollbackAsync();
        }
        await using var other = await Seed(); var (_, duty) = await StartSetup(other); await TaskPermission(other, duty);
        await Starter(other).RunAsync(other.Tenant); var actualTask = await other.Db.Wf_FlowTasks.SingleAsync(); TaskActor(other, duty);
        var original = await ApprovalTaskAssignmentsV2.Read(other.Db, ScopeEnvironment, actualTask.Id, default);
        await other.Db.Sys_Users.Where(x => x.Id == duty).ExecuteUpdateAsync(x => x.SetProperty(u => u.RoleId, 72));
        await using var transaction = await other.Db.Database.BeginTransactionAsync();
        Assert.Equal("TASK_DUTY_REVOKED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => TaskAuthority(other).AcquireAsync(actualTask.Id))).Message);
        Assert.Equal(ApprovalCanonicalV2.Bytes(original), ApprovalCanonicalV2.Bytes(await ApprovalTaskAssignmentsV2.Read(other.Db, ScopeEnvironment, actualTask.Id, default)));
        await transaction.RollbackAsync();
    }
}

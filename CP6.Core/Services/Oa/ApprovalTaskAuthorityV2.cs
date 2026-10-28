using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

internal sealed record ApprovalTaskAuthorityEvidenceV2(ApprovalTaskAssignmentV2 Assignment, Guid PrincipalId,
    Guid ActualHandlerId, Guid? OnBehalfOfId, Guid? GrantId, string? GrantRevision, string? GrantBody,
    Guid[] SeparationSubjectIds, ScopeAuthorizationSnapshotV1 Scope, string PermissionJson);
internal sealed record ApprovalTaskAuthorityLeaseV2(ScopeAuthorizationLeaseV1 Scope, ApprovalTaskAuthorityEvidenceV2 Evidence);

/// <summary>Transaction participant for the native TxD writer. This captures current human authority;
/// it does not cast a vote, satisfy the terminal business policy or authorize an external callback.</summary>
public sealed class ApprovalTaskAuthorityV2(CP6Context db, IHttpContextAccessor http,
    ScopeAuthorizationFenceV1 fence, Func<CancellationToken, Task<Guid>> currentActor)
{
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 403) => new(code, status);
    private async Task<UserPermissionContext> User(Guid id, CancellationToken ct, bool write = true)
    {
        if (id == Guid.Empty || !await db.Sys_Users.AsNoTracking().AnyAsync(u => u.Id == id && u.Enable
            && (u.LockedUntil == null || u.LockedUntil <= DateTime.Now), ct)) throw Error("COMMAND_FORBIDDEN");
        var permission = await PostgreSqlPermissionReader.ReadAsync(db, id, ct);
        var action = write ? "approve" : "query";
        if (!permission.MenuKeys.Contains("oa-inbox") || !permission.ActionKeys.Contains("oa-inbox:" + action)
            || !await (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
                       where m.Enable && m.MenuKey == "oa-inbox" && a.ActionCode == action select a).AnyAsync(ct))
            throw Error("COMMAND_FORBIDDEN");
        return permission;
    }
    internal async Task<Guid> CurrentActor(bool write, CancellationToken ct)
    {
        var actual = await currentActor(ct);
        if (actual != CurrentPermissionContext.RequireCurrentUser(db, http)
            || http.HttpContext!.User.HasClaim(c => c.Type == "impersonator_id")) throw Error("AUTHENTICATION_REQUIRED", 401);
        var exp = http.HttpContext.User.FindFirst("exp")?.Value;
        if (exp != null && (!long.TryParse(exp, out var seconds) || seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            throw Error("AUTHENTICATION_REQUIRED", 401);
        if (!await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == db.CurrentTenantId && t.Enable
            && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct)) throw Error("AUTHENTICATION_REQUIRED", 401);
        await User(actual, ct, write);
        return actual;
    }
    private sealed record Original(byte[] Payload);
    private async Task<ApprovalTaskAuthorityEvidenceV2> Check(Guid taskId, Guid? onBehalfOf,
        ScopeAuthorizationLeaseV1 lease, CancellationToken ct, bool requirePending = true)
    {
        var actual = await CurrentActor(true, ct);
        var actualPermission = await User(actual, ct);
        var principal = onBehalfOf ?? actual;
        var principalPermission = principal == actual ? actualPermission : await User(principal, ct);
        var task = await db.Wf_FlowTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw Error("RESOURCE_NOT_FOUND", 404);
        var instance = await db.Wf_FlowInstances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == task.InstanceId, ct);
        if (instance == null || instance.Creator != FlowEngine.ApprovalRuntimeV2 || task.AssigneeId != principal)
            throw Error("RESOURCE_NOT_FOUND", 404);
        if (requirePending && (instance.Status != 0 || task.Status != 0)) throw Error("TASK_NOT_PENDING", 409);
        var assignment = await ApprovalTaskAssignmentsV2.Read(db, fence.EnvironmentId, taskId, ct);
        if (assignment.InstanceId != instance.Id || assignment.PrincipalId != principal || assignment.TokenId != task.TokenId
            || assignment.FlowVersionId != instance.FlowDefVersionId || assignment.NodeId != task.NodeId
            || assignment.StageIndex != task.StageIndex || assignment.StageRound != task.StageRound)
            throw Error("APP_TASK_EVIDENCE_UNAVAILABLE", 503);
        var raw = await db.Database.SqlQuery<Original>($"""
            SELECT p.payload AS "Payload" FROM cp6_approval_start_v2.starts s JOIN cp6_approval_intake_v2.submissions p
              ON p.tenant_id=s.tenant_id AND p.environment_id=s.environment_id AND p.owner=s.owner AND p.id=s.submission_id
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={fence.EnvironmentId} AND s.instance_id={instance.Id}
            """).SingleAsync(ct);
        var original = ApprovalSubmissionWireV2.Parse(raw.Payload).Value;
        if (original.SubmissionId != assignment.SubmissionId) throw Error("APP_TASK_EVIDENCE_UNAVAILABLE", 503);
        if (original.SeparationSubjectIds.Contains(principal) || original.SeparationSubjectIds.Contains(actual))
            throw Error("SOD_VIOLATION");
        var current = await new ApproverResolver(db, exactConditions: true).ResolveAsync(assignment.Rule,
            new() { StarterUserId = instance.StarterId, VarsJson = instance.VarsJson });
        if (!current.ApproverIds.Contains(principal)) throw Error("TASK_DUTY_REVOKED");
        Guid? grantId = null; string? grantRevision = null, grantBody = null;
        if (principal != actual)
        {
            var now = DateTime.Now;
            var candidates = await db.Wf_FlowDelegates.AsNoTracking().Where(g => g.GrantorId == principal
                && g.DelegateId == actual && g.Enable && g.ValidFrom <= now && now <= g.ValidTo)
                .OrderByDescending(g => g.CreateDate).ThenBy(g => g.Id).ToArrayAsync(ct);
            // Scope is the exact FlowKey. Database collation must not broaden this grant.
            var grant = candidates.FirstOrDefault(g => string.IsNullOrEmpty(g.Scope) || string.Equals(g.Scope, instance.FlowKey, StringComparison.Ordinal))
                ?? throw Error("DELEGATION_EXPIRED");
            grantId = grant.Id;
            grantBody = ApprovalBindingOwnerV2.Serialize(grant);
            // The native fence changes for every grant mutation, including delete/recreate/ABA.
            // The exact source bytes are retained with the version; no mutable latest reference.
            grantRevision = lease.Snapshot.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(grant));
        }
        var permission = PermissionEvidence(actualPermission,principalPermission);
        return new(assignment, principal, actual, principal == actual ? null : principal, grantId, grantRevision,
            grantBody, original.SeparationSubjectIds, lease.Snapshot, permission);
    }
    internal static string PermissionEvidence(UserPermissionContext actual,UserPermissionContext principal)
    {
        // SQL row order is not an authority change. Keep every permission fact, ordering only
        // set-valued collections; actual membership, scope and field changes still differ.
        static object Facts(UserPermissionContext p)=>new {p.UserId,p.UserName,p.DeptId,p.DeptPath,
            RoleIds=p.RoleIds.Order().ToArray(),MenuKeys=p.MenuKeys.Order(StringComparer.Ordinal).ToArray(),
            ActionKeys=p.ActionKeys.Order(StringComparer.Ordinal).ToArray(),p.DataScopes,p.FieldPerms,
            CustomDeptIds=p.CustomDeptIds.ToDictionary(x=>x.Key,x=>x.Value.Order().ToArray())};
        return System.Text.Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(new {
            profile="oa.app.current-task-authority/2.0",actual=Facts(actual),principal=Facts(principal),action="oa-inbox:approve" }));
    }
    internal async Task<ApprovalTaskAuthorityLeaseV2> AcquireAsync(Guid taskId, Guid? onBehalfOf = null, CancellationToken ct = default)
    {
        var scope = await fence.AcquireAsync(ct);
        var evidence = await Check(taskId, onBehalfOf, scope, ct);
        await fence.ValidateAsync(scope, ct);
        return new(scope, evidence);
    }
    internal async Task ValidateAsync(ApprovalTaskAuthorityLeaseV2 lease, CancellationToken ct = default, bool afterVote = false)
    {
        await fence.ValidateAsync(lease.Scope, ct);
        var current = await Check(lease.Evidence.Assignment.TaskId, lease.Evidence.OnBehalfOfId, lease.Scope, ct, !afterVote);
        if (!ApprovalCanonicalV2.Bytes(current).SequenceEqual(ApprovalCanonicalV2.Bytes(lease.Evidence)))
            throw Error("TASK_AUTHORITY_CHANGED", 409);
        await fence.ValidateAsync(lease.Scope, ct);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlowPublicationPolicyCommandV2(Guid OperationId, string ExpectedVersion, bool Enabled, bool DistinctAuthor, int[] ForbiddenRoles, string Reason);
public sealed record FlowPublicationPolicyV2(bool Enabled, bool DistinctAuthor, int[] ForbiddenRoles, Guid Version, string Profile);
public sealed record FlowPublicationPolicyResultV2(Guid OperationId, Guid TenantId, Guid EnvironmentId, FlowPublicationPolicyV2 Policy);


public sealed partial class FlowAuthoringOwnerV2
{
    private const string PublicationPolicyOwner = "Main.Workflow.PublicationPolicy.v2", PublicationPolicyContract = "Main.Workflow.PublicationPolicy.configure.v2";
    private Task<FlowPublicationPolicyV2?> ReadPublicationPolicy(CancellationToken ct) => db.Database.SqlQuery<FlowPublicationPolicyV2>($"""
        SELECT enabled AS "Enabled",distinct_author AS "DistinctAuthor",forbidden_roles AS "ForbiddenRoles",row_version AS "Version",profile AS "Profile"
        FROM cp6_workflow_authoring_v2.publication_policy WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """).SingleOrDefaultAsync(ct);
    public async Task<FlowPublicationPolicyV2?> PublicationPolicyAsync(CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct);
        await Action(await Actor(ct), "pub-role-perm", "edit", ct); return await ReadPublicationPolicy(ct);
    }
    private FlowPublicationPolicyResultV2 PublicationPolicyOriginal(MainOwnerOperationJournalV1.Original original, Guid actor, Guid operation, byte[]? input)
    {
        if (original.Actor != actor || original.Contract != PublicationPolicyContract) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BODY_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<FlowPublicationPolicyResultV2>(original.Result) ?? throw Error("ORIGINAL_RESULT_INVALID");
        if (result.OperationId != operation || result.EnvironmentId != fence.EnvironmentId || result.TenantId != db.CurrentTenantId) throw Error("ORIGINAL_RESULT_INVALID");
        return result;
    }
    public async Task<FlowPublicationPolicyResultV2?> PublicationPolicyOriginalAsync(Guid operation, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var original = await new MainOwnerOperationJournalV1(db).Read(PublicationPolicyOwner, operation, ct);
        return original == null ? null : PublicationPolicyOriginal(original, actor.UserId, operation, null);
    }
    public async Task<FlowPublicationPolicyResultV2> ConfigurePublicationAsync(FlowPublicationPolicyCommandV2 command, CancellationToken ct = default)
    {
        if (command.OperationId == Guid.Empty || command.ForbiddenRoles == null || command.ForbiddenRoles.Length > 100
            || command.ForbiddenRoles.Distinct().Count() != command.ForbiddenRoles.Length || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256
            || command.ExpectedVersion != "absent" && !Guid.TryParseExact(command.ExpectedVersion, "D", out _)) throw Error("POLICY_INPUT_INVALID", 400);
        Entry(); var input = MainOwnerOperationJournalV1.Encode(new { EnvironmentId = fence.EnvironmentId, Command = command });
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Root(ct); var actor = await Actor(ct); var journal = new MainOwnerOperationJournalV1(db);
            var previous = await journal.Read(PublicationPolicyOwner, command.OperationId, ct);
            if (previous != null) return PublicationPolicyOriginal(previous, actor.UserId, command.OperationId, input);
            await fence.AcquireAsync(ct); await Action(actor, "pub-role-perm", "edit", ct);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_workflow_authoring_v2.publication_ready() AS \"Value\"").SingleAsync(ct)) throw Error("PUBLICATION_WRITER_COVERAGE_UNAVAILABLE");
            if (await db.Sys_Roles.CountAsync(r => command.ForbiddenRoles.Contains(r.RoleId), ct) != command.ForbiddenRoles.Length) throw Error("ROLE_REFERENCE_INVALID", 422);
            // Register the explicitly configured capability; no role receives it automatically.
            var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "oa-designer" && m.Enable, ct)
                ?? throw Error("PUBLICATION_CAPABILITY_UNAVAILABLE");
            if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == "publish", ct))
            {
                db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "publish", ActionName = "oa.definition.publish" });
                await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            }
            var old = await ReadPublicationPolicy(ct);
            if (command.ExpectedVersion != (old?.Version.ToString("D") ?? "absent")) throw Error("POLICY_CHANGED", 409);
            if (old == null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_workflow_authoring_v2.publication_policy(tenant_id,environment_id,enabled,distinct_author,forbidden_roles,profile)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{command.Enabled},{command.DistinctAuthor},{command.ForbiddenRoles},'DIRECT_PUBLICATION_V1')
                    """, ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_workflow_authoring_v2.publication_policy SET enabled={command.Enabled},distinct_author={command.DistinctAuthor},forbidden_roles={command.ForbiddenRoles}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                    """, ct);
            var saved = await ReadPublicationPolicy(ct) ?? throw Error("PUBLICATION_POLICY_REQUIRED");
            var proof = await fence.AcquireAsync(ct); // The policy writer has advanced the same Scope epoch.
            var result = new FlowPublicationPolicyResultV2(command.OperationId, db.CurrentTenantId, fence.EnvironmentId, saved);
            await journal.Store(PublicationPolicyOwner, PublicationPolicyContract, command.OperationId, actor.UserId, input, result, [proof.Snapshot.PermissionDomain],
                new { command.Reason, command.ExpectedVersion, Policy = saved, Scope = proof.Snapshot }, ct);
            var finalActor = await Actor(ct);
            if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
            await Action(finalActor, "pub-role-perm", "edit", ct);
            if ((await ReadPublicationPolicy(ct))?.Version != saved.Version) throw Error("POLICY_CHANGED", 409);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_workflow_authoring_v2.publication_ready() AS \"Value\"").SingleAsync(ct)) throw Error("PUBLICATION_WRITER_COVERAGE_UNAVAILABLE");
            await fence.ValidateAsync(proof, ct); CompletionClock(); ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, error);
            throw;
        }
    }
}

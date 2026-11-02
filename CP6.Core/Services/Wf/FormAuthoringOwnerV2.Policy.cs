using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormAuthoringPolicyCommandV2(Guid OperationId, string ExpectedVersion, bool Enabled, int[] ForbiddenRoles, string Reason);
public sealed record FormAuthoringPolicyV2(bool Enabled, int[] ForbiddenRoles, Guid Version, string Profile);
public sealed record FormAuthoringPolicyResultV2(Guid OperationId, Guid TenantId, Guid EnvironmentId, FormAuthoringPolicyV2 Policy);
public sealed record FormAuthoringEntryV2(bool CanCreate, string State, string[] WritableFields);

public sealed partial class FormAuthoringOwnerV2
{
    private const string PolicyOwner = "Main.Workflow.FormAuthoringPolicy.v2", PolicyContract = "Main.Workflow.FormAuthoringPolicy.configure.v2";
    private Task<FormAuthoringPolicyV2?> ReadPolicy(CancellationToken ct) => db.Database.SqlQuery<FormAuthoringPolicyV2>($"""
        SELECT enabled AS "Enabled",forbidden_roles AS "ForbiddenRoles",row_version AS "Version",profile AS "Profile"
        FROM cp6_form_authoring_v2.policy WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """).SingleOrDefaultAsync(ct);
    public async Task<FormAuthoringEntryV2> EntryAsync(CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var policy = await ReadPolicy(ct);
        if (policy == null || !policy.Enabled) { CompletionClock(); return new(false, "AUTHORING_POLICY_REQUIRED", []); }
        await fence.AcquireAsync(ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
        var virtualHead = new Head(Guid.Empty, Guid.Empty, "", actor.UserId, actor.DeptId, Guid.Empty, "", "", null, null, actor.UserId);
        var permission = await Permission(actor, virtualHead, true, true, ct);
        var writable = Fields.Where(x => permission.Fields[x].CanWrite).ToArray();
        CompletionClock();
        return new(writable.Length == Fields.Length, writable.Length == Fields.Length ? "READY" : "FIELD_WRITE_FORBIDDEN", writable);
    }
    public async Task<FormAuthoringPolicyV2?> PolicyAsync(CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct);
        await Action(await Actor(ct), "pub-role-perm", "edit", ct); var policy = await ReadPolicy(ct); CompletionClock(); return policy;
    }
    private FormAuthoringPolicyResultV2 PolicyOriginal(MainOwnerOperationJournalV1.Original original, Guid actor, Guid operation, byte[]? input)
    {
        if (original.Actor != actor || original.Contract != PolicyContract) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BODY_CONFLICT", 409);
        return JsonSerializer.Deserialize<FormAuthoringPolicyResultV2>(original.Result)!;
    }
    public async Task<FormAuthoringPolicyResultV2?> PolicyOriginalAsync(Guid operation, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var original = await new MainOwnerOperationJournalV1(db).Read(PolicyOwner, operation, ct);
        CompletionClock();
        return original == null ? null : PolicyOriginal(original, actor.UserId, operation, null);
    }
    public async Task<FormAuthoringPolicyResultV2> ConfigureAsync(FormAuthoringPolicyCommandV2 command, CancellationToken ct = default)
    {
        if (command.OperationId == Guid.Empty || command.ForbiddenRoles == null || command.ForbiddenRoles.Length > 100
            || command.ForbiddenRoles.Distinct().Count() != command.ForbiddenRoles.Length || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256
            || command.ExpectedVersion != "absent" && !Guid.TryParseExact(command.ExpectedVersion, "D", out _)) throw Error("POLICY_INPUT_INVALID", 400);
        Entry(); var input = MainOwnerOperationJournalV1.Encode(new { EnvironmentId = fence.EnvironmentId, Command = command });
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Root(ct); var actor = await Actor(ct); var journal = new MainOwnerOperationJournalV1(db);
            var previous = await journal.Read(PolicyOwner, command.OperationId, ct);
            if (previous != null) { CompletionClock(); return PolicyOriginal(previous, actor.UserId, command.OperationId, input); }
            await fence.AcquireAsync(ct); await Action(actor, "pub-role-perm", "edit", ct);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
            if (await db.Sys_Roles.CountAsync(r => command.ForbiddenRoles.Contains(r.RoleId), ct) != command.ForbiddenRoles.Length) throw Error("ROLE_REFERENCE_INVALID", 422);
            var old = await ReadPolicy(ct);
            if (command.ExpectedVersion != (old?.Version.ToString("D") ?? "absent")) throw Error("POLICY_CHANGED", 409);
            if (old == null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_form_authoring_v2.policy(tenant_id,environment_id,enabled,forbidden_roles,profile)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{command.Enabled},{command.ForbiddenRoles},'DIRECT_FORM_AUTHORING_V1')
                    """, ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_form_authoring_v2.policy SET enabled={command.Enabled},forbidden_roles={command.ForbiddenRoles}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                    """, ct);
            var saved = await ReadPolicy(ct) ?? throw Error("AUTHORING_POLICY_REQUIRED");
            var proof = await fence.AcquireAsync(ct); // The policy writer has advanced the same Scope epoch.
            var result = new FormAuthoringPolicyResultV2(command.OperationId, db.CurrentTenantId, fence.EnvironmentId, saved);
            await journal.Store(PolicyOwner, PolicyContract, command.OperationId, actor.UserId, input, result, [proof.Snapshot.PermissionDomain],
                new { command.Reason, command.ExpectedVersion, Policy = saved, Scope = proof.Snapshot }, ct);
            var finalActor = await Actor(ct);
            if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
            await Action(finalActor, "pub-role-perm", "edit", ct);
            if ((await ReadPolicy(ct))?.Version != saved.Version) throw Error("POLICY_CHANGED", 409);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
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

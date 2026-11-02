using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormActivationBodyV2([property: JsonRequired] bool Enabled, string ExpectedActivationRevision, Guid PublishedVersionId, string Reason);
public sealed record FormActivationV2(Guid DefinitionId, string Key, Guid Revision, bool Enabled, Guid? PublishedVersionId,
    DateTimeOffset? ChangedAtUtc, bool InflightUnchanged = true, string ConsumerState = "PERSONAL_FORM_PINNED_RUNTIME_ADOPTED");
public sealed record FormActivationHistoryV2(Guid Revision, Guid PreviousRevision, bool Enabled, Guid PublishedVersionId,
    Guid ActorId, string Reason, DateTimeOffset ChangedAtUtc);
public sealed record FormActivationChoiceV2(Guid VersionId, int Number, string? Name);
public sealed record FormActivationPreviewV2(FormActivationV2 Current, FormActivationChoiceV2[] Versions,
    int Page, string[] AffectedEntries, int? InflightCount = null,DefinitionRuntimeImpactV2? Impact = null);
public sealed record FormActivationResultV2(string CommandId, string Outcome, Guid? ResourceId, string? ResourceVersion,
    FormActivationV2? Result, DateTimeOffset? RecordedAtUtc, string? CorrelationId);

public sealed partial class FormAuthoringOwnerV2
{
    public const string ActivationOperation = "FORM_SET_ACTIVATION";
    private const string AdminMenu = "oa-flow-admin";
    private sealed record ActivationRow(Guid Revision, bool Enabled, Guid? PublishedVersionId, DateTimeOffset? ChangedAtUtc);
    private Task<ActivationRow> Activation(Guid head, CancellationToken ct) => db.Database.SqlQuery<ActivationRow>($"""
        SELECT revision AS "Revision",enabled AS "Enabled",published_version_id AS "PublishedVersionId",changed_at AS "ChangedAtUtc"
        FROM cp6_form_authoring_v2.activation_current WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head}
        """).SingleAsync(ct);
    private static FormActivationV2 ActivationView(Head head, ActivationRow row) => new(head.Id, head.Key, row.Revision, row.Enabled, row.PublishedVersionId, row.ChangedAtUtc);
    private async Task ActivationReady(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.activation_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("ACTIVATION_WRITER_COVERAGE_UNAVAILABLE");
    }
    private async Task<FieldPermissionDecisionV1> ActivationPermission(UserPermissionContext actor, Head head, bool write, CancellationToken ct)
    {
        var fields = await Permission(actor, head, false, false, ct, AdminMenu);
        if (write) await Action(actor, AdminMenu, "form-enable", ct);
        return fields;
    }
    public Task<FormAuthoringViewV2[]> ActivationListAsync(int page = 1, CancellationToken ct = default) => ListAsync(page, ct, AdminMenu);
    public async Task<FormActivationPreviewV2> ActivationPreviewAsync(string key, int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000) throw Error("PAGE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var fields = await ActivationPermission(actor, head, false, ct); var current = ActivationView(head, await Activation(head.Id, ct));
        var rows = await db.Database.SqlQuery<PublicationRow>($"""
            SELECT id AS "Id",snapshot_json AS "Snapshot",version_number AS "Number" FROM cp6_form_authoring_v2.versions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            """).OrderByDescending(x => x.Number).Skip((page - 1) * 100).Take(100).ToArrayAsync(ct);
        var versions = rows.Select(r => new FormActivationChoiceV2(r.Id, r.Number, fields.Fields["Name"].CanRead
            ? JsonSerializer.Deserialize<FormPublicationV2>(r.Snapshot, Json)?.Fields.GetValueOrDefault("Name") : null)).ToArray();
        var impact=await DefinitionRuntimeImpactReaderV2.Read(db,fence.EnvironmentId,head.Id,true,ct);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
        return new(current, versions, page,DefinitionRuntimeImpactReaderV2.Entries(impact),impact.RunningInstances+impact.SuspendedInstances,impact);
    }
    public async Task<FormActivationHistoryV2[]> ActivationHistoryAsync(string key, int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000) throw Error("PAGE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        await ActivationPermission(actor, head, false, ct);
        var rows = await db.Database.SqlQuery<FormActivationHistoryV2>($"""
            SELECT revision AS "Revision",previous_revision AS "PreviousRevision",enabled AS "Enabled",published_version_id AS "PublishedVersionId",
              actor_id AS "ActorId",reason AS "Reason",changed_at AS "ChangedAtUtc" FROM cp6_form_authoring_v2.activation_history
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            """).OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Revision).Skip((page - 1) * 100).Take(100).ToArrayAsync(ct);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock(); return rows;
    }
    public async Task<FormActivationResultV2> ActivationOriginalAsync(string key, string command, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(command, "\\A[A-Za-z0-9_.-]{1,100}\\z")) throw Error("COMMAND_SCOPE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        await ActivationPermission(actor, head, false, ct); var old = await LifecycleOriginal(actor.UserId, key, ActivationOperation, command, ct);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
        return old == null ? new(command, "NOT_OBSERVED", null, null, null, null, null)
            : JsonSerializer.Deserialize<FormActivationResultV2>(old.ResultJson, Json)!;
    }
    private async Task<FormPublicationV2> ActivationSource(Head head, Guid id, CancellationToken ct)
    {
        var row = await Published(head.Id, id, ct) ?? throw Error("VERSION_NOT_FOUND", 404);
        var p = JsonSerializer.Deserialize<FormPublicationV2>(row.Snapshot, Json)!;
        return p;
    }
    private void ActivationDependencies(Head head, FormPublicationV2 p, CancellationToken ct)
    {
        // Validate the chosen immutable version, never the current draft or latest publication.
        var source = head with { DraftId = p.VersionId, Name = p.Fields["Name"]!, SchemaJson = p.Fields["SchemaJson"]!,
            Category = p.Fields["Category"], SubCategory = p.Fields["SubCategory"], Number = p.Version };
        var inputs = PublicationInputs(source, ct);
        if (inputs.Diagnostics.Length > 0 || !inputs.Dependencies.SequenceEqual(p.DependencyRefs)) throw Error("ACTIVATION_DEPENDENCY_CHANGED", 422);
    }
    public async Task<FormActivationResultV2> SetActivationAsync(string key, byte[] raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Contains('\0')) throw Error("DEFINITION_KEY_INVALID", 400);
        var command = ParseLifecycle<FormActivationBodyV2>(raw); var body = command.Body;
        if (body.ExpectedActivationRevision != command.Expected || body.PublishedVersionId == Guid.Empty
            || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 500 || body.Reason.Contains('\0')) throw Error("COMMAND_SHAPE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Root(ct); var actor = await Actor(ct); var original = await LifecycleOriginal(actor.UserId, key, ActivationOperation, command.Id, ct);
            if (original != null)
            {
                var h = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
                await ActivationPermission(actor, h, false, ct);
                if (original.BodyDigest != command.Digest) throw Error("COMMAND_BODY_CONFLICT", 409);
                if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
                return JsonSerializer.Deserialize<FormActivationResultV2>(original.ResultJson, Json)!;
            }
            var beforeScope = await fence.AcquireAsync(ct); await ActivationReady(ct);
            var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
            var fields = await ActivationPermission(actor, head, true, ct); var current = await Activation(head.Id, ct);
            if (command.Expected != current.Revision.ToString("D")) throw Error("ACTIVATION_CHANGED", 409);
            var source = await ActivationSource(head, body.PublishedVersionId, ct);
            if (body.Enabled)
            {
                if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("FIELD_READ_FORBIDDEN", 403);
                ActivationDependencies(head, source, ct);
            }
            else if (current.PublishedVersionId != body.PublishedVersionId) throw Error("ACTIVATION_CHANGED", 409);
            var at = DateTimeOffset.UtcNow; var revision = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.activation_history(tenant_id,environment_id,head_id,revision,previous_revision,enabled,published_version_id,actor_id,reason,changed_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{head.Id},{revision},{current.Revision},{body.Enabled},{body.PublishedVersionId},{actor.UserId},{body.Reason},{at})
                """, ct);
            await fence.ValidateAsync(beforeScope, ct);
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_form_authoring_v2.activation_current SET revision={revision},enabled={body.Enabled},published_version_id={body.PublishedVersionId},changed_at={at}
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} AND revision={current.Revision}
                """, ct);
            if (changed != 1) throw Error("ACTIVATION_CHANGED", 409);
            var scope = await fence.AcquireAsync(ct); // This authority writer advanced the shared epoch.
            var next = await Activation(head.Id, ct); var result = new FormActivationResultV2(command.Id, "SUCCEEDED", head.Id, revision.ToString("D"), ActivationView(head, next), at, command.Correlation);
            var resultJson = JsonSerializer.Serialize(result, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id,canonical_version,body_digest,raw_body,raw_digest,result_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{ActivationOperation},{key},{command.Id},'WFC-CJ-1',{command.Digest},{command.Raw},{MainOwnerOperationJournalV1.Digest(command.Raw)},{resultJson},{at})
                """, ct);
            var finalActor = await Actor(ct); if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
            var finalFields = await ActivationPermission(finalActor, head, true, ct);
            if (body.Enabled)
            {
                if (Fields.Any(f => !finalFields.Fields[f].CanRead)) throw Error("FIELD_READ_FORBIDDEN", 403);
                ActivationDependencies(head, source, ct);
            }
            var evidence = JsonSerializer.Serialize(new { ActualActor = actor.UserId, EffectiveActor = actor.UserId, CheckedAtUtc = DateTimeOffset.UtcNow, BeforeScope = beforeScope.Snapshot, Scope = scope.Snapshot, finalActor.RoleIds,
                ResourceScope = finalActor.DataScopes.GetValueOrDefault(Resource, 1), finalActor.DeptId, finalActor.DeptPath, finalActor.CustomDeptIds,
                FieldSources = finalFields.Input.Versions, FieldRows = finalFields.Input.Rows, head.OwnerId, source.VersionId, source.SchemaDigest,
                source.DependencyRefs, PreviousActivation = current, Activation = next, body.Reason, command.Digest, RawDigest = MainOwnerOperationJournalV1.Digest(command.Raw) }, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.audit(tenant_id,environment_id,actor_id,operation,resource_key,command_id,evidence_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{ActivationOperation},{key},{command.Id},{evidence},{at})
                """, ct);
            if (await Activation(head.Id, ct) != next || await Heads().SingleAsync(x => x.Key == key, ct) != (head with { Enabled = next.Enabled, ActivationRevision = next.Revision })) throw Error("ACTIVATION_CHANGED", 409);
            await ActivationReady(ct); await fence.ValidateAsync(scope, ct); CompletionClock(); ct.ThrowIfCancellationRequested();
            committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, error);
            if (error is Npgsql.PostgresException { SqlState: "23505" }) throw Error("ACTIVATION_CHANGED", 409);
            throw;
        }
    }
}

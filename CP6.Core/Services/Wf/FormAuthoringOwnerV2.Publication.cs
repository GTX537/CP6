using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormPublicationPreviewInputV2(Guid DraftVersionId, string ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormPublicationBodyV2(Guid DraftVersionId, Guid PreviewId, string PreviewDigest, string ExpectedVersion, string Comment);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormNextDraftBodyV2(Guid SourceVersionId);
public sealed record FormPublicationPreviewV2(Guid Id, string Digest, Guid DraftVersionId, string ExpectedVersion,
    string SchemaDigest, DateTimeOffset ExpiresAtUtc, string[] Diagnostics, FlowDependencyRefV2[] Dependencies,
    bool CanPublish, int? AffectedDraftCount = null, string ConsumerState = "PERSONAL_FORM_PINNED_RUNTIME_ADOPTED",DefinitionRuntimeImpactV2? Impact = null);
public sealed record FormPublicationV2(Guid DefinitionId, Guid VersionId, int Version, string Key,
    Dictionary<string, string?> Fields, string? SchemaDigest, DateTimeOffset PublishedAtUtc, Guid PublisherRef,
    FlowDependencyRefV2[] DependencyRefs, string SourceDraftToken, bool ActivationChanged = false, string EvidenceState = "ORIGINAL_PUBLICATION");
public sealed record FormLifecycleValueV2(FormPublicationV2? Publication, FormAuthoringViewV2? Draft);
public sealed record FormLifecycleResultV2(string CommandId, string Outcome, Guid? ResourceId, string? ResourceVersion,
    FormLifecycleValueV2? Result, DateTimeOffset? RecordedAtUtc, string? CorrelationId);

public sealed partial class FormAuthoringOwnerV2
{
    internal Func<DateTimeOffset> PublicationClock { get; set; } = () => DateTimeOffset.UtcNow;
    public const string PublishOperation = "FORM_PUBLISH", NextDraftOperation = "FORM_CREATE_DRAFT";
    private sealed record LifecycleCommand<T>(string Id, string Expected, T Body, string Digest, byte[] Raw, string? Correlation);
    private sealed record PreviewRow(Guid Actor, Guid HeadId, Guid DraftId, Guid Token, string Digest, DateTimeOffset Expires, string Snapshot);
    private sealed record PublicationRow(Guid Id, string Snapshot, int Number);
    private async Task PublicationReady(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.publication_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("PUBLICATION_WRITER_COVERAGE_UNAVAILABLE");
    }
    private async Task<(FieldPermissionDecisionV1 Fields, FormPublicationPolicyV2 Policy)> PublishingPermission(UserPermissionContext actor, Head head, CancellationToken ct)
    {
        var fields = await Permission(actor, head, true, false, ct);
        await Action(actor, "oa-designer", "form-publish", ct);
        var policy = await ReadPublicationPolicy(ct) ?? throw Error("PUBLICATION_POLICY_REQUIRED");
        if (!policy.Enabled || policy.Profile != "DIRECT_FORM_PUBLICATION_V1") throw Error("PUBLICATION_POLICY_DISABLED", 403);
        if (policy.ForbiddenRoles.Intersect(actor.RoleIds).Any() || policy.DistinctAuthor && head.SavedBy == actor.UserId)
            throw Error("PUBLICATION_SOD_FORBIDDEN", 403);
        if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("FIELD_READ_FORBIDDEN", 403);
        return (fields, policy);
    }
    private static LifecycleCommand<T> ParseLifecycle<T>(byte[] raw)
    {
        try
        {
            using var doc = CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Parse(raw); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId"))) throw new JsonException();
            var id = root.GetProperty("commandId").GetString(); var expected = root.GetProperty("expectedVersion").GetString();
            if (id == null || !Regex.IsMatch(id, "\\A[A-Za-z0-9_.-]{1,100}\\z") || !Guid.TryParseExact(expected, "D", out var token) || token == Guid.Empty) throw new JsonException();
            var body = root.GetProperty("body"); var typed = body.Deserialize<T>(Json) ?? throw new JsonException();
            var correlation = root.TryGetProperty("clientCorrelationId", out var c) ? c.GetString() : null;
            if (correlation?.Length > 100) throw new JsonException();
            return new(id, expected!, typed, CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Digest(body, expected), raw.ToArray(), correlation);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { throw Error("COMMAND_SHAPE_INVALID", 400); }
    }
    private static string SchemaDigest(string schema) => MainOwnerOperationJournalV1.Digest(Encoding.UTF8.GetBytes(schema));
    private static (string[] Diagnostics, FlowDependencyRefV2[] Dependencies) PublicationInputs(Head head, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Saved schemas already crossed the W268 boundary. Reuse that internal contract;
        // only external source qualification is a new publication concern here.
        var schema = JsonSerializer.Deserialize<FormSchema>(head.SchemaJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var uploads = schema.Fields.Where(f => f.Type.Equals("upload",StringComparison.OrdinalIgnoreCase)).ToArray();
        return (uploads.Select(f => "FORM_FILE_SOURCE_REQUIRED:" + f.Name).ToArray(),
            uploads.Select(f => new FlowDependencyRefV2("FILE",f.Name,"file-owner",null,"REQUIRED")).ToArray());
    }
    private static string PreviewDigest(Head head, Guid policy, string[] diagnostics, FlowDependencyRefV2[] dependencies)
        => MainOwnerOperationJournalV1.Digest(JsonSerializer.SerializeToUtf8Bytes(new
        { Contract = "FORM-PUBLICATION-PREVIEW-1", head.Id, head.DraftId, head.Version, head.Number, head.SavedBy, Fields = Values(head), PolicyVersion = policy, diagnostics, dependencies }, Json));
    public async Task<FormPublicationPreviewV2> PreviewPublicationAsync(string key, FormPublicationPreviewInputV2 input, CancellationToken ct = default)
    {
        if (input.DraftVersionId == Guid.Empty || !Guid.TryParseExact(input.ExpectedVersion, "D", out var expected)) throw Error("PREVIEW_INPUT_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var lease = await fence.AcquireAsync(ct); await PublicationReady(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var permission = await PublishingPermission(actor, head, ct);
        if (head.Status != 0 || head.DraftId != input.DraftVersionId || head.Version != expected) throw Error("DEFINITION_CHANGED", 409);
        var inputs = PublicationInputs(head, ct); var digest = PreviewDigest(head, permission.Policy.Version, inputs.Diagnostics, inputs.Dependencies);
        var impact=await DefinitionRuntimeImpactReaderV2.Read(db,fence.EnvironmentId,head.Id,true,ct);
        var result = new FormPublicationPreviewV2(Guid.NewGuid(), digest, head.DraftId, input.ExpectedVersion, SchemaDigest(head.SchemaJson),
            PublicationClock().AddMinutes(10), inputs.Diagnostics, inputs.Dependencies, inputs.Diagnostics.Length == 0,impact.ActiveDrafts,Impact:impact);
        var snapshot = JsonSerializer.Serialize(result, Json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_form_authoring_v2.publication_previews(tenant_id,environment_id,id,actor_id,head_id,draft_id,draft_token,digest,expires_at,snapshot_json)
            VALUES({db.CurrentTenantId},{fence.EnvironmentId},{result.Id},{actor.UserId},{head.Id},{head.DraftId},{head.Version},{digest},{result.ExpiresAtUtc},{snapshot})
            """, ct);
        var finalActor = await Actor(ct); if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        var final = await PublishingPermission(finalActor, head, ct); var finalInputs = PublicationInputs(head, ct);
        if (await Heads().SingleAsync(x => x.Key == key, ct) != head || PreviewDigest(head, final.Policy.Version, finalInputs.Diagnostics, finalInputs.Dependencies) != digest) throw Error("PREVIEW_STALE", 409);
        await PublicationReady(ct); await fence.ValidateAsync(lease, ct); CompletionClock(); await tx.CommitAsync(ct); return result;
    }
    private Task<Saved?> LifecycleOriginal(Guid actor, string key, string operation, string command, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT body_digest AS "BodyDigest",result_json AS "ResultJson" FROM cp6_form_authoring_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
          AND operation={operation} AND resource_key={key} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    private static FormPublicationV2 ProjectPublication(FormPublicationV2 p, FieldPermissionDecisionV1 fields) => p with
    {
        Fields = p.Fields.Where(x => fields.Fields[x.Key].CanRead).ToDictionary(),
        SchemaDigest = fields.Fields["SchemaJson"].CanRead ? p.SchemaDigest : null,
        DependencyRefs = fields.Fields["SchemaJson"].CanRead ? p.DependencyRefs : []
    };
    private static FormLifecycleResultV2 ProjectLifecycle(FormLifecycleResultV2 value, FieldPermissionDecisionV1 fields)
        => value with { Result = value.Result == null ? null : new(value.Result.Publication == null ? null : ProjectPublication(value.Result.Publication, fields),
            value.Result.Draft == null ? null : value.Result.Draft with { Fields = value.Result.Draft.Fields.Where(x => fields.Fields[x.Key].CanRead).ToDictionary(), WritableFields = [] }) };
    public async Task<FormLifecycleResultV2> LifecycleOriginalAsync(string key, string operation, string command, CancellationToken ct = default)
    {
        if (operation is not (PublishOperation or NextDraftOperation) || !Regex.IsMatch(command, "\\A[A-Za-z0-9_.-]{1,100}\\z")) throw Error("COMMAND_SCOPE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var fields = await Permission(actor, head, false, false, ct); var original = await LifecycleOriginal(actor.UserId, key, operation, command, ct);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
        return original == null ? new(command, "NOT_OBSERVED", null, null, null, null, null)
            : ProjectLifecycle(JsonSerializer.Deserialize<FormLifecycleResultV2>(original.ResultJson, Json)!, fields);
    }
    private Task<PublicationRow?> Published(Guid head, Guid version, CancellationToken ct) => db.Database.SqlQuery<PublicationRow>($"""
        SELECT id AS "Id",snapshot_json AS "Snapshot",version_number AS "Number" FROM cp6_form_authoring_v2.versions
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head} AND id={version}
        """).SingleOrDefaultAsync(ct);
    public async Task<FormPublicationV2[]> VersionsAsync(string key, int page = 1, Guid? version = null, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000) throw Error("PAGE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var fields = await Permission(actor, head, false, false, ct);
        var rows = await db.Database.SqlQuery<PublicationRow>($"""
            SELECT id AS "Id",snapshot_json AS "Snapshot",version_number AS "Number" FROM cp6_form_authoring_v2.versions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            """).Where(x => version == null || x.Id == version).OrderByDescending(x => x.Number).Skip((page - 1) * 100).Take(100).ToArrayAsync(ct);
        if (version != null && rows.Length == 0) throw Error("VERSION_NOT_FOUND", 404);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
        return rows.Select(x => ProjectPublication(JsonSerializer.Deserialize<FormPublicationV2>(x.Snapshot, Json)!, fields)).ToArray();
    }
    public Task<FormLifecycleResultV2> PublishAsync(string key, byte[] raw, CancellationToken ct = default)
        => Lifecycle(key, ParseLifecycle<FormPublicationBodyV2>(raw), PublishOperation, ct);
    public Task<FormLifecycleResultV2> CreateNextDraftAsync(string key, byte[] raw, CancellationToken ct = default)
        => Lifecycle(key, ParseLifecycle<FormNextDraftBodyV2>(raw), NextDraftOperation, ct);
    private async Task<FormLifecycleResultV2> Lifecycle<T>(string key, LifecycleCommand<T> command, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Contains('\0')) throw Error("DEFINITION_KEY_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Root(ct); var actor = await Actor(ct); var old = await LifecycleOriginal(actor.UserId, key, operation, command.Id, ct);
            if (old != null)
            {
                var originalHead = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
                var read = await Permission(actor, originalHead, false, false, ct);
                if (old.BodyDigest != command.Digest) throw Error("COMMAND_BODY_CONFLICT", 409);
                if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
                return ProjectLifecycle(JsonSerializer.Deserialize<FormLifecycleResultV2>(old.ResultJson, Json)!, read);
            }
            var lease = await fence.AcquireAsync(ct); await PublicationReady(ct);
            var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
            var fields = operation == PublishOperation ? (await PublishingPermission(actor, head, ct)).Fields : await Permission(actor, head, true, false, ct);
            if (command.Expected != head.Version.ToString("D")) throw Error("DEFINITION_CHANGED", 409);
            DateTimeOffset? previewDeadline = null;
            var at = DateTimeOffset.UtcNow; var next = head; FormLifecycleValueV2 result;
            if (command.Body is FormPublicationBodyV2 body)
            {
                if (body.DraftVersionId == Guid.Empty || body.PreviewId == Guid.Empty || body.PreviewDigest?.Length != 64 || body.ExpectedVersion != command.Expected || body.Comment == null || body.Comment.Length > 1000)
                    throw Error("COMMAND_SHAPE_INVALID", 400);
                if (head.Status != 0 || head.DraftId != body.DraftVersionId) throw Error("DRAFT_REQUIRED", 409);
                var preview = await db.Database.SqlQuery<PreviewRow>($"""
                    SELECT actor_id AS "Actor",head_id AS "HeadId",draft_id AS "DraftId",draft_token AS "Token",digest AS "Digest",expires_at AS "Expires",snapshot_json AS "Snapshot"
                    FROM cp6_form_authoring_v2.publication_previews WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={body.PreviewId}
                    """).SingleOrDefaultAsync(ct);
                if (preview == null || preview.Actor != actor.UserId || preview.HeadId != head.Id || preview.DraftId != head.DraftId || preview.Token != head.Version
                    || preview.Digest != body.PreviewDigest || preview.Expires <= PublicationClock()) throw Error("PREVIEW_STALE", 409);
                previewDeadline = preview.Expires;
                var policy = await ReadPublicationPolicy(ct) ?? throw Error("PUBLICATION_POLICY_REQUIRED"); var inputs = PublicationInputs(head, ct);
                if (PreviewDigest(head, policy.Version, inputs.Diagnostics, inputs.Dependencies) != preview.Digest) throw Error("PREVIEW_STALE", 409);
                if (inputs.Diagnostics.Length > 0) throw Error("PUBLICATION_VALIDATION_FAILED", 422);
                var publication = new FormPublicationV2(head.Id, head.DraftId, head.Number, key, Values(head), SchemaDigest(head.SchemaJson), at, actor.UserId, inputs.Dependencies, command.Expected);
                var text = JsonSerializer.Serialize(publication, Json);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id,version_number,schema_digest,snapshot_json,published_at,publisher)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{head.Id},{head.DraftId},{head.Number},{publication.SchemaDigest},{text},{at},{actor.UserId})
                    """, ct);
                next = next with { Status = 1 }; result = new(publication, null);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_form_authoring_v2.drafts SET status=1
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} AND row_version={head.Version}
                    """, ct);
            }
            else if (command.Body is FormNextDraftBodyV2 create)
            {
                if (create.SourceVersionId == Guid.Empty) throw Error("COMMAND_SHAPE_INVALID", 400);
                if (head.Status != 1) throw Error("DRAFT_EXISTS", 409);
                var source = await Published(head.Id, create.SourceVersionId, ct) ?? throw Error("VERSION_NOT_FOUND", 404);
                var p = JsonSerializer.Deserialize<FormPublicationV2>(source.Snapshot, Json)!;
                next = next with { DraftId = Guid.NewGuid(), Status = 0, Number = checked(head.Number + 1), SavedBy = actor.UserId,
                    Name = p.Fields["Name"]!, SchemaJson = p.Fields["SchemaJson"]!, Category = p.Fields["Category"], SubCategory = p.Fields["SubCategory"] };
                CheckWrites(head, next, fields);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_form_authoring_v2.drafts SET id={next.DraftId},status=0,version_number={next.Number},saved_by={actor.UserId},source_version_id={source.Id},
                      name={next.Name},schema_json={next.SchemaJson},category={next.Category},sub_category={next.SubCategory}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} AND row_version={head.Version}
                    """, ct);
                result = new(null, View(next, fields));
            }
            else throw Error("COMMAND_SHAPE_INVALID", 400);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_form_authoring_v2.heads SET draft_id={next.DraftId}
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={head.Id}
                """, ct);
            next=await Heads().SingleAsync(x=>x.Key==key,ct); // The database owns each new opaque token.
            if(operation==NextDraftOperation)result=new(null,View(next,fields));
            var receipt = new FormLifecycleResultV2(command.Id, "SUCCEEDED", head.Id, next.Version.ToString("D"), result, at, command.Correlation);
            var receiptJson = JsonSerializer.Serialize(receipt, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id,canonical_version,body_digest,raw_body,raw_digest,result_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{operation},{key},{command.Id},'WFC-CJ-1',{command.Digest},{command.Raw},{MainOwnerOperationJournalV1.Digest(command.Raw)},{receiptJson},{at})
                """, ct);
            var finalActor = await Actor(ct); if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
            var finalFields = operation == PublishOperation ? (await PublishingPermission(finalActor, head, ct)).Fields : await Permission(finalActor, head, true, false, ct);
            if (operation == NextDraftOperation) CheckWrites(head, next, finalFields);
            var evidence = JsonSerializer.Serialize(new { ActualActor = actor.UserId, EffectiveActor = actor.UserId, operation,
                CheckedAtUtc = DateTimeOffset.UtcNow, Scope = lease.Snapshot,
                PublicationPolicy = operation == PublishOperation ? await ReadPublicationPolicy(ct) : null, AuthoringPolicy = await ReadPolicy(ct),
                finalActor.RoleIds, ResourceScope = finalActor.DataScopes.GetValueOrDefault(Resource, 1), finalActor.DeptId, finalActor.DeptPath,
                finalActor.CustomDeptIds, FieldSources = finalFields.Input.Versions, FieldRows = finalFields.Input.Rows,
                FieldAccess = finalFields.Fields.ToDictionary(x => x.Key, x => x.Value.Access), head.OwnerId, DraftAuthor = head.SavedBy,
                SourceDraft = head.DraftId, SourceToken = head.Version, command.Digest, RawDigest = MainOwnerOperationJournalV1.Digest(command.Raw) }, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.audit(tenant_id,environment_id,actor_id,operation,resource_key,command_id,evidence_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{operation},{key},{command.Id},{evidence},{at})
                """, ct);
            if (await Heads().SingleAsync(x => x.Key == key, ct) != next) throw Error("DEFINITION_CHANGED", 409);
            await PublicationReady(ct); await fence.ValidateAsync(lease, ct); CompletionClock(); ct.ThrowIfCancellationRequested();
            if (previewDeadline <= PublicationClock()) throw Error("PREVIEW_STALE", 409);
            committing = true; await tx.CommitAsync(ct); return ProjectLifecycle(receipt, finalFields);
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, error);
            if (error is Npgsql.PostgresException { SqlState: "23505" }) throw Error("DEFINITION_CHANGED", 409);
            throw;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

public sealed class FileDispositionOptionsV1
{
    public string? ExclusiveLocalRoot { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FileDispositionCommandV1(Guid OperationId, FileContentRefV1 ContentRef, string ExpectedStateVersion,
    Guid ExpectedEtag, Guid PolicyVersion, string RequestedMode, string Reason);

public sealed record FileDispositionResultV1(Guid OperationId, FileContentRefV1? ContentRef, Guid? PolicyVersion,
    string State, DateTimeOffset? OccurredAt);

public sealed partial class FileContentOwnerV1
{
    private const string DispositionContract = "Main.File.Content.disposition.v1";
    private sealed record Disposition(string InitialState, Guid? StorageId, DateTimeOffset? StartedAt,
        DateTimeOffset? DeletedAt);

    public Task<FileDispositionResultV1> RequestDispositionAsync(FileDispositionCommandV1 input, CancellationToken ct = default)
    {
        ValidateContentRef(input.ContentRef);
        Revision(input.ExpectedStateVersion);
        EngineeringRoutingValuesV1.Text(input.Reason);
        if (input.OperationId == Guid.Empty || input.ExpectedEtag == Guid.Empty || input.PolicyVersion == Guid.Empty
            || input.RequestedMode is not ("UNLINK_ONLY" or "PHYSICAL"))
            throw Error("FILE_DISPOSITION_INVALID", 400);

        var command = OperationsOwnerV1.Freeze(input);
        var body = MainOwnerOperationJournalV1.Encode(command);
        var store = new FileContentStoreV1(db);
        var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "delete", true, async (access, token) =>
        {
            if (original is not null)
                return await DiscloseDisposition(access, store, original, command.OperationId, body, token);

            var (file, row) = await VerifyDispositionCandidate(access, store, command, token);
            if (await FindDispositionForFile(row.Id, token))
                throw Error("FILE_DISPOSITION_EXISTS", 409);
            var policy = await RequireDispositionPolicy(command, file, token);
            await RequireNoDispositionReferences(row, token);
            var state = "UNLINKED";
            Guid? storageId = null;
            if (command.RequestedMode == "PHYSICAL")
            {
                var storage = await ResolveDispositionStorage(row, create: true, token);
                storageId = storage.Id;
                state = "DISPOSAL_PENDING";
            }

            var at = await DispositionTime(token);
            var result = new FileDispositionResultV1(command.OperationId, file.ContentRef, policy.Version, state, at);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_file_content_v1.dispositions(tenant_id,file_id,operation_id,initial_state,storage_id)
                VALUES({db.CurrentTenantId},{row.Id},{command.OperationId},{state},{storageId})
                """, token);
            await journal.Store(FileContentBytesV1.Owner, DispositionContract, command.OperationId, access.ActorId, body,
                result, [row.Id], new { result, command.Reason, policy.PolicyRef, policy.BasisRef, actor = access.ActorId }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            await store.Fence(true, token);
            original = await journal.Read(FileContentBytesV1.Owner, command.OperationId, token);
            return original is null ? "delete" : "query";
        });
    }

    public Task<FileDispositionResultV1> ReadDispositionAsync(Guid operation, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (access, token) =>
        {
            access.RequireFileFields(false);
            var store = new FileContentStoreV1(db);
            await store.Fence(false, token);
            var original = await new MainOwnerOperationJournalV1(db).Read(FileContentBytesV1.Owner, operation, token);
            if (original is null) return new FileDispositionResultV1(operation, null, null, "UNKNOWN", null);

            var before = await DiscloseDisposition(access, store, original, operation, null, token);
            var plan = await FindDisposition(operation, token) ?? throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
            return ConfirmedDisposition(before, plan);
        }, ct);
    }

    public async Task<FileDispositionResultV1> CompleteDispositionAsync(Guid operation, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        var prepared = await ProcessDisposition(operation, execute: false, ct);
        return prepared.State == "PROCESSING" ? await ProcessDisposition(operation, execute: true, ct) : prepared;
    }

    private Task<FileDispositionResultV1> ProcessDisposition(Guid operation, bool execute, CancellationToken ct)
    {
        var store = new FileContentStoreV1(db);
        var journal = new MainOwnerOperationJournalV1(db);
        Disposition? plan = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "delete", true, async (access, token) =>
        {
            var original = await journal.Read(FileContentBytesV1.Owner, operation, token)
                ?? throw Error("FILE_DISPOSITION_UNKNOWN", 409);
            var before = await DiscloseDisposition(access, store, original, operation, null, token);
            if (plan is null) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
            if (plan.DeletedAt is not null) return ConfirmedDisposition(before, plan);
            if (plan.InitialState != "DISPOSAL_PENDING") throw Error("FILE_DISPOSITION_NOT_SCHEDULED", 409);
            if (!execute && plan.StartedAt is not null) return ConfirmedDisposition(before, plan);

            var command = JsonSerializer.Deserialize<FileDispositionCommandV1>(original.Input!)!;
            var (file, row) = await VerifyDispositionCandidate(access, store, command, token, checkInitialState: false);
            await RequireDispositionPolicy(command, file, token);
            await RequireNoDispositionReferences(row, token);
            var storage = await ResolveDispositionStorage(row, create: false, token);
            if (storage.Id != plan.StorageId)
                throw Error("FILE_DISPOSITION_STORAGE_CHANGED", 409);
            if (!execute)
            {
                await RequireDispositionOriginal(row, storage.Path, token);
                var started = await DispositionTime(token);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_file_content_v1.disposition_attempts(tenant_id,file_id,operation_id,started_at)
                    VALUES({db.CurrentTenantId},{row.Id},{operation},{started})
                    """, token);
                return before with { State = "PROCESSING", OccurredAt = started };
            }

            // 已确认开始处理后，同一存储身份下才能恢复原操作的物理结果。
            try
            {
                if (LocalFileStore.ExistsManagedFile(storage.Path))
                {
                    await VerifyStored(row.StorePath, row.ByteLength, row.Digest, token);
                    await Authority.ValidateFinalAuthorityAsync(access, access.ActorId, token);
                    token.ThrowIfCancellationRequested();
                    File.Delete(storage.Path);
                    if (LocalFileStore.ExistsManagedFile(storage.Path)) throw Error("FILE_DISPOSITION_OUTCOME_UNKNOWN", 503);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw Error("FILE_DISPOSITION_OUTCOME_UNKNOWN", 503);
            }

            var at = await DispositionTime(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_file_content_v1.disposition_completions(tenant_id,file_id,operation_id,deleted_at)
                VALUES({db.CurrentTenantId},{row.Id},{operation},{at})
                """, token);
            return before with { State = "DELETED", OccurredAt = at };
        }, ct, actionAtFence: async token =>
        {
            await store.Fence(true, token);
            plan = await FindDisposition(operation, token);
            return plan?.DeletedAt is null ? "delete" : "query";
        });
    }

    private async Task<FileDispositionResultV1> DiscloseDisposition(OperationsAccessV1 access, FileContentStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? body, CancellationToken ct)
    {
        access.RequireFileFields(false);
        if (original.Actor != access.ActorId || original.Contract != DispositionContract)
            throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (body is not null && !MainOwnerOperationJournalV1.Matches(original, body))
            throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<FileDispositionResultV1>(original.Result);
        if (result?.ContentRef is not { } content || result.OperationId != operation || !original.Objects.SequenceEqual(new[] { content.Id }))
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await ObserveDraftSources(access, store, [content], ct);
        return result;
    }

    private async Task<(FileContentSnapshotV1 File, FileContentRowV1 Row)> VerifyDispositionCandidate(OperationsAccessV1 access,
        FileContentStoreV1 store, FileDispositionCommandV1 command, CancellationToken ct, bool checkInitialState = true)
    {
        access.RequireFileFields(true);
        var file = await ReadVisibleContent(access, store, command.ContentRef.Id, ct);
        if (file.Held) throw Error("FILE_RETENTION_HOLD_ACTIVE", 409);
        if (file.ContentRef != command.ContentRef || checkInitialState && (file.StateVersion != command.ExpectedStateVersion || file.Etag != command.ExpectedEtag))
            throw Error("FILE_STATE_CHANGED", 412);
        var target = await Parent(access, file.Parent, true, ct);
        if (target.State != "DRAFT") throw Error("FILE_BINDING_TARGET_FROZEN", 409);
        return (file, await store.Row(file.ContentRef.Id, ct));
    }

    private async Task<AuditRetentionPolicyV1> RequireDispositionPolicy(FileDispositionCommandV1 command,
        FileContentSnapshotV1 file, CancellationToken ct)
    {
        var tenant = db.CurrentTenantId;
        var retention = new AuditRetentionV1(db);
        await retention.LockAsync(ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_privacy_disposition_v1.holds IN SHARE MODE", ct);
        var policy = await retention.PolicyAsync(tenant, AuditRetentionV1.TechnicalContent, ct);
        if (policy is not { Enabled: true, DisposalMethod: "DELETE" } || command.PolicyVersion != policy.Version)
            throw Error("FILE_RETENTION_POLICY_UNAVAILABLE", 409);
        var revision = Revision(file.StateVersion);
        var due = await db.Database.SqlQuery<bool>($"""
            SELECT clock_timestamp() >= recorded_at + make_interval(days => {policy.DurationDays}) AS "Value"
            FROM cp6_file_content_v1.events WHERE tenant_id={tenant} AND file_id={file.ContentRef.Id} AND revision={revision}
            """).SingleAsync(ct);
        if (!due) throw Error("FILE_RETENTION_NOT_DUE", 409);
        var id = file.ContentRef.Id.ToString("D");
        var retained = await db.Database.SqlQuery<bool>($"""
            SELECT (
              EXISTS(SELECT FROM (
                  SELECT DISTINCT ON (kind,reference_id) active FROM cp6_audit_v1.safeguards
                  WHERE tenant_id={tenant} AND retention_class={AuditRetentionV1.TechnicalContent} AND native_id={id}
                  ORDER BY kind,reference_id,sequence DESC
                ) records WHERE active)
              OR EXISTS(SELECT FROM (
                  SELECT DISTINCT ON (environment_id,subject_id) active FROM cp6_privacy_disposition_v1.holds
                  WHERE target_tenant={tenant} ORDER BY environment_id,subject_id,sequence DESC
                ) holds WHERE active)
              ) AS "Value"
            """).SingleAsync(ct);
        if (retained) throw Error("FILE_RETENTION_HOLD_ACTIVE", 409);
        return policy;
    }

    private async Task RequireNoDispositionReferences(FileContentRowV1 row, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE public."Pub_Attachment",cp6_form_files_v2.contents IN SHARE MODE
            """, ct);
        var tenant = db.CurrentTenantId;
        var file = row.Id;
        // 旧附件可能共用物理路径。当前租户的可见行不能作为存储引用全集。
        var complete = await db.Database.SqlQueryRaw<bool>("""
            SELECT NOT (row_security_active('public."Pub_Attachment"') OR row_security_active('cp6_form_files_v2.contents')
              OR row_security_active('cp6_controlled_document_v1.bindings') OR row_security_active('cp6_rma_formal_v1.versions')
              OR row_security_active('cp6_rma_formal_v1.delivery_evidence') OR row_security_active('cp6_rma_formal_v1.facts')) AS "Value"
            """).SingleAsync(ct);
        if (!complete) throw Error("FILE_REFERENCE_GRAPH_UNAVAILABLE", 503);
        var referenced = await db.Database.SqlQuery<bool>($"""
            SELECT (
              EXISTS(SELECT FROM cp6_file_content_v1.draft_items WHERE tenant_id={tenant} AND file_id={file})
              OR EXISTS(SELECT FROM cp6_controlled_document_v1.bindings WHERE tenant_id={tenant} AND file_id={file})
              OR EXISTS(SELECT FROM public."Pub_Attachment"
                WHERE lower(replace("StorePath",chr(92),'/'))=lower({row.StorePath}))
              OR EXISTS(SELECT FROM cp6_form_files_v2.contents
                WHERE lower(replace(store_path,chr(92),'/'))=lower({row.StorePath}))
              OR EXISTS(SELECT FROM (
                  SELECT body FROM cp6_rma_formal_v1.delivery_evidence WHERE tenant_id={tenant}
                  UNION ALL SELECT body FROM cp6_rma_formal_v1.versions WHERE tenant_id={tenant}
                  UNION ALL SELECT body FROM cp6_rma_formal_v1.facts WHERE tenant_id={tenant}
                ) facts
                CROSS JOIN LATERAL jsonb_path_query(convert_from(facts.body,'UTF8')::jsonb,
                  'lax $.** ? (@.type() == "object" && @.owner == $owner)',
                  jsonb_build_object('owner',{FileContentBytesV1.Owner})) AS binding(ref)
                WHERE CASE WHEN pg_input_is_valid(binding.ref->>'id','uuid') THEN (binding.ref->>'id')::uuid={file}
                    ELSE true END)
              ) AS "Value"
            """).SingleAsync(ct);
        if (referenced) throw Error("FILE_DISPOSITION_REFERENCED", 409);
    }

    private async Task<(string Path, Guid Id)> ResolveDispositionStorage(FileContentRowV1 row, bool create, CancellationToken ct)
    {
        if (files is not LocalFileStore local || dispositionOptions?.Value.ExclusiveLocalRoot is not { } configured)
            throw Error("FILE_DISPOSITION_STORAGE_UNAVAILABLE", 503);
        try
        {
            var path = local.ManagedNativePath(configured, db.CurrentTenantId, row.StorePath);
            if (create) await RequireDispositionOriginal(row, path, ct);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
            return (path, LocalFileStore.ManagedStorageId(root, create));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Error("FILE_DISPOSITION_STORAGE_UNAVAILABLE", 503);
        }
    }

    private async Task RequireDispositionOriginal(FileContentRowV1 row, string path, CancellationToken ct)
    {
        try
        {
            if (!LocalFileStore.ExistsManagedFile(path)) throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503);
            await VerifyStored(row.StorePath, row.ByteLength, row.Digest, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503);
        }
    }

    private Task<bool> FindDispositionForFile(Guid file, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_file_content_v1.dispositions WHERE tenant_id={db.CurrentTenantId} AND file_id={file}) AS "Value"
        """).SingleAsync(ct);

    private Task<Disposition?> FindDisposition(Guid operation, CancellationToken ct) => db.Database.SqlQuery<Disposition>($"""
        SELECT p.initial_state AS "InitialState",p.storage_id AS "StorageId",a.started_at AS "StartedAt",c.deleted_at AS "DeletedAt"
        FROM cp6_file_content_v1.dispositions p
        LEFT JOIN cp6_file_content_v1.disposition_attempts a USING(tenant_id,file_id,operation_id)
        LEFT JOIN cp6_file_content_v1.disposition_completions c USING(tenant_id,file_id,operation_id)
        WHERE p.tenant_id={db.CurrentTenantId} AND p.operation_id={operation}
        """).SingleOrDefaultAsync(ct);

    private Task<DateTimeOffset> DispositionTime(CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private static FileDispositionResultV1 ConfirmedDisposition(FileDispositionResultV1 initial, Disposition plan) =>
        plan.DeletedAt is { } deleted ? initial with { State = "DELETED", OccurredAt = deleted }
        : plan.StartedAt is { } started ? initial with { State = "PROCESSING", OccurredAt = started } : initial;
}

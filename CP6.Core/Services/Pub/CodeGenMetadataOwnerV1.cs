using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Pub;

/// <summary>The host resolver supplies the current original session, using this Owner's context.
/// The Owner decides pub-codegen permissions under the existing shared IAM transaction gate.</summary>
public sealed partial class CodeGenMetadataOwnerV1(Func<CP6Context> contexts, Guid tenant, Guid metadataId,
    Func<CP6Context, CancellationToken, Task<Guid>> currentActor)
{
    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static byte[] Key(string operation) => SHA256.HashData(Bytes(operation));
    private static void Operation(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new CodeGenMetadataExceptionV1("CODEGEN_OPERATION_REQUIRED", 400);
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException e) { throw new CodeGenMetadataExceptionV1("CODEGEN_OPERATION_INVALID", 400, e); }
    }
    private async Task<Guid> Require(CP6Context db, string action, CancellationToken ct)
    {
        var actor = await currentActor(db, ct);
        var permissions = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
        if (!permissions.ActionKeys.Contains("pub-codegen:" + action) || !await
            (from a in db.Sys_MenuActions.AsNoTracking() join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
             where m.Enable && m.MenuKey == "pub-codegen" && a.ActionCode == action select a.Id).AnyAsync(ct))
            throw new CodeGenMetadataExceptionV1("ACTION_FORBIDDEN", 403);
        return actor;
    }
    private async Task<T> Execute<T>(bool write, Func<CP6Context, CancellationToken, Task<(T Value, Guid Actor, string Action)>> body, CancellationToken ct)
    {
        if (metadataId == Guid.Empty) throw new CodeGenMetadataExceptionV1("CODEGEN_METADATA_ID_REQUIRED", 400);
        await using var db = contexts();
        if (tenant == Guid.Empty || db.CurrentTenantId != tenant) throw new CodeGenMetadataExceptionV1("CODEGEN_TENANT_MISMATCH", 403);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw new CodeGenMetadataExceptionV1("CODEGEN_INDEPENDENT_CONTEXT_REQUIRED", 503);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); var committing = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var result = await body(db, ct);
            if (await Require(db, result.Action, ct) != result.Actor) throw new CodeGenMetadataExceptionV1("REAUTH_REQUIRED", 401);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result.Value;
        }
        catch (Exception error)
        {
            if (write && committing) throw new CodeGenMetadataExceptionV1("CODEGEN_OUTCOME_UNKNOWN", 503, error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new CodeGenMetadataExceptionV1("CODEGEN_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            throw;
        }
    }
    private sealed class Saved
    {
        public byte[] Operation { get; set; } = []; public byte[] Input { get; set; } = [];
        public Guid Actor { get; set; } public byte[] Result { get; set; } = [];
    }
    private Task<Saved?> Original(CP6Context db, string operation, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT operation_bytes AS "Operation", input_bytes AS "Input", actor_id AS "Actor", result_bytes AS "Result"
          FROM cp6_codegen_metadata_v1.commands WHERE tenant_id={tenant} AND metadata_id={metadataId} AND operation_hash={Key(operation)}
        """).SingleOrDefaultAsync(ct);
    private static CodeGenMetadataReceiptV1 Receipt(Saved saved, Guid actor, string operation, byte[]? input)
    {
        if (saved.Actor != actor) throw new CodeGenMetadataExceptionV1("CODEGEN_ORIGINAL_FORBIDDEN", 403);
        if (!saved.Operation.AsSpan().SequenceEqual(Bytes(operation)) || input != null && !saved.Input.AsSpan().SequenceEqual(input))
            throw new CodeGenMetadataExceptionV1("CODEGEN_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<CodeGenMetadataReceiptV1>(saved.Result)!;
    }
    public Task<CodeGenMetadataReceiptV1> SaveAsync(CodeGenMetadataSaveV1 command, CancellationToken ct = default)
    {
        Operation(command.OperationRef);
        if (command.ExpectedRevision < 0 || command.Document is null) throw new CodeGenMetadataExceptionV1("CODEGEN_INPUT_REQUIRED", 400);
        var input = Bytes(command);
        return Execute(true, async (db, token) =>
        {
            var actor = await Require(db, "view", token);
            var original = await Original(db, command.OperationRef, token);
            if (original != null) return (Receipt(original, actor, command.OperationRef, input), actor, "view");
            await Require(db, "save", token);
            _ = command.Document.Preview();
            // A legacy identity can only enter V1 through explicit adoption. Its original rows stay frozen.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Pub_GenTable\" IN SHARE ROW EXCLUSIVE MODE", token);
            var adopted = await IsAdopted(db, token);
            if (await db.Pub_GenTables.AnyAsync(x => (x.EntityName == command.Document.Table.EntityName || x.Id == metadataId)
                && !(adopted && x.Id == metadataId), token))
                throw new CodeGenMetadataExceptionV1("CODEGEN_LEGACY_METADATA_REQUIRES_ADOPTION");
            var revision = await db.Database.SqlQuery<long?>($"""
                SELECT revision AS "Value" FROM cp6_codegen_metadata_v1.documents
                  WHERE tenant_id={tenant} AND metadata_id={metadataId} FOR UPDATE
                """).SingleOrDefaultAsync(token) ?? 0;
            if (revision != command.ExpectedRevision) throw new CodeGenMetadataExceptionV1("CODEGEN_VERSION_CHANGED", 412);
            if (revision == long.MaxValue) throw new CodeGenMetadataExceptionV1("CODEGEN_REVISION_EXHAUSTED", 422);
            var result = await Persist(db, command.OperationRef, input, command.Document, revision, actor, token);
            return (result, actor, "save");
        }, ct);
    }
    private async Task<CodeGenMetadataReceiptV1> Persist(CP6Context db, string operation, byte[] input,
        CodeGenMetadataDocumentV1 document, long revision, Guid actor, CancellationToken token)
    {
            var next = revision + 1;
            var at = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var snapshot = new CodeGenMetadataSnapshotV1(metadataId, tenant, next, document, actor, at);
            var result = new CodeGenMetadataReceiptV1(operation, snapshot);
            try
            {
                if (revision == 0) await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_codegen_metadata_v1.documents(tenant_id,metadata_id,entity_name,revision)
                    VALUES({tenant},{metadataId},{document.Table.EntityName},{next})
                    """, token);
                else await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_codegen_metadata_v1.documents SET entity_name={document.Table.EntityName},revision={next}
                      WHERE tenant_id={tenant} AND metadata_id={metadataId}
                    """, token);
            }
            catch (PostgresException e) when (e.SqlState == "23505" && e.ConstraintName == "codegen_metadata_name")
            { throw new CodeGenMetadataExceptionV1("CODEGEN_ENTITY_NAME_EXISTS", 409, e); }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_codegen_metadata_v1.versions(tenant_id,metadata_id,revision,body)
                  VALUES({tenant},{metadataId},{next},{Bytes(snapshot)});
                INSERT INTO cp6_codegen_metadata_v1.commands(tenant_id,metadata_id,operation_hash,operation_bytes,input_bytes,actor_id,revision,result_bytes)
                  VALUES({tenant},{metadataId},{Key(operation)},{Bytes(operation)},{input},{actor},{next},{Bytes(result)});
                """, token);
            return result;
    }
    public Task<CodeGenMetadataReceiptV1?> ReadOperationAsync(string operation, CancellationToken ct = default)
    {
        Operation(operation);
        return Execute<CodeGenMetadataReceiptV1?>(false, async (db, token) =>
        {
            var actor = await Require(db, "view", token); var saved = await Original(db, operation, token);
            return (saved == null ? null : Receipt(saved, actor, operation, null), actor, "view");
        }, ct);
    }
    public Task<CodeGenMetadataSnapshotV1?> ReadAsync(long? revision = null, CancellationToken ct = default)
    {
        if (revision is <= 0) throw new CodeGenMetadataExceptionV1("CODEGEN_REVISION_INVALID", 400);
        return Execute<CodeGenMetadataSnapshotV1?>(false, async (db, token) =>
        {
            var actor = await Require(db, "view", token);
            var bytes = await db.Database.SqlQuery<byte[]>($"""
                SELECT v.body AS "Value" FROM cp6_codegen_metadata_v1.versions v
                JOIN cp6_codegen_metadata_v1.documents d USING(tenant_id,metadata_id)
                WHERE v.tenant_id={tenant} AND v.metadata_id={metadataId} AND v.revision=coalesce({revision},d.revision)
                """).SingleOrDefaultAsync(token);
            return (bytes == null ? null : JsonSerializer.Deserialize<CodeGenMetadataSnapshotV1>(bytes), actor, "view");
        }, ct);
    }
}

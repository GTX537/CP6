using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Sys;

public sealed record WorkspacePreferenceSnapshotV1(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Revision,
    string[] VisibleMetricKeys);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspacePreferenceCommandV1([property: JsonRequired] Guid OperationRef,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ExpectedRevision,
    [property: JsonRequired] string[] VisibleMetricKeys);

public sealed record WorkspacePreferenceReceiptV1(Guid OperationRef, WorkspacePreferenceSnapshotV1 Snapshot, DateTimeOffset AppliedAtUtc);
public sealed class WorkspacePreferenceExceptionV1(int status, string code, Exception? inner = null) : Exception(code, inner)
{
    public int Status { get; } = status;
}

public sealed class WorkspacePreferencesV1(CP6Context db, OperationsOwnerV1 authority)
{
    private static readonly string[] DefaultKeys = WorkspaceMetricsV1.Definitions.Select(x => x.Key).ToArray();
    private static readonly HashSet<string> KnownKeys = DefaultKeys.ToHashSet(StringComparer.Ordinal);

    private sealed class StoredPreference
    {
        public long Revision { get; init; }
        public string[] Keys { get; init; } = [];
    }

    private sealed class StoredCommand
    {
        public Guid OperationRef { get; init; }
        public long ExpectedRevision { get; init; }
        public long AppliedRevision { get; init; }
        public string[] Keys { get; init; } = [];
        public DateTimeOffset AppliedAtUtc { get; init; }
        internal WorkspacePreferenceReceiptV1 Receipt => new(OperationRef, new(AppliedRevision, Keys), AppliedAtUtc);
    }

    public Task<WorkspacePreferenceSnapshotV1> ReadAsync(CancellationToken ct)
        => ConsumeAsync(false, async (tenant, subject, token) => await ReadSnapshot(tenant, subject, token), ct);

    public Task<WorkspacePreferenceReceiptV1?> ReadOriginalAsync(Guid operationRef, CancellationToken ct)
        => ConsumeAsync(false, async (tenant, subject, token) => (await Original(tenant, subject, operationRef, token))?.Receipt, ct);

    public Task<WorkspacePreferenceReceiptV1> SaveAsync(WorkspacePreferenceCommandV1 command, CancellationToken ct)
    {
        if (command.OperationRef == Guid.Empty || command.ExpectedRevision is < 0 or long.MaxValue ||
            command.VisibleMetricKeys is not { } keys || keys.Length > DefaultKeys.Length ||
            keys.Any(x => !KnownKeys.Contains(x)) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new WorkspacePreferenceExceptionV1(400, "WORKSPACE_PREFERENCE_INPUT_INVALID");

        return ConsumeAsync(true, async (tenant, subject, token) =>
        {
            var original = await Original(tenant, subject, command.OperationRef, token);
            if (original is not null)
            {
                if (original.ExpectedRevision != command.ExpectedRevision || !original.Keys.SequenceEqual(keys))
                    throw new WorkspacePreferenceExceptionV1(409, "WORKSPACE_PREFERENCE_OPERATION_CONFLICT");
                return original.Receipt;
            }

            var current = await ReadSnapshot(tenant, subject, token);
            if (current.Revision != command.ExpectedRevision)
                throw new WorkspacePreferenceExceptionV1(412, "WORKSPACE_PREFERENCE_VERSION_CHANGED");
            var next = current.Revision + 1;
            var applied = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workspace_v1.preferences AS current(tenant_id,subject_id,revision,visible_metrics)
                VALUES({tenant},{subject},{next},{keys})
                ON CONFLICT(tenant_id,subject_id) DO UPDATE SET revision=EXCLUDED.revision,visible_metrics=EXCLUDED.visible_metrics
                WHERE current.revision={command.ExpectedRevision}
                """, token);
            if (applied != 1) throw new WorkspacePreferenceExceptionV1(412, "WORKSPACE_PREFERENCE_VERSION_CHANGED");

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workspace_v1.preference_commands(tenant_id,subject_id,operation_ref,expected_revision,visible_metrics,applied_revision)
                VALUES({tenant},{subject},{command.OperationRef},{command.ExpectedRevision},{keys},{next})
                """, token);
            return (await Original(tenant, subject, command.OperationRef, token))!.Receipt;
        }, ct);
    }

    private async Task<WorkspacePreferenceSnapshotV1> ReadSnapshot(Guid tenant, Guid subject, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<StoredPreference>($"""
            SELECT revision AS "Revision",visible_metrics AS "Keys"
            FROM cp6_workspace_v1.preferences WHERE tenant_id={tenant} AND subject_id={subject}
            """).SingleOrDefaultAsync(ct);
        return row is null ? new(0, DefaultKeys.ToArray()) : new(row.Revision, row.Keys);
    }

    private Task<StoredCommand?> Original(Guid tenant, Guid subject, Guid operationRef, CancellationToken ct)
        => db.Database.SqlQuery<StoredCommand>($"""
            SELECT operation_ref AS "OperationRef",expected_revision AS "ExpectedRevision",applied_revision AS "AppliedRevision",
                   visible_metrics AS "Keys",applied_at_utc AS "AppliedAtUtc"
            FROM cp6_workspace_v1.preference_commands
            WHERE tenant_id={tenant} AND subject_id={subject} AND operation_ref={operationRef}
            """).SingleOrDefaultAsync(ct);

    private async Task<T> ConsumeAsync<T>(bool write, Func<Guid, Guid, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() ||
            System.Transactions.Transaction.Current != null)
            throw new WorkspacePreferenceExceptionV1(503, "WORKSPACE_PREFERENCE_UNAVAILABLE");

        var committing = false;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var tenant = db.CurrentTenantId;
            var subject = await authority.CurrentSubjectAsync(ct);
            var result = await action(tenant, subject, ct);
            if (tenant != db.CurrentTenantId || await authority.CurrentSubjectAsync(ct) != subject)
                throw new WorkspacePreferenceExceptionV1(401, "REAUTH_REQUIRED");
            ct.ThrowIfCancellationRequested();
            committing = true;
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (OrderPriceCorrectionException error)
        {
            throw new WorkspacePreferenceExceptionV1(error.Status, error.Message, error);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            if (committing && write)
                throw new WorkspacePreferenceExceptionV1(503, "WORKSPACE_PREFERENCE_OUTCOME_UNKNOWN", error);
            if (error is OperationCanceledException && ct.IsCancellationRequested) throw;
            throw new WorkspacePreferenceExceptionV1(503, "WORKSPACE_PREFERENCE_UNAVAILABLE", error);
        }
    }
}

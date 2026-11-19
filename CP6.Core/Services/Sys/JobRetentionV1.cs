using System.Diagnostics.CodeAnalysis;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Sys;

/// <summary>由部署设置绑定已批准的数据库来源。具体保留策略由各数据 Owner 独立登记。</summary>
public sealed class JobRetentionSourceV1
{
    public Guid EnvironmentId { get; set; }
    public Guid SourceId { get; set; }
}

public sealed record JobRetentionShardV1(Guid Id, int Cursor, int NextCursor, int Disposed, int Held,
    int PendingTerminal, int Excluded, DateTimeOffset CommittedAt);

public sealed class JobRetentionOutcomeUnknownV1(Guid planId, int? cursor, Exception inner)
    : InvalidOperationException("RETENTION_OUTCOME_UNKNOWN", inner)
{
    public Guid PlanId { get; } = planId;
    public int? Cursor { get; } = cursor;
}

public sealed record JobRetentionResultV1(Guid? PlanId, string State, string? Reason, Guid? PolicyVersion,
    DateTimeOffset? Cutoff, DateTimeOffset? ExpiresAt, long? ReferenceVersion, int Planned, int NextCursor,
    JobRetentionShardV1[] Shards)
{
    public int Disposed => Shards.Sum(shard => shard.Disposed);
    public int PendingTerminal => Shards.Sum(shard => shard.PendingTerminal);
}

internal enum JobRetentionClassV1 { WorkflowJob, WorkflowTrigger, WmsRawScan }

internal sealed class JobRetentionV1(CP6Context db, JobRetentionSourceV1? approval, TimeProvider? time = null)
{
    private const int BatchSize = 500;
    private const int PlanSize = 5000;
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private Guid Tenant => db.CurrentTenantId;
    private Guid Environment => approval?.EnvironmentId ?? Guid.Empty;

    private sealed record Policy(Guid Version, Guid SourceId, Guid BasisRef, bool Enabled,
        string Name, long Oid, string Address, int Port, int RetentionDays);
    private sealed record Plan(Guid Id, Guid PolicyVersion, DateTimeOffset Cutoff, DateTimeOffset ExpiresAt,
        long ReferenceVersion, int Eligible);
    private sealed record Member(Guid EntityId, string RowVersion, bool Held, bool PendingTerminal);
    private sealed record Source(string Key, string Table, string Eligible, string LockTables);

    private static Source For(JobRetentionClassV1 kind) => kind switch
    {
        JobRetentionClassV1.WorkflowJob => new("WFS_SERVICE_JOB", """public."Wf_ServiceJob" """,
            $"""s."Status" IN ({ServiceJobStatus.Succeeded},{ServiceJobStatus.Failed},{ServiceJobStatus.Cancelled}) AND s."CompletedAtUtc"<@cutoff""",
            """public."Wf_ServiceJob" """),
        JobRetentionClassV1.WorkflowTrigger => new("WFS_TRIGGER_FIRE", """public."Wf_TriggerFire" """,
            """s."FiredUtc"<@cutoff AND (s."InstanceId" IS NOT NULL OR s."Error" IS NOT NULL)""",
            """public."Wf_TriggerFire" """),
        JobRetentionClassV1.WmsRawScan => new("WMS_RAW_SCAN", """public."T_MobileTaskScanLog" """,
            $"""
            s."RetainUntil"<=@cutoff AND EXISTS (
                SELECT FROM public."T_MobileTask" t WHERE t."TenantId"=@tenant AND t."MobileTaskNo"=s."TaskNo"
                  AND NOT t."IsDeleted" AND t."Status" IN ({MobileTaskStatus.Completed},{MobileTaskStatus.Cancelled})
                  AND t."ExecutionVersion">=s."ExecutionVersion")
            """, """public."T_MobileTask", public."T_MobileTaskScanLog" """),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static JobRetentionResultV1 Unavailable(string reason) =>
        new(null, "BLOCKED", reason, null, null, null, null, 0, 0, []);

    internal async Task<JobRetentionResultV1> RunForWorkerAsync(JobRetentionClassV1 kind, DateTimeOffset anchor,
        int days, bool resumeOnly, CancellationToken ct)
    {
        try
        {
            return await RunAsync(kind, anchor, days, resumeOnly, ct);
        }
        catch (JobRetentionOutcomeUnknownV1 uncertain)
        {
            var original = await ReadAsync(kind, uncertain.PlanId, ct);
            if (original != null && (uncertain.Cursor == null || original.NextCursor > uncertain.Cursor || original.Reason != null))
                return original;
            return original is null
                ? Unavailable("RETENTION_OUTCOME_UNKNOWN") with { PlanId = uncertain.PlanId, State = "UNKNOWN" }
                : original with { State = "UNKNOWN", Reason = "RETENTION_OUTCOME_UNKNOWN" };
        }
    }

    internal async Task<JobRetentionResultV1> RunAsync(JobRetentionClassV1 kind, DateTimeOffset cutoffAnchor,
        int days, bool resumeOnly, CancellationToken ct, Guid? originalPlanId = null)
    {
        if (!db.Database.IsNpgsql() || Tenant == Guid.Empty || Environment == Guid.Empty ||
            approval?.SourceId is null || approval.SourceId == Guid.Empty)
            return Unavailable("RETENTION_SOURCE_NOT_APPROVED");
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() ||
            System.Transactions.Transaction.Current != null)
            throw new ScopeAuthorizationExceptionV1("RETENTION_TRANSACTION_REQUIRED");

        var source = For(kind);
        Plan? plan;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await Gate(ct);
            var policy = await PolicyAsync(source.Key, ct);
            if (!await MatchesSourceAsync(policy, ct)) return Unavailable("RETENTION_SOURCE_OR_POLICY_UNAVAILABLE");
            plan = originalPlanId is Guid original ? await PlanAsync(source.Key, original, ct) : await PendingAsync(source.Key, ct);
            if (plan == null)
            {
                if (originalPlanId.HasValue) return Unavailable("RETENTION_ORIGINAL_PLAN_NOT_LOCATED");
                if (resumeOnly) return Unavailable("RETENTION_NO_PENDING_PLAN");
                if (policy is not { Enabled: true } || policy.RetentionDays != days)
                    return Unavailable("RETENTION_POLICY_DISABLED");
                await LockSources(source, ct);
                plan = await PrepareAsync(source, policy, cutoffAnchor.ToUniversalTime().AddDays(-days), ct);
                await Commit(tx, plan.Id, null, ct);
            }
        }

        for (var cursor = 0; cursor < plan.Eligible;)
        {
            ct.ThrowIfCancellationRequested();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Gate(ct);
            var progress = await ReadAsync(kind, plan.Id, ct)
                ?? throw new ScopeAuthorizationExceptionV1("RETENTION_PLAN_NOT_FOUND", 404);
            if (progress.Reason != null || progress.NextCursor == plan.Eligible) return progress;
            cursor = progress.NextCursor;
            var policy = await PolicyAsync(source.Key, ct);
            if (!await MatchesSourceAsync(policy, ct))
                return BlockedProgress(progress, "RETENTION_SOURCE_OR_POLICY_UNAVAILABLE");
            var reason = plan.ExpiresAt <= clock.GetUtcNow() ? "RETENTION_PREVIEW_EXPIRED"
                : policy is not { Enabled: true } || policy.Version != plan.PolicyVersion || policy.RetentionDays != days
                    ? "RETENTION_POLICY_CHANGED" : null;
            if (reason != null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_job_retention_v1.stops(plan_id,reason) VALUES({plan.Id},{reason})
                    """, ct);
                await Commit(tx, plan.Id, cursor, ct);
                return (await ReadAsync(kind, plan.Id, ct))!;
            }
            await LockSources(source, ct);
            if (plan.ExpiresAt <= clock.GetUtcNow()) throw new ScopeAuthorizationExceptionV1("RETENTION_PREVIEW_EXPIRED", 409);
            var next = await ApplyShard(source, plan, cursor, ct);
            if (plan.ExpiresAt <= clock.GetUtcNow() || !await MatchesSourceAsync(policy, ct))
                throw new ScopeAuthorizationExceptionV1("RETENTION_COMMIT_AUTHORITY_CHANGED", 409);
            await Commit(tx, plan.Id, cursor, ct);
            cursor = next;
        }
        return (await ReadAsync(kind, plan.Id, ct))!;
    }

    private static JobRetentionResultV1 BlockedProgress(JobRetentionResultV1 progress, string reason) =>
        progress with { State = progress.Disposed > 0 ? "PARTIALLY_COMPLETED" : "BLOCKED", Reason = reason };

    /// <summary>按原计划和当前租户作用域回读。来源或执行策略停用时，已提交分片仍保留。</summary>
    internal async Task<JobRetentionResultV1?> ReadAsync(JobRetentionClassV1 kind, Guid planId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || Tenant == Guid.Empty || Environment == Guid.Empty) return null;
        var key = For(kind).Key;
        var plan = await PlanAsync(key, planId, ct);
        if (plan == null) return null;
        var shards = await db.Database.SqlQuery<JobRetentionShardV1>($"""
            SELECT id AS "Id",cursor AS "Cursor",next_cursor AS "NextCursor",disposed AS "Disposed",held AS "Held",
              pending_terminal AS "PendingTerminal",excluded AS "Excluded",committed_at AS "CommittedAt"
            FROM cp6_job_retention_v1.shards WHERE plan_id={plan.Id} ORDER BY cursor
            """).ToArrayAsync(ct);
        var stop = await db.Database.SqlQuery<string>($"""
            SELECT reason AS "Value" FROM cp6_job_retention_v1.stops WHERE plan_id={plan.Id}
            """).SingleOrDefaultAsync(ct);
        var next = shards.LastOrDefault()?.NextCursor ?? 0;
        var disposed = shards.Sum(s => s.Disposed);
        var state = stop != null ? disposed > 0 ? "PARTIALLY_COMPLETED" : "BLOCKED"
            : next < plan.Eligible ? disposed > 0 ? "PARTIALLY_COMPLETED" : "READY"
            : plan.Eligible == 0 ? "COMPLETED_NO_CHANGES"
            : disposed == plan.Eligible ? "COMPLETED_WITH_VERIFIED_RESULTS"
            : disposed > 0 ? "PARTIALLY_COMPLETED" : "BLOCKED";
        return new(plan.Id, state, stop, plan.PolicyVersion, plan.Cutoff, plan.ExpiresAt,
            plan.ReferenceVersion, plan.Eligible, next, shards);
    }

    private Task<Plan?> PlanAsync(string key, Guid planId, CancellationToken ct) =>
        db.Database.SqlQuery<Plan>($"""
            SELECT id AS "Id",policy_version AS "PolicyVersion",cutoff AS "Cutoff",expires_at AS "ExpiresAt",
              reference_version AS "ReferenceVersion",eligible AS "Eligible"
            FROM cp6_job_retention_v1.plans WHERE id={planId} AND tenant_id={Tenant} AND environment_id={Environment} AND data_class={key}
            """).SingleOrDefaultAsync(ct);

    private Task<Plan?> PendingAsync(string key, CancellationToken ct) => db.Database.SqlQuery<Plan>($"""
        SELECT p.id AS "Id",p.policy_version AS "PolicyVersion",p.cutoff AS "Cutoff",p.expires_at AS "ExpiresAt",
          p.reference_version AS "ReferenceVersion",p.eligible AS "Eligible"
        FROM cp6_job_retention_v1.plans p
        WHERE p.tenant_id={Tenant} AND p.environment_id={Environment} AND p.data_class={key}
          AND NOT EXISTS (SELECT FROM cp6_job_retention_v1.stops x WHERE x.plan_id=p.id)
          AND p.eligible>COALESCE((SELECT max(s.next_cursor) FROM cp6_job_retention_v1.shards s WHERE s.plan_id=p.id),0)
        ORDER BY p.planned_at,p.id LIMIT 1
        """).SingleOrDefaultAsync(ct);

    private Task<Policy?> PolicyAsync(string key, CancellationToken ct) => db.Database.SqlQuery<Policy>($"""
        SELECT version AS "Version",source_id AS "SourceId",basis_ref AS "BasisRef",enabled AS "Enabled",
          database_name AS "Name",database_oid AS "Oid",database_address AS "Address",database_port AS "Port",
          retention_days AS "RetentionDays"
        FROM cp6_job_retention_v1.policies WHERE tenant_id={Tenant} AND environment_id={Environment} AND data_class={key}
        ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);

    private async Task<bool> MatchesSourceAsync(Policy? policy, CancellationToken ct)
    {
        if (policy == null || policy.SourceId != approval?.SourceId || policy.BasisRef == Guid.Empty) return false;
        var actual = await PrivacyRestoreNativeV1.Database(db, ct);
        if (actual.Name != policy.Name || actual.Oid != policy.Oid || actual.Address != policy.Address || actual.Port != policy.Port)
            return false;
        return await db.Database.SqlQuery<bool>($"""
            SELECT (NOT pg_is_in_recovery() AND cp6_job_retention_v1.ready() AND
              EXISTS(SELECT FROM cp6_scope_authorization_v1.environment WHERE singleton AND environment_id={Environment}) AND
              EXISTS(SELECT FROM public."Sys_Tenants" WHERE "Id"={Tenant} AND "Enable" AND
                ("ExpireDate" IS NULL OR "ExpireDate">clock_timestamp()::timestamp))) AS "Value"
            """).SingleAsync(ct);
    }

    private async Task Gate(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'", ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
    }

    private Task<int> LockSources(Source source, CancellationToken ct)
        // 表名来自本文件中的固定映射，避免 Owner 修改状态与本批处置交叉。
        => db.Database.ExecuteSqlRawAsync("LOCK TABLE " + source.LockTables + " IN SHARE ROW EXCLUSIVE MODE", ct);

    private async Task<Plan> PrepareAsync(Source source, Policy policy, DateTimeOffset cutoff, CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        var candidates = await CandidatesAsync(source, cutoff, null, ct);
        var referenceVersion = await db.Database.SqlQuery<long>($"""
            SELECT COALESCE(max(sequence),0) AS "Value" FROM cp6_job_retention_v1.safeguards
            WHERE tenant_id={Tenant} AND environment_id={Environment} AND data_class={source.Key}
            """).SingleAsync(ct);
        var plan = new Plan(Guid.NewGuid(), policy.Version, cutoff, at.AddHours(1), referenceVersion, candidates.Length);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_job_retention_v1.plans(id,tenant_id,environment_id,data_class,policy_version,planned_at,cutoff,expires_at,reference_version,eligible)
            VALUES({plan.Id},{Tenant},{Environment},{source.Key},{policy.Version},{at},{cutoff},{plan.ExpiresAt},{referenceVersion},{plan.Eligible})
            """, ct);
        if (candidates.Length > 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_job_retention_v1.members(plan_id,ordinal,entity_id,row_version,held,pending_terminal)
                SELECT {plan.Id},(ordinality-1)::int,entity_id,row_version,held,pending_terminal
                FROM unnest({candidates.Select(c => c.EntityId).ToArray()},{candidates.Select(c => c.RowVersion).ToArray()},
                    {candidates.Select(c => c.Held).ToArray()},{candidates.Select(c => c.PendingTerminal).ToArray()})
                  WITH ORDINALITY AS c(entity_id,row_version,held,pending_terminal,ordinality)
                """, ct);
        return plan;
    }

    private async Task<int> ApplyShard(Source source, Plan plan, int cursor, CancellationToken ct)
    {
        var members = await db.Database.SqlQuery<Member>($"""
            SELECT entity_id AS "EntityId",row_version AS "RowVersion",held AS "Held",pending_terminal AS "PendingTerminal"
            FROM cp6_job_retention_v1.members WHERE plan_id={plan.Id} AND ordinal>={cursor} ORDER BY ordinal LIMIT {BatchSize}
            """).ToArrayAsync(ct);
        if (members.Length != Math.Min(BatchSize, plan.Eligible - cursor))
            throw new ScopeAuthorizationExceptionV1("RETENTION_PLAN_INCOMPLETE");
        var current = (await CandidatesAsync(source, plan.Cutoff, members.Select(m => m.EntityId).ToArray(), ct))
            .ToDictionary(c => c.EntityId);
        var delete = new List<Guid>(members.Length);
        var held = 0;
        var pending = 0;
        foreach (var member in members)
        {
            current.TryGetValue(member.EntityId, out var row);
            if (member.Held || row is { Held: true }) held++;
            else if (row != null && row.RowVersion == member.RowVersion) delete.Add(member.EntityId);
            if (member.PendingTerminal || row is { PendingTerminal: true }) pending++;
        }
        if (delete.Count > 0)
        {
            var affected = await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM " + source.Table + """ WHERE "TenantId"=@tenant AND "Id"=ANY(@ids)""",
                [P("tenant", NpgsqlDbType.Uuid, Tenant), P("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, delete.ToArray())], ct);
            if (affected != delete.Count) throw new ScopeAuthorizationExceptionV1("RETENTION_WRITE_SET_CHANGED", 409);
        }

        var next = cursor + members.Length;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_job_retention_v1.shards(plan_id,cursor,next_cursor,id,disposed,held,pending_terminal,excluded)
            VALUES({plan.Id},{cursor},{next},{Guid.NewGuid()},{delete.Count},{held},{pending},{members.Length-delete.Count-held})
            """, ct);
        return next;
    }

    [SuppressMessage("Security", "EF1002", Justification = "表名和谓词只取自内部枚举映射，场景值走 Npgsql 参数。")]
    private Task<Member[]> CandidatesAsync(Source source, DateTimeOffset cutoff, Guid[]? ids, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Member>($"""
            SELECT s."Id" AS "EntityId",s.xmin::text AS "RowVersion",p.pending_terminal OR
              EXISTS(SELECT FROM (
                SELECT DISTINCT ON (kind,reference_id) active FROM cp6_job_retention_v1.safeguards
                WHERE tenant_id=@tenant AND environment_id=@environment AND data_class=@class AND entity_id=s."Id"
                ORDER BY kind,reference_id,sequence DESC) safeguards WHERE active) OR
              EXISTS(SELECT FROM (
                SELECT DISTINCT ON (subject_id) active FROM cp6_privacy_disposition_v1.holds
                WHERE target_tenant=@tenant AND environment_id=@environment
                ORDER BY subject_id,sequence DESC) holds WHERE active) AS "Held",
              p.pending_terminal AS "PendingTerminal"
            FROM {source.Table} s
            CROSS JOIN LATERAL(SELECT EXISTS(SELECT FROM cp6_job_retention_v1.purpose_uses u
              WHERE u.tenant_id=@tenant AND u.environment_id=@environment AND u.data_class=@class AND u.entity_id=s."Id"
              AND NOT EXISTS(SELECT FROM cp6_native_timeout_v2.results r
                JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id
                JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
                JOIN cp6_native_timeout_v2.attempts a ON a.execution_id=c.execution_id AND a.input_digest=u.input_digest
                WHERE u.owner_key='OA_NATIVE_TIMEOUT' AND r.execution_id=u.operation_id
                  AND d.tenant_id=u.tenant_id AND d.environment_id=u.environment_id)) AS pending_terminal) p
            WHERE s."TenantId"=@tenant AND ({source.Eligible}) AND (@ids IS NULL OR s."Id"=ANY(@ids))
            ORDER BY s."Id" LIMIT @limit
            """,
            P("tenant", NpgsqlDbType.Uuid, Tenant), P("environment", NpgsqlDbType.Uuid, Environment),
            P("class", NpgsqlDbType.Text, source.Key), P("cutoff", NpgsqlDbType.TimestampTz, cutoff),
            P("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, (object?)ids ?? DBNull.Value),
            P("limit", NpgsqlDbType.Integer, ids?.Length ?? PlanSize)).ToArrayAsync(ct);

    private static NpgsqlParameter P(string name, NpgsqlDbType type, object value) => new(name, type) { Value = value };

    private static async Task Commit(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Guid planId, int? cursor, CancellationToken ct)
    {
        try { await tx.CommitAsync(ct); }
        catch (PostgresException error) when (error.SqlState != "40003" && error.SqlState[..2] is "22" or "23" or "40" or "42")
        {
            throw;
        }
        catch (Exception error)
        {
            throw new JobRetentionOutcomeUnknownV1(planId, cursor, error);
        }
    }
}

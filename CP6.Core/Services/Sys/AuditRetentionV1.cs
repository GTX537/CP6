using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Sys;

public sealed record AuditRetentionPolicyV1(Guid Version, Guid PolicyRef, Guid BasisRef, bool Enabled,
    bool WorkerEnabled, int DurationDays, string? LegacyZone, string DisposalMethod, string[] RedactableFields, string RetentionAnchor);
public sealed record AuditRetentionPolicyChangeV1(Guid? ExpectedVersion, Guid PolicyRef, Guid BasisRef,
    bool Enabled, bool WorkerEnabled, int DurationDays, string? LegacyZone, string? DisposalMethod = null, string[]? RedactableFields = null);
public sealed record AuditSafeguardChangeV1(Guid ReferenceId, string NativeId, string Kind, bool Active, Guid? ExpectedVersion);
public sealed record AuditDisposalV1(Guid PlanId, Guid TenantId, string RetentionClass, Guid? EffectiveActorId,
    Guid? ActualActorId, string? Service, Guid PolicyVersion, DateTimeOffset PlannedAt, DateTimeOffset Cutoff,
    DateTimeOffset ExpiresAt, int Eligible, string State, int Affected, string? Reason, DateTimeOffset? CompletedAt,
    string Method, Guid PolicyRef, Guid BasisRef);

public sealed class AuditRetentionV1(CP6Context db, TimeProvider? time = null)
{
    public const string Telemetry = "TECH_TELEMETRY";
    public const string Security = "SECURITY_EVENT";
    public const string Field = "FIELD_HISTORY";
    public const string ManagePermission = "operlog:retention-manage";
    public const string Worker = "CP6.WebApi.AuditRetentionWorker.v1";
    public const int BatchSize = 500;
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    internal sealed record Actor(Guid? Effective, Guid? Actual, string? Service);
    private static readonly Actor WorkerActor = new(null, null, Worker);
    private sealed record Source(string Table, string LegacyTime, bool IntegerId);
    private sealed record Key(string NativeId, string Revision);
    private sealed record GuardStamp(long Safeguard, long Privacy, bool PrivacyActive);
    private sealed record Receipt(string State, string Reason, int Affected, DateTimeOffset At);
    private sealed record Plan(Guid Id, string RetentionClass, Guid? ActorId, Guid? Actual, string? Service,
        Guid PolicyVersion, DateTimeOffset Cutoff, DateTimeOffset PlannedAt, DateTimeOffset ExpiresAt,
        long Safeguard, long Privacy, string Candidates, string Mode, string Method, Guid PolicyRef, Guid BasisRef);

    // 请求中的字段键仅用于查此映射，不能成为 SQL 列名。
    private static string[] RedactionColumns(string key, string method, string[] fields)
    {
        if (method == "DELETE" && fields.Length == 0) return [];
        if (method != "ANONYMIZE" || fields.Length == 0 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw Fail("AUDIT_RETENTION_POLICY_INVALID", 400);
        if (key is not (Telemetry or Security)) throw Fail("AUDIT_RETENTION_NATIVE_OWNER_REQUIRED", 412);
        return fields.Select(field => (key, field) switch
        {
            (Telemetry, "requestBody") => "RequestBody",
            (Telemetry or Security, "userName") => "UserName",
            (Telemetry or Security, "clientIp") => "ClientIp",
            (Security, "userAgent") => "UserAgent",
            _ => throw Fail("AUDIT_RETENTION_REDACTION_FIELD_INVALID", 400)
        }).ToArray();
    }

    private static Source For(string retentionClass) => retentionClass switch
    {
        Telemetry => new("Sys_OperLogs", "CreateDate", true),
        Security => new("Sys_SecurityLogs", "CreatedAt", false),
        Field => new("Sys_FieldAuditLogs", "ChangedAt", false),
        _ => throw Fail("AUDIT_RETENTION_NATIVE_OWNER_REQUIRED", 412)
    };

    private static bool KnownClass(string key) => key is Telemetry or Security or Field or "OWNER_RESULT_EVIDENCE"
        or "REPLAY_AUDIT" or "TECHNICAL_CONTENT_EVIDENCE" or "PRIVACY_EXECUTION_EVIDENCE";

    internal Task LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        LOCK TABLE cp6_audit_v1.policies, cp6_audit_v1.safeguards, cp6_audit_v1.plans, cp6_audit_v1.receipts IN SHARE ROW EXCLUSIVE MODE
        """, ct);

    internal Task<AuditRetentionPolicyV1?> PolicyAsync(Guid tenant, string key, CancellationToken ct)
        => db.Database.SqlQuery<AuditRetentionPolicyV1>($"""
            SELECT version AS "Version", policy_ref AS "PolicyRef", basis_ref AS "BasisRef", enabled AS "Enabled",
                worker_enabled AS "WorkerEnabled", duration_days AS "DurationDays", legacy_zone AS "LegacyZone",
                disposal_method AS "DisposalMethod", redactable_fields AS "RedactableFields", retention_anchor AS "RetentionAnchor"
            FROM cp6_audit_v1.policies WHERE tenant_id={tenant} AND retention_class={key} ORDER BY sequence DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal async Task<AuditRetentionPolicyV1> PublishAsync(Guid tenant, Guid owner, string key,
        AuditRetentionPolicyChangeV1 change, CancellationToken ct)
    {
        if (!KnownClass(key) || change.PolicyRef == Guid.Empty || change.BasisRef == Guid.Empty
            || change.DurationDays is < 1 or > 36500 || change.LegacyZone is not null && key != Telemetry
            || change.WorkerEnabled && (!change.Enabled || key is not (Telemetry or Security or Field)))
            throw Fail("AUDIT_RETENTION_POLICY_INVALID", 400);
        if (change.LegacyZone is not null) LegacyZone(change.LegacyZone);
        var method = change.DisposalMethod ?? "DELETE";
        var fields = change.RedactableFields ?? [];
        RedactionColumns(key, method, fields);
        if ((await PolicyAsync(tenant, key, ct))?.Version != change.ExpectedVersion)
            throw Fail("AUDIT_RETENTION_POLICY_CHANGED", 409);
        var version = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_audit_v1.policies(tenant_id,retention_class,version,policy_ref,basis_ref,owner_id,enabled,worker_enabled,
                duration_days,legacy_zone,disposal_method,redactable_fields)
            VALUES({tenant},{key},{version},{change.PolicyRef},{change.BasisRef},{owner},{change.Enabled},{change.WorkerEnabled},
                {change.DurationDays},{change.LegacyZone},{method},{fields})
            """, ct);
        return new(version, change.PolicyRef, change.BasisRef, change.Enabled, change.WorkerEnabled, change.DurationDays,
            change.LegacyZone, method, fields, "AUDIT_AT");
    }

    internal async Task<Guid> SafeguardAsync(Guid tenant, Guid owner, string key, AuditSafeguardChangeV1 change, CancellationToken ct)
    {
        var source = For(key);
        var nativeId = change.NativeId;
        if (change.ReferenceId == Guid.Empty || change.Kind is not ("HOLD" or "UNKNOWN" or "PROCESSING" or "RESCHEDULING" or "DISPUTE" or "REQUIRED_REFERENCE")
            || (source.IntegerId ? !int.TryParse(nativeId, out var number) || number <= 0 || number.ToString(System.Globalization.CultureInfo.InvariantCulture) != nativeId
                : !Guid.TryParseExact(nativeId, "D", out var guid) || guid.ToString("D") != nativeId || guid == Guid.Empty))
            throw Fail("AUDIT_RETENTION_SAFEGUARD_INVALID", 400);
        var current = await db.Database.SqlQuery<Guid?>($"""
            SELECT version AS "Value" FROM cp6_audit_v1.safeguards
            WHERE tenant_id={tenant} AND retention_class={key} AND native_id={nativeId} AND kind={change.Kind}
              AND reference_id={change.ReferenceId} ORDER BY sequence DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (current != change.ExpectedVersion || !change.Active && current is null)
            throw Fail("AUDIT_RETENTION_SAFEGUARD_CHANGED", 409);
        await LockSourceAsync(source, ct);
        await using var entry = Command($"""
            SELECT EXISTS(SELECT FROM public."{source.Table}" WHERE "TenantId"=@tenant AND "Id"=@id)
            """);
        entry.Parameters.AddWithValue("tenant", tenant);
        entry.Parameters.AddWithValue("id", source.IntegerId ? (object)int.Parse(nativeId, System.Globalization.CultureInfo.InvariantCulture) : Guid.Parse(nativeId));
        if (!((bool)(await entry.ExecuteScalarAsync(ct))!)) throw Fail("AUDIT_RETENTION_RECORD_NOT_FOUND", 404);
        var version = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_audit_v1.safeguards(tenant_id,retention_class,native_id,reference_id,kind,active,version,owner_id)
            VALUES({tenant},{key},{nativeId},{change.ReferenceId},{change.Kind},{change.Active},{version},{owner})
            """, ct);
        return version;
    }

    internal async Task<AuditDisposalV1> PreviewAsync(Guid tenant, string key, Actor actor, Guid id,
        string mode, CancellationToken ct)
    {
        var source = For(key);
        var existing = mode == "LEGACY" ? await LegacyAsync(tenant, actor, ct) : await PlanAsync(tenant, id, ct);
        if (existing is not null)
        {
            Match(existing, actor, key, mode);
            return await ViewAsync(tenant, existing, ct);
        }

        var policy = await PolicyAsync(tenant, key, ct);
        if (policy is not { Enabled: true } || mode == "WORKER" && !policy.WorkerEnabled)
            throw Fail("AUDIT_RETENTION_POLICY_REQUIRED", 412);
        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-policy.DurationDays);
        await LockSourceAsync(source, ct);
        var stamp = await SafeguardsAsync(tenant, key, ct);
        var candidates = stamp.PrivacyActive ? [] : await CandidatesAsync(tenant, key, source, cutoff, policy, ct);
        var plan = new Plan(id, key, actor.Effective, actor.Actual, actor.Service, policy.Version, cutoff,
            now, now.AddHours(24), stamp.Safeguard, stamp.Privacy, JsonSerializer.Serialize(candidates), mode,
            policy.DisposalMethod, policy.PolicyRef, policy.BasisRef);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_audit_v1.plans(tenant_id,id,retention_class,actor_id,actual_actor_id,service,mode,policy_version,cutoff,
                planned_at,expires_at,safeguard_sequence,privacy_sequence,candidates)
            VALUES({tenant},{id},{key},{actor.Effective},{actor.Actual},{actor.Service},{mode},{policy.Version},{cutoff},
                {now},{plan.ExpiresAt},{stamp.Safeguard},{stamp.Privacy},CAST({plan.Candidates} AS jsonb))
            """, ct);
        return View(tenant, plan, candidates.Count, null);
    }

    internal async Task<AuditDisposalV1> ReadAsync(Guid tenant, Guid id, Actor actor, CancellationToken ct)
    {
        var plan = await PlanAsync(tenant, id, ct);
        if (plan is null || !Owns(plan, actor)) throw Fail("AUDIT_RETENTION_PLAN_NOT_FOUND", 404);
        return await ViewAsync(tenant, plan, ct);
    }

    internal async Task<AuditDisposalV1> ReadLegacyAsync(Guid tenant, Actor actor, CancellationToken ct)
        => await ViewAsync(tenant, await LegacyAsync(tenant, actor, ct)
            ?? throw Fail("AUDIT_RETENTION_PLAN_NOT_FOUND",404), ct);

    internal async Task<AuditDisposalV1> ExecuteAsync(Guid tenant, Guid id, Actor actor, CancellationToken ct)
    {
        var plan = await PlanAsync(tenant, id, ct);
        if (plan is null || !Owns(plan, actor)) throw Fail("AUDIT_RETENTION_PLAN_NOT_FOUND", 404);
        var keys = JsonSerializer.Deserialize<Key[]>(plan.Candidates)!;
        var original = await ReceiptAsync(tenant, plan.Id, ct);
        if (original is not null) return View(tenant, plan, keys.Length, original);
        var source = For(plan.RetentionClass);
        var policy = await PolicyAsync(tenant, plan.RetentionClass, ct);
        var state = "COMPLETED";
        var reason = "APPLIED";
        var affected = 0;
        if (clock.GetUtcNow() >= plan.ExpiresAt) (state, reason) = ("EXPIRED", "PREVIEW_EXPIRED");
        else if (policy is not { Enabled: true } || policy.Version != plan.PolicyVersion || actor.Service is not null &&
            (!policy.WorkerEnabled || !await WorkerTenantAsync(tenant, ct)))
            (state, reason) = ("BLOCKED", "POLICY_CHANGED");
        else
        {
            await LockSourceAsync(source, ct);
            var stamp = await SafeguardsAsync(tenant, plan.RetentionClass, ct);
            if (stamp.Safeguard != plan.Safeguard || stamp.Privacy != plan.Privacy || stamp.PrivacyActive)
                (state, reason) = ("BLOCKED", "SAFEGUARDS_CHANGED_OR_ACTIVE");
            else if (keys.Length != 0)
            {
                await using var check = EntryCommand("COUNT", source, tenant, keys);
                if ((long)(await check.ExecuteScalarAsync(ct))! != keys.Length)
                    (state, reason) = ("BLOCKED", "NATIVE_RECORD_CHANGED");
                else
                {
                    var columns = RedactionColumns(plan.RetentionClass, policy.DisposalMethod, policy.RedactableFields);
                    await using var remove = EntryCommand(policy.DisposalMethod, source, tenant, keys, columns);
                    affected = await remove.ExecuteNonQueryAsync(ct);
                    if (affected != keys.Length) throw Fail("AUDIT_RETENTION_WRITE_CONFLICT", 409);
                }
            }
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_audit_v1.receipts(tenant_id,plan_id,state,reason,affected)
            VALUES({tenant},{plan.Id},{state},{reason},{affected})
            """, ct);
        return View(tenant, plan, keys.Length, (await ReceiptAsync(tenant, plan.Id, ct))!);
    }

    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) return 0;
        var due = await db.Database.SqlQueryRaw<Work>("""
            SELECT c.tenant_id AS "Tenant",c.retention_class AS "RetentionClass" FROM (
                SELECT DISTINCT ON (tenant_id,retention_class) tenant_id,retention_class,enabled,worker_enabled
                FROM cp6_audit_v1.policies ORDER BY tenant_id,retention_class,sequence DESC
            ) c WHERE c.enabled AND c.worker_enabled AND c.retention_class IN ('TECH_TELEMETRY','SECURITY_EVENT','FIELD_HISTORY')
            UNION SELECT p.tenant_id,p.retention_class FROM cp6_audit_v1.plans p WHERE p.mode='WORKER'
              AND NOT EXISTS (SELECT FROM cp6_audit_v1.receipts r WHERE r.tenant_id=p.tenant_id AND r.plan_id=p.id)
            """).ToListAsync(ct);
        var total = 0;
        foreach (var work in due)
        {
            var plan = await WorkerTransactionAsync(async token =>
            {
                var today = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
                var recent = await db.Database.SqlQuery<Guid>($"""
                    SELECT p.id AS "Value" FROM cp6_audit_v1.plans p WHERE p.tenant_id={work.Tenant}
                      AND p.retention_class={work.RetentionClass} AND p.mode='WORKER'
                      AND (p.planned_at>={today} OR NOT EXISTS (SELECT FROM cp6_audit_v1.receipts r WHERE r.tenant_id=p.tenant_id AND r.plan_id=p.id))
                    ORDER BY p.planned_at,p.id LIMIT 1
                    """).SingleOrDefaultAsync(token);
                if (recent != Guid.Empty) return recent;
                var policy = await PolicyAsync(work.Tenant, work.RetentionClass, token);
                var enabled = await WorkerTenantAsync(work.Tenant, token);
                if (!enabled || policy is not { Enabled: true, WorkerEnabled: true }) return Guid.Empty;
                var preview = await PreviewAsync(work.Tenant, work.RetentionClass, WorkerActor, Guid.NewGuid(), "WORKER", token);
                return preview.PlanId;
            }, ct);
            if (plan != Guid.Empty)
                total += (await WorkerTransactionAsync(token => ExecuteAsync(work.Tenant, plan, WorkerActor, token), ct)).Affected;
        }
        return total;
    }

    private sealed record Work(Guid Tenant, string RetentionClass);

    private Task<bool> WorkerTenantAsync(Guid tenant, CancellationToken ct)
        => db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant && x.Enable
            && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct);

    private async Task<T> WorkerTransactionAsync<T>(Func<CancellationToken,Task<T>> execute, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        var attempted = false;
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            await LockAsync(ct);
            var result = await execute(ct);
            ct.ThrowIfCancellationRequested();
            attempted = true;
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (attempted)
        {
            throw new OrderPriceCorrectionException("AUDIT_RETENTION_OUTCOME_UNKNOWN", 503, error);
        }
    }

    private Task<Plan?> PlanAsync(Guid tenant, Guid id, CancellationToken ct) => db.Database.SqlQuery<Plan>($"""
        SELECT p.id AS "Id",p.retention_class AS "RetentionClass",p.actor_id AS "ActorId", p.actual_actor_id AS "Actual",p.service AS "Service",p.mode AS "Mode",
          p.policy_version AS "PolicyVersion",p.cutoff AS "Cutoff",p.planned_at AS "PlannedAt",p.expires_at AS "ExpiresAt",
          p.safeguard_sequence AS "Safeguard",p.privacy_sequence AS "Privacy", p.candidates::text AS "Candidates",
          c.disposal_method AS "Method",c.policy_ref AS "PolicyRef",c.basis_ref AS "BasisRef"
        FROM cp6_audit_v1.plans p JOIN cp6_audit_v1.policies c ON c.version=p.policy_version
          AND c.tenant_id=p.tenant_id AND c.retention_class=p.retention_class
        WHERE p.tenant_id={tenant} AND p.id={id}
        """).SingleOrDefaultAsync(ct);

    private async Task<Plan?> LegacyAsync(Guid tenant, Actor actor, CancellationToken ct)
    {
        var id = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_audit_v1.plans WHERE tenant_id={tenant} AND mode='LEGACY'
              AND actor_id={actor.Effective} AND actual_actor_id IS NOT DISTINCT FROM {actor.Actual}
            """).SingleOrDefaultAsync(ct);
        return id == Guid.Empty ? null : await PlanAsync(tenant, id, ct);
    }

    private Task<Receipt?> ReceiptAsync(Guid tenant, Guid id, CancellationToken ct) => db.Database.SqlQuery<Receipt>($"""
        SELECT state AS "State", reason AS "Reason", affected AS "Affected", at AS "At"
        FROM cp6_audit_v1.receipts WHERE tenant_id={tenant} AND plan_id={id}
        """).SingleOrDefaultAsync(ct);

    private Task<GuardStamp> SafeguardsAsync(Guid tenant, string key, CancellationToken ct) => db.Database.SqlQuery<GuardStamp>($"""
        SELECT coalesce((SELECT max(sequence) FROM cp6_audit_v1.safeguards WHERE tenant_id={tenant} AND retention_class={key}),0) AS "Safeguard",
          coalesce((SELECT max(sequence) FROM cp6_privacy_disposition_v1.holds WHERE target_tenant={tenant}),0) AS "Privacy",
          EXISTS (SELECT FROM (SELECT DISTINCT ON (environment_id,subject_id) active FROM cp6_privacy_disposition_v1.holds
            WHERE target_tenant={tenant} ORDER BY environment_id,subject_id,sequence DESC) holds WHERE active) AS "PrivacyActive"
        """).SingleAsync(ct);

    private async Task<List<Key>> CandidatesAsync(Guid tenant, string key, Source source, DateTimeOffset cutoff,
        AuditRetentionPolicyV1 policy, CancellationToken ct)
    {
        var zone = policy.LegacyZone is null ? null : LegacyZone(policy.LegacyZone);
        var localCutoff = zone is null ? (DateTime?)null : TimeZoneInfo.ConvertTime(cutoff, zone).DateTime;
        var columns = RedactionColumns(key, policy.DisposalMethod, policy.RedactableFields);
        var pendingRedaction = columns.Length == 0 ? "" : $"AND ({string.Join(" OR ", columns.Select(c => $"s.\"{c}\" IS NOT NULL"))})";
        await using var command = Command($"""
            SELECT s."Id"::text,s.xmin::text,s."AuditAtUtc",s."{source.LegacyTime}"
            FROM public."{source.Table}" s WHERE s."TenantId"=@tenant
              AND ((s."AuditAtUtc"<@cutoff AND s."AuditEvidenceJson"->>'schemaVersion'='1'
                    AND s."AuditEvidenceJson"->>'outcome' IN ('HTTP_OBSERVED','HTTP_DENIED','HTTP_ERROR','OWNER_RECORDED','SERVICE_RECORDED'))
                OR (s."AuditAtUtc" IS NULL AND s."AuditEvidenceJson" IS NULL AND s."{source.LegacyTime}"<@local))
              AND s."AuditEvidenceJson"->>'operationId' IS NULL
              AND s."AuditEvidenceJson"->>'originalOperationDigest' IS NULL
              {(source.IntegerId ? """
                   AND NOT s."IsAlert" AND s."StatusCode" >= 200 AND s."StatusCode" < 500 AND s."StatusCode" <> 202
                   AND (s."AuditAtUtc" IS NOT NULL OR
                       (s."HttpMethod" IN ('GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS')
                        AND s."Controller" IS DISTINCT FROM 'BankReconciliation'))
                   """ : "")}
              {pendingRedaction}
              AND NOT EXISTS (SELECT FROM (SELECT DISTINCT ON (kind,reference_id) active FROM cp6_audit_v1.safeguards
                WHERE tenant_id=@tenant AND retention_class=@class AND native_id=s."Id"::text
                ORDER BY kind,reference_id,sequence DESC) guards WHERE active)
            ORDER BY s."{source.LegacyTime}",s."Id"
            """);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("class", key);
        command.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);
        command.Parameters.AddWithValue("local", NpgsqlDbType.Timestamp, (object?)localCutoff ?? DBNull.Value);
        var selected = new List<Key>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (selected.Count < BatchSize && await rows.ReadAsync(ct))
        {
            if (rows.IsDBNull(2))
            {
                var recorded = rows.GetDateTime(3);
                if (zone!.IsAmbiguousTime(recorded) || zone.IsInvalidTime(recorded)
                    || TimeZoneInfo.ConvertTimeToUtc(recorded, zone) >= cutoff.UtcDateTime) continue;
            }
            selected.Add(new(rows.GetString(0), rows.GetString(1)));
        }
        return selected;
    }

    private NpgsqlCommand EntryCommand(string action, Source source, Guid tenant, Key[] entries, string[]? columns = null)
    {
        var operation = action switch
        {
            "COUNT" => $"SELECT count(*) FROM public.\"{source.Table}\" AS s",
            "DELETE" => $"DELETE FROM public.\"{source.Table}\" AS s",
            "ANONYMIZE" => $"UPDATE public.\"{source.Table}\" AS s SET {string.Join(",", columns!.Select(x => $"\"{x}\"=NULL"))}",
            _ => throw new InvalidOperationException("未知的审计记录处置方式。")
        };
        var command = Command($"""
            {operation} WHERE s."TenantId"=@tenant
              AND EXISTS (SELECT FROM unnest(@ids,@revisions) k(id,revision) WHERE s."Id"=k.id AND s.xmin::text=k.revision)
            """);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("ids", source.IntegerId ? (object)entries.Select(x => int.Parse(x.NativeId, System.Globalization.CultureInfo.InvariantCulture)).ToArray()
            : entries.Select(x => Guid.Parse(x.NativeId)).ToArray());
        command.Parameters.AddWithValue("revisions", entries.Select(x => x.Revision).ToArray());
        return command;
    }

    private async Task LockSourceAsync(Source source, CancellationToken ct)
    {
        await using var command = Command($"LOCK TABLE public.\"{source.Table}\" IN SHARE ROW EXCLUSIVE MODE");
        await command.ExecuteNonQueryAsync(ct);
    }

    private NpgsqlCommand Command(string sql) => new(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
        (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());

    private async Task<AuditDisposalV1> ViewAsync(Guid tenant, Plan plan, CancellationToken ct)
        => View(tenant, plan, JsonSerializer.Deserialize<Key[]>(plan.Candidates)!.Length, await ReceiptAsync(tenant, plan.Id, ct));

    private static AuditDisposalV1 View(Guid tenant, Plan p, int eligible, Receipt? r) => new(p.Id, tenant,
        p.RetentionClass, p.ActorId, p.Actual, p.Service, p.PolicyVersion, p.PlannedAt, p.Cutoff, p.ExpiresAt,
        eligible, r?.State ?? "READY", r?.Affected ?? 0, r?.Reason, r?.At, p.Method, p.PolicyRef, p.BasisRef);

    private static bool Owns(Plan p, Actor actor) => p.ActorId == actor.Effective && p.Actual == actor.Actual && p.Service == actor.Service;
    private static void Match(Plan p, Actor actor, string key, string mode)
    {
        if (!Owns(p, actor) || p.RetentionClass != key || p.Mode != mode) throw Fail("AUDIT_RETENTION_PLAN_CONFLICT", 409);
    }

    private static TimeZoneInfo LegacyZone(string id)
    {
        if (id.Length > 128 || id.Trim() != id) throw Fail("AUDIT_RETENTION_TIME_ZONE_INVALID",400);
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw Fail("AUDIT_RETENTION_TIME_ZONE_INVALID",400); }
        catch (InvalidTimeZoneException) { throw Fail("AUDIT_RETENTION_TIME_ZONE_INVALID",400); }
    }

    internal static OrderPriceCorrectionException Fail(string code, int status) => new(code, status);
}

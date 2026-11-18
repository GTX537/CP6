using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record AuditFilterV1(string? Keyword = null, int? EventType = null, string? EntityName = null,
    string? EntityKey = null, Guid? UserId = null, DateTime? From = null, DateTime? To = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, string? TimeSource = null);
public sealed record AuditExportRequestV1(Guid PurposeRef, string ExpectedPolicyVersion, AuditFilterV1? Filter = null);
public sealed record AuditExportV1(Guid Id, Guid TenantId, string EvidenceClass, string PolicyVersion,
    int RowCount, string Sha256, DateTimeOffset ExpiresAt);

public sealed class AuditBrowseV1(CP6Context db, OperationLogOwnerV1 authority, OperationsOwnerV1 operations)
{
    public const string PolicyVersion = "Main.Audit.SafeProjection.v1";
    private const int ExportLimit = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] TelemetryRestrictions = ["requestBody", "physicalUrl", "clientIp", "userName"];
    private static readonly string[] SecurityRestrictions = ["userName", "clientIp", "userAgent", "nativeContext", "freeTextReason"];
    private static readonly string[] FieldRestrictions = ["nativeChanges", "userName", "fieldsOutsideCurrentPolicy"];

    private sealed record Native
    {
        public int IntId { get; init; }
        public Guid GuidId { get; init; }
        public Guid TenantId { get; init; }
        public DateTime LegacyTime { get; init; }
        public DateTimeOffset? UtcTime { get; init; }
        public string? Evidence { get; init; }
        public string? Route { get; init; }
        public string? Controller { get; init; }
        public string? Action { get; init; }
        public string? Method { get; init; }
        public int Kind { get; init; }
        public long Duration { get; init; }
        public bool IsAlert { get; init; }
        public string? EntityName { get; init; }
        public string? EntityKey { get; init; }
        public string? Changes { get; init; }
        public Guid? Subject { get; init; }
        public string? TargetTenantCode { get; init; }
    }

    private sealed record Scope(IQueryable<Native> Query, HashSet<string> HiddenMachineFields);
    private sealed record ExportRegistration(Guid Purpose, string EvidenceClass, string PolicyVersion, string PermissionVersion,
        DateTimeOffset Expiry, string NativeKeys, string Sha256);

    public Task<object> ListAsync(string key, AuditFilterV1 filter, int page, int pageSize, string? cursor,
        CancellationToken ct = default)
        => authority.Execute<object>(Menu(key) + ":query", async (actor, token) =>
            await PageAsync(key, await SourceAsync(key, actor, token), filter, page, pageSize, cursor, token), ct);

    internal async Task<object> PlatformPageAsync(string? targetTenantCode, AuditFilterV1 filter, int page, int pageSize,
        string? cursor, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_SecurityLogs" IN SHARE MODE""", ct);
        var source = SecuritySource(platform: true);
        if (!string.IsNullOrWhiteSpace(targetTenantCode))
            source = source.Where(x => x.TargetTenantCode == targetTenantCode);
        return await PageAsync(AuditRetentionV1.Security, new(source, []), filter, page, pageSize, cursor, ct);
    }

    public Task<object> ContentAsync(string key, string id, CancellationToken ct = default)
        => authority.Execute<object>(Menu(key) + ":read-content", async (actor, token) =>
        {
            Require(actor, Menu(key) + ":query");
            var scope = await SourceAsync(key, actor, token);
            var row = await ById(scope.Query, key, id).SingleOrDefaultAsync(token);
            if (row is null) throw Error("AUDIT_RECORD_NOT_VISIBLE", 404);
            if (key == AuditRetentionV1.Field && row.EntityKey is null) throw Error("DETAIL_UNAVAILABLE", 503);
            return Project(key, row, true, scope.HiddenMachineFields);
        }, ct);

    public Task<AuditExportV1> ExportAsync(string key, AuditExportRequestV1 request, CancellationToken ct = default)
        => authority.Execute(Menu(key) + ":export", async (actor, token) =>
        {
            RequireExport(actor, key);
            if (request.PurposeRef == Guid.Empty || request.ExpectedPolicyVersion != PolicyVersion)
                throw Error("AUDIT_EXPORT_POLICY_OR_PURPOSE_REQUIRED", 412);
            var scope = await SourceAsync(key, actor, token);
            var query = Filter(scope.Query, key, request.Filter ?? new());
            var rows = await Order(query, key).Take(ExportLimit + 1).ToArrayAsync(token);
            if (rows.Length > ExportLimit) throw Error("AUDIT_EXPORT_NARROW_SCOPE", 422);
            var frozenIds = JsonSerializer.Serialize(rows.Select(x => Id(key, x)).ToArray(), Json);
            var digest = Convert.ToHexString(SHA256.HashData(Bytes(key, rows, scope.HiddenMachineFields)));
            var expires = DateTimeOffset.UtcNow.AddHours(1);
            var id = Guid.NewGuid();
            var permission = AuditEvidenceV1.PermissionDecisionVersion(db.CurrentTenantId, actor);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_audit_v1.exports(tenant_id,id,actor_id,purpose_ref,evidence_class,policy_version,
                    permission_version,expires_at,native_keys,sha256)
                VALUES({db.CurrentTenantId},{id},{actor.UserId},{request.PurposeRef},{key},{PolicyVersion},{permission},
                    {expires},CAST({frozenIds} AS jsonb),{digest})
                """, token);
            return new AuditExportV1(id, db.CurrentTenantId, key, PolicyVersion, rows.Length, digest, expires);
        }, ct);

    public Task<byte[]> DownloadAsync(string key, Guid id, CancellationToken ct = default)
        => authority.Execute(Menu(key) + ":export", async (actor, token) =>
        {
            RequireExport(actor, key);
            var frozen = await db.Database.SqlQuery<ExportRegistration>($"""
                SELECT purpose_ref AS "Purpose",evidence_class AS "EvidenceClass",policy_version AS "PolicyVersion",
                    permission_version AS "PermissionVersion",expires_at AS "Expiry",native_keys::text AS "NativeKeys",sha256 AS "Sha256"
                FROM cp6_audit_v1.exports WHERE tenant_id={db.CurrentTenantId} AND id={id} AND actor_id={actor.UserId}
                  AND evidence_class={key}
                """).SingleOrDefaultAsync(token);
            if (frozen is null) throw Error("AUDIT_EXPORT_NOT_FOUND", 404);
            if (frozen.Expiry <= DateTimeOffset.UtcNow) throw Error("AUDIT_EXPORT_EXPIRED", 410);
            if (frozen.PolicyVersion != PolicyVersion
                || frozen.PermissionVersion != AuditEvidenceV1.PermissionDecisionVersion(db.CurrentTenantId, actor))
                throw Error("AUDIT_EXPORT_ACCESS_CHANGED", 403);
            var scope = await SourceAsync(key, actor, token);
            var ids = JsonSerializer.Deserialize<string[]>(frozen.NativeKeys, Json)!;
            var exportable = scope.Query;
            if (key == AuditRetentionV1.Telemetry)
            {
                var numeric = ids.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                exportable = exportable.Where(x => numeric.Contains(x.IntId));
            }
            else
            {
                var guids = ids.Select(Guid.Parse).ToArray();
                exportable = exportable.Where(x => guids.Contains(x.GuidId));
            }
            var rows = (await exportable.ToArrayAsync(token)).ToDictionary(r => Id(key, r));
            if (rows.Count != ids.Length) throw Error("AUDIT_EXPORT_ACCESS_CHANGED", 403);
            var bytes = Bytes(key, ids.Select(x => rows[x]), scope.HiddenMachineFields);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != frozen.Sha256)
                throw Error("AUDIT_EXPORT_ACCESS_CHANGED", 403);
            if (frozen.Expiry <= DateTimeOffset.UtcNow) throw Error("AUDIT_EXPORT_EXPIRED", 410);
            return bytes;
        }, ct);

    private void RequireExport(UserPermissionContext actor, string key)
    {
        Require(actor, Menu(key) + ":query");
        Require(actor, Menu(key) + ":read-content");
        if (authority.IsImpersonated) throw Error("AUDIT_EXPORT_IMPERSONATION_FORBIDDEN", 403);
    }

    private async Task<Scope> SourceAsync(string key, UserPermissionContext actor, CancellationToken ct)
    {
        switch (key)
        {
            case AuditRetentionV1.Telemetry:
                await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_OperLogs" IN SHARE MODE""", ct);
                return new(db.Sys_OperLogs.AsNoTracking().Select(x => new Native
                {
                    IntId = x.Id, TenantId = x.TenantId, LegacyTime = x.CreateDate, UtcTime = x.AuditAtUtc, Evidence = x.AuditEvidenceJson,
                    Route = x.AuditAtUtc == null ? null : x.Controller == "IntegrationEvent" ? "/integration-event/{id}" : x.RequestUrl,
                    Controller = x.Controller, Action = x.Action,
                    Method = x.HttpMethod, Duration = x.ElapsedMs, Kind = x.StatusCode, IsAlert = x.IsAlert
                }), []);
            case AuditRetentionV1.Security:
                await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_SecurityLogs" IN SHARE MODE""", ct);
                return new(SecuritySource(false), []);
            case AuditRetentionV1.Field:
                var audit = db.Sys_FieldAuditLogs.AsNoTracking();
                var query = audit.Where(a => a.EntityName != "Machine");
                var hasMachine = actor.ActionKeys.Contains(OperationsOwnerV1.Machine + ":query");
                if (hasMachine)
                {
                    await operations.LockAuditMachineGraphAsync(ct);
                    var nativeAuthority = await operations.AuthorityAsync(OperationsOwnerV1.Machine, "query", ct);
                    var machines = new OperationsAccessV1(db, operations, nativeAuthority).Machines();
                    query = audit.Where(a => a.EntityName != "Machine" || machines.Any(m => m.Id.ToString() == a.EntityKey));
                }
                await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_FieldAuditLogs" IN SHARE MODE""", ct);
                return new(query.Select(x => new Native
                {
                    GuidId = x.Id, TenantId = x.TenantId, LegacyTime = x.ChangedAt, UtcTime = x.AuditAtUtc, Evidence = x.AuditEvidenceJson,
                    EntityName = x.EntityName, EntityKey = x.EntityName == "Machine" ? x.EntityKey : null,
                    Subject = x.EntityName == "Machine" ? x.UserId : null, Kind = x.Operation, Changes = x.Changes
                }), HiddenFields(actor));
            default: throw Error("DETAIL_UNAVAILABLE", 503);
        }
    }

    private IQueryable<Native> SecuritySource(bool platform)
    {
        var query = db.Sys_SecurityLogs.AsNoTracking();
        if (platform) query = query.IgnoreQueryFilters();
        else if (db.CurrentTenantId == TenantContext.DefaultTenant)
            query = query.Where(x => x.EventType != (int)CP6.Entity.DomainModels.Sys.SecurityEventType.ImpersonationStarted
                && x.EventType != (int)CP6.Entity.DomainModels.Sys.SecurityEventType.ImpersonationEnded);
        return query.Select(x => new Native
        {
            GuidId = x.Id, TenantId = x.TenantId, LegacyTime = x.CreatedAt, UtcTime = x.AuditAtUtc,
            Evidence = x.AuditEvidenceJson, Kind = x.EventType, Subject = x.UserId,
            TargetTenantCode = platform && x.TenantId == TenantContext.DefaultTenant
                && (x.EventType >= (int)SecurityEventType.TenantCreated && x.EventType <= (int)SecurityEventType.GdprSubjectErased)
                ? x.RequestTenantCode : null
        });
    }

    private static async Task<object> PageAsync(string key, Scope scope, AuditFilterV1 filter, int page, int size,
        string? cursor, CancellationToken ct)
    {
        size = Math.Clamp(size, 1, 200);
        page = Math.Clamp(page, 1, int.MaxValue / size);
        var asOf = DateTimeOffset.UtcNow;
        var filtered = Filter(scope.Query, key, filter);
        var total = await filtered.CountAsync(ct);
        if (!string.IsNullOrEmpty(cursor))
        {
            var anchor = await ById(filtered, key, cursor).SingleOrDefaultAsync(ct);
            if (anchor is null) throw Error("AUDIT_CURSOR_INVALID", 400);
            filtered = key == AuditRetentionV1.Telemetry
                ? filtered.Where(x => x.LegacyTime < anchor.LegacyTime || x.LegacyTime == anchor.LegacyTime && x.IntId < anchor.IntId)
                : filtered.Where(x => x.LegacyTime < anchor.LegacyTime || x.LegacyTime == anchor.LegacyTime && x.GuidId.CompareTo(anchor.GuidId) < 0);
        }
        var pageRows = await Order(filtered, key).Skip(string.IsNullOrEmpty(cursor) ? (page - 1) * size : 0).Take(size + 1).ToListAsync(ct);
        var rows = pageRows.Take(size).Select(x => Project(key, x, false, scope.HiddenMachineFields)).ToArray();
        return new { rows, total, nextCursor = pageRows.Count > size ? Id(key, pageRows[size - 1]) : null,
            asOf, evidenceClass = key, redactedFields = Restrictions(key), policyVersion = PolicyVersion };
    }

    private static IQueryable<Native> Filter(IQueryable<Native> query, string key, AuditFilterV1 filter)
    {
        query = filter.TimeSource switch
        {
            null or "" => query,
            "UTC" => query.Where(x => x.UtcTime != null),
            "LEGACY_LOCAL" when !filter.FromUtc.HasValue && !filter.ToUtc.HasValue => query.Where(x => x.UtcTime == null),
            _ => throw Error("AUDIT_TIME_SOURCE_INVALID", 400)
        };
        if (filter.FromUtc.HasValue || filter.ToUtc.HasValue)
        {
            if (filter.From.HasValue || filter.To.HasValue || filter.FromUtc >= filter.ToUtc)
                throw Error("AUDIT_TIME_RANGE_INVALID", 400);
            if (filter.FromUtc is { } from) query = query.Where(x => x.UtcTime >= from.ToUniversalTime());
            if (filter.ToUtc is { } to) query = query.Where(x => x.UtcTime < to.ToUniversalTime());
        }
        else
        {
            if (filter.From is { } from) query = query.Where(x => x.LegacyTime >= DateTime.SpecifyKind(from, DateTimeKind.Unspecified));
            if (filter.To is { } to && to.Date != DateTime.MaxValue.Date)
            {
                var end = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Unspecified);
                query = query.Where(x => x.LegacyTime < end);
            }
        }
        if (filter.Keyword is { Length: > 120 } || filter.EntityName is { Length: > 120 } || filter.EntityKey is { Length: > 120 })
            throw Error("AUDIT_FILTER_INVALID", 400);
        if (key == AuditRetentionV1.Telemetry && !string.IsNullOrWhiteSpace(filter.Keyword))
            query = query.Where(x => x.Controller != null && x.Controller.Contains(filter.Keyword)
                    || x.Action != null && x.Action.Contains(filter.Keyword) || x.Route != null && x.Route.Contains(filter.Keyword));
        if (key == AuditRetentionV1.Security && filter.EventType.HasValue) query = query.Where(x => x.Kind == filter.EventType.Value);
        if (key == AuditRetentionV1.Field)
        {
            if (!string.IsNullOrWhiteSpace(filter.EntityName)) query = query.Where(x => x.EntityName == filter.EntityName);
            if (!string.IsNullOrWhiteSpace(filter.EntityKey)) query = query.Where(x => x.EntityKey == filter.EntityKey);
            if (filter.UserId.HasValue) throw Error("AUDIT_SEARCH_FIELD_RESTRICTED", 403);
        }
        return query;
    }

    private static IOrderedQueryable<Native> Order(IQueryable<Native> query, string key)
    {
        var ordered = query.OrderByDescending(x => x.LegacyTime);
        return key == AuditRetentionV1.Telemetry ? ordered.ThenByDescending(x => x.IntId) : ordered.ThenByDescending(x => x.GuidId);
    }

    private static IQueryable<Native> ById(IQueryable<Native> query, string key, string id)
        => key == AuditRetentionV1.Telemetry
            ? int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
                ? query.Where(x => x.IntId == n) : throw Error("AUDIT_ID_INVALID", 400)
            : Guid.TryParseExact(id, "D", out var g) && g != Guid.Empty
                ? query.Where(x => x.GuidId == g) : throw Error("AUDIT_ID_INVALID", 400);

    private static object Project(string key, Native row, bool content, HashSet<string> hidden)
    {
        var evidence = AuditEvidenceV1.Decode(row.Evidence);
        var timeSource = row.UtcTime.HasValue ? evidence?.Source ?? "UTC" : "LEGACY_LOCAL";
        var evidenceGap = evidence is null || evidence.PermissionVersion is null || evidence.ActorKind == "UNBOUND";
        if (key == AuditRetentionV1.Telemetry)
            return new { id = row.IntId, row.TenantId, controller = row.Controller, row.Action, httpMethod = row.Method,
                requestUrl = row.Route, statusCode = row.Kind, elapsedMs = row.Duration, createDate = row.LegacyTime,
                auditAtUtc = row.UtcTime, timeSource, row.IsAlert, outcome = evidence?.Outcome ?? "LEGACY_OBSERVATION",
                evidenceGap, evidence = content ? SafeEvidence(evidence) : null };
        if (key == AuditRetentionV1.Security)
            return new { id = row.GuidId, row.TenantId, requestTenantCode = row.TargetTenantCode, eventType = row.Kind,
                createdAt = row.LegacyTime, auditAtUtc = row.UtcTime, timeSource, evidenceGap,
                evidence = content ? SafeEvidence(evidence) : null };

        var mapped = row.EntityKey is not null;
        var changes = mapped ? FieldAuditChangesV1.MachineStatus(row.Changes ?? "", hidden) : null;
        var state = changes?.EvidenceStatus ?? "DETAIL_UNAVAILABLE";
        int? count = changes is not null && state != "UNREADABLE" ? changes.Changes.Count : null;
        var gap = evidenceGap || evidence?.EntityVersion is null || !mapped;
        if (!content)
            return new { id = row.GuidId, row.TenantId, row.EntityName, row.EntityKey, operation = row.Kind,
                changedAt = row.LegacyTime, auditAtUtc = row.UtcTime, timeSource, changeCount = count, evidenceStatus = state, evidenceGap = gap };
        return new { id = row.GuidId, row.TenantId, row.EntityName, row.EntityKey, operation = row.Kind,
            changedAt = row.LegacyTime, auditAtUtc = row.UtcTime, timeSource, changeCount = count, evidenceStatus = state,
            evidenceGap = gap, changes = changes?.Changes,
            nativeDigest = mapped && row.Changes is not null ? NativeDigest(row.Changes) : null,
            evidence = mapped ? SafeEvidence(evidence) : null };
    }

    internal static string NativeDigest(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    internal static HashSet<string> HiddenFields(UserPermissionContext actor)
        => actor.FieldPerms.TryGetValue(OperationsOwnerV1.Machine, out var list)
            ? list.Where(x => x.Value == 3).Select(x => x.Key).ToHashSet(StringComparer.Ordinal) : [];

    private static object? SafeEvidence(AuditEvidenceV1? e) => e is null ? null : new
    {
        e.SchemaVersion, e.Source, e.Reliability, e.ActorKind, e.ActualActor, e.EffectiveActor, e.Service,
        e.PermissionVersion, e.OperationId, e.Outcome, e.TraceId, e.EntityVersion, e.RecordedAtUtc, e.TransactionRef, e.DataPolicy, e.Action,
        e.OriginalOperationDigest
    };
    private static byte[] Bytes(string key, IEnumerable<Native> rows, HashSet<string> hidden)
        => JsonSerializer.SerializeToUtf8Bytes(new { evidenceClass = key, policyVersion = PolicyVersion,
            rows = rows.Select(x => Project(key, x, true, hidden)).ToArray() }, Json);

    private static string Id(string key, Native row) => key == AuditRetentionV1.Telemetry
        ? row.IntId.ToString(CultureInfo.InvariantCulture) : row.GuidId.ToString("D");
    private static string[] Restrictions(string key) => key switch
    { AuditRetentionV1.Telemetry => TelemetryRestrictions, AuditRetentionV1.Security => SecurityRestrictions, _ => FieldRestrictions };
    private static string Menu(string key) => key switch
    { AuditRetentionV1.Telemetry => "operlog", AuditRetentionV1.Security => "sys-security-log", AuditRetentionV1.Field => "sys-field-audit", _ => throw Error("DETAIL_UNAVAILABLE", 503) };
    private static void Require(UserPermissionContext actor, string action)
    { if (!actor.ActionKeys.Contains(action)) throw Error("AUDIT_ACTION_FORBIDDEN", 403); }
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

internal sealed class ToolMaintenanceStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Wms.ToolMaintenance.v1", PolicyOwner = "Main.Wms.ToolMaintenancePolicy.v1",
        ReportOwner = "Main.Wms.ToolMaintenanceReport.v1", QualificationOwner = "Main.Master.ToolMaintenanceRelease.v1", CycleOwner = "Main.Wms.ToolCycle.v1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static string OwnerOf(string kind) => kind switch { "POLICY" => PolicyOwner, "REPORT" => ReportOwner, "ASSESSMENT" => QualificationOwner, _ => Owner };
    internal static ToolMaintenanceRefV1 Ref(ToolMaintenanceFactV1 fact) => new(OwnerOf(fact.Kind), fact.RecordId, "1", Hash(JsonSerializer.Serialize(fact, Json)));
    internal static ToolMaintenanceRefV1 CycleRef(ToolMaintenanceCycleV1 cycle) => new(CycleOwner, cycle.Id, cycle.ProjectionRevision ?? "1", Hash(JsonSerializer.Serialize(cycle, Json)));
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_maintenance_v1.facts,cp6_tool_maintenance_v1.open_maintenance " + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=6 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid IN
              ('cp6_tool_maintenance_v1.facts'::regclass,'cp6_tool_maintenance_v1.open_maintenance'::regclass)
              AND tgname IN ('tool_maintenance_immutable','tool_maintenance_no_truncate','tool_maintenance_validate','tool_maintenance_open_insert','tool_maintenance_open_delete','tool_maintenance_open_update')
              AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_MAINTENANCE_UNAVAILABLE", 503);
    }
    private sealed class Row { public string Json { get; set; } = ""; public string Digest { get; set; } = ""; }
    internal async Task<ToolMaintenanceFactViewV1[]> Facts(Guid tool, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Row>($"""
            SELECT body_json AS "Json",body_digest AS "Digest" FROM cp6_tool_maintenance_v1.facts
            WHERE tenant_id={db.CurrentTenantId} AND tool_id={tool} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(row =>
        {
            var fact = JsonSerializer.Deserialize<ToolMaintenanceFactV1>(row.Json, Json);
            if (fact == null || fact.TenantId != db.CurrentTenantId || fact.ToolId != tool || Hash(row.Json) != row.Digest || Ref(fact).Digest != row.Digest)
                throw Error("TOOL_MAINTENANCE_UNAVAILABLE", 503);
            return new ToolMaintenanceFactViewV1(Ref(fact), fact);
        }).ToArray();
    }
    internal async Task<ToolMaintenanceFactViewV1> Append(ToolMaintenanceFactV1 fact, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(fact, Json); var reference = Ref(fact);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_maintenance_v1.facts(tenant_id,tool_id,record_id,kind,maintenance_id,body_json,body_digest)
            VALUES({fact.TenantId},{fact.ToolId},{fact.RecordId},{fact.Kind},{fact.MaintenanceId},{json},{reference.Digest})
            """, ct);
        return new(reference, fact);
    }
    internal static T Body<T>(ToolMaintenanceFactViewV1 fact) => fact.Fact.Body.Deserialize<T>(Json) ?? throw Error("TOOL_MAINTENANCE_UNAVAILABLE", 503);
    internal static ToolMaintenanceFactViewV1 Exact(ToolMaintenanceFactViewV1[] facts, ToolMaintenanceRefV1 reference, string kind, Guid? maintenance = null)
        => reference != null && facts.SingleOrDefault(x => x.Reference == reference && x.Fact.Kind == kind && (maintenance == null || x.Fact.MaintenanceId == maintenance)) is { } found
            ? found : throw Error("TOOL_MAINTENANCE_REFERENCE_CHANGED", 412);
    internal static ToolMaintenanceEntryV1[] Entries(ToolMaintenanceFactViewV1[] facts) => facts.Where(x => x.Fact.Kind == "START").Select(start =>
    {
        var items = facts.Where(x => x.Fact.MaintenanceId == start.Fact.MaintenanceId).ToArray();
        var completion = items.SingleOrDefault(x => x.Fact.Kind == "COMPLETE"); var reset = items.SingleOrDefault(x => x.Fact.Kind == "RESET");
        var outcome = completion?.Fact.Body.GetProperty("outcome").GetString();
        return new ToolMaintenanceEntryV1(start.Fact.MaintenanceId!.Value, completion == null ? "OPEN" : reset != null ? "COMPLETED_PASS_RELEASED"
            : outcome == "PASS" ? "COMPLETED_PASS_PENDING_RESET" : outcome!, start, completion, reset, items.Where(x => x.Fact.Kind == "REPORT").ToArray());
    }).ToArray();
    // Each native lifecycle transition must have an exact immutable Owner event. Restoring raw values does not erase drift.
    internal static async Task<(bool Coherent, ToolMaintenanceCycleV1 Cycle)> Project(CP6Context db, ToolRegistrationOriginalV1 registration, CancellationToken ct)
    {
        var store = new ToolMaintenanceStoreV1(db); await store.Fence(false, ct);
        var actual = new ToolActualUsageStoreV1(db); await actual.Fence(false, ct);
        var initial = Revision(registration.NativeAtRegistration.Version);
        var coherent = await db.Database.SqlQuery<bool>($"""
            WITH observations AS (
              SELECT revision,source_present,source_json::jsonb AS body,
                lag(revision) OVER(ORDER BY revision) AS previous_revision,
                lag(source_json::jsonb) OVER(ORDER BY revision) AS previous
              FROM cp6_tool_instance_v1.versions WHERE tenant_id={db.CurrentTenantId} AND object_id={registration.ObjectId} AND revision>={initial})
            SELECT NOT EXISTS(SELECT FROM observations v WHERE NOT source_present OR previous IS NOT NULL AND
              (body->'usedShots',body->'lastUsedAt',body->'nextMaintenanceDate',body->'status') IS DISTINCT FROM
              (previous->'usedShots',previous->'lastUsedAt',previous->'nextMaintenanceDate',previous->'status') AND NOT EXISTS(
                SELECT FROM cp6_tool_maintenance_v1.facts f WHERE f.tenant_id={db.CurrentTenantId} AND f.tool_id={registration.ObjectId}
                AND f.kind IN ('START','RESET') AND f.body_json::jsonb->'nativeBefore'->>'version'=v.previous_revision::text
                AND f.body_json::jsonb->'nativeAfter'->>'version'=v.revision::text) AND NOT EXISTS(
                SELECT FROM cp6_tool_actual_v1.facts f WHERE f.tenant_id={db.CurrentTenantId} AND f.tool_id={registration.ObjectId}
                AND f.kind='USE' AND f.body_json::jsonb->'nativeBefore'->>'version'=v.previous_revision::text
                AND f.body_json::jsonb->'nativeAfter'->>'version'=v.revision::text)) AS "Value"
            """).SingleAsync(ct);
        var lastReset = (await store.Facts(registration.ObjectId, ct)).LastOrDefault(x => x.Fact.Kind == "RESET");
        var cycle = lastReset?.Fact.CycleAfter ?? new(registration.CycleAnchorId, registration.CycleNo,
            int.Parse(registration.InitialCycleShots, CultureInfo.InvariantCulture), registration.LifetimeKnowledge, registration.LifetimeShots);
        return (coherent, ToolActualUsageStoreV1.ProjectCycle(registration, cycle, await actual.Facts(registration.ObjectId, ct)));
    }
}

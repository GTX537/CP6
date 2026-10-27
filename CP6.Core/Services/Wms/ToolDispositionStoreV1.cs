using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
using static CP6.Core.Services.Wms.ToolMaintenanceStoreV1;
namespace CP6.Core.Services.Wms;

// Native lifecycle locks precede this table for every reader/writer. No external closure is inferred here.
internal sealed class ToolDispositionStoreV1(CP6Context db)
{
    internal const string DispositionOwner = "Main.Wms.ToolDisposition.v1";
    internal static ToolDispositionRefV1 Ref(ToolDispositionFactV1 f) => new(DispositionOwner, f.RecordId, "1", Hash(JsonSerializer.Serialize(f, ToolMaintenanceStoreV1.Json)));
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_disposition_v1.facts " + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=3 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid='cp6_tool_disposition_v1.facts'::regclass
              AND tgname IN ('tool_disposition_immutable','tool_disposition_no_truncate','tool_disposition_validate')
              AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_DISPOSITION_UNAVAILABLE", 503);
    }
    private sealed class Row { public string Json { get; set; } = ""; public string Digest { get; set; } = ""; }
    internal async Task<ToolDispositionFactViewV1[]> Facts(Guid id, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Row>($"""
            SELECT body_json AS "Json", body_digest AS "Digest" FROM cp6_tool_disposition_v1.facts
            WHERE tenant_id={db.CurrentTenantId} AND tool_id={id} ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(r =>
        {
            var f = JsonSerializer.Deserialize<ToolDispositionFactV1>(r.Json, ToolMaintenanceStoreV1.Json);
            if (f == null || f.TenantId != db.CurrentTenantId || f.ToolId != id || f.Kind is not ("REQUEST" or "BLOCK")
                || f.ActorId == Guid.Empty || Hash(r.Json) != r.Digest || Ref(f).Digest != r.Digest) throw Error("TOOL_DISPOSITION_UNAVAILABLE", 503);
            return new ToolDispositionFactViewV1(Ref(f), f);
        }).ToArray();
    }
    internal async Task<ToolDispositionFactViewV1> Append(ToolDispositionFactV1 f, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(f, ToolMaintenanceStoreV1.Json); var reference = Ref(f);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_disposition_v1.facts(tenant_id,tool_id,record_id,kind,body_json,body_digest)
            VALUES({f.TenantId},{f.ToolId},{f.RecordId},{f.Kind},{json},{reference.Digest})
            """, ct);
        return new(reference, f);
    }
    internal async Task<bool> Blocked(Guid id, CancellationToken ct) => (await Facts(id, ct)).Any(x => x.Fact.Kind == "BLOCK");
    internal async Task RequireNewUseAllowed(Guid id, CancellationToken ct)
    {
        await Fence(false, ct);
        if (await Blocked(id, ct)) throw Error("TOOL_NEW_USE_BLOCKED", 409);
    }
}

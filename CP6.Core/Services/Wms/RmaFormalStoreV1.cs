using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

internal sealed record RmaFormalLineOriginalV1(Guid LineId, int LineVersion, Guid OutboundId, FulfilmentRefV1 Selection,
    OriginalFulfilmentV1 Original, FulfilmentRangeV1[] Ranges, FulfilmentQtyV1 Quantity, string? Reason, FulfilmentRefV1[] Evidence);
internal sealed record RmaFormalOriginalV1(Guid Id, int Version, Guid ActorId, RmaTrustedScopeV1 Scope, FulfilmentRefV1 Customer,
    string ReasonCode, string Outcome, DateTimeOffset RequestedAt, FulfilmentRefV1 RequestedBy, RmaFormalLineOriginalV1[] Lines,
    string? Remarks, string State, FulfilmentRefV1? Previous);
internal sealed record RmaFormalOriginalResultV1(RmaFormalApplicationV1 Application, RmaCommandBindingV1 Binding,
    FulfilmentRefV1 BindingRef, RmaApplicationWorkV1 Work, RmaApplicationReceiptV1 Receipt);
internal sealed class RmaFormalHeadV1
{
    public Guid Id { get; init; }
    public Guid Actor { get; init; }
    public int Version { get; init; }
}

internal sealed class RmaFormalStoreV1(CP6Context db, IDataProtector protector)
{
    internal const string ApplicationOwner = "Main.Wms.RmaApplicationFormal.v1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    internal static string Digest<T>(T value) => MainOwnerOperationJournalV1.Digest(Encode(value));
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    private sealed class FactRow
    {
        public Guid Actor { get; init; }
        public string Owner { get; init; } = null!;
        public string Id { get; init; } = null!;
        public string Digest { get; init; } = null!;
        public byte[] Body { get; init; } = null!;
    }
    private sealed class NamespaceRow
    {
        public Guid Environment { get; init; }
        public string Mode { get; init; } = null!;
    }
    internal async Task Fence(Guid environment, string mode, bool write, CancellationToken ct)
    {
        // Site enablement and the warehouse/site mapping are mutable scope facts at this decision.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Space_Site\",cp6_warehouse_draft_v1.warehouses IN SHARE MODE", ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_rma_formal_v1.namespace,cp6_rma_formal_v1.heads,cp6_rma_formal_v1.scope_heads"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        var bound = await db.Database.SqlQueryRaw<NamespaceRow>("SELECT environment_id AS \"Environment\",mode AS \"Mode\" FROM cp6_rma_formal_v1.namespace WHERE singleton").SingleOrDefaultAsync(ct);
        if (bound == null && write)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.namespace(singleton,environment_id,mode) VALUES(true,{environment},{mode})", ct);
        else if (bound == null || bound.Environment != environment || bound.Mode != mode) throw Error("RMA_ENVIRONMENT_MISMATCH", 503);
    }
    internal async Task<FulfilmentRefV1> Fact<T>(string owner, Guid actor, T value, bool selection, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var external = selection ? protector.Protect(id.ToString("N")) : id.ToString();
        var body = Encode(value); var digest = MainOwnerOperationJournalV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.facts(tenant_id,id,actor_id,owner,reference_id,digest,body)
            VALUES({db.CurrentTenantId},{id},{actor},{owner},{external},{digest},{body})
            """, ct);
        return new(owner, external, "1", digest);
    }
    internal async Task<T> Selection<T>(FulfilmentRefV1 reference, string owner, Guid actor, CancellationToken ct)
    {
        if (reference == null || reference.Owner != owner || reference.Version != "1" || reference.Id?.Length is not (> 0 and <= 200))
            throw Error("RMA_SELECTION_INVALID", 400);
        Guid id;
        try { id = Guid.ParseExact(protector.Unprotect(reference.Id), "N"); }
        catch (Exception e) when (e is CryptographicException or FormatException) { throw Error("RMA_SELECTION_INVALID", 400); }
        var row = await db.Database.SqlQuery<FactRow>($"""
            SELECT actor_id AS "Actor",owner AS "Owner",reference_id AS "Id",digest AS "Digest",body AS "Body"
            FROM cp6_rma_formal_v1.facts WHERE tenant_id={db.CurrentTenantId} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RMA_SELECTION_NOT_FOUND", 404);
        if (row.Actor != actor || row.Owner != owner) throw Error("RMA_SELECTION_FORBIDDEN", 403);
        if (row.Id != reference.Id || row.Digest != reference.Digest) throw Error("RMA_SELECTION_CHANGED", 412);
        return JsonSerializer.Deserialize<T>(row.Body, Json)!;
    }
    internal async Task<FulfilmentRefV1> ScopeHead(Guid warehouse, Guid actor, object coordinates, CancellationToken ct)
    {
        var digest = Digest(coordinates);
        var reference = await db.Database.SqlQuery<string>($"""
            SELECT reference::text AS "Value" FROM cp6_rma_formal_v1.scope_heads
            WHERE tenant_id={db.CurrentTenantId} AND warehouse_id={warehouse} AND coordinate_digest={digest}
            """).SingleOrDefaultAsync(ct);
        if (reference != null) return JsonSerializer.Deserialize<FulfilmentRefV1>(reference, Json)!;
        var saved = await Fact("Main.Wms.RmaScope.v1", actor, coordinates, false, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.scope_heads(tenant_id,warehouse_id,coordinate_digest,reference)
            VALUES({db.CurrentTenantId},{warehouse},{digest},CAST({JsonSerializer.Serialize(saved, Json)} AS jsonb))
            ON CONFLICT(tenant_id,warehouse_id) DO UPDATE SET coordinate_digest=EXCLUDED.coordinate_digest,reference=EXCLUDED.reference
            """, ct);
        return saved;
    }
    internal IQueryable<RmaFormalHeadV1> Heads() => db.Database.SqlQuery<RmaFormalHeadV1>($"""
        SELECT id AS "Id",actor_id AS "Actor",version AS "Version" FROM cp6_rma_formal_v1.heads WHERE tenant_id={db.CurrentTenantId}
        """);
    internal async Task<RmaFormalOriginalV1> Read(Guid id, int version, CancellationToken ct)
    {
        var body = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_rma_formal_v1.versions WHERE tenant_id={db.CurrentTenantId} AND id={id} AND version={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RMA_APPLICATION_VERSION_NOT_FOUND", 404);
        return JsonSerializer.Deserialize<RmaFormalOriginalV1>(body, Json)!;
    }
    internal static RmaFormalApplicationV1 View(RmaFormalOriginalV1 o)
    {
        var reference = new FulfilmentRefV1(ApplicationOwner, o.Id.ToString(), o.Version.ToString(CultureInfo.InvariantCulture), Digest(o));
        return new(o.Id, o.Version, reference, o.Scope, o.Customer, o.ReasonCode, o.Outcome, o.RequestedAt, o.RequestedBy,
            o.Lines.Select(x => new RmaRequestedLineSnapshotV1(reference, o.Version, x.LineId, x.LineVersion, x.Selection, x.Original,
                x.Ranges, x.Quantity, x.Reason, x.Evidence, Digest(x))).ToArray(), o.Lines.SelectMany(x => x.Evidence).Distinct().ToArray(),
            o.State, null, o.Previous);
    }
    internal async Task<RmaFormalApplicationV1> Append(RmaFormalOriginalV1 o, CancellationToken ct)
    {
        if (o.Version == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.heads(tenant_id,id,actor_id,version) VALUES({db.CurrentTenantId},{o.Id},{o.ActorId},1)", ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_rma_formal_v1.heads SET version={o.Version} WHERE tenant_id={db.CurrentTenantId} AND id={o.Id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.versions(tenant_id,id,version,body) VALUES({db.CurrentTenantId},{o.Id},{o.Version},{Encode(o)})
            """, ct);
        return View(o);
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;

namespace CP6.Core.Services.Mes;

internal sealed class ResourceCapabilityStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await new ResourceSourceStoreV1(db).Fence(false, ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_resource_capability_v1.heads,cp6_resource_capability_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_resource_capability_v1.ready() AS \"Value\"").SingleAsync(ct)) throw Error("RESOURCE_CAPABILITY_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }
    private void Transaction()
    { if (fenced == null || !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503); }
    internal Task<long?> Head(string kind, Guid id, CancellationToken ct)
    {
        Transaction(); return db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM cp6_resource_capability_v1.heads
            WHERE tenant_id={db.CurrentTenantId} AND kind={kind} AND resource_id={id}
            """).SingleOrDefaultAsync(ct);
    }
    private sealed class Row
    {
        public long Revision { get; init; }
        public long Current { get; init; }
        public Guid Creator { get; init; }
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }
    internal async Task<ResourceCapabilitySnapshotV1> Read(string kind, Guid id, long? version, CancellationToken ct)
    {
        Transaction(); var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.revision AS "Revision",h.revision AS "Current",h.creator_id AS "Creator",v.body AS "Body",v.digest AS "Digest"
            FROM cp6_resource_capability_v1.heads h JOIN cp6_resource_capability_v1.versions v USING(tenant_id,kind,resource_id)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.kind={kind} AND h.resource_id={id} AND v.revision=coalesce({version},h.revision)
            """).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_CAPABILITY_NOT_FOUND", 404);
        ResourceCapabilityStoredV1? value;
        try { value = JsonSerializer.Deserialize<ResourceCapabilityStoredV1>(row.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("RESOURCE_CAPABILITY_ORIGINAL_UNAVAILABLE", 503); }
        if (value == null || value.TenantId != db.CurrentTenantId || value.Id != id || value.Kind != kind || value.CreatorId != row.Creator
            || value.Version != row.Revision.ToString(CultureInfo.InvariantCulture) || value.Body.Source.Id != id || value.Body.Source.Kind != kind
            || EngineeringProductStoreV1.Digest(row.Body) != row.Digest) throw Error("RESOURCE_CAPABILITY_ORIGINAL_UNAVAILABLE", 503);
        return new(db.CurrentTenantId, new(ResourceCapabilityValuesV1.Owner, kind, id, value.Version, row.Digest), row.Revision == row.Current,
            value.Body, value.ActorId, value.RecordedAt, Encoding.UTF8.GetString(row.Body), []);
    }
    internal async Task<ResourceCapabilitySnapshotV1> Append(string kind, ResourceCapabilityCommandV1 input, Guid actor,
        ResourceSourceVersionV1 resource, ResourceCapabilityOriginalV1[] units, string? timeBasis, CancellationToken ct)
    {
        Transaction(); var id = input.Body.Source.Id; var current = await Head(kind, id, ct);
        var expected = input.ExpectedVersion == "absent" ? (long?)null : Revision(input.ExpectedVersion);
        if (current != expected) throw Error("RESOURCE_CAPABILITY_CHANGED", 412);
        ResourceCapabilitySnapshotV1? previous = current == null ? null : await Read(kind, id, current, ct);
        if (previous != null && EngineeringProductStoreV1.Encode(previous.Body).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(input.Body))) return previous;
        if (current == long.MaxValue) throw Error("RESOURCE_CAPABILITY_VERSION_EXHAUSTED", 503);
        var next = (current ?? 0) + 1;
        var creator = previous == null ? actor : JsonSerializer.Deserialize<ResourceCapabilityStoredV1>(previous.OriginalJson, EngineeringProductStoreV1.Json)!.CreatorId;
        if (current == null) await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_resource_capability_v1.heads(tenant_id,kind,resource_id,revision,creator_id) VALUES({db.CurrentTenantId},{kind},{id},1,{creator})
            """, ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_resource_capability_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND kind={kind} AND resource_id={id}
            """, ct);
        var body = EngineeringProductStoreV1.Encode(new ResourceCapabilityStoredV1(db.CurrentTenantId, id, kind, next.ToString(CultureInfo.InvariantCulture),
            creator, actor, DateTimeOffset.UtcNow, input.Body, input.Reason, resource.OriginalJson, units, timeBasis));
        if (body.Length > 1048576) throw Error("RESOURCE_CAPABILITY_SIZE_LIMIT", 413);
        var digest = EngineeringProductStoreV1.Digest(body); var sourceVersion = Revision(input.Body.Source.Version);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_resource_capability_v1.versions(tenant_id,kind,resource_id,revision,source_revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{kind},{id},{next},{sourceVersion},{input.OperationId},{actor},{body},{digest})
            """, ct);
        return await Read(kind, id, next, ct);
    }
}

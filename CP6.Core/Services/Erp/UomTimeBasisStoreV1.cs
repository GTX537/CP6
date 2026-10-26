using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Erp.UomProfileValuesV1;
namespace CP6.Core.Services.Erp;

internal sealed class UomTimeBasisStoreV1(CP6Context db)
{
    internal const string BasisOwner = "Main.Erp.UomTimeBasis.v1";
    private IDbContextTransaction? fenced;
    internal CP6Context Context => db;
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_uom_time_v1.heads,cp6_uom_time_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        fenced = db.Database.CurrentTransaction;
    }
    private void Transaction()
    { if (fenced == null || !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503); }
    internal Task<long?> Head(Guid id, CancellationToken ct)
    {
        Transaction(); return db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM cp6_uom_time_v1.heads WHERE tenant_id={db.CurrentTenantId} AND profile_id={id}
            """).SingleOrDefaultAsync(ct);
    }
    private sealed class Row
    {
        public long Revision { get; init; }
        public long Head { get; init; }
        public long BaseVersion { get; init; }
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }
    internal async Task<UomTimeBasisVersionV1> Read(Guid id, long? version, CancellationToken ct)
    {
        Transaction(); var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.revision AS "Revision",h.revision AS "Head",v.base_version AS "BaseVersion",v.body_bytes AS "Body",v.body_digest AS "Digest"
            FROM cp6_uom_time_v1.heads h JOIN cp6_uom_time_v1.versions v ON v.tenant_id=h.tenant_id AND v.profile_id=h.profile_id
              AND v.revision=COALESCE({version},h.revision) WHERE h.tenant_id={db.CurrentTenantId} AND h.profile_id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TIME_BASIS_REQUIRED");
        if (UomProfileStoreV1.Digest(row.Body) != row.Digest) throw Error("TIME_BASIS_ORIGINAL_UNAVAILABLE", 503);
        try
        {
            var value = JsonSerializer.Deserialize<UomTimeBasisStoredV1>(row.Body, UomProfileStoreV1.Json);
            if (value == null || value.TenantId != db.CurrentTenantId || value.ProfileId != id || value.Version != row.Revision.ToString(CultureInfo.InvariantCulture)
                || value.Definition.BaseRef.Id != id || Revision(value.Definition.BaseRef.Version) != row.BaseVersion)
                throw Error("TIME_BASIS_ORIGINAL_UNAVAILABLE", 503);
            UomTimeBasisOwnerV1.Validate(value.Definition);
            return new(db.CurrentTenantId, new(BasisOwner, id, value.Version, row.Digest), row.Revision == row.Head, value.Definition, Encoding.UTF8.GetString(row.Body));
        }
        catch (JsonException e) { throw new OrderPriceCorrectionException("TIME_BASIS_ORIGINAL_UNAVAILABLE", 503, e); }
    }
    internal async Task<UomTimeBasisVersionV1> Append(UomTimeBasisDefinitionV1 definition, long? expected, Guid actor, CancellationToken ct)
    {
        Transaction(); var id = definition.BaseRef.Id; var current = await Head(id, ct);
        if (current != expected) throw Error("TIME_BASIS_CHANGED", 412);
        if (current != null)
        { var prior = await Read(id, current, ct); if (prior.Definition == definition) return prior; }
        if (current == long.MaxValue) throw Error("UOM_VERSION_EXHAUSTED", 503);
        var next = (current ?? 0) + 1;
        if (current == null) await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_uom_time_v1.heads VALUES({db.CurrentTenantId},{id},1)
            """, ct);
        else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_uom_time_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND profile_id={id} AND revision={current.Value}
            """, ct) != 1) throw Error("TIME_BASIS_CHANGED", 412);
        var body = UomProfileStoreV1.Encode(new UomTimeBasisStoredV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture), definition));
        var digest = UomProfileStoreV1.Digest(body); var baseVersion = Revision(definition.BaseRef.Version);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_uom_time_v1.versions(tenant_id,profile_id,revision,base_version,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{next},{baseVersion},{body},{digest},{actor})
            """, ct);
        return await Read(id, next, ct);
    }
}

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.UomProfileValuesV1;

namespace CP6.Core.Services.Erp;

internal sealed partial class UomProfileStoreV1
{
    private sealed class ProductVersionRow
    {
        public long Revision { get; init; }
        public long Head { get; init; }
        public long ProductVersion { get; init; }
        public Guid Profile { get; init; }
        public long ProfileVersion { get; init; }
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }
    internal Task<long?> ProductHead(Guid product, CancellationToken ct)
    {
        Transaction();
        return db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM cp6_uom_profile_v1.product_heads
            WHERE tenant_id={db.CurrentTenantId} AND product_id={product}
            """).SingleOrDefaultAsync(ct);
    }
    internal async Task<(CommercialProductUnitStoredV1 Value, string Digest, bool Latest, string OriginalJson)> ReadProduct(Guid product, long? revision, CancellationToken ct)
    {
        Transaction();
        var row = await db.Database.SqlQuery<ProductVersionRow>($"""
            SELECT v.revision AS "Revision",h.revision AS "Head",v.product_identity_version AS "ProductVersion",
              v.profile_id AS "Profile",v.profile_version AS "ProfileVersion",v.body_bytes AS "Body",v.body_digest AS "Digest"
            FROM cp6_uom_profile_v1.product_heads h JOIN cp6_uom_profile_v1.product_versions v
              ON v.tenant_id=h.tenant_id AND v.product_id=h.product_id AND v.revision=COALESCE({revision},h.revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.product_id={product}
            """).SingleOrDefaultAsync(ct) ?? throw Error("PRODUCT_UNIT_BINDING_REQUIRED");
        if (Digest(row.Body) != row.Digest) throw Error("PRODUCT_UNIT_ORIGINAL_UNAVAILABLE", 503);
        try
        {
            var original = JsonSerializer.Deserialize<CommercialProductUnitStoredV1>(row.Body, Json);
            if (original == null || original.TenantId != db.CurrentTenantId || original.ProductId != product
                || original.Version != row.Revision.ToString(CultureInfo.InvariantCulture)
                || original.ProductVersion != row.ProductVersion.ToString(CultureInfo.InvariantCulture)
                || original.ProfileRef.Id != row.Profile || original.ProfileRef.Version != row.ProfileVersion.ToString(CultureInfo.InvariantCulture))
                throw Error("PRODUCT_UNIT_ORIGINAL_UNAVAILABLE", 503);
            Reference(original.ProfileRef);
            return (original, row.Digest, row.Revision == row.Head, System.Text.Encoding.UTF8.GetString(row.Body));
        }
        catch (JsonException error) { throw new OrderPriceCorrectionException("PRODUCT_UNIT_ORIGINAL_UNAVAILABLE", 503, error); }
    }
    internal async Task<(CommercialProductUnitStoredV1 Value, string Digest)> AppendProduct(CommercialProductIdentityV1 product,
        UomProfileVersionV1 profile, long? expected, Guid actor, CancellationToken ct)
    {
        Transaction();
        var current = await db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM cp6_uom_profile_v1.product_heads
            WHERE tenant_id={db.CurrentTenantId} AND product_id={product.ProductId}
            """).SingleOrDefaultAsync(ct);
        if (current != expected) throw Error("PRODUCT_UNIT_BINDING_CHANGED", 412);
        if (current == long.MaxValue) throw Error("UOM_VERSION_EXHAUSTED", 503);
        var next = (current ?? 0) + 1;
        if (current == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_uom_profile_v1.product_heads(tenant_id,product_id,revision) VALUES({db.CurrentTenantId},{product.ProductId},1)
                """, ct);
        else
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_uom_profile_v1.product_heads SET revision={next}
                WHERE tenant_id={db.CurrentTenantId} AND product_id={product.ProductId} AND revision={current.Value}
                """, ct) != 1) throw Error("PRODUCT_UNIT_BINDING_CHANGED", 412);
        var original = new CommercialProductUnitStoredV1(db.CurrentTenantId, product.ProductId, next.ToString(CultureInfo.InvariantCulture),
            product.Version, product.Digest, profile.Profile.Code, profile.Reference);
        var bytes = Encode(original); var digest = Digest(bytes);
        var productVersion = Revision(product.Version); var profileVersion = Revision(profile.Reference.Version);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_uom_profile_v1.product_versions(tenant_id,product_id,revision,product_identity_version,
              profile_id,profile_version,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{product.ProductId},{next},{productVersion},{profile.Reference.Id},{profileVersion},{bytes},{digest},{actor})
            """, ct);
        return (original, digest);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed record CommercialProductIdentityV1(Guid TenantId, Guid ProductId, string Version, string Digest,
    bool IsCurrent, bool SourcePresent, JsonElement Source, string OriginalJson,
    string FormalAdoption = "REQUIRED_FENCED",
    string[]? Missing = null);

/// <summary>Exact native product-head source. Does not attest UOM, engineering mapping or manufacturing eligibility.</summary>
public sealed class CommercialProductIdentityReader(CP6Context db, OperationsOwnerV1 owner)
{
    public Task<CommercialProductIdentityV1> ReadAsync(Guid productId, string? version = null, CancellationToken ct = default)
    {
        if (!owner.Owns(db)) throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        long? exact = null;
        if (productId == Guid.Empty || version != null && (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1 || parsed.ToString(CultureInfo.InvariantCulture) != version))
            throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_INPUT_INVALID", 400);
        if (version != null) exact = long.Parse(version, CultureInfo.InvariantCulture);
        return owner.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (a, token) =>
        {
            // Current object authority is required even for an old version. A hard-deleted object is not disclosed.
            if (!await a.Products(includeDeleted: true).AnyAsync(p => p.Id == productId, token))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            if (!await db.Database.SqlQueryRaw<bool>("""
                SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger
                WHERE tgrelid='public."T_ProductMaster"'::regclass
                  AND tgname IN ('product_identity_capture','product_identity_no_truncate')
                  AND tgenabled IN ('O','A') AND NOT tgisinternal
                """).SingleAsync(token)) throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_SOURCE_UNAVAILABLE", 503);
            var current = await db.Database.SqlQuery<SourceRow>($"""
                SELECT v.revision AS "Revision",v.source_present AS "Present",v.source_json AS "Json",v.source_digest AS "Digest"
                FROM cp6_product_identity_v1.heads h JOIN cp6_product_identity_v1.versions v
                  USING(tenant_id,product_id,revision)
                JOIN public."T_ProductMaster" p ON p."TenantId"=h.tenant_id AND p."Id"=h.product_id
                WHERE h.tenant_id={db.CurrentTenantId} AND h.product_id={productId}
                  AND v.source_present AND v.source_json=cp6_product_identity_v1.payload(to_jsonb(p))
                """).SingleOrDefaultAsync(token) ?? throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_SOURCE_UNAVAILABLE", 503);
            var selected = exact == null || exact == current.Revision ? current : await db.Database.SqlQuery<SourceRow>($"""
                SELECT revision AS "Revision",source_present AS "Present",source_json AS "Json",source_digest AS "Digest"
                FROM cp6_product_identity_v1.versions WHERE tenant_id={db.CurrentTenantId}
                  AND product_id={productId} AND revision={exact.Value}
                """).SingleOrDefaultAsync(token) ?? throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_VERSION_NOT_FOUND", 404);
            if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected.Json))).ToLowerInvariant() != selected.Digest)
                throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_SOURCE_UNAVAILABLE", 503);
            using var doc = JsonDocument.Parse(selected.Json);
            return new CommercialProductIdentityV1(db.CurrentTenantId, productId, selected.Revision.ToString(CultureInfo.InvariantCulture), selected.Digest,
                selected.Revision == current.Revision, selected.Present, doc.RootElement.Clone(), selected.Json,
                Missing: ["VERSIONED_UOM_PROFILE", "CURRENCY_IDENTITY", "DUAL_OWNER_ENGINEERING_MAPPING"]);
        }, ct);
    }

    private sealed class SourceRow
    {
        public long Revision { get; set; }
        public bool Present { get; set; }
        public string Json { get; set; } = "";
        public string Digest { get; set; } = "";
    }
}

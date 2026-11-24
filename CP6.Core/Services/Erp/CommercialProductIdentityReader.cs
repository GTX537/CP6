using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

/// <summary>按原主账号段标识部材。同名或同一套件的部材仍有独立的产品 UUID。</summary>
public sealed record CommercialMaterialKeyV1(string ItemCode, string Branch1, string Branch2, string Branch3);

public sealed record CommercialProductIdentityV1(Guid TenantId, Guid ProductId, string Version, string Digest,
    bool IsCurrent, bool SourcePresent, JsonElement Source, string OriginalJson,
    string FormalAdoption = "REQUIRED_FENCED",
    string[]? Missing = null)
{
    public EngineeringSourceRefV1 Reference => new(EngineeringProductValuesV1.BusinessOwner, ProductId, Version, Digest);
    public string ProductCode => Source.GetProperty("productCd").GetString()!;
    public CommercialMaterialKeyV1 MaterialKey => new(Source.GetProperty("itemCd").GetString()!,
        Source.GetProperty("branch1").GetString()!, Source.GetProperty("branch2").GetString()!,
        Source.GetProperty("branch3").GetString()!);
    public string? DeclaredUnitCode => Source.GetProperty("qtyUnitCode").GetString();
    public int LegacyStatus => Source.GetProperty("status").GetInt32();

    public string EffectiveState => !SourcePresent || Source.GetProperty("isDeleted").GetBoolean() ? "INACTIVE" : LegacyStatus switch
    {
        0 => "PENDING_APPROVAL",
        1 => "APPROVED",
        9 => "TRANSFERRED",
        _ => "UNPROVEN"
    };
}

/// <summary>从原产品头和现有 Owner 精确读取版本。旧版仍按当前读取资格披露。</summary>
public sealed class CommercialProductIdentityReader(CP6Context db, OperationsOwnerV1 owner)
{
    public Task<CommercialProductIdentityV1> ReadAsync(Guid productId, string? version = null, CancellationToken ct = default)
    {
        if (!owner.Owns(db)) throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (productId == Guid.Empty) throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_INPUT_INVALID", 400);
        var exact = version == null ? (long?)null : ParseVersion(version);
        return owner.ExecuteAsync(OperationsOwnerV1.Product, "query", false,
            (access, token) => ReadOwnedAsync(db, access, productId, exact, token), ct);
    }

    public Task<CommercialProductQualificationV1> ReadQualificationAsync(CommercialProductQualificationQueryV1 input,
        CancellationToken ct = default)
    {
        if (!owner.Owns(db)) throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (input == null || input.ProductRef is not { } reference || reference.Owner != EngineeringProductValuesV1.BusinessOwner
            || reference.Id == Guid.Empty || !EngineeringProductValuesV1.ValidDigest(reference.Digest))
            throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_INPUT_INVALID", 400);
        var version = ParseVersion(reference.Version);
        var unitVersion = input.UnitBindingVersion == null ? (long?)null : UomProfileValuesV1.Revision(input.UnitBindingVersion);

        return owner.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (access, token) =>
        {
            var product = await ReadOwnedAsync(db, access, reference.Id, version, token);
            if (product.Reference != reference)
                throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_VERSION_CHANGED", 412);

            CommercialProductUnitVersionV1? unit = null;
            if (unitVersion is not null)
            {
                unit = await CommercialProductUnitOwnerV1.ReadOwnedAsync(db, access, reference.Id, unitVersion, token);
                if (unit.ProductVersion != reference.Version || unit.ProductDigest != reference.Digest)
                    throw new OrderPriceCorrectionException("PRODUCT_UNIT_SOURCE_CHANGED", 412);
            }

            CurrencyPolicyVersionV1? currency = null;
            if (input.CurrencyPolicyRef is { } currencyRef)
            {
                CurrencyPolicyValuesV1.Reference(currencyRef);
                var currencyAccess = await access.SourceAsync(OperationsOwnerV1.CurrencyPolicy, token);
                currency = await CurrencyPolicyOwnerV1.ReadOwnedAsync(db, currencyAccess, currencyRef.Id,
                    CurrencyPolicyValuesV1.Revision(currencyRef.Version), token);
                if (currency.Reference != currencyRef)
                    throw new OrderPriceCorrectionException("CURRENCY_POLICY_CHANGED", 412);
            }

            var mapping = input.MappingRef is { } mappingRef
                ? await EngineeringProductMappingService.ReadProductMappingAsync(db, access, mappingRef, reference, token)
                : null;

            var missing = new List<string> { "PRODUCT_CURRENCY_BINDING", "LEGAL_ENTITY_APPLICABILITY", "MANUFACTURING_APPLICABILITY" };
            if (unit == null) missing.Add("VERSIONED_UOM_PROFILE");
            if (mapping?.ConfirmedIdentitySite == null) missing.Add("DUAL_OWNER_ENGINEERING_MAPPING");
            return new CommercialProductQualificationV1(product, unit, currency, mapping, missing.ToArray());
        }, ct);
    }

    private static long ParseVersion(string? version)
    {
        if (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            || result < 1 || result.ToString(CultureInfo.InvariantCulture) != version)
            throw new OrderPriceCorrectionException("PRODUCT_IDENTITY_INPUT_INVALID", 400);
        return result;
    }

    // The original reader is shared with the actual product-unit writer, on its exact Main transaction.
    // The caller supplies the real current Product Owner access; no nested transaction or fabricated permit.
    internal static async Task<CommercialProductIdentityV1> ReadOwnedAsync(CP6Context db, OperationsAccessV1 a,
        Guid productId, long? exact, CancellationToken token)
    {
            a.RequireProductSourceAccess(db);
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
    }

    private sealed class SourceRow
    {
        public long Revision { get; set; }
        public bool Present { get; set; }
        public string Json { get; set; } = "";
        public string Digest { get; set; } = "";
    }
}

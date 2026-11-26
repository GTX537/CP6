using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OperationalUnitConversionQueryV1(UomProfileRefV1 From, UomProfileRefV1 To,
    string? Quantity = null, bool RequireCurrent = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OperationalCapacityMetricQueryV1(string MetricId, EngineeringSourceRefV1? Policy = null,
    bool RequireCurrent = true);

public sealed record OperationalCatalogueResolutionV1<T>(T? Evidence) where T : class
{
    public string State => Evidence is null ? "UNKNOWN" : "KNOWN";
}

public sealed record OperationalUnitConversionEvidenceV1(UomProfileVersionV1 From, UomProfileVersionV1 To,
    UomProfileVersionV1 Base, bool IsCurrent, string? Quantity, string? ConvertedQuantity);

public sealed record OperationalCapacityMetricEvidenceV1(Guid WarehouseId, EngineeringSourceRefV1 Policy,
    RuleMetricV1 Metric, UomProfileVersionV1[] Units, UomProfileVersionV1 Base, RulePrecisionV1[] Precisions,
    RuleDomainV1[] Domains, bool IsCurrent)
{
    public bool AdmissionGrant => false;
}

/// <summary>在同一 Main Owner 查询事务内读取原单位定义和仓库批准的容量指标。</summary>
public sealed class OperationalCatalogueEvidenceAdapter(CP6Context db, OperationsOwnerV1 owner, string? configuredSource)
{
    private bool NativeSourceConfigured => string.Equals(configuredSource, "MAIN_NATIVE_V1", StringComparison.Ordinal);
    private OperationsOwnerV1 Authority => owner.Owns(db)
        ? owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    public Task<OperationalCatalogueResolutionV1<OperationalUnitConversionEvidenceV1>> ReadConversionAsync(
        OperationalUnitConversionQueryV1 input, CancellationToken ct = default)
    {
        if (input?.From is null || input.To is null)
            throw new OrderPriceCorrectionException("UOM_CONVERSION_INVALID", 400);
        ValidateReference(input.From.Owner, input.From.Id, input.From.Version, input.From.Digest);
        ValidateReference(input.To.Owner, input.To.Id, input.To.Version, input.To.Digest);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (access, token) =>
        {
            var unknown = new OperationalCatalogueResolutionV1<OperationalUnitConversionEvidenceV1>(null);
            if (!NativeSourceConfigured || input.From.Owner != UomProfileValuesV1.Owner || input.To.Owner != UomProfileValuesV1.Owner)
                return unknown;
            var store = await OpenUnits(token);
            if (store is null) return unknown;
            var from = await ReadUnit(access, store, input.From, token);
            var to = input.To == input.From ? from : await ReadUnit(access, store, input.To, token);
            if (from is null || to is null || from.Profile.DimensionKey != to.Profile.DimensionKey || BaseRef(from) != BaseRef(to))
                return unknown;

            var reference = BaseRef(from);
            var basis = reference == from.Reference ? from : reference == to.Reference ? to
                : await ReadUnit(access, store, reference, token);
            if (basis is null || !IsBase(basis, from.Profile.DimensionKey)) return unknown;
            var isCurrent = from.IsCurrent && to.IsCurrent && basis.IsCurrent;
            if (input.RequireCurrent && !isCurrent) return unknown;

            var converted = input.Quantity is null ? null : UomProfileValuesV1.ConvertExact(input.Quantity, from.Profile, to.Profile);
            return new(new(from, to, basis, isCurrent, input.Quantity, converted));
        }, ct);
    }

    public Task<OperationalCatalogueResolutionV1<OperationalCapacityMetricEvidenceV1>> ReadCapacityMetricAsync(
        Guid warehouse, OperationalCapacityMetricQueryV1 input, CancellationToken ct = default)
    {
        if (warehouse == Guid.Empty || !LocationRuleValuesV1.Text(input?.MetricId, 100))
            throw new OrderPriceCorrectionException("RULE_DICTIONARY_KEY_INVALID", 400);
        if (input!.Policy is { } reference)
            ValidateReference(reference.Owner, reference.Id, reference.Version, reference.Digest);
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse, "query", false, async (access, token) =>
        {
            var unknown = new OperationalCatalogueResolutionV1<OperationalCapacityMetricEvidenceV1>(null);
            await access.RequireMaster("warehouses", warehouse, token);
            access.AtCommit(last => access.RequireMaster("warehouses", warehouse, last));
            if (!NativeSourceConfigured || input.Policy is { Owner: not LocationRuleReferencesValuesV1.Owner })
                return unknown;

            // 固定锁序与审批字典一致，先仓库原件，后 ERP 单位原件。
            var policies = await OpenPolicies(token);
            if (policies is null) return unknown;
            var current = await ReadPolicy(policies, warehouse, null, token);
            var original = current;
            if (input.Policy is { } exact && exact != current?.Reference)
            {
                if (input.RequireCurrent) return unknown;
                original = await ReadPolicy(policies, warehouse, EngineeringProductValuesV1.Revision(exact.Version), token);
                if (original?.Reference != exact) return unknown;
            }
            if (original is null || !original.Original.Definition.Enabled) return unknown;
            var definition = original.Original.Definition;
            var metric = definition.Metrics.SingleOrDefault(candidate => candidate.Id == input.MetricId);
            if (metric is null) return unknown;

            var units = await OpenUnits(token);
            if (units is null) return unknown;
            var unitAccess = await access.SourceAsync(OperationsOwnerV1.UomProfile, token);
            var bindings = original.Original.Units.Where(unit => unit is not null && metric.Units.Contains(unit.Reference)).ToArray();
            if (bindings.Length != metric.Units.Length || bindings.Select(unit => unit.Reference).Distinct().Count() != metric.Units.Length)
                return unknown;
            var baseReference = bindings[0].BaseReference;
            if (bindings.Any(unit => unit.BaseReference != baseReference || unit.DimensionKey != metric.DimensionKey))
                return unknown;

            var profiles = new List<UomProfileVersionV1>(bindings.Length);
            foreach (var binding in bindings)
            {
                var profile = await ReadUnit(unitAccess, units, binding.Reference, token);
                if (profile is null || profile.Profile.Code != binding.Code || profile.Profile.DimensionKey != binding.DimensionKey
                    || BaseRef(profile) != binding.BaseReference)
                    return unknown;
                profiles.Add(profile);
            }
            var basis = profiles.SingleOrDefault(profile => profile.Reference == baseReference)
                ?? await ReadUnit(unitAccess, units, baseReference, token);
            if (basis is null || !IsBase(basis, metric.DimensionKey)) return unknown;

            var isCurrent = original.Reference == current?.Reference && basis.IsCurrent && profiles.All(profile => profile.IsCurrent);
            if ((input.RequireCurrent || input.Policy is null) && !isCurrent) return unknown;
            var domains = definition.Domains.Where(domain => domain.MetricIds.Contains(metric.Id, StringComparer.Ordinal)).ToArray();
            var precisionIds = domains.SelectMany(domain => domain.PrecisionIds).ToHashSet(StringComparer.Ordinal);
            var precisions = definition.Precisions.Where(precision => precisionIds.Contains(precision.Id)).ToArray();
            return new(new(warehouse, original.Reference, metric, profiles.ToArray(), basis, precisions, domains, isCurrent));
        }, ct);
    }

    private static void ValidateReference(string referencedOwner, Guid id, string version, string digest)
    {
        if (id == Guid.Empty || !EngineeringProductValuesV1.ValidText(referencedOwner, 128)
            || !EngineeringProductValuesV1.ValidText(version, 64) || !EngineeringProductValuesV1.ValidDigest(digest))
            throw new OrderPriceCorrectionException("CATALOGUE_REFERENCE_INVALID", 400);
    }

    private static UomProfileRefV1 BaseRef(UomProfileVersionV1 unit) => unit.Profile.BaseRef ?? unit.Reference;

    private static bool IsBase(UomProfileVersionV1 unit, string dimension) =>
        unit.Profile.Mode == "BASE" && unit.Profile.DimensionKey == dimension;

    private async Task<UomProfileStoreV1?> OpenUnits(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT (pg_catalog.to_regclass('cp6_uom_profile_v1.heads') IS NOT NULL
                AND pg_catalog.to_regclass('cp6_uom_profile_v1.versions') IS NOT NULL
                AND pg_catalog.to_regclass('cp6_uom_profile_v1.product_heads') IS NOT NULL
                AND pg_catalog.to_regclass('cp6_uom_profile_v1.product_versions') IS NOT NULL) AS "Value"
            """).SingleAsync(ct)) return null;
        var store = new UomProfileStoreV1(db);
        await store.Fence(false, ct);
        return store;
    }

    private static async Task<UomProfileVersionV1?> ReadUnit(OperationsAccessV1 access, UomProfileStoreV1 store,
        UomProfileRefV1 reference, CancellationToken ct)
    {
        access.RequireUomSourceAccess(store.Context);
        UomProfileValuesV1.Reference(reference);
        await access.RequireUomProfile(reference.Id, ct);
        access.AtCommit(last => access.RequireUomProfile(reference.Id, last));
        try
        {
            var original = await store.Read(reference.Id, UomProfileValuesV1.Revision(reference.Version), ct);
            return original.Reference == reference && original.Profile.Enabled == true ? original : null;
        }
        catch (OrderPriceCorrectionException error) when (error.Message is "UOM_VERSION_NOT_FOUND" or "UOM_ORIGINAL_UNAVAILABLE")
        {
            return null;
        }
    }

    private async Task<LocationRuleReferencesStoreV1?> OpenPolicies(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT (pg_catalog.to_regclass('cp6_location_rule_refs_v1.heads') IS NOT NULL
                AND pg_catalog.to_regclass('cp6_location_rule_refs_v1.versions') IS NOT NULL
                AND pg_catalog.to_regprocedure('cp6_location_rule_refs_v1.ready()') IS NOT NULL) AS "Value"
            """).SingleAsync(ct)) return null;
        var store = new LocationRuleReferencesStoreV1(db);
        try { await store.Fence(false, ct); }
        catch (OrderPriceCorrectionException error) when (error.Message == "RULE_DICTIONARY_STORE_UNAVAILABLE") { return null; }
        return store;
    }

    private static async Task<RuleReferencePolicyV1?> ReadPolicy(LocationRuleReferencesStoreV1 store,
        Guid warehouse, long? version, CancellationToken ct)
    {
        try { return await store.Read(warehouse, version, ct); }
        catch (JsonException) { return null; }
        catch (OrderPriceCorrectionException error) when (error.Status == 400 || error.Message == "RULE_DICTIONARY_ORIGINAL_UNAVAILABLE")
        {
            return null;
        }
    }
}

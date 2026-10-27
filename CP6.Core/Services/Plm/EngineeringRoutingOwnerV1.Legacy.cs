using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRoutingOwnerV1
{
    private async Task LegacyDisclosure(OperationsAccessV1 root, EngineeringRoutingLegacyV1 legacy, CancellationToken ct)
    {
        var a = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        var mapping = await store.ReadMapping(legacy.MappingRef.Id, Revision(legacy.MappingRef.Version), ct);
        if (mapping.Reference != legacy.MappingRef || mapping.Candidate.BusinessRef != legacy.ProductRef) throw Error("ROUTING_LEGACY_ORIGINAL_UNAVAILABLE", 503);
        await a.RequireEngineeringProduct(mapping.Candidate.EngineeringRef.Id, ct);
        a.AtCommit(t => a.RequireEngineeringProduct(mapping.Candidate.EngineeringRef.Id, t));
        var products = await root.SourceAsync(OperationsOwnerV1.Product, ct);
        async Task Check(CancellationToken token)
        { if (!await products.Products(includeDeleted: true).AnyAsync(x => x.Id == legacy.ProductRef.Id, token)) throw Error("OBJECT_FORBIDDEN", 403); }
        await Check(ct); products.AtCommit(Check);
        if (mapping.Candidate.SiteRef != null) (await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct)).RequirePhysicalSiteSourceAccess(db);
    }
    public Task<EngineeringRoutingLegacyV1> LegacyPreviewAsync(EngineeringMappingRefV1 reference, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, "query", false,
            (a, token) => Legacy(a, reference, null, null, token), ct);

    private sealed class LegacyRow { public string Json { get; init; } = ""; }
    private async Task<EngineeringRoutingLegacyV1> Legacy(OperationsAccessV1 root, EngineeringMappingRefV1 reference,
        EngineeringProductRefV1? product, EngineeringSourceRefV1? site, CancellationToken ct)
    {
        var a = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        var mapping = await EngineeringProductMappingService.CurrentExact(a, store, reference, ct);
        if (product != null && mapping.Candidate.EngineeringRef != product || site != null && mapping.Candidate.SiteRef != site)
            throw Error("ROUTING_LEGACY_MAPPING_MISMATCH", 422);
        var business = mapping.Candidate.BusinessRef!; var products = await root.SourceAsync(OperationsOwnerV1.Product, ct);
        var master = await products.Products().Where(x => x.Id == business.Id).Select(x => new { x.ProductCd }).SingleOrDefaultAsync(ct)
            ?? throw Error("OBJECT_FORBIDDEN", 403);
        products.AtCommit(async token => { if (!await products.Products().AnyAsync(x => x.Id == business.Id, token)) throw Error("OBJECT_FORBIDDEN", 403); });
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_ProductProcess\" IN SHARE MODE", ct);
        var rows = await db.Database.SqlQuery<LegacyRow>($"""
            SELECT to_jsonb(p)::text AS "Json" FROM public."T_ProductProcess" p
            WHERE p."TenantId"={db.CurrentTenantId} AND p."ProductCd"={master.ProductCd} AND NOT p."IsDeleted"
            ORDER BY p."SortOrder",p."TaskCd",p."Id" LIMIT 1001
            """).ToArrayAsync(ct);
        if (rows.Length > 1000) throw Error("ROUTING_LEGACY_SIZE_LIMIT", 422);
        var originals = rows.Select(x => JsonDocument.Parse(x.Json)).ToArray();
        try
        {
            var elements = originals.Select(x => x.RootElement.Clone()).ToArray();
            // Digest includes exact mapping/product identities and the actual ordered database originals, never caller-supplied rows.
            var digest = EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(new { mapping = reference, product = business, rows = elements }));
            var suggested = elements.Select((row, index) =>
            {
                string Text(string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                var parameters = new List<RoutingParameterV1>();
                foreach (var name in new[] { "SetupHour", "CycleTime", "StandardCrewSize", "LossRate", "MachineCount", "LeadTime" })
                    if (row.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number)
                        parameters.Add(new(name, number.GetRawText(), null, null, null, ""));
                var id = row.GetProperty("Id").GetGuid();
                return new RoutingOperationV1(id, Text("ProcessCd"), Text("TaskCd"), index,
                    [], null, "UNKNOWN", "Imported definition requires applicability review", string.Join("\n", new[] { Text("ProcessNote1"), Text("ProcessNote2") }.Where(x => x.Length != 0)),
                    [], [], parameters.ToArray(), "", id);
            }).ToArray();
            return new(reference, business, digest, elements, DateTimeOffset.UtcNow, suggested);
        }
        finally { foreach (var original in originals) original.Dispose(); }
    }
}

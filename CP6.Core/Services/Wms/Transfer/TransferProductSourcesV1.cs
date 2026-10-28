using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Wms.Transfer;

public sealed class TransferProductSourcesV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope,
    IOptions<TransferEnvironmentOptionsV1> options, QualitySpecificationRuntimeV1 runtime, InventoryIdentityOwnerV1 identities)
{
    public Task<TransferProductSourceV1> ReadAsync(InventoryIdentityRefV1 reference, CancellationToken ct = default)
    {
        InventoryIdentityValuesV1.Reference(reference);
        if (!owner.Owns(db) || !identities.Owns(db)) throw TransferEnvironmentStoreV1.Error("AUTHORITY_FENCE_UNAVAILABLE");
        return owner.ExecuteAsync(OperationsOwnerV1.TransferSource, "query", false, async (a, t) =>
        {
            var s = new TransferEnvironmentStoreV1(db, scope, options, runtime); var registration = await s.Acquire(a, t);
            var product = await identities.CatalogueSource(a, reference, t);
            await s.RequireSource(new(reference.Owner, reference.Id, reference.Version, reference.Digest), t);
            foreach (var source in product.Sources) await Provenance(s, source, 0, t);
            var d = registration.Original.Deployment;
            return new TransferProductSourceV1(new(d.EnvironmentId, d.Mode, d.SandboxScopeId, registration.Reference.Version),
                registration.Reference, product.CatalogueRef, product.Option);
        }, ct);
    }
    private static async Task Provenance(TransferEnvironmentStoreV1 s, InventoryIdentitySourceV1 source, int depth, CancellationToken ct)
    {
        if (depth > 2) throw TransferEnvironmentStoreV1.Error("TRANSFER_SOURCE_GRAPH_INVALID");
        await s.RequireSource(source.Reference, ct);
        if (source.Reference.Owner == InventoryIdentityValuesV1.Owner)
        {
            var o = JsonSerializer.Deserialize<InventoryIdentityOriginalV1>(source.OriginalJson, EngineeringProductStoreV1.Json)
                ?? throw TransferEnvironmentStoreV1.Error("TRANSFER_SOURCE_GRAPH_INVALID");
            foreach (var child in o.Sources) await Provenance(s, child, depth + 1, ct);
        }
        else if (source.Reference.Owner == EngineeringProductValuesV1.MappingOwner)
        {
            using var doc = JsonDocument.Parse(source.OriginalJson); var candidate = doc.RootElement.GetProperty("candidate");
            foreach (var field in new[] { "engineeringRef", "businessRef", "siteRef" })
                if (candidate.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
                    await s.RequireSource(value.Deserialize<EngineeringSourceRefV1>(EngineeringProductStoreV1.Json)!, ct);
        }
    }
}

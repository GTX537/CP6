using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Space;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRoutingOwnerV1
{
    private async Task<EngineeringRoutingSnapshotV1> Observe(OperationsAccessV1 a, EngineeringRoutingSnapshotV1 value, CancellationToken ct)
    {
        await a.RequireEngineeringRouting(value.Reference.Id, ct); a.AtCommit(t => a.RequireEngineeringRouting(value.Reference.Id, t));
        var findings = (await Sources(a, value.Body, false, ct)).ToList();
        if (value.Legacy != null) { await LegacyDisclosure(a, value.Legacy, ct); findings.Add("LEGACY_TECHNICAL_REVIEW_REQUIRED"); }
        return value with { Missing = findings.ToArray() };
    }
    private async Task<string[]> Sources(OperationsAccessV1 root, EngineeringRoutingBodyV1 body, bool current, CancellationToken ct, bool requireCurrentParent = true)
    {
        var findings = EngineeringRoutingValuesV1.Validate(body).ToList();
        var revisions = await root.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        await revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, ct);
        revisions.AtCommit(t => revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, t));
        var parents = new EngineeringRevisionStoreV1(db); await parents.Fence(false, ct);
        // Read the exact parent original directly: recursively observing its components would recurse back into this route.
        var parent = await parents.Read(body.RevisionOrigin.Id, Revision(body.RevisionOrigin.Version), ct);
        if (parent.Reference != body.RevisionOrigin || parent.Draft.ProductRef != body.ProductRef) throw Error("ROUTING_REVISION_SOURCE_CHANGED", 412);
        if (current && (requireCurrentParent && !parent.IsCurrent || parent.State != "DRAFT")) throw Error("ROUTING_REVISION_SOURCE_CHANGED", 412);
        var products = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await products.RequireEngineeringProduct(body.ProductRef.Id, ct); products.AtCommit(t => products.RequireEngineeringProduct(body.ProductRef.Id, t));
        var productStore = new EngineeringProductStoreV1(db); await productStore.Fence(false, ct);
        var product = await productStore.Read(body.ProductRef.Id, Revision(body.ProductRef.Version), ct);
        if (product.Reference != body.ProductRef) throw Error("ROUTING_PRODUCT_SOURCE_CHANGED", 412);
        if (!product.IsCurrent || product.Product.Enabled != true)
        { if (current) throw Error("ROUTING_PRODUCT_SOURCE_CHANGED", 412); findings.Add("PRODUCT_SOURCE_CHANGED"); }
        if (body.SiteRef is { } site)
        {
            var sites = await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct);
            var actual = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, sites, site.Id, ct);
            if (actual.Reference != site || !actual.Enabled)
            { if (current) throw Error("ROUTING_SITE_SOURCE_CHANGED", 412); findings.Add("SITE_SOURCE_CHANGED"); }
        }
        // Reuse exact source observations within this transaction; never substitute a newer version.
        var resourceCache = new Dictionary<CP6.Core.Services.Mes.ResourceSourceRefV1, ResourceSourceVersionV1>();
        var unitCache = new Dictionary<UomProfileRefV1, UomProfileVersionV1>();
        var toolCache = new Dictionary<EngineeringSourceRefV1, ToolSpecificationViewV1>();
        foreach (var step in body.Operations)
        {
            foreach (var source in step.Resources)
            {
                if (!resourceCache.TryGetValue(source, out var value))
                {
                    var a = await root.SourceAsync(ResourceSourceStoreV1.Resource(source.Kind), ct);
                    var store = new ResourceSourceStoreV1(db); await store.Fence(false, ct);
                    value = await store.Read(a, source.Kind, source.Id, Revision(source.Version), ct);
                    a.AtCommit(t => a.RequireResourceSource(db, source.Kind, source.Id, t));
                    if (value.Reference != source) throw Error("ROUTING_RESOURCE_SOURCE_CHANGED", 412);
                    resourceCache.Add(source, value);
                }
                if (!value.IsCurrent || !value.SourcePresent || !value.SourceEnabled)
                { if (current) throw Error("ROUTING_RESOURCE_SOURCE_CHANGED", 412); findings.Add("RESOURCE_SOURCE_CHANGED:" + step.OccurrenceId); }
                findings.Add("RESOURCE_QUALIFICATION_REQUIRED:" + step.OccurrenceId);
            }
            foreach (var source in step.Tools)
            {
                if (!toolCache.TryGetValue(source, out var value))
                { value = await new ToolSpecificationOwnerV1(db, owner).Source(root, source, ct); toolCache.Add(source, value); }
                if (!value.IsCurrent || !value.SourceMatches || !value.ProductMatches || value.Specification!.Requirements.ProductRef != body.ProductRef)
                { if (current) throw Error("ROUTING_TOOL_SOURCE_CHANGED", 412); findings.Add("TOOL_SOURCE_CHANGED:" + step.OccurrenceId); }
                findings.Add("TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED:" + step.OccurrenceId);
            }
            foreach (var parameter in step.Parameters)
            {
                if (parameter.UnitRef is not { } source) continue;
                if (!unitCache.TryGetValue(source, out var value))
                {
                    var a = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct); var store = new UomProfileStoreV1(db); await store.Fence(false, ct);
                    await a.RequireUomProfile(source.Id, ct); a.AtCommit(t => a.RequireUomProfile(source.Id, t));
                    value = await store.Read(source.Id, Revision(source.Version), ct);
                    if (value.Reference != source) throw Error("ROUTING_UNIT_SOURCE_CHANGED", 412);
                    if (current) { await UomProfileOwnerV1.CurrentExact(a, store, source, ct); await UomProfileOwnerV1.CurrentBase(a, store, value, ct); }
                    unitCache.Add(source, value);
                }
                if (!value.IsCurrent || value.Profile.Enabled != true) findings.Add("UNIT_SOURCE_CHANGED:" + step.OccurrenceId + ":" + parameter.Name);
                foreach (var number in new[] { parameter.Value, parameter.Minimum, parameter.Maximum })
                    if (number != null && EngineeringRoutingValuesV1.Number(number).Scale > value.Profile.QuantityScale)
                        findings.Add("PARAMETER_UNIT_PRECISION_CONFLICT:" + step.OccurrenceId + ":" + parameter.Name);
            }
        }
        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }
}

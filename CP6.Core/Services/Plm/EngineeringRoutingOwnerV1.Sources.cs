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
        var catalogues = new List<RoutingCatalogueObservationV1>();
        var findings = (await Sources(a, value.Body, false, ct, catalogues: catalogues)).ToList();
        if (value.Legacy != null) { await LegacyDisclosure(a, value.Legacy, ct); findings.Add("LEGACY_TECHNICAL_REVIEW_REQUIRED"); }
        var inputs = value.IsCurrent ? findings : [..findings, "ROUTING_VERSION_HISTORICAL"];
        return value with
        {
            Missing = findings.ToArray(),
            InputValidation = InputValidation(inputs, catalogues)
        };
    }
    private async Task<string[]> Sources(OperationsAccessV1 root, EngineeringRoutingBodyV1 body, bool current, CancellationToken ct,
        bool requireCurrentParent = true, List<RoutingCatalogueObservationV1>? catalogues = null)
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
        // 同一事务内复用准确来源的读取。
        var resourceCache = new Dictionary<ResourceSourceRefV1, ResourceSourceVersionV1>();
        var unitCache = new Dictionary<UomProfileRefV1, (UomProfileVersionV1 Value, bool Current)>();
        var toolCache = new Dictionary<EngineeringSourceRefV1, ToolSpecificationViewV1>();
        var capabilityCache = new Dictionary<ResourceCapabilityRefV1, ResourceCapabilitySnapshotV1>();
        var specificationCache = new Dictionary<EngineeringSourceRefV1, QualitySpecificationSourceProofV1>();
        var rules = new Dictionary<EngineeringSourceRefV1, ConfigurationRuleSnapshotV1>();
        var at = DateTimeOffset.UtcNow;
        foreach (var step in body.Operations)
        {
            if (step.Alternative is { } alternative)
            {
                if (!rules.TryGetValue(alternative.Rule, out var rule))
                {
                    rule = await new ConfigurationRuleOwnerV1(db, owner).Source(root, alternative.Rule, body.ProductRef, current, ct);
                    rules.Add(alternative.Rule, rule);
                }
                CheckAlternative(step.OccurrenceId, alternative, rule, body.ProductRef, findings);
            }
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
                if (step.ResourceCapabilities?.Any(catalogue => catalogue.Kind == source.Kind && catalogue.Id == source.Id) != true)
                    findings.Add("RESOURCE_CAPABILITY_REQUIRED:" + step.OccurrenceId);
            }
            foreach (var reference in step.ResourceCapabilities ?? [])
            {
                if (!capabilityCache.TryGetValue(reference, out var capability))
                {
                    capability = await new ResourceCapabilityOwnerV1(db, owner).Source(root, reference, current, ct);
                    capabilityCache.Add(reference, capability);
                }
                if (!step.Resources.Contains(capability.Body.Source)) throw Error("ROUTING_RESOURCE_CAPABILITY_SOURCE_MISMATCH", 412);
                foreach (var missing in capability.Missing) findings.Add(missing + ":" + step.OccurrenceId);
                var observation = ObserveCatalogue(step.OccurrenceId, capability, at, findings);
                catalogues?.Add(observation);
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
                if (parameter.Evidence is { } evidence)
                {
                    if (quality == null || !quality.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
                    if (!specificationCache.TryGetValue(evidence.Specification, out var proof))
                    {
                        proof = await quality.Source(root, evidence.Specification, body.ProductRef, current, ct,
                            expectedSite: body.SiteRef, expectedStage: "IPQC");
                        specificationCache.Add(evidence.Specification, proof);
                    }
                    if (current && proof.Approval != evidence.Approval) throw Error("ROUTING_PARAMETER_APPROVAL_CHANGED", 412);
                    CheckParameterEvidence(step.OccurrenceId, parameter, proof, body.SiteRef != null, findings);
                }
                if (parameter.UnitRef is not { } source) continue;
                if (!unitCache.TryGetValue(source, out var value))
                {
                    value = await ParameterUnit(root, source, current, ct);
                    unitCache.Add(source, value);
                }
                var parameterPath = ":" + step.OccurrenceId + ":" + parameter.Name;
                if (!value.Current) findings.Add("UNIT_SOURCE_CHANGED" + parameterPath);
                if (parameter.DimensionKey != null && value.Value.Profile.DimensionKey != parameter.DimensionKey)
                    findings.Add("PARAMETER_DIMENSION_MISMATCH" + parameterPath);
                if (value.Value.Profile.QuantityScale is not { } scale)
                    findings.Add("PARAMETER_UNIT_PRECISION_UNKNOWN" + parameterPath);
                else if (new[] { parameter.Value, parameter.Minimum, parameter.Maximum }.OfType<string>()
                    .Any(number => EngineeringRoutingValuesV1.Number(number).Scale > scale))
                    findings.Add("PARAMETER_UNIT_PRECISION_CONFLICT" + parameterPath);
            }
        }
        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }
}

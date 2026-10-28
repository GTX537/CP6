using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringManufacturingOwnerV1
{
    internal async Task<EngineeringManufacturingSnapshotV1> Source(OperationsAccessV1 root, EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        IterationCandidateValuesV1.Reference(reference, EngineeringManufacturingValuesV1.Owner);
        var a = await root.SourceAsync(OperationsOwnerV1.EngineeringManufacturing, ct);
        var store = new EngineeringManufacturingStoreV1(db); await store.Fence(false, ct);
        var value = await Observe(a, await store.Read(reference.Id, Revision(reference.Version), ct), ct);
        if (value.Reference != reference) throw Error("MANUFACTURING_REFERENCE_CHANGED", 412);
        return value;
    }
    private async Task<EngineeringManufacturingSnapshotV1> Observe(OperationsAccessV1 root, EngineeringManufacturingSnapshotV1 value, CancellationToken ct)
    {
        await root.RequireEngineeringManufacturing(value.Reference.Id, ct);
        root.AtCommit(t => root.RequireEngineeringManufacturing(value.Reference.Id, t));
        return value with { Missing = await Sources(root, value.Body, false, ct) };
    }
    private async Task<string[]> Sources(OperationsAccessV1 root, EngineeringManufacturingBodyV1 body, bool current, CancellationToken ct)
    {
        var findings = EngineeringManufacturingValuesV1.Validate(body).ToList();
        var revisions = await root.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        await revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, ct); revisions.AtCommit(t => revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, t));
        var parents = new EngineeringRevisionStoreV1(db); await parents.Fence(false, ct);
        // Read the exact original, without recursively expanding its dependent components.
        var parent = await parents.Read(body.RevisionOrigin.Id, Revision(body.RevisionOrigin.Version), ct);
        if (parent.Reference != body.RevisionOrigin || parent.Draft.ProductRef != body.ProductRef
            || body.ConfigurationRef != parent.Draft.Configuration?.Reference) throw Error("MANUFACTURING_REVISION_SOURCE_CHANGED", 412);
        if (!parent.IsCurrent) { if (current) throw Error("MANUFACTURING_REVISION_SOURCE_CHANGED", 412); findings.Add("REVISION_SOURCE_CHANGED"); }
        if (parent.Draft.Configuration is { } configuration)
        {
            if (EngineeringRevisionStoreV1.Digest(EngineeringRevisionStoreV1.Encode(configuration.Input)) != configuration.Reference.Digest)
                throw Error("MANUFACTURING_CONFIGURATION_ORIGINAL_UNAVAILABLE", 503);
            if (configuration.Input.RuleRef is { } rule)
            {
                var actual = await new ConfigurationRuleOwnerV1(db, owner).Source(root, rule, body.ProductRef, current, ct);
                if (!actual.IsActive || actual.State != "APPROVED" || actual.Missing!.Length != 0 || actual.Body.ProductRef != body.ProductRef) findings.Add("CONFIGURATION_RULE_CHANGED");
                if (ConfigurationRuleValuesV1.Evaluate(actual, configuration.Input.Options).Status != "OPTIONS_VALIDATED") findings.Add("CONFIGURATION_OPTIONS_UNRESOLVED");
            }
            else findings.Add("CONFIGURATION_RULE_REQUIRED");
            findings.Add("CONFIGURATION_APPLICABILITY_REVIEW_REQUIRED");
        }
        var boms = new EngineeringBomOwnerV1(db, owner);
        var ebom = await boms.Source(root, body.EbomRef, parent.Reference.Id, body.ProductRef, "EBOM", current, ct);
        var mbom = await boms.Source(root, body.MbomRef, parent.Reference.Id, body.ProductRef, "MBOM", current, ct);
        var routing = await new EngineeringRoutingOwnerV1(db, owner).Source(root, body.RoutingRef, parent.Reference.Id, body.ProductRef, current, ct);
        foreach (var (prefix, missing, isCurrent) in new[] { ("EBOM", ebom.Missing!, ebom.IsCurrent), ("MBOM", mbom.Missing!, mbom.IsCurrent), ("ROUTING", routing.Missing!, routing.IsCurrent) })
        { findings.AddRange(missing.Select(x => prefix + ":" + x)); if (!isCurrent) findings.Add(prefix + "_SOURCE_CHANGED"); }
        if (mbom.Body.RoutingRef != body.RoutingRef) findings.Add("MBOM_ROUTING_MISMATCH");
        if (body.SiteRef != mbom.Body.SiteRef || body.SiteRef != routing.Body.SiteRef) findings.Add("MANUFACTURING_SITE_MISMATCH");
        if (body.SiteRef is { } site)
        {
            var actual = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct), site.Id, ct);
            if (actual.Reference != site || !actual.Enabled)
            { if (current) throw Error("MANUFACTURING_SITE_SOURCE_CHANGED", 412); findings.Add("SITE_SOURCE_CHANGED"); }
        }
        var products = new HashSet<EngineeringProductRefV1>();
        var units = new Dictionary<UomProfileRefV1, UomProfileVersionV1>();
        foreach (var output in body.Outputs)
        {
            if (output.ProductRef is { } product && products.Add(product))
            {
                var a = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct); var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
                await a.RequireEngineeringProduct(product.Id, ct); a.AtCommit(t => a.RequireEngineeringProduct(product.Id, t));
                var value = await store.Read(product.Id, Revision(product.Version), ct);
                if (value.Reference != product) throw Error("MANUFACTURING_PRODUCT_REFERENCE_CHANGED", 412);
                if (!value.IsCurrent || value.Product.Enabled != true)
                { if (current) throw Error("MANUFACTURING_OUTPUT_PRODUCT_CHANGED", 412); findings.Add("OUTPUT_PRODUCT_SOURCE_CHANGED:" + product.Id); }
            }
            foreach (var reference in new[] { output.UnitRef, output.DenominatorUnitRef }.Where(x => x != null).Cast<UomProfileRefV1>())
            {
                if (units.ContainsKey(reference)) continue;
                var a = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct); var store = new UomProfileStoreV1(db); await store.Fence(false, ct);
                await a.RequireUomProfile(reference.Id, ct); a.AtCommit(t => a.RequireUomProfile(reference.Id, t));
                var value = await store.Read(reference.Id, Revision(reference.Version), ct);
                if (value.Reference != reference) throw Error("MANUFACTURING_UNIT_REFERENCE_CHANGED", 412);
                if (current) { await UomProfileOwnerV1.CurrentExact(a, store, reference, ct); await UomProfileOwnerV1.CurrentBase(a, store, value, ct); }
                if (!value.IsCurrent || value.Profile.Enabled != true) findings.Add("UNIT_SOURCE_CHANGED:" + reference.Id);
                units.Add(reference, value);
            }
            if (output.UnitRef is { } unit)
                foreach (var quantity in new[] { output.Quantity, output.Minimum, output.Maximum }.Where(x => x != null))
                    if (EngineeringRoutingValuesV1.Number(quantity!).Scale > units[unit].Profile.QuantityScale) findings.Add("OUTPUT_UNIT_PRECISION_CONFLICT:" + output.OutputOccurrenceId);
            if (output.DenominatorUnitRef is { } denominatorUnit && output.Denominator != null
                && EngineeringRoutingValuesV1.Number(output.Denominator).Scale > units[denominatorUnit].Profile.QuantityScale) findings.Add("DENOMINATOR_UNIT_PRECISION_CONFLICT:" + output.OutputOccurrenceId);
        }
        Closure(body, ebom, mbom, routing, findings);
        // This authoring asset is distinct from the closed ENG-OUTPUT-RELATIONS/1 wire.
        // Text requirements cannot be promoted to native specification or professional evidence references.
        findings.Add("FORMAL_RELATION_SET_PROOFS_REQUIRED");
        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void Closure(EngineeringManufacturingBodyV1 body, EngineeringBomSnapshotV1 ebom, EngineeringBomSnapshotV1 mbom,
        EngineeringRoutingSnapshotV1 routing, List<string> missing)
    {
        var source = ebom.Body.Occurrences.Select(x => x.OccurrenceId).ToHashSet();
        var target = mbom.Body.Occurrences.Select(x => x.OccurrenceId).ToHashSet();
        var operations = routing.Body.Operations.ToDictionary(x => x.OccurrenceId); var outputs = body.Outputs.ToDictionary(x => x.OutputOccurrenceId);
        var mappedSource = new HashSet<Guid>(); var mappedTarget = new HashSet<Guid>(); var consumed = new HashSet<Guid>();
        foreach (var map in body.Mappings)
        {
            foreach (var id in map.EbomOccurrenceIds) { if (!source.Contains(id)) missing.Add("MAPPING_EBOM_OCCURRENCE_MISSING:" + id); else mappedSource.Add(id); }
            foreach (var id in map.MbomOccurrenceIds) { if (!target.Contains(id)) missing.Add("MAPPING_MBOM_OCCURRENCE_MISSING:" + id); else mappedTarget.Add(id); }
            foreach (var id in map.OperationOccurrenceIds) if (!operations.ContainsKey(id)) missing.Add("MAPPING_OPERATION_MISSING:" + id);
        }
        foreach (var id in source.Except(mappedSource).Order()) missing.Add("UNMAPPED_EBOM_OCCURRENCE:" + id);
        foreach (var id in target.Except(mappedTarget).Order()) missing.Add("UNMAPPED_MBOM_OCCURRENCE:" + id);
        if (!body.Outputs.Any(x => x.Role == "PRIMARY" && x.ProductRef == body.ProductRef)) missing.Add("PRIMARY_PRODUCT_OUTPUT_REQUIRED");
        foreach (var output in body.Outputs)
        {
            var id = output.OutputOccurrenceId;
            if (output.GeneratingOperationOccurrenceId is { } op && !operations.ContainsKey(op)) missing.Add("GENERATING_OPERATION_MISSING:" + id);
            foreach (var input in output.InputOccurrenceIds) { if (!target.Contains(input)) missing.Add("OUTPUT_INPUT_MISSING:" + id + ":" + input); else consumed.Add(input); }
            foreach (var from in output.FromOutputOccurrenceIds)
            {
                if (!outputs.TryGetValue(from, out var previous)) { missing.Add("INTERMEDIATE_OUTPUT_MISSING:" + id + ":" + from); continue; }
                if (previous.Role != "INTERMEDIATE") missing.Add("INTERMEDIATE_ROLE_REQUIRED:" + from);
                var reached = new HashSet<Guid>();
                bool Before(Guid next)
                {
                    if (!reached.Add(next) || !operations.TryGetValue(next, out var step)) return false;
                    return step.DependsOn.Any(p => p == previous.GeneratingOperationOccurrenceId || Before(p));
                }
                if (output.GeneratingOperationOccurrenceId is not { } end || previous.GeneratingOperationOccurrenceId == null
                    || previous.GeneratingOperationOccurrenceId == end || !Before(end)) missing.Add("INTERMEDIATE_FLOW_ORDER_UNPROVEN:" + id);
            }
            var seen = new HashSet<Guid>(); var active = new HashSet<Guid>();
            bool Cycle(Guid next)
            {
                if (active.Contains(next)) return true;
                if (!seen.Add(next) || !outputs.TryGetValue(next, out var step)) return false;
                active.Add(next); var cycle = step.FromOutputOccurrenceIds.Any(Cycle); active.Remove(next); return cycle;
            }
            if (Cycle(id)) missing.Add("OUTPUT_FLOW_CYCLE:" + id);
            if (output.Role == "INTERMEDIATE" && !body.Outputs.Any(x => x.FromOutputOccurrenceIds.Contains(id))) missing.Add("INTERMEDIATE_DESTINATION_REQUIRED:" + id);
        }
        foreach (var id in target.Except(consumed).Order()) missing.Add("UNUSED_MANUFACTURING_INPUT:" + id);
    }
}

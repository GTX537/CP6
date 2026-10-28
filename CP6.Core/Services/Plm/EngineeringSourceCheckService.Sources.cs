using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Space;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringSourceCheckValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringSourceCheckService
{
    private sealed class Observation
    {
        internal readonly List<EngineeringCheckItemV1> Items = [];
        internal readonly List<EngineeringSourcePinV1> Sources = [];
        internal void Item(string path, string state, string code, string reason = "", EngineeringSourceRefV1? source = null)
            => Items.Add(new(path, state, code, reason, source));
        internal void Pin(string path, EngineeringSourceRefV1 reference, string schema, string json, object watermark, IEnumerable<string> findings)
        {
            var missing = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Sources.Add(new(path, reference, reference.Owner, schema, "NATIVE_SAME_UOW_RECHECK", Digest(watermark), json, missing));
            Item(path + "/content", "PASS", "EXACT_NATIVE_SOURCE", "Exact source observed under the Main transaction; final use requires recheck", reference);
            foreach (var code in missing)
                Item(path, code.Contains("CHANGED", StringComparison.Ordinal) || code.Contains("MISMATCH", StringComparison.Ordinal)
                    || code.Contains("CONFLICT", StringComparison.Ordinal) || code.Contains("CYCLE", StringComparison.Ordinal)
                    || code.Split(':', 2)[0] is "QUANTITY_NONPOSITIVE" or "LOSS_NEGATIVE" or "ITEM_SELF_CONTAINMENT"
                        or "PARENT_OCCURRENCE_MISSING" or "ROUTING_OCCURRENCE_MISSING" or "DEPENDENCY_MISSING"
                    ? "BLOCK" : "UNKNOWN", code, source: reference);
        }
        internal string Fingerprint() => Digest(new { Items, Sources });
    }
    private async Task Attempt(OperationsAccessV1 root, Observation observation, string path,
        Func<OperationsAccessV1, CancellationToken, Task> read, CancellationToken ct)
    {
        var itemCount = observation.Items.Count; var sourceCount = observation.Sources.Count;
        try { await root.EngineeringObservation(async (a, t) => { await read(a, t); return true; }, ct); }
        catch (OrderPriceCorrectionException e) when (e.Status is 403 or 404 or 409 or 412 or 422 or 503)
        {
            observation.Items.RemoveRange(itemCount, observation.Items.Count - itemCount);
            observation.Sources.RemoveRange(sourceCount, observation.Sources.Count - sourceCount);
            observation.Item(path, e.Status is 409 or 412 or 422 ? "BLOCK" : "UNKNOWN", e.Status == 403 ? "SOURCE_ACCESS_DENIED" : e.Message);
        }
    }
    private async Task<EngineeringProductVersionV1> Product(OperationsAccessV1 root, Observation o, string path,
        EngineeringProductRefV1 reference, CancellationToken ct)
    {
        var a = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct); await a.RequireEngineeringProduct(reference.Id, ct);
        a.AtCommit(t => a.RequireEngineeringProduct(reference.Id, t)); var s = new EngineeringProductStoreV1(db); await s.Fence(false, ct);
        var v = await s.Read(reference.Id, Revision(reference.Version), ct); if (v.Reference != reference) throw Error("ENGINEERING_REFERENCE_CHANGED", 412);
        var current = await s.Read(reference.Id, null, ct);
        o.Pin(path, Ref(reference), "cp6_engineering_product_v1", v.OriginalJson, current.Reference,
            v.IsCurrent && v.Product.Enabled == true ? [] : ["PRODUCT_SOURCE_CHANGED"]);
        return v;
    }
    private async Task Unit(OperationsAccessV1 root, Observation o, string path, UomProfileRefV1 reference, CancellationToken ct, HashSet<Guid>? visited = null)
    {
        visited ??= []; if (!visited.Add(reference.Id)) throw Error("UNIT_SOURCE_CYCLE", 422);
        var a = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct); await a.RequireUomProfile(reference.Id, ct);
        a.AtCommit(t => a.RequireUomProfile(reference.Id, t)); var s = new UomProfileStoreV1(db); await s.Fence(false, ct);
        var v = await s.Read(reference.Id, Revision(reference.Version), ct); if (v.Reference != reference) throw Error("UNIT_REFERENCE_CHANGED", 412);
        var current = await s.Read(reference.Id, null, ct);
        o.Pin(path, new(reference.Owner, reference.Id, reference.Version, reference.Digest), "cp6_uom_profile_v1", v.OriginalJson, current.Reference,
            v.IsCurrent && v.Profile.Enabled == true ? [] : ["UNIT_SOURCE_CHANGED"]);
        if (v.Profile.BaseRef is { } parent) await Unit(root, o, path + "/base", parent, ct, visited);
    }
    private async Task Site(OperationsAccessV1 root, Observation o, string path, EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        var v = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct), reference.Id, ct);
        var version = Revision(reference.Version);
        var original = await db.Database.SqlQuery<string>($"""
            SELECT source_json AS "Value" FROM cp6_physical_site_identity_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND site_id={reference.Id} AND revision={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("PHYSICAL_SITE_ORIGINAL_UNAVAILABLE", 503);
        if (EngineeringProductStoreV1.Digest(System.Text.Encoding.UTF8.GetBytes(original)) != reference.Digest) throw Error("PHYSICAL_SITE_ORIGINAL_UNAVAILABLE", 503);
        o.Pin(path, reference, "cp6_physical_site_identity_v1", original, v.Reference,
            v.Reference == reference && v.Enabled ? [] : ["SITE_SOURCE_CHANGED"]);
    }
    private async Task Document(OperationsAccessV1 root, Observation o, string path, EngineeringSourceRefV1 reference,
        EngineeringProductRefV1? product, CancellationToken ct)
    {
        var access = await root.SourceAsync(OperationsOwnerV1.ControlledDocument, ct);
        var v = await documents.ReviewSource(access, reference, false, ct); var missing = v.Missing!.ToList();
        if (v.Body.ProductRef != product) missing.Add("DOCUMENT_PRODUCT_MISMATCH");
        if (!v.IsCurrent) missing.Add("DOCUMENT_SOURCE_CHANGED");
        if (v.State != "APPROVED") missing.Add("DOCUMENT_APPROVAL_REQUIRED");
        o.Pin(path, reference, "cp6_controlled_document_v1", v.OriginalJson, new { v.IsCurrent, v.State, v.ContentStatus }, missing);
        // The document original preserves every immutable content ID/hash/occurrence. File state
        // watermarks come from the actual FILE Owner, not a URL or a second document registry.
        foreach (var member in v.Body.Contents)
        {
            var status = v.ContentStatus!.Single(x => x.OccurrenceId == member.OccurrenceId);
            o.Item(path + "/files/" + member.OccurrenceId, status.Availability == "AVAILABLE" ? "PASS" : "UNKNOWN",
                "FILE_" + status.Availability, "Exact immutable content identity/hash retained in the native document original", reference);
        }
    }
    private async Task Component(OperationsAccessV1 root, Observation o, string path, EngineeringComponentDraftV1 component,
        EngineeringRevisionSnapshotV1 revision, CancellationToken ct)
    {
        var reference = component.SourceRef!; var product = revision.Draft.ProductRef;
        switch (component.Kind)
        {
            case "PRODUCT": await Product(root, o, path, new(reference.Owner, reference.Id, reference.Version, reference.Digest), ct); break;
            case "DOCUMENT": await Document(root, o, path, reference, product, ct); break;
            case "QUALITY_SPEC":
            {
                var p = await quality.Source(root, reference, product, false, ct);
                o.Pin(path, reference, "cp6_quality_specification_v1", p.OriginalSourceJson ?? throw Error("QUALITY_SPEC_ORIGINAL_UNAVAILABLE", 503),
                    new { p.Control, p.Approval, p.RequirementsDigest, p.Availability }, p.Missing);
                if (p.Missing.Length == 0) await quality.HoldExact(root, reference, p.Control, product!, ct);
                using var original = JsonDocument.Parse(p.OriginalSourceJson!);
                var body = JsonSerializer.Deserialize<QualitySpecificationBodyV1>(original.RootElement.GetProperty("body"), EngineeringProductStoreV1.Json)
                    ?? throw Error("QUALITY_SPEC_ORIGINAL_UNAVAILABLE", 503);
                await Site(root, o, path + "/site", body.Site, ct);
                foreach (var document in QualitySpecificationValuesV1.Documents(body))
                    await Document(root, o, path + "/standards/" + document.Id, document, product, ct);
                foreach (var unit in p.Requirements.Select(x => x.Unit).OfType<UomProfileRefV1>().Distinct())
                    await Unit(root, o, path + "/units/" + unit.Id, unit, ct);
                break;
            }
            case "ROUTING":
            {
                var v = await new EngineeringRoutingOwnerV1(db, owner).Source(root, reference, revision.Reference.Id, product, false, ct);
                o.Pin(path, reference, "cp6_engineering_routing_v1", v.OriginalJson, new { v.Reference, v.IsCurrent, v.Missing },
                    v.IsCurrent ? v.Missing! : [..v.Missing!, "ROUTING_SOURCE_CHANGED"]);
                if (v.Body.SiteRef is { } site) await Site(root, o, path + "/site", site, ct);
                foreach (var step in v.Body.Operations)
                {
                    var stepPath = path + "/operations/" + step.OccurrenceId;
                    foreach (var r in step.Resources)
                    {
                        var a = await root.SourceAsync(ResourceSourceStoreV1.Resource(r.Kind), ct); var store = new ResourceSourceStoreV1(db); await store.Fence(false, ct);
                        var resource = await store.Read(a, r.Kind, r.Id, Revision(r.Version), ct);
                        if (resource.Reference != r) throw Error("RESOURCE_REFERENCE_CHANGED", 412);
                        o.Pin(stepPath + "/resources/" + r.Id, new(r.Owner, r.Id, r.Version, r.Digest), "cp6_resource_source_v1", resource.OriginalJson,
                            new { resource.IsCurrent, resource.Axes, resource.SourceEnabled, resource.Qualification },
                            resource.IsCurrent && resource.SourcePresent && resource.SourceEnabled ? ["RESOURCE_QUALIFICATION_REQUIRED"] : ["RESOURCE_SOURCE_CHANGED"]);
                    }
                    foreach (var tool in step.Tools)
                    {
                        var vtool = await new ToolSpecificationOwnerV1(db, owner).Source(root, tool, ct);
                        o.Pin(stepPath + "/tools/" + tool.Id, tool, "cp6_tool_specification_v1", vtool.Specification!.OriginalJson,
                            new { vtool.CurrentControl, vtool.Source.Reference, vtool.Source.CurrentNativeRowVersion, vtool.ProductMatches },
                            vtool.IsCurrent && vtool.SourceMatches && vtool.ProductMatches
                                ? ["TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED"] : ["TOOL_SOURCE_CHANGED"]);
                    }
                    foreach (var unit in step.Parameters.Select(x => x.UnitRef).OfType<UomProfileRefV1>().Distinct())
                        await Unit(root, o, stepPath + "/units/" + unit.Id, unit, ct);
                }
                break;
            }
            case "EBOM": case "MBOM":
            {
                var v = await new EngineeringBomOwnerV1(db, owner).Source(root, reference, revision.Reference.Id, product, component.Kind, false, ct);
                o.Pin(path, reference, "cp6_engineering_bom_v1", v.OriginalJson, new { v.Reference, v.IsCurrent, v.Missing },
                    v.IsCurrent ? v.Missing! : [..v.Missing!, "BOM_SOURCE_CHANGED"]);
                if (v.Body.SiteRef is { } site) await Site(root, o, path + "/site", site, ct);
                if (v.Body.RoutingRef is { } route) await Component(root, o, path + "/routing", component with { Kind = "ROUTING", SourceRef = route }, revision, ct);
                foreach (var row in v.Body.Occurrences)
                {
                    var rowPath = path + "/occurrences/" + row.OccurrenceId;
                    if (row.ItemRef is { } item)
                    {
                        if (item.Owner == EngineeringProductValuesV1.Owner) await Product(root, o, rowPath + "/item", new(item.Owner, item.Id, item.Version, item.Digest), ct);
                        else
                        {
                            var a = await root.SourceAsync(OperationsOwnerV1.Product, ct);
                            var actual = await CommercialProductIdentityReader.ReadOwnedAsync(db, a, item.Id, Revision(item.Version), ct);
                            if (actual.Digest != item.Digest) throw Error("BOM_ITEM_REFERENCE_CHANGED", 412);
                            o.Pin(rowPath + "/item", item, "cp6_product_identity_v1", actual.OriginalJson, new { actual.IsCurrent, actual.SourcePresent },
                                actual.IsCurrent && actual.SourcePresent ? ["ITEM_TECHNICAL_REVISION_REQUIRED"] : ["ITEM_SOURCE_CHANGED"]);
                        }
                    }
                    if (row.UnitRef is { } unit) await Unit(root, o, rowPath + "/unit", unit, ct);
                }
                break;
            }
            default: throw Error("ENGINEERING_SOURCE_PARTICIPANT_REQUIRED", 503);
        }
    }
}

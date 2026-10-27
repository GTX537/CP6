using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringBomOwnerV1
{
    private async Task<EngineeringBomSnapshotV1> Observe(OperationsAccessV1 a, EngineeringBomSnapshotV1 value, CancellationToken ct)
    {
        await a.RequireEngineeringBom(value.Reference.Id, ct); a.AtCommit(t => a.RequireEngineeringBom(value.Reference.Id, t));
        var findings = (await Sources(a, value.Body, false, ct)).ToList();
        if (value.Legacy != null) { await LegacyDisclosure(a, value.Legacy, ct); findings.Add("LEGACY_TECHNICAL_REVIEW_REQUIRED"); }
        return value with { Missing = findings.Distinct(StringComparer.Ordinal).ToArray() };
    }
    private async Task<bool> ItemSource(OperationsAccessV1 root, EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        EngineeringBomValuesV1.Item(reference);
        if (reference.Owner == BusinessOwner)
        {
            var a = await root.SourceAsync(OperationsOwnerV1.Product, ct);
            var value = await CommercialProductIdentityReader.ReadOwnedAsync(db, a, reference.Id, Revision(reference.Version), ct);
            a.AtCommit(async t => { if (!await a.Products(includeDeleted: true).AnyAsync(p => p.Id == reference.Id, t)) throw Error("OBJECT_FORBIDDEN", 403); });
            if (value.Digest != reference.Digest) throw Error("BOM_ITEM_REFERENCE_CHANGED", 412);
            return value.IsCurrent && value.SourcePresent && !value.Source.GetProperty("isDeleted").GetBoolean();
        }
        var engineering = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await engineering.RequireEngineeringProduct(reference.Id, ct); engineering.AtCommit(t => engineering.RequireEngineeringProduct(reference.Id, t));
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        var product = await store.Read(reference.Id, Revision(reference.Version), ct);
        if (product.Reference != new EngineeringProductRefV1(reference.Owner, reference.Id, reference.Version, reference.Digest)) throw Error("BOM_ITEM_REFERENCE_CHANGED", 412);
        return product.IsCurrent && product.Product.Enabled == true;
    }
    private async Task<string[]> Sources(OperationsAccessV1 root, EngineeringBomBodyV1 body, bool current, CancellationToken ct, bool requireCurrentParent = true)
    {
        var findings = EngineeringBomValuesV1.Validate(body).ToList();
        var revisions = await root.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        await revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, ct); revisions.AtCommit(t => revisions.RequireEngineeringRevision(body.RevisionOrigin.Id, t));
        var parents = new EngineeringRevisionStoreV1(db); await parents.Fence(false, ct);
        var parent = await parents.Read(body.RevisionOrigin.Id, Revision(body.RevisionOrigin.Version), ct);
        if (parent.Reference != body.RevisionOrigin || parent.Draft.ProductRef != body.ProductRef
            || current && (requireCurrentParent && !parent.IsCurrent || parent.State != "DRAFT")) throw Error("BOM_REVISION_SOURCE_CHANGED", 412);
        var product = body.ProductRef;
        if (!await ItemSource(root, new(product.Owner, product.Id, product.Version, product.Digest), ct))
        { if (current) throw Error("BOM_PRODUCT_SOURCE_CHANGED", 412); findings.Add("PRODUCT_SOURCE_CHANGED"); }
        if (body.SiteRef is { } site)
        {
            var actual = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct), site.Id, ct);
            if (actual.Reference != site || !actual.Enabled)
            { if (current) throw Error("BOM_SITE_SOURCE_CHANGED", 412); findings.Add("SITE_SOURCE_CHANGED"); }
        }
        EngineeringRoutingSnapshotV1? routing = null;
        if (body.RoutingRef is { } route)
        {
            routing = await new EngineeringRoutingOwnerV1(db, owner).Source(root, route, body.RevisionOrigin.Id, product, current, ct);
            if (!routing.IsCurrent) findings.Add("ROUTING_SOURCE_CHANGED");
            if (routing.Body.SiteRef != body.SiteRef) findings.Add("ROUTING_SITE_MISMATCH");
            findings.Add("ROUTING_REVIEW_AND_FREEZE_REQUIRED");
        }
        var items = new Dictionary<EngineeringSourceRefV1, bool>(); var units = new Dictionary<UomProfileRefV1, UomProfileVersionV1>();
        foreach (var row in body.Occurrences)
        {
            if (row.ItemRef is { } item)
            {
                if (!items.TryGetValue(item, out var active)) items[item] = active = await ItemSource(root, item, ct);
                if (!active) { if (current) throw Error("BOM_ITEM_SOURCE_CHANGED", 412); findings.Add("ITEM_SOURCE_CHANGED:" + row.OccurrenceId); }
                if (item.Owner == BusinessOwner) findings.Add("ITEM_TECHNICAL_REVISION_REQUIRED:" + row.OccurrenceId);
            }
            if (row.UnitRef is { } unit)
            {
                if (!units.TryGetValue(unit, out var value))
                {
                    var a = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct); var store = new UomProfileStoreV1(db); await store.Fence(false, ct);
                    await a.RequireUomProfile(unit.Id, ct); a.AtCommit(t => a.RequireUomProfile(unit.Id, t));
                    value = await store.Read(unit.Id, Revision(unit.Version), ct);
                    if (value.Reference != unit) throw Error("BOM_UNIT_REFERENCE_CHANGED", 412);
                    if (current) { await UomProfileOwnerV1.CurrentExact(a, store, unit, ct); await UomProfileOwnerV1.CurrentBase(a, store, value, ct); }
                    units.Add(unit, value);
                }
                if (!value.IsCurrent || value.Profile.Enabled != true) findings.Add("UNIT_SOURCE_CHANGED:" + row.OccurrenceId);
                if (row.Quantity != null && EngineeringRoutingValuesV1.Number(row.Quantity).Scale > value.Profile.QuantityScale)
                    findings.Add("QUANTITY_UNIT_PRECISION_CONFLICT:" + row.OccurrenceId);
            }
            if (row.RoutingOccurrenceId is { } operation && (routing == null || !routing.Body.Operations.Any(x => x.OccurrenceId == operation)))
                findings.Add("ROUTING_OCCURRENCE_MISSING:" + row.OccurrenceId);
        }
        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }
    public Task<EngineeringBomItemChoiceV1[]> ItemChoicesAsync(string kind, string search, int page = 1, CancellationToken ct = default)
    {
        if (kind is not ("BUSINESS" or "ENGINEERING") || search == null || search.Length > 256 || page is < 1 or > 100000) throw Error("BOM_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBom, "query", false, async (root, token) =>
        {
            var result = new List<EngineeringBomItemChoiceV1>();
            if (kind == "BUSINESS")
            {
                var a = await root.SourceAsync(OperationsOwnerV1.Product, token);
                var rows = await a.Products().Where(x => x.ProductCd.Contains(search)).OrderBy(x => x.ProductCd).ThenBy(x => x.Id)
                    .Skip((page - 1) * 20).Take(20).Select(x => new { x.Id, x.ProductCd }).ToArrayAsync(token);
                foreach (var row in rows)
                {
                    var source = await CommercialProductIdentityReader.ReadOwnedAsync(db, a, row.Id, null, token);
                    var reference = new EngineeringSourceRefV1(BusinessOwner, row.Id, source.Version, source.Digest);
                    await ItemSource(root, reference, token); result.Add(new(reference, row.ProductCd, row.ProductCd));
                }
            }
            else
            {
                var a = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, token); var store = new EngineeringProductStoreV1(db); await store.Fence(false, token);
                var ids = await a.EngineeringProducts().Where(x => x.Code.Contains(search)).OrderBy(x => x.Code).ThenBy(x => x.Id)
                    .Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(token);
                foreach (var id in ids)
                {
                    var value = await store.Read(id, null, token); var r = value.Reference; var reference = new EngineeringSourceRefV1(r.Owner, r.Id, r.Version, r.Digest);
                    if (await ItemSource(root, reference, token)) result.Add(new(reference, value.Product.Code, value.Product.Name));
                }
            }
            return result.ToArray();
        }, ct);
    }
    private sealed class UsageRow { public Guid BomId { get; init; } }
    public Task<EngineeringBomUsageV1[]> WhereUsedAsync(EngineeringSourceRefV1 reference, int page = 1, CancellationToken ct = default)
    {
        EngineeringBomValuesV1.Item(reference); if (page is < 1 or > 100000) throw Error("BOM_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBom, "query", false, async (a, token) =>
        {
            var store = new EngineeringBomStoreV1(db); await store.Fence(false, token); await ItemSource(a, reference, token);
            var candidates = db.Database.SqlQuery<UsageRow>($"""
                SELECT DISTINCT h.bom_id AS "BomId" FROM cp6_engineering_bom_v1.heads h
                JOIN cp6_engineering_bom_v1.versions v USING(tenant_id,bom_id,revision)
                CROSS JOIN LATERAL jsonb_array_elements(convert_from(v.body_bytes,'UTF8')::jsonb->'body'->'occurrences') item
                WHERE h.tenant_id={db.CurrentTenantId} AND item->'itemRef'->>'owner'={reference.Owner}
                  AND item->'itemRef'->>'id'={reference.Id.ToString()} AND item->'itemRef'->>'version'={reference.Version}
                  AND item->'itemRef'->>'digest'={reference.Digest}
                """);
            var visible = a.EngineeringBoms(); var ids = await candidates.Where(x => visible.Any(h => h.Id == x.BomId))
                .OrderBy(x => x.BomId).Skip((page - 1) * 20).Take(20).Select(x => x.BomId).ToArrayAsync(token);
            var result = new List<EngineeringBomUsageV1>();
            foreach (var id in ids)
            {
                var value = await Observe(a, await store.Read(id, null, token), token);
                foreach (var row in value.Body.Occurrences.Where(x => x.ItemRef == reference)) result.Add(new(value.Reference, value.Body.Name, value.Body.Kind, row.OccurrenceId, row.Path));
            }
            return result.ToArray();
        }, ct);
    }
}

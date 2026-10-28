using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.InventoryIdentityValuesV1;

namespace CP6.Core.Services.Wms;

public sealed partial class InventoryIdentityOwnerV1
{
    private async Task<InventoryIdentityViewV1> Exact(OperationsAccessV1 root, InventoryIdentityStoreV1 s,
        InventoryIdentityRefV1 reference, string kind, bool current, CancellationToken ct)
    {
        Reference(reference); var a = await root.SourceAsync(OperationsOwnerV1.InventoryIdentity, ct);
        var v = await Original(a, s, reference.Id, Revision(reference.Version), ct);
        if (v.Reference != reference || v.Original.Definition.Kind != kind) throw Error("INVENTORY_IDENTITY_REFERENCE_INVALID", 422);
        // Only PRODUCT can depend on PARTY/PURPOSE, so saved reference graphs cannot recurse.
        if (current) await Selectable(a, s, v, ct);
        else
        {
            var historical = await Sources(a, s, v.Original.Definition, false, ct);
            if (!SameSources(historical, v.Original.Sources)) throw Error("INVENTORY_IDENTITY_ORIGINAL_UNAVAILABLE", 503);
        }
        return v;
    }
    private async Task Selectable(OperationsAccessV1 a, InventoryIdentityStoreV1 s, InventoryIdentityViewV1 v, CancellationToken ct)
    {
        if (!v.IsCurrent) throw Error("INVENTORY_IDENTITY_CHANGED", 412);
        if (v.Original.State != "APPROVED") throw Error("INVENTORY_IDENTITY_APPROVAL_REQUIRED");
        var sources = await Sources(a, s, v.Original.Definition, true, ct);
        if (!SameSources(sources, v.Original.Sources)) throw Error("INVENTORY_IDENTITY_SOURCES_CHANGED", 412);
        await Decision(a, v, ct);
    }
    private async Task<InventoryIdentitySourceV1[]> Sources(OperationsAccessV1 root, InventoryIdentityStoreV1 s,
        InventoryIdentityDefinitionV1 definition, bool current, CancellationToken ct)
    {
        Definition(definition); var sources = new List<InventoryIdentitySourceV1>();
        if (definition.Party?.Customer is {} customer)
        {
            var access = await root.SourceAsync(OperationsOwnerV1.BusinessPartner, ct);
            var value = await BusinessPartnerIdentityOwnerV1.ReadOwnedAsync(db, access, customer.Id, Revision(customer.Version), ct);
            if (value.Reference != customer) throw Error("INVENTORY_CUSTOMER_REFERENCE_INVALID");
            if (value.Projection != "FULL" || value.OriginalJson == null) throw Error("FIELD_READ_FORBIDDEN", 403);
            if (current && (!value.IsCurrent || !value.SourcePresent || !value.Source.GetProperty("customerFlg").GetBoolean()
                || value.Source.GetProperty("isDeleted").GetBoolean() || value.Source.GetProperty("isFrozen").GetBoolean())) throw Error("INVENTORY_CUSTOMER_UNAVAILABLE", 412);
            sources.Add(new("customer", new(customer.Owner, customer.Id, customer.Version, customer.Digest), value.OriginalJson));
        }
        if (definition.Product is not {} p) return sources.ToArray();
        var products = await root.SourceAsync(OperationsOwnerV1.Product, ct);
        var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, products, p.Product.Id, Revision(p.Product.Version), ct);
        if (product.Version != p.Product.Version || product.Digest != p.Product.Digest) throw Error("INVENTORY_PRODUCT_REFERENCE_INVALID");
        if (current && (!product.IsCurrent || !product.SourcePresent || product.Source.GetProperty("isDeleted").GetBoolean())) throw Error("INVENTORY_PRODUCT_CHANGED", 412);
        products.AtCommit(async t => { if (!await products.Products(true).AnyAsync(x => x.Id == p.Product.Id, t)) throw Error("OBJECT_FORBIDDEN", 403); });
        sources.Add(new("product", p.Product, product.OriginalJson));
        var units = new UomProfileStoreV1(db); await units.Fence(false, ct);
        var binding = await units.ReadProduct(p.Product.Id, Revision(p.UnitBinding.Version), ct);
        if (binding.Digest != p.UnitBinding.Digest || binding.Value.ProductVersion != p.Product.Version || binding.Value.ProductDigest != p.Product.Digest)
            throw Error("INVENTORY_PRODUCT_UNIT_MISMATCH");
        if (current && (!binding.Latest || binding.Value.UnitCode != product.Source.GetProperty("qtyUnitCode").GetString())) throw Error("INVENTORY_PRODUCT_UNIT_CHANGED", 412);
        sources.Add(new("unitBinding", p.UnitBinding, binding.OriginalJson));
        var uom = await root.SourceAsync(OperationsOwnerV1.UomProfile, ct);
        async Task<UomProfileVersionV1> Unit(UomProfileRefV1 r)
        {
            await uom.RequireUomProfile(r.Id, ct); uom.AtCommit(t => uom.RequireUomProfile(r.Id, t));
            var v = await units.Read(r.Id, Revision(r.Version), ct);
            if (v.Reference != r) throw Error("INVENTORY_UOM_REFERENCE_INVALID");
            if (current && (!v.IsCurrent || v.Profile.Enabled != true)) throw Error("INVENTORY_UOM_CHANGED", 412);
            return v;
        }
        var profile = await Unit(binding.Value.ProfileRef); var baseline = profile.Profile.Mode == "BASE" ? profile : await Unit(profile.Profile.BaseRef!);
        if (baseline.Profile.Mode != "BASE" || baseline.Profile.DimensionKey != profile.Profile.DimensionKey) throw Error("INVENTORY_UOM_BASE_INVALID");
        sources.Add(new("uomProfile", new(profile.Reference.Owner, profile.Reference.Id, profile.Reference.Version, profile.Reference.Digest), profile.OriginalJson));
        sources.Add(new("baseUom", new(baseline.Reference.Owner, baseline.Reference.Id, baseline.Reference.Version, baseline.Reference.Digest), baseline.OriginalJson));
        var engineering = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct); var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        var mapping = current ? await EngineeringProductMappingService.CurrentExact(engineering, store, p.Mapping, ct) : await store.ReadMapping(p.Mapping.Id, Revision(p.Mapping.Version), ct);
        if (mapping.Reference != p.Mapping || mapping.Candidate.BusinessRef != p.Product || mapping.State != "CONFIRMED") throw Error("INVENTORY_MAPPING_MISMATCH");
        await engineering.RequireEngineeringProduct(mapping.Candidate.EngineeringRef.Id, ct); engineering.AtCommit(t => engineering.RequireEngineeringProduct(mapping.Candidate.EngineeringRef.Id, t));
        if (!current && mapping.Candidate.SiteRef != null) (await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct)).RequirePhysicalSiteSourceAccess(db);
        sources.Add(new("mapping", new(p.Mapping.Owner, p.Mapping.Id, p.Mapping.Version, p.Mapping.Digest), mapping.OriginalJson));
        if (p.Baseline is {} baselineRef)
        {
            var frozen = await baselines.Source(root, baselineRef, current, ct);
            if (frozen.Original.Revision.Draft.ProductRef != mapping.Candidate.EngineeringRef) throw Error("INVENTORY_BASELINE_PRODUCT_MISMATCH");
            sources.Add(new("technicalBaseline", baselineRef, frozen.OriginalJson));
        }
        var party = await Exact(root, s, p.Party, "PARTY", current, ct); var purpose = await Exact(root, s, p.Purpose, "PURPOSE", current, ct);
        sources.Add(new("party", AsSource(p.Party), party.OriginalJson)); sources.Add(new("purpose", AsSource(p.Purpose), purpose.OriginalJson));
        return sources.ToArray();
    }
    private async Task<EngineeringOwnerConfirmationV1[]> Confirmations(OperationsAccessV1 a, InventoryIdentityDefinitionV1 d, CancellationToken ct)
    {
        var confirmations = new List<EngineeringOwnerConfirmationV1> { a.EngineeringConfirmation(OperationsOwnerV1.InventoryIdentity, "approve", Owner) };
        if (d.Product is {} p)
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct); var mapping = await store.ReadMapping(p.Mapping.Id, Revision(p.Mapping.Version), ct);
            var erp = await a.ActionAsync(OperationsOwnerV1.Product, "confirm-inventory", ct);
            var plm = await a.ActionAsync(OperationsOwnerV1.EngineeringProduct, "confirm-inventory", ct);
            async Task Objects(CancellationToken t)
            {
                if (!await erp.Products().AnyAsync(x => x.Id == p.Product.Id, t)) throw Error("OBJECT_FORBIDDEN", 403);
                await plm.RequireEngineeringProduct(mapping.Candidate.EngineeringRef.Id, t);
            }
            await Objects(ct); a.AtCommit(Objects);
            confirmations.Add(erp.EngineeringConfirmation(OperationsOwnerV1.Product, "confirm-inventory", EngineeringProductValuesV1.BusinessOwner));
            confirmations.Add(plm.EngineeringConfirmation(OperationsOwnerV1.EngineeringProduct, "confirm-inventory", EngineeringProductValuesV1.Owner));
        }
        return confirmations.ToArray();
    }
    private async Task Decision(OperationsAccessV1 root, InventoryIdentityViewV1 v, CancellationToken ct)
    {
        var o = v.Original; var p = o.Definition.Product;
        var expected = new List<(string Resource, string Action, string Owner, Guid Id)>
        { (OperationsOwnerV1.InventoryIdentity, "approve", Owner, o.Id) };
        if (p != null)
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct); var mapping = await store.ReadMapping(p.Mapping.Id, Revision(p.Mapping.Version), ct);
            expected.Add((OperationsOwnerV1.Product, "confirm-inventory", EngineeringProductValuesV1.BusinessOwner, p.Product.Id));
            expected.Add((OperationsOwnerV1.EngineeringProduct, "confirm-inventory", EngineeringProductValuesV1.Owner, mapping.Candidate.EngineeringRef.Id));
        }
        if (o.AuthorId == o.ActorId || o.Confirmations.Length != expected.Count) throw Error("INVENTORY_IDENTITY_APPROVAL_ORIGINAL_INVALID", 503);
        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i]; var c = o.Confirmations[i];
            if (c.Resource != e.Resource || c.Action != e.Action || c.Owner != e.Owner || c.ActorId != o.ActorId) throw Error("INVENTORY_IDENTITY_APPROVAL_ORIGINAL_INVALID", 503);
            async Task Check(CancellationToken t)
            {
                // Original subject only; never accept a caller-supplied approving actor or add it to the caller authority list.
                if (!await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == c.ActorId && x.Enable, t))
                    throw Error("INVENTORY_IDENTITY_APPROVER_UNAVAILABLE", 403);
                var a = await Authority.InventoryIdentityDecisionAuthorityAsync(c.Resource, c.Action, c.ActorId, t);
                // Re-evaluate the real grant and object scope. An unrelated permission/mapping revision is not a revocation.
                var access = new OperationsAccessV1(db, owner, a);
                if (e.Resource == OperationsOwnerV1.InventoryIdentity) await access.RequireInventoryIdentity(Environment, e.Id, t);
                else if (e.Resource == OperationsOwnerV1.EngineeringProduct) await access.RequireEngineeringProduct(e.Id, t);
                else if (!await access.Products().AnyAsync(x => x.Id == e.Id, t)) throw Error("OBJECT_FORBIDDEN", 403);
            }
            await Check(ct); root.AtCommit(Check);
        }
    }
}

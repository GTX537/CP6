using System.Text;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Wms;
internal static class InventoryIdentityValuesV1
{
    internal const string Owner = "Main.Wms.InventoryIdentity.v1", Contract = "Main.Wms.InventoryIdentity.commands.v1";
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    internal static long Revision(string? value) => EngineeringProductValuesV1.Revision(value);
    internal static void Text(string? value, int limit)
    {
        if (!EngineeringProductValuesV1.ValidText(value, limit) || value != value!.Trim() || value != value.Normalize(NormalizationForm.FormC))
            throw Error("INVENTORY_IDENTITY_TEXT_INVALID", 400);
    }
    internal static void Reference(EngineeringSourceRefV1? value, string owner)
    {
        if (value == null || value.Owner != owner || value.Id == Guid.Empty || !EngineeringProductValuesV1.ValidDigest(value.Digest))
            throw Error("INVENTORY_IDENTITY_REFERENCE_INVALID", 400);
        Revision(value.Version);
    }
    internal static EngineeringSourceRefV1 Ref(InventoryIdentityRefV1 v) => new(v.Owner, v.Id, v.Version, v.Digest);
    internal static void Reference(InventoryIdentityRefV1? value)
    { if (value == null) throw Error("INVENTORY_IDENTITY_REFERENCE_INVALID", 400); Reference(Ref(value), Owner); }
    internal static void Definition(InventoryIdentityDefinitionV1 d)
    {
        if (d == null || d.Kind is not ("PARTY" or "PURPOSE" or "PRODUCT")) throw Error("INVENTORY_IDENTITY_KIND_INVALID", 400);
        Text(d.Code, 64); Text(d.Name, 200); Text(d.Basis, 4000);
        if ((d.Party != null ? 1 : 0) + (d.Purpose != null ? 1 : 0) + (d.Product != null ? 1 : 0) != 1)
            throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400);
        if (d.Kind == "PARTY")
        {
            var p = d.Party ?? throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400);
            if (p.Kind == "SELF")
            { if (p.Customer != null) throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400); Text(p.LegalName, 200); Text(p.Jurisdiction, 64); Text(p.RegistrationNumber, 100); }
            else if (p.Kind == "CUSTOMER")
            {
                if (p.LegalName != null || p.Jurisdiction != null || p.RegistrationNumber != null || p.Customer == null)
                    throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400);
                Reference(new(p.Customer.Owner, p.Customer.Id, p.Customer.Version, p.Customer.Digest), BusinessPartnerIdentityOwnerV1.Owner);
            }
            else throw Error("INVENTORY_IDENTITY_PARTY_INVALID", 400);
        }
        else if (d.Kind == "PURPOSE")
        {
            var p = d.Purpose ?? throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400);
            if (p.Category is not ("TRIAL" or "PRODUCTION" or "OTHER")) throw Error("INVENTORY_IDENTITY_PURPOSE_INVALID", 400);
            Text(p.Meaning, 2000);
        }
        else
        {
            var p = d.Product ?? throw Error("INVENTORY_IDENTITY_BODY_INVALID", 400);
            Reference(p.Product, EngineeringProductValuesV1.BusinessOwner); Reference(p.UnitBinding, "Main.Erp.ProductUnit.v1");
            if (p.UnitBinding.Id != p.Product.Id || p.Mapping == null) throw Error("INVENTORY_IDENTITY_PRODUCT_INVALID", 400);
            Reference(new(p.Mapping.Owner, p.Mapping.Id, p.Mapping.Version, p.Mapping.Digest), EngineeringProductValuesV1.MappingOwner);
            if (p.TechnicalKind == "KNOWN") { Reference(p.Baseline, "Main.Plm.EngineeringBaseline.v1"); }
            else if (p.TechnicalKind != "NOT_APPLICABLE" || p.Baseline != null) throw Error("INVENTORY_IDENTITY_TECHNICAL_INVALID", 400);
            Text(p.TechnicalBasis, 4000); Reference(p.Party); Reference(p.Purpose);
        }
    }
    internal static string StableKey(InventoryIdentityDefinitionV1 d) => d.Kind switch
    {
        "PARTY" => d.Party!.Kind == "SELF" ? "SELF:" + EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(new[] { d.Party.Jurisdiction, d.Party.RegistrationNumber })) : "CUSTOMER:" + d.Party.Customer!.Id,
        "PURPOSE" => d.Purpose!.Category,
        _ => d.Product!.Product.Id.ToString()
    };
}

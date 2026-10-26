using System.Globalization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Plm;
internal static class EngineeringProductValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringProduct.v1";
    internal const string MappingOwner = "Main.Plm.EngineeringMapping.v1";
    internal const string BusinessOwner = "Main.Erp.ProductIdentity.v1";
    internal const string SiteOwner = "Main.Space.PhysicalSiteIdentity.v1";
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    internal static long Revision(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 1 || revision.ToString(CultureInfo.InvariantCulture) != value) throw Error("ENGINEERING_VERSION_INVALID", 400);
        return revision;
    }
    internal static void Reference(EngineeringProductRefV1? reference)
    {
        if (reference == null || reference.Owner != Owner || reference.Id == Guid.Empty || !ValidDigest(reference.Digest)) throw Error("ENGINEERING_REFERENCE_INVALID", 400);
        Revision(reference.Version);
    }
    internal static bool ValidDigest(string? digest) => digest?.Length == 64 && digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool ValidText(string? value, int limit, bool lines = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(c => char.IsControl(c) && !(lines && c is '\r' or '\n' or '\t'))) return false;
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i]))) return false;
        return true;
    }
    internal static EngineeringProductInputV1 Validate(EngineeringProductInputV1 product)
    {
        if (!ValidText(product.Code, 256) || product.Code != product.Code.Trim()) throw Error("ENGINEERING_IDENTITY_INVALID", 400);
        if (!ValidText(product.Name, 512) || !ValidText(product.Category, 64)
            || !ValidText(product.Definition, 8000, true) || !ValidText(product.IdentificationSource, 8000, true)
            || product.Enabled == null) throw Error("ENGINEERING_DEFINITION_INCOMPLETE", 400);
        return product;
    }
}

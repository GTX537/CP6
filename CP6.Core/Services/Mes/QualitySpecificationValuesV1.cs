using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Mes;
internal static class QualitySpecificationValuesV1
{
    internal const string Owner = "Main.Quality.Specification.v1", Journal = "Main.Quality.Specification.operations.v1",
        Contract = "Main.Quality.Specification.commands.v1", ControlOwner = "Main.Quality.SpecificationControl.v1";
    internal static void Ref(EngineeringSourceRefV1? value, string expected = Owner)
    { if (value == null || value.Owner != expected || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("QUALITY_SPEC_REFERENCE_INVALID", 400); Revision(value.Version); }
    internal static void Text(string? value, int maximum = 8000)
    { if (!ValidText(value, maximum, true)) throw Error("QUALITY_SPEC_INPUT_INVALID", 400); }
    private static readonly Regex Decimal = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static (BigInteger Number, int Scale) Exact(string value)
    {
        if (value.Length is < 1 or > 2050 || !Decimal.IsMatch(value)) throw Error("QUALITY_SPEC_EXACT_VALUE_INVALID", 400);
        var dot = value.IndexOf('.'); var scale = dot < 0 ? 0 : value.Length - dot - 1;
        var integers = (dot < 0 ? value.Length : dot) - (value.StartsWith('-') ? 1 : 0);
        if (scale > 1024 || integers > 1024) throw Error("QUALITY_SPEC_EXACT_VALUE_INVALID", 400);
        return (BigInteger.Parse(value.Replace(".", ""), CultureInfo.InvariantCulture), scale);
    }
    private static int Compare(string left, string right)
    { var a = Exact(left); var b = Exact(right); return (a.Number * BigInteger.Pow(10, b.Scale)).CompareTo(b.Number * BigInteger.Pow(10, a.Scale)); }
    internal static IEnumerable<EngineeringSourceRefV1> Documents(QualitySpecificationBodyV1 b)
        => new[] { b.CompletenessDocument }.Concat(b.Requirements.SelectMany(r => new[] { r.StandardDocument, r.ApplicabilityBasis, r.WaiverBasis }.OfType<EngineeringSourceRefV1>())).Distinct();
    internal static void Body(QualitySpecificationBodyV1? b)
    {
        if (b == null || b.Requirements == null || b.Requirements.Length > 256 || b.Purposes == null || b.Purposes.Length is < 1 or > 100
            || b.Stage is not ("IQC" or "IPQC" or "FQC" or "TRIAL_INSPECTION")) throw Error("QUALITY_SPEC_INPUT_INVALID", 400);
        Text(b.Name, 512); Text(b.CompletenessStatement); Reference(b.Product); Ref(b.Site, SiteOwner);
        if (b.Purposes.Distinct(StringComparer.Ordinal).Count() != b.Purposes.Length) throw Error("QUALITY_SPEC_INPUT_INVALID", 400);
        foreach (var purpose in b.Purposes) Text(purpose, 256);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in b.Requirements)
        {
            if (r == null || !ids.Add(r.Id)) throw Error("QUALITY_REQUIREMENT_IDENTITY_INVALID", 400);
            Text(r.Id, 256); Text(r.Characteristic, 1000); Text(r.Method); Text(r.Definition);
            ControlledDocumentValuesV1.Ref(r.StandardDocument);
            if (r.Applicability is not ("UNKNOWN" or "APPLIES" or "NOT_APPLICABLE"))
                throw Error("QUALITY_REQUIREMENT_APPLICABILITY_INVALID", 400);
            if ((r.Applicability == "NOT_APPLICABLE") != (r.ApplicabilityBasis != null) || r.Waivable != (r.WaiverBasis != null))
                throw Error("QUALITY_REQUIREMENT_BASIS_REQUIRED", 400);
            foreach (var value in new[] { r.LowerLimit, r.UpperLimit, r.Target }) if (value != null) _ = Exact(value);
            if ((r.LowerLimit != null || r.UpperLimit != null || r.Target != null) && r.Unit == null) throw Error("QUALITY_REQUIREMENT_UNIT_REQUIRED", 400);
            if (r.LowerLimit != null && r.UpperLimit != null && Compare(r.LowerLimit, r.UpperLimit) > 0
                || r.Target != null && (r.LowerLimit != null && Compare(r.Target, r.LowerLimit) < 0 || r.UpperLimit != null && Compare(r.Target, r.UpperLimit) > 0))
                throw Error("QUALITY_REQUIREMENT_LIMIT_INVALID", 400);
            if (r.Unit != null) UomProfileValuesV1.Reference(r.Unit);
        }
        foreach (var reference in Documents(b)) ControlledDocumentValuesV1.Ref(reference);
    }
    internal static void Policy(QualityReviewPolicyInputV1? p)
    {
        if (p == null || p.State is not ("ENABLED" or "DISABLED") || p.Roles == null || p.Roles.Length is < 1 or > 100
            || p.Roles.Any(x => x == null) || p.EffectiveTo <= p.EffectiveFrom || p.Roles.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != p.Roles.Length)
            throw Error("QUALITY_REVIEW_POLICY_INVALID", 400);
        foreach (var r in p.Roles) { Text(r.Code, 256); if (r.RoleId <= 0 || r.MinimumApprovals is < 1 or > 100) throw Error("QUALITY_REVIEW_POLICY_INVALID", 400); }
    }
}

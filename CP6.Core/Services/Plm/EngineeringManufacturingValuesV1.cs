using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRoutingValuesV1;

namespace CP6.Core.Services.Plm;

internal static class EngineeringManufacturingValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringManufacturingDraft.v1";
    private static void Ids(Guid[]? ids)
    { if (ids == null || ids.Length > 1000 || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length) throw Error("MANUFACTURING_OCCURRENCES_INVALID", 400); }
    internal static void Configuration(EngineeringRevisionRefV1? value)
    {
        if (value == null) return;
        if (value.Owner != EngineeringRevisionValuesV1.ConfigurationOwner || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("MANUFACTURING_CONFIGURATION_INVALID", 400);
        Revision(value.Version);
    }
    internal static string[] Validate(EngineeringManufacturingBodyV1? body)
    {
        if (body == null || body.Mappings == null || body.Outputs == null || body.Mappings.Length > 1000 || body.Outputs.Length > 1000
            || body.RevisionOrigin == null || body.RevisionOrigin.Owner != EngineeringRevisionValuesV1.RevisionOwner
            || body.RevisionOrigin.Id == Guid.Empty || !ValidDigest(body.RevisionOrigin.Digest)) throw Error("MANUFACTURING_INPUT_INVALID", 400);
        Text(body.Name, 256); Revision(body.RevisionOrigin.Version); Reference(body.ProductRef); Configuration(body.ConfigurationRef);
        EngineeringBomValuesV1.Ref(body.EbomRef); EngineeringBomValuesV1.Ref(body.MbomRef); EngineeringRoutingValuesV1.Ref(body.RoutingRef);
        if (body.SiteRef is { } site)
        { if (site.Owner != SiteOwner || site.Id == Guid.Empty || !ValidDigest(site.Digest)) throw Error("MANUFACTURING_SITE_INVALID", 400); Revision(site.Version); }
        var ids = new HashSet<Guid>(); var missing = new List<string>();
        if (body.ConfigurationRef == null) missing.Add("CONFIGURATION_REQUIRED");
        if (body.SiteRef == null) missing.Add("SITE_REQUIRED");
        if (body.Outputs.Length == 0) missing.Add("OUTPUTS_REQUIRED");
        foreach (var map in body.Mappings)
        {
            if (map == null || map.MapId == Guid.Empty || !ids.Add(map.MapId) || map.Kind is not ("MAP" or "SUPPLEMENT" or "EXCLUDE")) throw Error("MANUFACTURING_MAPPING_INVALID", 400);
            Ids(map.EbomOccurrenceIds); Ids(map.MbomOccurrenceIds); Ids(map.OperationOccurrenceIds); Text(map.Reason, optional: true); Text(map.QuantityBasis, optional: true);
            if (map.Kind == "MAP" && (map.EbomOccurrenceIds.Length == 0 || map.MbomOccurrenceIds.Length == 0)
                || map.Kind == "SUPPLEMENT" && (map.EbomOccurrenceIds.Length != 0 || map.MbomOccurrenceIds.Length == 0)
                || map.Kind == "EXCLUDE" && (map.EbomOccurrenceIds.Length == 0 || map.MbomOccurrenceIds.Length != 0)) missing.Add("MAPPING_ENDPOINTS_REQUIRED:" + map.MapId);
            if (map.Reason == "") missing.Add("MAPPING_REASON_REQUIRED:" + map.MapId);
            if (map.QuantityBasis == "") missing.Add("MAPPING_QUANTITY_BASIS_REQUIRED:" + map.MapId);
            // A recorded explanation is not an approved exclusion, substitution or unit conversion.
            missing.Add("MAPPING_PROFESSIONAL_EVIDENCE_REQUIRED:" + map.MapId);
        }
        ids.Clear();
        foreach (var output in body.Outputs)
        {
            if (output == null || output.OutputOccurrenceId == Guid.Empty || !ids.Add(output.OutputOccurrenceId)
                || output.GeneratingOperationOccurrenceId == Guid.Empty || output.Role is not ("PRIMARY" or "CO_PRODUCT" or "BY_PRODUCT" or "INTERMEDIATE")
                || output.QuantityKind is not ("UNKNOWN" or "FIXED_RATIO" or "TARGET" or "RANGE" or "OBSERVATION")) throw Error("MANUFACTURING_OUTPUT_INVALID", 400);
            Text(output.Name, 256); Ids(output.InputOccurrenceIds); Ids(output.FromOutputOccurrenceIds); Configuration(output.ConfigurationRef);
            foreach (var text in new[] { output.QuantityBasis, output.SpecRequirement, output.RelationEvidence, output.NoInputBasis, output.DispositionRequirement }) Text(text, optional: true);
            if (output.ProductRef != null) Reference(output.ProductRef); else missing.Add("OUTPUT_PRODUCT_REQUIRED:" + output.OutputOccurrenceId);
            if (output.ConfigurationRef == null) missing.Add("OUTPUT_CONFIGURATION_REQUIRED:" + output.OutputOccurrenceId);
            else if (body.ConfigurationRef != output.ConfigurationRef) missing.Add("OUTPUT_CONFIGURATION_MISMATCH:" + output.OutputOccurrenceId);
            if (output.GeneratingOperationOccurrenceId == null) missing.Add("GENERATING_OPERATION_REQUIRED:" + output.OutputOccurrenceId);
            if (output.InputOccurrenceIds.Length == 0 && output.FromOutputOccurrenceIds.Length == 0 && output.NoInputBasis == "") missing.Add("NO_INPUT_BASIS_REQUIRED:" + output.OutputOccurrenceId);
            if (output.RelationEvidence == "") missing.Add("RELATION_BASIS_REQUIRED:" + output.OutputOccurrenceId);
            foreach (var number in new[] { output.Quantity, output.Minimum, output.Maximum, output.Denominator }) if (number != null) Number(number);
            if (output.QuantityKind == "UNKNOWN" || output.QuantityKind == "RANGE" && (output.Minimum == null || output.Maximum == null)
                || output.QuantityKind is "FIXED_RATIO" or "TARGET" or "OBSERVATION" && output.Quantity == null) missing.Add("QUANTITY_DEFINITION_REQUIRED:" + output.OutputOccurrenceId);
            if (output.Quantity != null && Number(output.Quantity).Value.Sign < 0 || output.Minimum != null && Number(output.Minimum).Value.Sign < 0
                || output.Maximum != null && Number(output.Maximum).Value.Sign < 0 || output.Denominator != null && Number(output.Denominator).Value.Sign <= 0)
                missing.Add("QUANTITY_BOUNDARY_INVALID:" + output.OutputOccurrenceId);
            if (output.Minimum != null && output.Maximum != null && Compare(output.Minimum, output.Maximum) > 0) missing.Add("QUANTITY_RANGE_REVERSED:" + output.OutputOccurrenceId);
            if (output.UnitRef != null) CP6.Core.Services.Erp.UomProfileValuesV1.Reference(output.UnitRef); else missing.Add("OUTPUT_UNIT_REQUIRED:" + output.OutputOccurrenceId);
            if (output.DenominatorUnitRef != null) CP6.Core.Services.Erp.UomProfileValuesV1.Reference(output.DenominatorUnitRef);
            if (output.QuantityKind == "FIXED_RATIO" && (output.Denominator == null || output.DenominatorUnitRef == null)) missing.Add("DENOMINATOR_REQUIRED:" + output.OutputOccurrenceId);
            if (output.QuantityBasis == "") missing.Add("QUANTITY_BASIS_REQUIRED:" + output.OutputOccurrenceId);
            missing.Add("OUTPUT_SPECIFICATION_PROOF_REQUIRED:" + output.OutputOccurrenceId);
            missing.Add("OUTPUT_RELATION_PROOF_REQUIRED:" + output.OutputOccurrenceId);
            missing.Add("QUANTITY_APPLICATION_REVIEW_REQUIRED:" + output.OutputOccurrenceId);
        }
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static int Compare(string x, string y)
    { var a = Number(x); var b = Number(y); return (a.Value * System.Numerics.BigInteger.Pow(10, b.Scale)).CompareTo(b.Value * System.Numerics.BigInteger.Pow(10, a.Scale)); }
}

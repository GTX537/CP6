using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRoutingValuesV1;

namespace CP6.Core.Services.Plm;

internal static class EngineeringBomValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringBom.v1";
    internal static void Ref(EngineeringSourceRefV1? value)
    {
        if (value == null || value.Owner != Owner || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("BOM_REFERENCE_INVALID", 400);
        Revision(value.Version);
    }
    internal static void Item(EngineeringSourceRefV1? value)
    {
        if (value == null || value.Owner is not (BusinessOwner or EngineeringProductValuesV1.Owner)
            || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("BOM_ITEM_REFERENCE_INVALID", 400);
        Revision(value.Version);
    }
    internal static string[] Validate(EngineeringBomBodyV1? body)
    {
        if (body == null || body.Occurrences == null || body.Occurrences.Length > 1000 || body.Kind is not ("EBOM" or "MBOM")
            || body.RevisionOrigin == null || body.RevisionOrigin.Owner != EngineeringRevisionValuesV1.RevisionOwner
            || body.RevisionOrigin.Id == Guid.Empty || !ValidDigest(body.RevisionOrigin.Digest)) throw Error("BOM_INPUT_INVALID", 400);
        Text(body.Name, 256); Reference(body.ProductRef); Revision(body.RevisionOrigin.Version);
        if (body.SiteRef is { } site)
        { if (site.Owner != SiteOwner || site.Id == Guid.Empty || !ValidDigest(site.Digest)) throw Error("BOM_SITE_INVALID", 400); Revision(site.Version); }
        if (body.RoutingRef != null) EngineeringRoutingValuesV1.Ref(body.RoutingRef);
        var missing = new List<string>(); var rows = new Dictionary<Guid, BomOccurrenceV1>();
        var orders = new HashSet<(Guid?, int)>(); var paths = new HashSet<string>(StringComparer.Ordinal);
        if (body.Occurrences.Length == 0) missing.Add("OCCURRENCES_REQUIRED");
        if (body.Kind == "MBOM")
        {
            if (body.SiteRef == null) missing.Add("SITE_REQUIRED");
            if (body.RoutingRef == null) missing.Add("ROUTING_REQUIRED");
            missing.Add("MANUFACTURING_MAPPING_REVIEW_REQUIRED");
        }
        foreach (var row in body.Occurrences)
        {
            if (row == null || row.OccurrenceId == Guid.Empty || !rows.TryAdd(row.OccurrenceId, row)
                || row.ParentOccurrenceId == Guid.Empty || row.LegacyRowId == Guid.Empty || row.RoutingOccurrenceId == Guid.Empty
                || row.SemanticOrder < 0 || !orders.Add((row.ParentOccurrenceId, row.SemanticOrder)) || !paths.Add(row.Path))
                throw Error("BOM_OCCURRENCE_INVALID", 400);
            Text(row.Name, 256); Text(row.Path, 2048); Text(row.QuantityBasis, optional: true); Text(row.LossBasis, optional: true);
            Text(row.ConfigurationCondition, optional: true); Text(row.SubstitutionRequirement, optional: true); Text(row.Note, 8000, true);
            EngineeringRevisionValuesV1.Applicability(row.Applicability, row.Reason);
            if (row.ItemRef != null) Item(row.ItemRef); else missing.Add("ITEM_REQUIRED:" + row.OccurrenceId);
            if (row.Quantity == null) missing.Add("QUANTITY_REQUIRED:" + row.OccurrenceId);
            else if (Number(row.Quantity).Value.Sign <= 0) missing.Add("QUANTITY_NONPOSITIVE:" + row.OccurrenceId);
            if (row.Loss != null)
            { if (Number(row.Loss).Value.Sign < 0) missing.Add("LOSS_NEGATIVE:" + row.OccurrenceId); if (row.LossBasis == "") missing.Add("LOSS_BASIS_REQUIRED:" + row.OccurrenceId); }
            if (row.UnitRef != null) CP6.Core.Services.Erp.UomProfileValuesV1.Reference(row.UnitRef);
            else missing.Add("UNIT_REQUIRED:" + row.OccurrenceId);
            if (row.QuantityBasis == "") missing.Add("QUANTITY_BASIS_REQUIRED:" + row.OccurrenceId);
            if (row.Applicability != "APPLIES" || row.ConfigurationCondition != "") missing.Add("APPLICABILITY_REVIEW_REQUIRED:" + row.OccurrenceId);
            if (row.SubstitutionRequirement != "") missing.Add("SUBSTITUTION_APPROVAL_REQUIRED:" + row.OccurrenceId);
            // A unit's existence alone is not an approved item conversion or an approved BOM usage rule.
            missing.Add("QUANTITY_APPLICATION_REVIEW_REQUIRED:" + row.OccurrenceId);
        }
        foreach (var row in body.Occurrences)
        {
            var seen = new HashSet<Guid>(); var items = new HashSet<(string, Guid)> { (EngineeringProductValuesV1.Owner, body.ProductRef.Id) };
            BomOccurrenceV1? cursor = row;
            while (cursor != null)
            {
                if (!seen.Add(cursor.OccurrenceId)) { missing.Add("STRUCTURE_CYCLE:" + row.OccurrenceId); break; }
                if (cursor.ItemRef is { } item && !items.Add((item.Owner, item.Id))) missing.Add("ITEM_SELF_CONTAINMENT:" + row.OccurrenceId);
                if (cursor.ParentOccurrenceId is not { } parent) break;
                if (!rows.TryGetValue(parent, out cursor)) missing.Add("PARENT_OCCURRENCE_MISSING:" + row.OccurrenceId);
            }
        }
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }
}

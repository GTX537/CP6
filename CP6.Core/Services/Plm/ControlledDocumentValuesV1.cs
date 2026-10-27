using System.Globalization;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal static class ControlledDocumentValuesV1
{
    internal const string Owner = "Main.Plm.ControlledDocument.v1";
    internal static void Text(string? value) => EngineeringRoutingValuesV1.Text(value);
    internal static void Ref(EngineeringSourceRefV1 value)
    {
        if (value == null || value.Owner != Owner) throw Error("DOCUMENT_REFERENCE_INVALID",400);
        Reference(new(EngineeringProductValuesV1.Owner,value.Id,value.Version,value.Digest));
    }
    internal static string[] Validate(ControlledDocumentBodyV1 body)
    {
        if (body == null || body.Source == null || body.Contents == null || body.Contents.Length is < 1 or > 256
            || body.RevisionOrigin == null || body.RevisionOrigin.Owner != EngineeringRevisionValuesV1.RevisionOwner
            || body.Source.Kind is not ("INTERNAL" or "EXTERNAL")) throw Error("DOCUMENT_INPUT_INVALID",400);
        Text(body.Name); Text(body.RevisionLabel); Reference(body.ProductRef);
        Reference(new(EngineeringProductValuesV1.Owner,body.RevisionOrigin.Id,body.RevisionOrigin.Version,body.RevisionOrigin.Digest));
        foreach (var text in new[] { body.Source.OwnerName,body.Source.Organization,body.Source.SourceVersion,body.Source.AcquisitionBasis,
            body.Source.AuthorityLanguage,body.Source.AuthorityBasis }) EngineeringRevisionValuesV1.Text(text,empty:true);
        if (body.Source.SourceDate != null && !DateOnly.TryParseExact(body.Source.SourceDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw Error("DOCUMENT_SOURCE_DATE_INVALID",400);
        var occurrences = new HashSet<Guid>(); var files = new HashSet<Guid>();
        foreach (var item in body.Contents)
        {
            if (item == null || item.OccurrenceId == Guid.Empty || !occurrences.Add(item.OccurrenceId) || item.File == null
                || !files.Add(item.File.Id) || item.Role is not ("SOURCE" or "CONTROLLED" or "PREVIEW" or "TRANSLATION")
                || item.File.Owner != "Main.File.Content.v1" || item.File.Version != "1" || item.File.HashAlgorithm != "SHA256"
                || item.File.ByteLength is < 1 or > 20971520 || string.IsNullOrWhiteSpace(item.File.MediaType)) throw Error("DOCUMENT_CONTENT_INVALID",400);
            Reference(new(EngineeringProductValuesV1.Owner,item.File.Id,item.File.Version,item.File.Digest));
            EngineeringRevisionValuesV1.Text(item.Language,empty:true); EngineeringRevisionValuesV1.Text(item.DerivationBasis,empty:true);
        }
        foreach (var item in body.Contents)
        {
            if (item.Role is "PREVIEW" or "TRANSLATION")
            {
                var source = body.Contents.SingleOrDefault(x => x.OccurrenceId == item.DerivedFromOccurrenceId);
                if (source == null || source.Role is not ("SOURCE" or "CONTROLLED") || source.File.Id == item.File.Id || string.IsNullOrWhiteSpace(item.DerivationBasis))
                    throw Error("DOCUMENT_DERIVATION_INVALID",400);
            }
            else if (item.DerivedFromOccurrenceId != null || item.DerivationBasis != "") throw Error("DOCUMENT_DERIVATION_INVALID",400);
        }
        var missing = new List<string> { "DOCUMENT_PROFESSIONAL_REVIEW_REQUIRED" };
        if (body.Source.OwnerName == "" || body.Source.Organization == "" || body.Source.SourceVersion == ""
            || body.Source.SourceDate == null || body.Source.AcquisitionBasis == "") missing.Add("SOURCE_EVIDENCE_INCOMPLETE");
        if (body.Source.AuthorityLanguage == "" || body.Source.AuthorityBasis == "") missing.Add("LANGUAGE_AUTHORITY_UNRESOLVED");
        else missing.Add("LANGUAGE_AUTHORITY_REVIEW_REQUIRED");
        if (body.Contents.Any(x => x.Language == "")) missing.Add("CONTENT_LANGUAGE_UNRESOLVED");
        return missing.ToArray();
    }
}

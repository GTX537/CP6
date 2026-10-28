using CP6.Core.Services.Mes;
using System.Text.RegularExpressions;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal static class EngineeringRevisionValuesV1
{
    internal const string RevisionOwner = "Main.Plm.EngineeringRevision.v1";
    internal const string CandidateOwner = "Main.Plm.EngineeringCandidateDraft.v1";
    internal const string ConfigurationOwner = "Main.Plm.EngineeringConfigurationDraft.v1";
    internal const string NumberingKey = "ENG_REV";
    private static readonly Regex DecimalText = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static bool ExactDecimal(string value)
    {
        if (value.Length is < 1 or > 2050 || !DecimalText.IsMatch(value)) return false;
        var negative = value[0] == '-'; var dot = value.IndexOf('.');
        var integerLength = (dot < 0 ? value.Length : dot) - (negative ? 1 : 0);
        return integerLength <= 1024 && (dot < 0 || value.Length - dot - 1 <= 1024)
            && !(negative && value.Skip(1).All(c => c is '0' or '.'));
    }
    internal static void Text(string? value, int limit = 8000, bool empty = false)
    { if (!(empty && value == "") && !ValidText(value, limit, true)) throw Error("ENGINEERING_WORKING_INPUT_INVALID", 400); }
    internal static void Origin(EngineeringRevisionOriginV1? value)
    {
        if (value == null || value.Type is not ("NEW_PRODUCT" or "CHANGE" or "LEGACY_REVIEW")) throw Error("ENGINEERING_ORIGIN_INVALID", 400);
        if (value.Type == "CHANGE") throw Error("ENGINEERING_PARENT_CHANGE_OWNER_REQUIRED", 503);
        if (value.Type == "LEGACY_REVIEW") throw Error("ENGINEERING_LEGACY_OWNER_REQUIRED", 503);
        if (value.ParentBaselineRef != null || value.ChangeRef != null || value.LegacyRef != null) throw Error("ENGINEERING_ORIGIN_INVALID", 400);
        Text(value.NoParentReason);
    }
    internal static void Applicability(string value, string reason)
    {
        if (value is not ("UNKNOWN" or "APPLIES" or "NOT_APPLICABLE")) throw Error("ENGINEERING_APPLICABILITY_INVALID", 400);
        Text(reason, empty: value != "NOT_APPLICABLE");
    }
    internal static EngineeringConfigurationDraftV1 Configuration(EngineeringConfigurationInputV1 input)
    {
        if (input.Options == null || input.Options.Length > 256) throw Error("ENGINEERING_CONFIGURATION_INVALID", 400);
        Applicability(input.Applicability, input.Reason);
        if (input.RuleRef != null) ConfigurationRuleValuesV1.Ref(input.RuleRef);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in input.Options)
        {
            if (option == null || !ValidText(option.Name, 256) || !names.Add(option.Name)) throw Error("ENGINEERING_CONFIGURATION_INVALID", 400);
            if (option.Type == "STRING" && option.Decimal == null && option.Boolean == null) Text(option.Text, empty: true);
            else if (option.Type == "BOOLEAN" && option.Text == null && option.Decimal == null && option.Boolean != null) { }
            else if (option.Type == "DECIMAL" && option.Text == null && option.Boolean == null && option.Decimal != null
                && ExactDecimal(option.Decimal)) { }
            else throw Error("ENGINEERING_CONFIGURATION_INVALID", 400);
        }
        return new(new(ConfigurationOwner, Guid.NewGuid(), "1", EngineeringRevisionStoreV1.Digest(EngineeringRevisionStoreV1.Encode(input))), input);
    }
    internal static void Component(EngineeringComponentDraftV1 value)
    {
        if (value.OccurrenceId == Guid.Empty || value.Kind is not ("PRODUCT" or "EBOM" or "MBOM" or "ROUTING" or "DOCUMENT" or "QUALITY_SPEC") || value.SemanticOrder < 0)
            throw Error("ENGINEERING_COMPONENT_INVALID", 400);
        Text(value.Path, 2048); Text(value.Note, empty: true); Applicability(value.Applicability, value.Reason);
        if (value.SourceRef is { } source)
        {
            if (value.Kind == "PRODUCT" && source.Owner == EngineeringProductValuesV1.Owner)
                Reference(new(source.Owner, source.Id, source.Version, source.Digest));
            else if (value.Kind == "QUALITY_SPEC" && source.Owner == QualitySpecificationValuesV1.Owner) QualitySpecificationValuesV1.Ref(source);
            else if (value.Kind == "DOCUMENT" && source.Owner == ControlledDocumentValuesV1.Owner) ControlledDocumentValuesV1.Ref(source);
            else if (value.Kind == "ROUTING" && source.Owner == EngineeringRoutingValuesV1.Owner) EngineeringRoutingValuesV1.Ref(source);
            else if (value.Kind is "EBOM" or "MBOM" && source.Owner == EngineeringBomValuesV1.Owner) EngineeringBomValuesV1.Ref(source);
            else throw Error("ENGINEERING_COMPONENT_SOURCE_OWNER_REQUIRED", 503);
        }
    }
    internal static EngineeringWorkingDraftV1 Patch(EngineeringWorkingDraftV1 original, EngineeringDraftPatchV1[]? patches)
    {
        if (patches == null || patches.Length is < 1 or > 256) throw Error("ENGINEERING_PATCH_REQUIRED", 400);
        var value = original; var components = original.Components.ToList(); var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patch in patches)
        {
            if (patch == null) throw Error("ENGINEERING_PATCH_INVALID", 400);
            var field = patch.Op switch { "UPSERT_COMPONENT" => "component:" + patch.Component?.OccurrenceId, "REMOVE_COMPONENT" => "component:" + patch.OccurrenceId, _ => patch.Op };
            if (!changed.Add(field)) throw Error("ENGINEERING_PATCH_DUPLICATE", 400);
            if (patch.Op == "SET_GOAL" && patch.ProductRef == null && patch.Configuration == null && patch.Component == null && patch.OccurrenceId == null)
            { Text(patch.Goal); value = value with { Goal = patch.Goal! }; }
            else if (patch.Op == "SET_PRODUCT" && patch.Goal == null && patch.Configuration == null && patch.Component == null && patch.OccurrenceId == null)
            {
                Reference(patch.ProductRef);
                if (original.ProductRef != null && original.ProductRef.Id != patch.ProductRef!.Id) throw Error("ENGINEERING_REVISION_PRODUCT_IMMUTABLE", 409);
                value = value with { ProductRef = patch.ProductRef };
            }
            else if (patch.Op == "SET_CONFIGURATION" && patch.Configuration != null && patch.Goal == null && patch.ProductRef == null && patch.Component == null && patch.OccurrenceId == null)
                value = value with { Configuration = Configuration(patch.Configuration) };
            else if (patch.Op == "CLEAR_CONFIGURATION" && patch.Configuration == null && patch.Goal == null && patch.ProductRef == null && patch.Component == null && patch.OccurrenceId == null)
                value = value with { Configuration = null };
            else if (patch.Op == "UPSERT_COMPONENT" && patch.Component != null && patch.Goal == null && patch.ProductRef == null && patch.Configuration == null && patch.OccurrenceId == null)
            {
                Component(patch.Component); var index = components.FindIndex(x => x.OccurrenceId == patch.Component.OccurrenceId);
                if (index < 0) components.Add(patch.Component); else components[index] = patch.Component;
            }
            else if (patch.Op == "REMOVE_COMPONENT" && patch.OccurrenceId is Guid id && id != Guid.Empty && patch.Component == null && patch.Goal == null && patch.ProductRef == null && patch.Configuration == null)
            { if (components.RemoveAll(x => x.OccurrenceId == id) != 1) throw Error("ENGINEERING_OCCURRENCE_NOT_FOUND", 409); }
            else throw Error("ENGINEERING_PATCH_INVALID", 400);
        }
        if (components.Count > 1000) throw Error("ENGINEERING_COMPONENT_LIMIT", 400);
        return value with { Components = components.ToArray() };
    }
}

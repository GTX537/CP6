namespace CP6.WebApi.Services;

// Explicit catalog evolution. Consent accepts its exact legacy domain or this domain;
// export/download require this complete domain. Registration grants no action or data scope.
internal static class InquiryPrivacyFieldCatalogV1
{
    internal const string Resource = "crm-privacy";
    internal static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[] { "consent", "exportArtifact" });
    internal static bool Matches(IEnumerable<string> fields, bool allowLegacyConsent = false)
    {
        var names = fields.Order(StringComparer.Ordinal).ToArray();
        return names.SequenceEqual(Fields.Order(StringComparer.Ordinal))
            || allowLegacyConsent && names.SequenceEqual(new[] { "consent" });
    }
}

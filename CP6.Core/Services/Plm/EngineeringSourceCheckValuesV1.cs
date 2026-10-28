using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;

internal static class EngineeringSourceCheckValuesV1
{
    internal const string Owner = "Main.Plm.EngineeringSourceCheck.v1";
    internal const string Contract = "Main.Plm.EngineeringSourceCheck.commands.v1";
    internal const string PolicyScope = "ENGINEERING_OPTIONS_AND_CHECKS_V1";
    internal static readonly string[] Kinds = ["EBOM", "MBOM", "ROUTING", "DOCUMENT", "QUALITY_SPEC"];
    internal static void Policy(EngineeringCheckPolicyV1 policy)
    {
        if (policy.Schema != "engineering-check-policy/1" || policy.Manufacturing is not ("INTERNAL" or "PURCHASED_FINISHED")
            || policy.ConfigurationApplicability is not ("APPLIES" or "NOT_APPLICABLE") || policy.Composition == null
            || policy.Composition.Length != Kinds.Length || policy.Composition.Any(x => x == null)
            || !policy.Composition.Select(x => x.Kind).Order(StringComparer.Ordinal).SequenceEqual(Kinds.Order(StringComparer.Ordinal)))
            throw Error("ENGINEERING_CHECK_POLICY_INVALID", 400);
        Text(policy.ProductCategory, 256); Text(policy.Reason);
        foreach (var item in policy.Composition)
        {
            Text(item.Reason);
            if (item.Applicability is not ("APPLIES" or "NOT_APPLICABLE")
                || (item.Applicability == "APPLIES" ? item.MinimumOccurrences is < 1 or > 1000 : item.MinimumOccurrences != 0)
                || item.Kind is "DOCUMENT" or "QUALITY_SPEC" && item.Applicability != "APPLIES"
                || policy.Manufacturing == "INTERNAL" && item.Kind is "EBOM" or "MBOM" or "ROUTING" && item.Applicability != "APPLIES")
                throw Error("ENGINEERING_CHECK_POLICY_BYPASS", 400);
        }
    }
    internal static EngineeringSourceRefV1 Ref(EngineeringRevisionRefV1 x) => new(x.Owner, x.Id, x.Version, x.Digest);
    internal static EngineeringSourceRefV1 Ref(EngineeringProductRefV1 x) => new(x.Owner, x.Id, x.Version, x.Digest);
    internal static string Digest<T>(T value) => EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(value));
    internal static string State(IEnumerable<EngineeringCheckItemV1> items)
    {
        var states = items.Select(x => x.State).ToArray();
        return states.Contains("BLOCK") ? "BLOCK" : states.Contains("UNKNOWN") ? "UNKNOWN" : "PASS";
    }
    // Versioned native schema implementing CD-ENG-01 semantic separation. It does not claim RFC8785 or the candidate example wire.
    internal static EngineeringCheckManifestV1 Manifest(EngineeringRevisionSnapshotV1 candidate)
    {
        var draft = candidate.Draft;
        var body = new
        {
            schema = "main-engineering-technical-manifest/1",
            product = draft.ProductRef,
            configuration = draft.Configuration == null ? null : new
            {
                reference = draft.Configuration.Reference, rule = draft.Configuration.Input.RuleRef,
                options = draft.Configuration.Input.Options.OrderBy(x => x.Name, CodePoints.Instance).ToArray(),
                applicability = draft.Configuration.Input.Applicability, reason = draft.Configuration.Input.Reason
            },
            components = draft.Components.OrderBy(x => x.Kind, CodePoints.Instance)
                .ThenBy(x => x.SourceRef?.Owner, CodePoints.Instance).ThenBy(x => x.SourceRef?.Id.ToString("D"), CodePoints.Instance)
                .ThenBy(x => x.SourceRef?.Version, CodePoints.Instance).ThenBy(x => x.OccurrenceId.ToString("D"), CodePoints.Instance)
                .Select(x => new { x.Kind, x.SourceRef, x.OccurrenceId, x.Path, x.SemanticOrder, x.Applicability, x.Reason }).ToArray()
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Canonical(writer, JsonSerializer.SerializeToElement(body, EngineeringProductStoreV1.Json));
        var bytes = stream.ToArray();
        return new("main-engineering-technical-manifest/1", "CD-ENG-01-Main/1", "SHA-256", Encoding.UTF8.GetString(bytes), EngineeringProductStoreV1.Digest(bytes));
    }
    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var field in value.EnumerateObject().OrderBy(x => x.Name, CodePoints.Instance))
                { writer.WritePropertyName(field.Name); Canonical(writer, field.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(writer, item); writer.WriteEndArray(); break;
            default: value.WriteTo(writer); break;
        }
    }
    private sealed class CodePoints : IComparer<string?>
    {
        internal static readonly CodePoints Instance = new();
        public int Compare(string? left, string? right)
        {
            if (left == null || right == null) return left == right ? 0 : left == null ? -1 : 1;
            var a = left.EnumerateRunes().GetEnumerator(); var b = right.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var x = a.MoveNext(); var y = b.MoveNext();
                if (!x || !y) return x == y ? 0 : x ? 1 : -1;
                var c = a.Current.Value.CompareTo(b.Current.Value); if (c != 0) return c;
            }
        }
    }
}

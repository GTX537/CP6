using System.Text.Json;

namespace CP6.Core.Services.Sys;

/// <summary>Reads the existing native field-history shape without returning its original bytes.</summary>
public static class FieldAuditChangesV1
{
    public sealed record Change(string Field, string? Old, string? New);
    public sealed record Summary(int? ChangeCount, string EvidenceStatus);
    public sealed record Projection(IReadOnlyList<Change> Changes, string EvidenceStatus);

    public static Summary Summarize(string json)
    {
        var changes = Parse(json);
        return changes == null ? new(null, "UNREADABLE") : new(changes.Count, "READABLE");
    }

    // Owner-approved non-PII subset of the current Machine query response, not a generic field grant.
    internal static Projection MachineStatus(string json)
    {
        var source = Parse(json);
        if (source == null) return new([], "UNREADABLE");
        var allowed = new List<Change>();
        foreach (var change in source)
        {
            if (change.Field is not ("Status" or "ActiveFlg")) continue;
            bool Valid(string? value) => value == null || (change.Field == "Status"
                ? value is "0" or "1" or "2" or "3" or "4"
                : value is "True" or "False");
            if (!Valid(change.Old) || !Valid(change.New)) return new([], "UNREADABLE");
            allowed.Add(change);
        }
        return new(allowed, allowed.Count == source.Count ? "READABLE" : "RESTRICTED");
    }

    private static List<Change>? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var changes = new List<Change>(); var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject())
                {
                    var key = property.Name switch
                    { "Field" or "field" => "field", "Old" or "old" => "old", "New" or "new" => "new", _ => "" };
                    if (key == "" || !values.TryAdd(key, property.Value)) return null;
                }
                if (values.Count != 3 || values["field"].ValueKind != JsonValueKind.String
                    || values["old"].ValueKind is not (JsonValueKind.Null or JsonValueKind.String)
                    || values["new"].ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
                var field = values["field"].GetString();
                if (string.IsNullOrWhiteSpace(field) || !fields.Add(field)) return null;
                changes.Add(new(field, values["old"].GetString(), values["new"].GetString()));
            }
            return changes;
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

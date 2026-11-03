using System.Text.Json;

namespace CP6.Core.Services.Wf;

public sealed record DefinitionComparisonVersionV2(Guid VersionId, int Version, string? NameSnapshot,
    DateTimeOffset PublishedAtUtc, Guid PublisherRef, string? SchemaDigest, string EvidenceState,
    FlowDependencyRefV2[] DependencyRefs, string Status = "Published");
public sealed record DefinitionChangeV2(string Path, string Category, string Change, string? BeforeJson, string? AfterJson);
public sealed record DefinitionComparisonV2(string Kind, string Key, DefinitionComparisonVersionV2 Left,
    DefinitionComparisonVersionV2 Right, string Scope, string[] OmittedFields, bool DependenciesVisible,
    int Page, int PageSize, int TotalChanges, bool HasMore, DefinitionChangeV2[] Changes);

internal static class DefinitionComparerV2
{
    internal const int PageSize = 200;
    // Inputs are immutable publications already validated by their authoring boundary.
    // Preserve number spellings as JSON text; never round-trip schema values through double.
    internal static DefinitionChangeV2[] Compare(string kind, Dictionary<string, string?> left,
        Dictionary<string, string?> right, FlowDependencyRefV2[] leftDependencies, FlowDependencyRefV2[] rightDependencies)
    {
        var changes = new List<DefinitionChangeV2>();
        foreach (var field in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(field, out var a); right.TryGetValue(field, out var b);
            if (a == b) continue;
            if (field == "SchemaJson" && a != null && b != null)
            {
                using var before = JsonDocument.Parse(a, new() { MaxDepth = int.MaxValue });
                using var after = JsonDocument.Parse(b, new() { MaxDepth = int.MaxValue });
                Diff(before.RootElement, after.RootElement, "/SchemaJson", "SCHEMA", kind, changes);
            }
            else changes.Add(new("/" + field, "METADATA", "CHANGED", JsonSerializer.Serialize(a), JsonSerializer.Serialize(b)));
        }
        var depsA = JsonSerializer.SerializeToElement(leftDependencies);
        var depsB = JsonSerializer.SerializeToElement(rightDependencies);
        Diff(depsA, depsB, "/DependencyRefs", "DEPENDENCY", kind, changes);
        return changes.ToArray();
    }
    private static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => EqualObjects(a, b),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength()
                && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equal(p.First, p.Second)),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            _ => true
        };
    }
    private static bool EqualObjects(JsonElement a, JsonElement b)
    {
        var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var left = a.EnumerateObject().ToArray();
        return left.Length == right.Count && left.All(p => right.TryGetValue(p.Name, out var other) && Equal(p.Value, other));
    }
    private static string Category(string property, string inherited) => property.ToLowerInvariant() switch
    {
        "type" => "TYPE",
        "rules" or "condition" or "priority" or "required" or "pattern" or "maxlength" or "minrows" or "maxrows" => "RULE",
        "stages" or "approvalmode" or "approverids" or "approverstrategy" or "approvermapkey" or "approvermapexpr" => "APPROVER",
        "from" or "to" or "start" => "ROUTING",
        _ when property.StartsWith("approver", StringComparison.OrdinalIgnoreCase) => "APPROVER",
        _ => inherited
    };
    private static void Diff(JsonElement a, JsonElement b, string path, string category, string kind, List<DefinitionChangeV2> changes)
    {
        if (Equal(a, b)) return;
        if (a.ValueKind == JsonValueKind.Object && b.ValueKind == JsonValueKind.Object)
        {
            var before = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            var after = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            foreach (var name in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
            {
                var hasA = before.TryGetValue(name, out var av); var hasB = after.TryGetValue(name, out var bv);
                var location = path + "/" + Escape(name); var childCategory = Category(name, category);
                if (!hasA || !hasB) changes.Add(new(location, childCategory, hasA ? "REMOVED" : "ADDED", hasA ? av.GetRawText() : null, hasB ? bv.GetRawText() : null));
                else Diff(av, bv, location, childCategory, kind, changes);
            }
            return;
        }
        var identity = path.ToLowerInvariant() switch
        {
            "/schemajson/nodes" when kind == "FLOW" => "id",
            "/schemajson/edges" when kind == "FLOW" => "id",
            "/schemajson/fields" when kind == "FORM" => "name",
            _ when kind == "FORM" && path.StartsWith("/SchemaJson/", StringComparison.Ordinal)
                && path.Split('/').Length == 5 && path.EndsWith("/columns", StringComparison.OrdinalIgnoreCase) => "name",
            _ => null
        };
        if (identity != null && a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array)
        {
            var before = Keyed(a, identity); var after = Keyed(b, identity);
            if (before != null && after != null)
            {
                var elementCategory = identity == "name" ? "FIELD" : path.EndsWith("/nodes", StringComparison.OrdinalIgnoreCase) ? "NODE" : "EDGE";
                if (!before.Keys.SequenceEqual(after.Keys)) changes.Add(new(path, "ORDER", "CHANGED", JsonSerializer.Serialize(before.Keys), JsonSerializer.Serialize(after.Keys)));
                foreach (var id in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
                {
                    var hasA = before.TryGetValue(id, out var av); var hasB = after.TryGetValue(id, out var bv);
                    var location = path + "/@" + Escape(id);
                    if (!hasA || !hasB) changes.Add(new(location, elementCategory, hasA ? "REMOVED" : "ADDED", hasA ? av.GetRawText() : null, hasB ? bv.GetRawText() : null));
                    else Diff(av, bv, location, elementCategory, kind, changes);
                }
                return;
            }
            // Legacy edges can lack IDs. Compare their whole ordered sequence, without invented matches.
            changes.Add(new(path, "UNKEYED_SEQUENCE", "CHANGED", a.GetRawText(), b.GetRawText())); return;
        }
        changes.Add(new(path, category, "CHANGED", a.GetRawText(), b.GetRawText()));
    }
    private static Dictionary<string, JsonElement>? Keyed(JsonElement array, string identity)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return null;
            var fields = item.EnumerateObject().Where(p => p.Name.Equals(identity, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (fields.Length != 1 || fields[0].Value.ValueKind != JsonValueKind.String) return null;
            var id = fields[0].Value.GetString()!;
            if (!result.TryAdd(id, item)) return null;
        }
        return result;
    }
}

using System.Text.Json;

internal static class TenantStorageInput
{
    internal static bool Unique(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count()
            == element.EnumerateObject().Count() && element.EnumerateObject().All(p => Unique(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().All(Unique),
        _ => true
    };
}

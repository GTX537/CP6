using System.Text;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// GitHub JSON is not CP6 canonical JSON. Preserve normal numbers and whitespace, reject ambiguity and excess.
internal static class GitHubApiJson
{
    internal static JsonElement Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > GitHubWirePolicy.MaximumBytes) throw GitHubWirePolicy.Error("github-json-size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw GitHubWirePolicy.Error("github-json");
            Check(document.RootElement);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw GitHubWirePolicy.Error("github-json"); }
    }

    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || names.Count > 256 || Encoding.UTF8.GetByteCount(property.Name) > 65536)
                    throw GitHubWirePolicy.Error("github-json");
                Check(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() > 4096) throw GitHubWirePolicy.Error("github-json");
            foreach (var item in element.EnumerateArray()) Check(item);
        }
        else if (element.ValueKind == JsonValueKind.String && Encoding.UTF8.GetByteCount(element.GetString()!) > 65536)
            throw GitHubWirePolicy.Error("github-json");
    }
}

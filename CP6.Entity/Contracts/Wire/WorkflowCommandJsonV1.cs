using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Entity.Contracts.Wire;

/// <summary>WFC-CJ-1 only. This must not replace any Owner's different canonical or raw digest.</summary>
public static class WorkflowCommandJsonV1
{
    public const string Version = "WFC-CJ-1";
    public static StrictJsonDocument Parse(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new JsonException("JSON_SIZE_LIMIT");
        var document = StrictJson.Parse(bytes);
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 8 });
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                {
                    var value = StrictJson.DecodeString(ref reader);
                    for (var i = 0; i < value.Length; i++)
                    {
                        if (char.IsHighSurrogate(value[i]))
                        { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw new JsonException("INVALID_UNICODE"); }
                        else if (char.IsLowSurrogate(value[i])) throw new JsonException("INVALID_UNICODE");
                    }
                }
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Count() > 500)
                throw new JsonException("JSON_PROPERTY_LIMIT");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    public static string Canonical(JsonElement value)
    {
        var text = new StringBuilder(); Write(value, text); return text.ToString();
    }
    public static string Digest(JsonElement body, string? expectedVersion)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" +
            "{\"body\":" + Canonical(body) + ",\"expectedVersion\":" +
            (expectedVersion is null ? "null" : Quote(expectedVersion)) + "}"))).ToLowerInvariant();
    public static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
            if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4"));
            else if (c is '"' or '\\') text.Append('\\').Append(c);
            else text.Append(c);
        return text.Append('"').ToString();
    }
    private static void Write(JsonElement value, StringBuilder text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                text.Append('{'); var first = true;
                foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { if (!first) text.Append(','); first = false; text.Append(Quote(p.Name)).Append(':'); Write(p.Value, text); }
                text.Append('}'); break;
            case JsonValueKind.Array:
                text.Append('['); var initial = true;
                foreach (var item in value.EnumerateArray()) { if (!initial) text.Append(','); initial = false; Write(item, text); }
                text.Append(']'); break;
            case JsonValueKind.String: text.Append(Quote(value.GetString()!)); break;
            default: text.Append(value.GetRawText()); break; // Preserve JSON number lexemes exactly.
        }
    }
}

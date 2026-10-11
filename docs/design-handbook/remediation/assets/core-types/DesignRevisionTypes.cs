// Design contract support only; not linked to the CP6 application or a deployment.
using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Cp6.DesignRevision;

[JsonConverter(typeof(NullOnlyConverter))]
public readonly record struct NullOnly;

public sealed class NullOnlyConverter : JsonConverter<NullOnly>
{
    public override bool HandleNull => true;
    public override NullOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Null) throw new JsonException("NULL_ONLY_REQUIRED");
        return default;
    }
    public override void Write(Utf8JsonWriter writer, NullOnly value, JsonSerializerOptions options) => writer.WriteNullValue();
}

// Equality for schema const/enum/uniqueItems, never a signing or Owner digest codec.
// Duplicate object property names must already have been rejected by the strict parser.
public static class JsonSemanticEquality
{
    public static string Key(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "O{" + string.Join(",", value.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Key(p.Value))) + "}",
        JsonValueKind.Array => "A[" + string.Join(",", value.EnumerateArray().Select(Key)) + "]",
        JsonValueKind.String => "S" + JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.Number => NumberKey(value.GetRawText()),
        JsonValueKind.True => "B1",
        JsonValueKind.False => "B0",
        JsonValueKind.Null => "Z",
        _ => throw new JsonException("UNDEFINED_JSON_VALUE")
    };

    private static string NumberKey(string raw)
    {
        var exponentIndex = raw.IndexOfAny(new[] { 'e', 'E' });
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(raw[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var coefficient = BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture);
        if (coefficient.IsZero) return "N0";
        while (coefficient % 10 == 0) { coefficient /= 10; exponent++; }
        return "N" + coefficient.ToString(CultureInfo.InvariantCulture) + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }
}

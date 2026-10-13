using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Entity.Contracts.Wire;

namespace CP6.Entity.Contracts.ErpCredit;

// Private interpreter for the keywords in the pinned native closure. It is not an arbitrary schema API.
internal sealed class NativeSchemaValidator(IReadOnlyDictionary<string, JsonElement> documents, bool current)
{
    public void ValidateRoot(string file, string root, JsonElement value)
    {
        Validate(value, documents[file].GetProperty("$defs").GetProperty(root), file);
        NativeCreditSemantics.Validate(root, value, current);
    }
    private bool Matches(JsonElement value, JsonElement schema, string file)
    {
        try { Validate(value, schema, file); return true; }
        catch (JsonException) { return false; }
    }
    private static void Require(bool condition, string code)
    { if (!condition) throw new JsonException(code); }

    private void Validate(JsonElement value, JsonElement schema, string file)
    {
        if (schema.ValueKind == JsonValueKind.True) return;
        if (schema.ValueKind == JsonValueKind.False) throw new JsonException("FALSE_SCHEMA");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var parts = reference.GetString()!.Split('#', 2);
            var next = parts[0].Length == 0 ? file : parts[0].StartsWith("./", StringComparison.Ordinal) ? parts[0][2..] : parts[0];
            Require(documents.TryGetValue(next, out var target), "SCHEMA_REF");
            var pointer = parts.Length == 2 ? parts[1] : "";
            foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                target = target.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            Validate(value, target, next);
            if (pointer.StartsWith("/$defs/", StringComparison.Ordinal)) NativeCreditSemantics.Validate(pointer[7..], value, current);
        }
        if (schema.TryGetProperty("oneOf", out var one)) Require(one.EnumerateArray().Count(s => Matches(value, s, file)) == 1, "ONE_OF");
        if (schema.TryGetProperty("allOf", out var all)) foreach (var part in all.EnumerateArray()) Validate(value, part, file);
        if (schema.TryGetProperty("if", out var condition))
        {
            var keyword = Matches(value, condition, file) ? "then" : "else";
            if (schema.TryGetProperty(keyword, out var branch)) Validate(value, branch, file);
        }
        if (schema.TryGetProperty("const", out var constant)) Require(JsonSemanticEquality.AreEqual(value, constant), "CONST");
        if (schema.TryGetProperty("enum", out var values)) Require(values.EnumerateArray().Any(v => JsonSemanticEquality.AreEqual(value, v)), "ENUM");
        if (schema.TryGetProperty("type", out var type))
        {
            var valid = type.GetString() switch
            {
                "null" => value.ValueKind == JsonValueKind.Null,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "string" => value.ValueKind == JsonValueKind.String,
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "integer" => value.ValueKind == JsonValueKind.Number && ExactInteger(value.GetRawText()) is not null,
                _ => throw new JsonException("UNSUPPORTED_SCHEMA_TYPE")
            };
            Require(valid, "TYPE");
        }
        // JSON Schema keywords also apply when a conditional branch omits type.
        if (value.ValueKind == JsonValueKind.Number)
        {
            var integer = ExactInteger(value.GetRawText());
            if (schema.TryGetProperty("minimum", out var minimum)) Require(integer is not null && integer >= minimum.GetInt64(), "MINIMUM");
            if (schema.TryGetProperty("maximum", out var maximum)) Require(integer is not null && integer <= maximum.GetInt64(), "MAXIMUM");
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            var length = text.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var min)) Require(length >= min.GetInt32(), "MIN_LENGTH");
            if (schema.TryGetProperty("maxLength", out var max)) Require(length <= max.GetInt32(), "MAX_LENGTH");
            if (schema.TryGetProperty("pattern", out var pattern))
            {
                var expression = pattern.GetString()!;
                var match = Regex.Match(text, expression, RegexOptions.CultureInvariant);
                Require(match.Success && (!expression.EndsWith('$') || match.Index + match.Length == text.Length), "PATTERN");
            }
            if (schema.TryGetProperty("x-cp6-normalization", out _)) Require(text.IsNormalized(NormalizationForm.FormC), "NFC");
            if (schema.TryGetProperty("contentEncoding", out var encoding) && encoding.GetString() == "base64") DecodeBase64(text);
            if (schema.TryGetProperty("format", out var format)) ValidateFormat(text, format.GetString()!);
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var elements = value.EnumerateArray().ToArray();
            if (schema.TryGetProperty("minItems", out var min)) Require(elements.Length >= min.GetInt32(), "MIN_ITEMS");
            if (schema.TryGetProperty("maxItems", out var max)) Require(elements.Length <= max.GetInt32(), "MAX_ITEMS");
            if (schema.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean())
                Require(elements.Select(JsonSemanticEquality.Key).Distinct(StringComparer.Ordinal).Count() == elements.Length, "UNIQUE_ITEMS");
            if (schema.TryGetProperty("items", out var items)) foreach (var item in elements) Validate(item, items, file);
            if (schema.TryGetProperty("contains", out var contains)) Require(elements.Any(item => Matches(item, contains, file)), "CONTAINS");
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required)) foreach (var name in required.EnumerateArray())
                Require(value.TryGetProperty(name.GetString()!, out _), "REQUIRED");
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            foreach (var property in value.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out var child)) Validate(property.Value, child, file);
                else if (schema.TryGetProperty("additionalProperties", out var additional))
                {
                    Require(additional.ValueKind != JsonValueKind.False, "UNKNOWN_MEMBER");
                    if (additional.ValueKind == JsonValueKind.Object) Validate(property.Value, additional, file);
                }
            }
        }
    }

    // Every number in the published closure is an integer with a maximum <= 86400.
    // Lexical normalization occurs before conversion; fractions cannot round into integers.
    internal static BigInteger? ExactInteger(string raw)
    {
        var e = raw.IndexOfAny(['e', 'E']);
        var mantissa = e < 0 ? raw : raw[..e];
        var exponent = e < 0 ? BigInteger.Zero : BigInteger.Parse(raw[(e + 1)..], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var negative = mantissa[0] == '-';
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-').TrimStart('0');
        if (digits.Length == 0) return BigInteger.Zero;
        var trimmed = digits.TrimEnd('0'); exponent += digits.Length - trimmed.Length;
        if (exponent < 0 || exponent + trimmed.Length > 19) return null;
        return (negative ? -1 : 1) * BigInteger.Parse(trimmed, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)exponent);
    }
    internal static byte[] DecodeBase64(string text)
    {
        Require(Regex.IsMatch(text, @"\A(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?\z", RegexOptions.CultureInvariant), "BASE64");
        try { return Convert.FromBase64String(text); }
        catch (FormatException error) { throw new JsonException("BASE64", error); }
    }
    private static void ValidateFormat(string text, string format)
    {
        if (format == "uuid") Require(Guid.TryParseExact(text, "D", out _), "UUID");
        if (format == "date") Require(DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "DATE");
        if (format != "date-time") return;
        var match = Regex.Match(text, @"\A(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(Z|[+-](\d{2}):(\d{2}))\z", RegexOptions.CultureInvariant);
        Require(match.Success, "DATE_TIME");
        Require(DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) <= 23 && int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) <= 59 &&
            int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) <= 59 && (match.Groups[5].Value == "Z" ||
            (int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture) <= 23 && int.Parse(match.Groups[7].Value, CultureInfo.InvariantCulture) <= 59)), "DATE_TIME");
    }
}

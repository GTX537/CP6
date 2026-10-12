using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CP6.Entity.Contracts.Wire;

/// <summary>Exact JSON Schema const/enum/uniqueItems equality, not a signing/digest/canonicalization codec.</summary>
public static class JsonSemanticEquality
{
    /// <summary>Creates an opaque in-process equality key. The format is not a versioned wire/storage contract.</summary>
    /// <remarks>Traversal is iterative. Keys require memory proportional to content, with additional
    /// copying for nested containers; callers must budget resources for their accepted inputs.</remarks>
    public static string Key(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new JsonException("UNDEFINED_JSON_VALUE");
        var utf8 = Encoding.UTF8.GetBytes(value.GetRawText());
        // External JsonElements may not originate from our boundary. Never assign keys to ambiguous objects.
        StrictJson.Validate(utf8);
        var reader = new Utf8JsonReader(utf8, StrictJson.ReaderOptions);
        var containers = new Stack<Container>();
        string? result = null;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    containers.Push(new Container(reader.TokenType == JsonTokenType.StartObject));
                    continue;
                case JsonTokenType.PropertyName:
                    containers.Peek().Name = StrictJson.DecodeString(ref reader);
                    continue;
            }

            var key = reader.TokenType switch
            {
                JsonTokenType.EndObject or JsonTokenType.EndArray => containers.Pop().Key(),
                JsonTokenType.String => Frame('S', StrictJson.DecodeString(ref reader)),
                JsonTokenType.Number => Frame('N', NumberKey(Encoding.UTF8.GetString(reader.ValueSpan))),
                JsonTokenType.True => "T",
                JsonTokenType.False => "F",
                JsonTokenType.Null => "Z",
                _ => throw new JsonException("UNEXPECTED_JSON_TOKEN")
            };
            if (containers.Count == 0) result = key;
            else containers.Peek().Add(key);
        }
        return result!;
    }

    public static bool AreEqual(JsonElement left, JsonElement right) =>
        string.Equals(Key(left), Key(right), StringComparison.Ordinal);

    // Length frames preserve every UTF-16 code unit, including lone surrogates and delimiters.
    private static string Frame(char kind, string text) =>
        kind + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;

    private static string NumberKey(string raw)
    {
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(raw.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;

        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var negative = digits[0] == '-';
        var first = negative ? 1 : 0;
        while (first < digits.Length && digits[first] == '0') first++;
        if (first == digits.Length) return "0";
        var end = digits.Length;
        while (digits[end - 1] == '0') end--;
        exponent += digits.Length - end;
        // The significant coefficient remains exact decimal digits; only exponent arithmetic is needed.
        // Never expand 10^exponent or round through decimal/double.
        return (negative ? "-" : "") + digits[first..end] + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class Container(bool isObject)
    {
        private readonly List<(string Name, string Value)> children = [];
        public string Name { get; set; } = "";

        public void Add(string key) => children.Add((Name, key));

        public string Key()
        {
            if (isObject) children.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            var content = new StringBuilder();
            foreach (var (name, key) in children)
            {
                if (isObject) content.Append(Frame('P', name));
                content.Append(key);
            }
            return Frame(isObject ? 'O' : 'A', content.ToString());
        }
    }
}

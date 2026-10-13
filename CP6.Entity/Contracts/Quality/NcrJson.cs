using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Entity.Contracts.Wire;

namespace CP6.Entity.Contracts.Quality;

/// <summary>Strict UTF-8 entry for the explicitly adopted NCR native contract types.</summary>
/// <remarks>
/// Validates the complete pinned native schema, including nested unions. This does not
/// establish Owner currentness, authority, cross-object semantics or business acceptance.
/// Keep a StrictJsonDocument's original bytes separately for digest/replay operations;
/// reserialization preserves JSON values, not the original byte spelling.
/// </remarks>
public static class NcrJson
{
    public static T Deserialize<T>(ReadOnlySpan<byte> utf8) where T : class => (T)Deserialize(typeof(T), utf8);

    public static object Deserialize(Type type, ReadOnlySpan<byte> utf8)
    {
        using var document = StrictJson.Parse(utf8);
        return NativeContractRuntime.Read(type, document.RootElement);
    }

    public static byte[] Serialize<T>(T value) where T : class => NativeContractRuntime.Write(typeof(T), value);
}

/// <summary>Also protects ordinary System.Text.Json access to the adopted records.</summary>
public sealed class NativeContractConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => NativeContractRuntime.HasRoute(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(NativeConverter<>).MakeGenericType(typeToConvert), nonPublic: true)!;

    private sealed class NativeConverter<T> : JsonConverter<T> where T : class
    {
        public override bool HandleNull => true;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            // ParseValue retains raw nested properties. Validate before decoding or choosing a branch.
            return NcrJson.Deserialize<T>(Encoding.UTF8.GetBytes(document.RootElement.GetRawText()));
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteRawValue(NativeContractRuntime.Write(typeof(T), value), skipInputValidation: false);
    }
}

internal static class NativeContractRuntime
{
    private static readonly JsonElement Catalog = JsonDocument.Parse(NativeContractCatalog.Json).RootElement;
    private static readonly JsonElement Routes = Catalog.GetProperty("routes");
    private static readonly JsonElement Schemas = Catalog.GetProperty("schemas");
    private static readonly JsonElement Unions = Catalog.GetProperty("unions");
    private static readonly JsonElement Wrappers = Catalog.GetProperty("wrappers");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, string Name)[]> Properties = new();

    internal static bool HasRoute(Type type) => type.FullName is not null && Routes.TryGetProperty(type.FullName, out _);
    private static Type NativeType(string name) => typeof(NcrJson).Assembly.GetType(name, throwOnError: true)!;
    private static JsonElement Schema(Type type)
    {
        if (!HasRoute(type)) throw new JsonException("UNSUPPORTED_NATIVE_CONTRACT_TYPE");
        return Schemas.GetProperty(Routes.GetProperty(type.FullName!).GetString()!);
    }

    private static (PropertyInfo Property, string Name)[] Fields(Type type) => Properties.GetOrAdd(type, t =>
        t.GetProperties().Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
            .Select(p => (p, p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)).ToArray());

    internal static object Read(Type type, JsonElement value)
    {
        try
        {
            Validate(Schema(type), value, "$", true);
            return Materialize(type, value)!;
        }
        catch (InvalidOperationException error) { throw new JsonException("INVALID_NATIVE_JSON_VALUE", error); }
    }

    private static object? Materialize(Type type, JsonElement value)
    {
        if (type == typeof(NullOnly)) return JsonSerializer.Deserialize<NullOnly>(value.GetRawText());
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (type == typeof(string)) return value.GetString();
        if (type == typeof(bool)) return value.GetBoolean();
        if (type == typeof(long)) return ExactNumber.Parse(value).ToInt64();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var itemType = type.GetGenericArguments()[0];
            var items = Array.CreateInstance(itemType, value.GetArrayLength());
            var i = 0;
            foreach (var item in value.EnumerateArray()) items.SetValue(Materialize(itemType, item), i++);
            return items;
        }
        if (Unions.TryGetProperty(type.FullName!, out var alternatives))
        {
            var matches = alternatives.EnumerateArray().Select(a => NativeType(a.GetString()!))
                .Where(t => Validate(Schema(t), value, "$", false)).ToArray();
            if (matches.Length != 1) throw new JsonException("NATIVE_UNION_REQUIRES_EXACTLY_ONE_BRANCH");
            return Materialize(matches[0], value);
        }
        if (Wrappers.TryGetProperty(type.FullName!, out var payload))
            return Activator.CreateInstance(type, Materialize(NativeType(payload.GetString()!), value));
        var result = Activator.CreateInstance(type)!;
        foreach (var field in Fields(type))
            if (field.Property.SetMethod is not null)
                field.Property.SetValue(result, Materialize(field.Property.PropertyType, value.GetProperty(field.Name)));
        return result;
    }

    internal static byte[] Write(Type declaredType, object? value)
    {
        var schema = Schema(declaredType);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Emit(writer, declaredType, value);
        var bytes = stream.ToArray();
        using var document = StrictJson.Parse(bytes);
        Validate(schema, document.RootElement, "$", true);
        return bytes;
    }

    private static void Emit(Utf8JsonWriter writer, Type declaredType, object? value)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        if (declaredType == typeof(NullOnly)) { JsonSerializer.Serialize(writer, (NullOnly)value); return; }
        if (declaredType == typeof(string))
        {
            try { StrictUtf8.GetByteCount((string)value); }
            catch (EncoderFallbackException error) { throw new JsonException("INVALID_NATIVE_UNICODE", error); }
            writer.WriteStringValue((string)value);
            return;
        }
        if (declaredType == typeof(bool)) { writer.WriteBooleanValue((bool)value); return; }
        if (declaredType == typeof(long)) { writer.WriteNumberValue((long)value); return; }
        if (declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            writer.WriteStartArray();
            foreach (var item in (IEnumerable)value) Emit(writer, declaredType.GetGenericArguments()[0], item);
            writer.WriteEndArray();
            return;
        }
        var type = value.GetType();
        if (!HasRoute(type) || !declaredType.IsAssignableFrom(type)) throw new JsonException("UNDECLARED_NATIVE_BRANCH");
        if (Unions.TryGetProperty(declaredType.FullName!, out var alternatives) &&
            !alternatives.EnumerateArray().Any(a => a.GetString() == type.FullName))
            throw new JsonException("UNDECLARED_NATIVE_BRANCH");
        using var nativeStream = new MemoryStream();
        using (var nativeWriter = new Utf8JsonWriter(nativeStream))
        {
            if (Wrappers.TryGetProperty(type.FullName!, out var payload))
                Emit(nativeWriter, NativeType(payload.GetString()!), type.GetProperty("Value")!.GetValue(value));
            else
            {
                nativeWriter.WriteStartObject();
                foreach (var field in Fields(type))
                {
                    nativeWriter.WritePropertyName(field.Name);
                    Emit(nativeWriter, field.Property.PropertyType, field.Property.GetValue(value));
                }
                nativeWriter.WriteEndObject();
            }
        }
        var nativeBytes = nativeStream.ToArray();
        using var nativeDocument = JsonDocument.Parse(nativeBytes);
        // Check the concrete CLR branch as well as the outer union. Otherwise an
        // instance of variant 0 could change its tuple and silently become variant 1.
        Validate(Schema(type), nativeDocument.RootElement, "$", true);
        writer.WriteRawValue(nativeBytes, skipInputValidation: false);
    }

    private static bool Validate(JsonElement schema, JsonElement value, string path, bool throwing)
    {
        bool Fail(string keyword)
        {
            if (throwing) throw new JsonException($"NATIVE_SCHEMA_{keyword}: {path}");
            return false;
        }
        if (schema.TryGetProperty("$ref", out var reference) &&
            !Validate(Schemas.GetProperty(reference.GetString()!), value, path, throwing)) return false;
        if (schema.TryGetProperty("const", out var constant) && !JsonSemanticEquality.AreEqual(constant, value)) return Fail("CONST");
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(c => JsonSemanticEquality.AreEqual(c, value))) return Fail("ENUM");
        if (schema.TryGetProperty("oneOf", out var branches) &&
            branches.EnumerateArray().Count(b => Validate(b, value, path, false)) != 1) return Fail("ONE_OF");
        if (schema.TryGetProperty("type", out var type))
        {
            var matches = type.GetString() switch
            {
                "null" => value.ValueKind == JsonValueKind.Null,
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => value.ValueKind == JsonValueKind.Number && ExactNumber.Parse(value).IsInteger,
                _ => throw new InvalidOperationException("Unimplemented keyword in generated native schema.")
            };
            if (!matches) return Fail("TYPE");
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!value.TryGetProperty(name.GetString()!, out _)) return Fail("REQUIRED");
            if (schema.TryGetProperty("properties", out var properties))
                foreach (var property in value.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out var child))
                    {
                        if (!Validate(child, property.Value, path + "/" + property.Name, throwing)) return false;
                    }
                    else if (schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.False)
                        return Fail("ADDITIONAL_PROPERTY");
                }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var count = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var min) && count < min.GetInt32()) return Fail("MIN_ITEMS");
            if (schema.TryGetProperty("maxItems", out var max) && count > max.GetInt32()) return Fail("MAX_ITEMS");
            if (schema.TryGetProperty("items", out var itemSchema))
            {
                var i = 0;
                foreach (var item in value.EnumerateArray())
                    if (!Validate(itemSchema, item, path + "/" + i++, throwing)) return false;
            }
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            var length = text.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32()) return Fail("MIN_LENGTH");
            if (schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32()) return Fail("MAX_LENGTH");
            if (schema.TryGetProperty("pattern", out var pattern) && !NativePatternMatches(text, pattern.GetString()!)) return Fail("PATTERN");
            if (schema.TryGetProperty("format", out var format) && !ValidFormat(format.GetString()!, text)) return Fail("FORMAT");
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (schema.TryGetProperty("minimum", out var min) && ExactNumber.Parse(value).CompareTo(ExactNumber.Parse(min)) < 0) return Fail("MINIMUM");
            if (schema.TryGetProperty("maximum", out var max) && ExactNumber.Parse(value).CompareTo(ExactNumber.Parse(max)) > 0) return Fail("MAXIMUM");
        }
        return true;
    }

    private static bool NativePatternMatches(string text, string sourcePattern)
    {
        // JSON Schema uses ECMAScript without the multiline flag: terminal $ means
        // absolute end. .NET keeps its before-final-LF behavior even with ECMAScript.
        // Generation checks that every $ in this closure is an unescaped terminal anchor.
        var pattern = sourcePattern.EndsWith('$') ? sourcePattern[..^1] + @"\z" : sourcePattern;
        return Regex.IsMatch(text, pattern, RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
    }

    private static bool ValidFormat(string format, string text)
    {
        if (format == "uuid") return Regex.IsMatch(text, @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);
        if (format != "date-time") throw new InvalidOperationException("Unsupported native string format.");
        var match = Regex.Match(text, @"\A(\d{4})-(\d{2})-(\d{2})[Tt](\d{2}):(\d{2}):(\d{2})(?:\.[0-9]+)?([Zz]|([+-])(\d{2}):(\d{2}))\z", RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        int Number(int i) => int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture);
        var year = Number(1); var month = Number(2); var day = Number(3);
        var hour = Number(4); var minute = Number(5); var second = Number(6);
        if (month is < 1 or > 12 || day < 1 || day > GregorianMonthDays(year, month) || hour > 23 || minute > 59 || second > 60) return false;
        var offsetHours = match.Groups[9].Success ? Number(9) : 0;
        var offsetMinutes = match.Groups[10].Success ? Number(10) : 0;
        if (offsetHours > 23 || offsetMinutes > 59) return false;
        if (second != 60) return true;
        // RFC3339 admits years 0000..9999 and an offset-shifted second 60 at UTC
        // month end. Arithmetic avoids DateTime's year/precision restrictions.
        // This validates a possible position; it does not consult IERS announcements.
        var signedOffset = (offsetHours * 60 + offsetMinutes) * (match.Groups[8].Value == "-" ? -1 : 1);
        var utcMinute = hour * 60 + minute - signedOffset;
        if (utcMinute < 0) { utcMinute += 1440; day--; }
        else if (utcMinute >= 1440) { utcMinute -= 1440; day++; }
        if (day < 1)
        {
            if (--month == 0) { month = 12; year--; }
            day = GregorianMonthDays(year, month);
        }
        else if (day > GregorianMonthDays(year, month))
        {
            day = 1;
            if (++month == 13) { month = 1; year++; }
        }
        return utcMinute == 1439 && day == GregorianMonthDays(year, month);
    }

    private static int GregorianMonthDays(int year, int month) => month switch
    {
        2 => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31
    };

    // Only bounded native JSON integers use this; quantity/revision strings are never coerced.
    private readonly record struct ExactNumber(int Sign, string Digits, BigInteger Exponent) : IComparable<ExactNumber>
    {
        public bool IsInteger => Exponent.Sign >= 0;
        public static ExactNumber Parse(JsonElement value)
        {
            var raw = value.GetRawText();
            var index = raw.IndexOfAny(['e', 'E']);
            var exponent = index < 0 ? BigInteger.Zero : BigInteger.Parse(raw[(index + 1)..], CultureInfo.InvariantCulture);
            var mantissa = index < 0 ? raw : raw[..index];
            var sign = mantissa.StartsWith('-') ? -1 : 1;
            var dot = mantissa.IndexOf('.');
            if (dot >= 0) exponent -= mantissa.Length - dot - 1;
            var digits = mantissa.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            if (digits.Length == 0) return new(0, "0", BigInteger.Zero);
            var trimmed = digits.TrimEnd('0');
            exponent += digits.Length - trimmed.Length;
            return new(sign, trimmed, exponent);
        }

        public int CompareTo(ExactNumber other)
        {
            if (Sign != other.Sign) return Sign.CompareTo(other.Sign);
            if (Sign == 0) return 0;
            var magnitude = (Digits.Length + Exponent).CompareTo(other.Digits.Length + other.Exponent);
            if (magnitude == 0)
            {
                var width = Math.Max(Digits.Length, other.Digits.Length);
                magnitude = StringComparer.Ordinal.Compare(Digits.PadRight(width, '0'), other.Digits.PadRight(width, '0'));
            }
            return Sign * magnitude;
        }

        public long ToInt64()
        {
            if (!IsInteger || Exponent > 18 || Digits.Length + Exponent > 19) throw new JsonException("NATIVE_INTEGER_OUT_OF_RANGE");
            try { return checked((long)(Sign * BigInteger.Parse(Digits, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)Exponent))); }
            catch (OverflowException error) { throw new JsonException("NATIVE_INTEGER_OUT_OF_RANGE", error); }
        }
    }
}

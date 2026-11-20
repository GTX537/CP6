using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Platform.Messaging.NativeContracts;

namespace CP6.Core.Services.ErpIntegration;

public sealed class CommercialProtocolException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>由可信 Owner 路由登记填充。传输层从认证上下文取得这些值。</summary>
public sealed record CommercialRouteV1(Guid TenantId, string Authority, string LegalEntityKey,
    string Producer, string Consumer, string Contract, string Epoch, string RouteVersion);

public sealed record CommercialSourceHandoffV1(Guid HandoffId, string RequestVersion, Guid IntentId, string IntentDigest,
    Guid OpportunityId, Guid AccountId, string RequirementVersion, string ForecastVersion);

public sealed record CommercialWireCoordinatesV1(string EventId, string BusinessKey, string Ordinal,
    CommercialSourceHandoffV1 Source, string BusinessDigest);

/// <summary>已按准确 Owner 合同校验的原字节。摘要供身份比对，安全许可由真实 Owner 提供。</summary>
public sealed class CommercialOriginalV1
{
    public const string Handoff = "CRM-HO-OPP/1";
    public const string Intake = "OPP-INTAKE/1";
    public const string Quote = "OPP-QUOTE/1";
    public const string Commercial = "OPP-COMMERCIAL/1";
    public const int MaximumBodyBytes = 1024 * 1024;
    public const string OwnerDesignSha256 = "bdc91e2093023cf77ff76fc77339f215eb3ffe9ec8c6510a04ca6f605de5530f";
    public const string IntegrationDesignSha256 = "5282d5545f8c9b1db11f649541ca35af8dd1d9df1264af8df6a4ed34f2fd0c63";

    private readonly Cp6OriginalJson original;
    private CommercialOriginalV1(CommercialRouteV1 route, Cp6OriginalJson original, CommercialWireCoordinatesV1 coordinates)
    {
        Route = route;
        this.original = original;
        Coordinates = coordinates;
    }

    public CommercialRouteV1 Route { get; }
    public CommercialWireCoordinatesV1 Coordinates { get; }
    public string BodyHash => original.Sha256;
    public byte[] OriginalBytes() => original.GetBytes();
    public static string Hash(ReadOnlySpan<byte> utf8) => Convert.ToHexString(SHA256.HashData(utf8)).ToLowerInvariant();

    public static CommercialOriginalV1 Capture(CommercialRouteV1 trustedRoute, string expectedBodyHash, ReadOnlyMemory<byte> body)
    {
        CommercialWireV1.RequireRoute(trustedRoute);
        if (body.Length is 0 or > MaximumBodyBytes) throw new CommercialProtocolException("CONTRACT_BODY_SIZE");
        // 先核冻结字节。后续验证使用持有的副本，始终不从 JSON 对象重新构造原件。
        var owned = body.ToArray();
        if (!CommercialWireV1.IsDigest(expectedBodyHash) || Hash(owned) != expectedBodyHash)
            throw new CommercialProtocolException("ORIGINAL_BODY_HASH_MISMATCH");
        if (owned.AsSpan().StartsWith([(byte)0xef, (byte)0xbb, (byte)0xbf]))
            throw new CommercialProtocolException("CONTRACT_INVALID");
        try
        {
            var captured = Cp6OriginalJson.Capture(owned);
            using var document = Cp6NativeJson.Parse(owned);
            return new(trustedRoute, captured, CommercialWireV1.Validate(trustedRoute, document.RootElement));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException and not CommercialProtocolException
            or DecoderFallbackException or KeyNotFoundException)
        {
            throw new CommercialProtocolException("CONTRACT_INVALID");
        }
    }

    /// <summary>恢复接口收到的是原字节副本，并对照原路由及原 BodyHash 重新核验。</summary>
    public CommercialOriginalV1 Reopen(ReadOnlyMemory<byte> restored) => Capture(Route, BodyHash, restored);

    public string PartitionKey()
    {
        var source = Coordinates.Source;
        var stream = Route.Contract switch
        {
            Intake => new[] { source.HandoffId.ToString("D"), source.RequestVersion },
            Handoff => new[] { source.HandoffId.ToString("D") },
            _ => new[] { Coordinates.BusinessKey }
        };
        return Hash(CommercialCanonicalV1.Encode(JsonSerializer.SerializeToElement(new object[]
        {
            Route.TenantId, Route.LegalEntityKey, Route.Authority, Route.Contract, stream
        })));
    }
}

internal static class CommercialCanonicalV1
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new MillisecondUtc() }
    };

    private sealed class MillisecondUtc : JsonConverter<DateTimeOffset>
    {
        private const string Pattern = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.TryParseExact(reader.GetString(), Pattern, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
                ? utc : throw new JsonException("IC_UTC_INVALID");

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString(Pattern, CultureInfo.InvariantCulture));
    }

    internal static string Digest(string domain, JsonElement value, bool removeProof = false,
        string[]? rootFields = null, string[]? omitRoot = null)
    {
        var payload = Encode(value, removeProof, rootFields, omitRoot);
        return CommercialOriginalV1.Hash([.. Encoding.UTF8.GetBytes(domain + "\n"), .. payload]);
    }

    internal static string Digest<T>(string domain, T value) =>
        Digest(domain, JsonSerializer.SerializeToElement(value, Json), removeProof: false);
    internal static byte[] Data<T>(T value) => Encode(JsonSerializer.SerializeToElement(value, Json));

    internal static byte[] Encode(JsonElement value, bool removeProof = false, string[]? rootFields = null, string[]? omitRoot = null)
    {
        var json = new StringBuilder();
        Write(json, value, removeProof, rootFields, omitRoot);
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void Write(StringBuilder json, JsonElement value, bool removeProof, string[]? fields = null, string[]? omit = null,
        string? collection = null)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                json.Append('{');
                var first = true;
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (removeProof && property.Name == "proofRef" || fields is not null && !fields.Contains(property.Name, StringComparer.Ordinal) ||
                        omit is not null && omit.Contains(property.Name, StringComparer.Ordinal)) continue;
                    if (!first) json.Append(',');
                    first = false;
                    String(json, property.Name);
                    json.Append(':');
                    Write(json, property.Value, removeProof, collection: property.Name);
                }
                json.Append('}');
                break;
            case JsonValueKind.Array:
                json.Append('[');
                var comma = false;
                foreach (var item in Collection(value, collection))
                {
                    if (comma) json.Append(',');
                    comma = true;
                    Write(json, item, removeProof);
                }
                json.Append(']');
                break;
            case JsonValueKind.String:
                String(json, value.GetString()!);
                break;
            case JsonValueKind.Number:
                if (!long.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                    throw new CommercialProtocolException("CONTRACT_INVALID");
                json.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True: json.Append("true"); break;
            case JsonValueKind.False: json.Append("false"); break;
            case JsonValueKind.Null: json.Append("null"); break;
            default: throw new CommercialProtocolException("CONTRACT_INVALID");
        }
    }

    private static IEnumerable<JsonElement> Collection(JsonElement array, string? field) => field switch
    {
        "orders" => array.EnumerateArray().OrderBy(item => item.GetProperty("orderKey").GetString(), StringComparer.Ordinal),
        "members" or "retirements" => array.EnumerateArray().OrderBy(item => item.GetProperty("allocationKey").GetString(), StringComparer.Ordinal),
        "coveredOrderKeys" or "affectedAllocationKeys" or "correctsResultKeys" =>
            array.EnumerateArray().OrderBy(item => item.GetString(), StringComparer.Ordinal),
        _ => array.EnumerateArray()
    };

    private static void String(StringBuilder json, string value)
    {
        json.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\t': json.Append("\\t"); break;
                case '\n': json.Append("\\n"); break;
                case '\f': json.Append("\\f"); break;
                case '\r': json.Append("\\r"); break;
                default:
                    if (c < ' ') json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else json.Append(c);
                    break;
            }
        }
        json.Append('"');
    }
}

internal static partial class CommercialWireV1
{
    private static CommercialProtocolException Invalid() => new("CONTRACT_INVALID");
    private static CommercialProtocolException TypeMismatch() => new("DOMAIN_TYPE_MISMATCH");
    private static CommercialProtocolException RouteMismatch() => new("RESULT_GENERATION_UNSUPPORTED");

    internal static void RequireRoute(CommercialRouteV1 route)
    {
        if (route is null || route.TenantId == Guid.Empty ||
            route.Contract is not (CommercialOriginalV1.Handoff or CommercialOriginalV1.Intake or CommercialOriginalV1.Quote or CommercialOriginalV1.Commercial))
            throw RouteMismatch();
        foreach (var coordinate in new[] { route.Authority, route.LegalEntityKey, route.Producer, route.Consumer, route.Epoch, route.RouteVersion })
            RequireKey(coordinate);
        if (route.Contract == CommercialOriginalV1.Handoff && route.Producer != "CRM_HO") throw RouteMismatch();
    }

    internal static CommercialWireCoordinatesV1 Validate(CommercialRouteV1 route, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("schemaVersion", out var version) ||
            Key(version) != route.Contract) throw RouteMismatch();
        if (Id(body.GetProperty("tenantId")) != route.TenantId || Key(body.GetProperty("producer")) != route.Producer)
            throw RouteMismatch();
        if (route.Contract == CommercialOriginalV1.Handoff) return Handoff(route, body);
        if (Key(body.GetProperty("epoch")) != route.Epoch) throw RouteMismatch();
        var ordinal = Sequence(body.GetProperty("sequence"));
        var eventId = Key(body.GetProperty("eventId"));
        return route.Contract switch
        {
            CommercialOriginalV1.Intake => Intake(route, body, eventId, ordinal),
            CommercialOriginalV1.Quote => Quote(route, body, eventId, ordinal),
            _ => Group(route, body, eventId, ordinal)
        };
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
            names.Any(name => !value.TryGetProperty(name, out _))) throw Invalid();
    }

    internal static string RequireKey(string? text, int max = 128)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            text.EnumerateRunes().Count() > max || text.EnumerateRunes().Any(rune => Rune.IsControl(rune))) throw TypeMismatch();
        return text;
    }

    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw TypeMismatch();
    private static string Key(JsonElement value, int max = 128) => RequireKey(String(value), max);
    private static string Key(JsonElement body, string field) => Key(body.GetProperty(field));
    private static string? NullableKey(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : Key(value);
    private static void Text(JsonElement value, int max)
    {
        var text = String(value);
        if (string.IsNullOrWhiteSpace(text) || text.EnumerateRunes().Count() > max ||
            text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) throw TypeMismatch();
    }

    private static void NullableText(JsonElement value, int max)
    {
        if (value.ValueKind != JsonValueKind.Null) Text(value, max);
    }

    private static Guid Id(JsonElement value)
    {
        var text = String(value);
        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && id.ToString("D") == text ? id : throw TypeMismatch();
    }

    private static void NullableId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null) _ = Id(value);
    }

    internal static bool IsDigest(string? text) => text is { Length: 64 } &&
        text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Digest(JsonElement value)
    {
        var text = String(value);
        return IsDigest(text) ? text : throw TypeMismatch();
    }

    internal static long Revision(string? text) =>
        text is { Length: > 0 and <= 19 } && text[0] != '0' && text.All(char.IsAsciiDigit) &&
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : throw TypeMismatch();

    internal static string NextRevision(string value)
    {
        var current = Revision(value);
        if (current == long.MaxValue) throw new CommercialProtocolException("REVISION_EXHAUSTED");
        return (current + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string Rev(JsonElement value)
    {
        var text = String(value);
        _ = Revision(text);
        return text;
    }

    private static void Utc(JsonElement value)
    {
        if (!DateTimeOffset.TryParseExact(String(value), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out _)) throw TypeMismatch();
    }

    private static void NullableUtc(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null) Utc(value);
    }

    private static void NullableDate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null &&
            !DateOnly.TryParseExact(String(value), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw TypeMismatch();
    }

    private static int Integer(JsonElement value, int maximum) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 && number <= maximum
            ? number : throw TypeMismatch();

    private static string EnumValue(JsonElement value, params string[] choices)
    {
        var key = Key(value);
        return choices.Contains(key, StringComparer.Ordinal) ? key : throw TypeMismatch();
    }

    private static void Null(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null) throw TypeMismatch();
    }

    private static void Boolean(JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw TypeMismatch();
    }

    private static JsonElement[] Items(JsonElement value, int max = 200) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= max ? value.EnumerateArray().ToArray() : throw Invalid();
    private static string[] Keys(JsonElement value, int max = 200)
    {
        var keys = Items(value, max).Select(x => Key(x)).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw Invalid();
        return keys;
    }

    private static void Evidence(JsonElement value)
    {
        Exact(value, "authority", "key", "version", "proofRef");
        foreach (var field in value.EnumerateObject()) _ = Key(field.Value);
    }

    private static string Currency(JsonElement value)
    {
        var code = String(value);
        return code.Length == 3 && code.All(char.IsAsciiLetterUpper) ? code : throw TypeMismatch();
    }

    private const string Net = "NET_EX_TAX_AFTER_DISCOUNT";

    private static decimal FixedAmount(JsonElement value, int scale, int integerDigits, bool positive = false)
    {
        var text = String(value);
        var point = text.IndexOf('.');
        if (point < 1 || point > integerDigits || text.Length - point - 1 != scale ||
            point > 1 && text[0] == '0' || !text[..point].All(char.IsAsciiDigit) || !text[(point + 1)..].All(char.IsAsciiDigit) ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
            positive && parsed <= 0) throw TypeMismatch();
        return parsed;
    }

    private static void Money(JsonElement value)
    {
        Exact(value, "amount", "currency", "basis", "currencyPolicyVersion");
        _ = FixedAmount(value.GetProperty("amount"), 4, 15);
        _ = Currency(value.GetProperty("currency"));
        _ = EnumValue(value.GetProperty("basis"), Net);
        _ = Key(value, "currencyPolicyVersion");
    }

    internal static CommercialSourceHandoffV1 ReadSource(JsonElement value)
    {
        Exact(value, "handoffId", "requestVersion", "intentId", "intentDigest", "opportunityId", "accountId", "requirementVersion", "forecastVersion");
        return new(Id(value.GetProperty("handoffId")), Rev(value.GetProperty("requestVersion")), Id(value.GetProperty("intentId")),
            Digest(value.GetProperty("intentDigest")), Id(value.GetProperty("opportunityId")), Id(value.GetProperty("accountId")),
            Rev(value.GetProperty("requirementVersion")), Rev(value.GetProperty("forecastVersion")));
    }

    private static string Sequence(JsonElement value)
    {
        Exact(value, "ordinal", "previousOrdinal", "previousBusinessDigest");
        var ordinal = Rev(value.GetProperty("ordinal"));
        if (ordinal == "1")
        {
            Null(value.GetProperty("previousOrdinal"));
            Null(value.GetProperty("previousBusinessDigest"));
        }
        else
        {
            if (NextRevision(Rev(value.GetProperty("previousOrdinal"))) != ordinal) throw TypeMismatch();
            _ = Digest(value.GetProperty("previousBusinessDigest"));
        }
        return ordinal;
    }
}

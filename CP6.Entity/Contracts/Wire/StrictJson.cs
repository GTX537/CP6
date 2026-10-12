using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CP6.Entity.Contracts.Wire;

/// <summary>Validates one UTF-8 JSON value, rejecting duplicate decoded names before typed deserialization.</summary>
/// <remarks>
/// No application size/depth limit is imposed. The reader/document use Int32.MaxValue depth;
/// contiguous arrays, System.Text.Json metadata and available memory still bound actual capacity.
/// Escaped UTF-16 code units are compared ordinally, without Unicode normalization. A target
/// deserializer or JsonElement.GetString may reject unpaired surrogates that the JSON grammar permits.
/// </remarks>
public static class StrictJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static JsonReaderOptions ReaderOptions => new() { MaxDepth = int.MaxValue };

    /// <summary>Copies and retains exactly the supplied bytes, including whitespace and number spelling.</summary>
    public static StrictJsonDocument Parse(ReadOnlySpan<byte> utf8Json)
    {
        var original = utf8Json.ToArray();
        Validate(original);
        var document = JsonDocument.Parse(original, new JsonDocumentOptions { MaxDepth = int.MaxValue });
        return new StrictJsonDocument(original, document);
    }

    /// <summary>Completes strict validation before any target converter runs.</summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions? options = null)
    {
        using var document = Parse(utf8Json);
        return document.Deserialize<T>(options);
    }

    internal static void Validate(ReadOnlySpan<byte> utf8Json)
    {
        // Utf8JsonReader can postpone decoding string bytes. Reject invalid UTF-8 even if
        // a target ignores the offending property/value, rather than accepting replacement text.
        try { StrictUtf8.GetCharCount(utf8Json); }
        catch (DecoderFallbackException error) { throw new JsonException("INVALID_UTF8", error); }

        var reader = new Utf8JsonReader(utf8Json, ReaderOptions);
        var objects = new Stack<HashSet<string>>();
        var anyToken = false;
        while (reader.Read())
        {
            anyToken = true;
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    objects.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    if (!objects.Peek().Add(DecodeString(ref reader)))
                        throw new JsonException("DUPLICATE_PROPERTY_NAME");
                    break;
            }
        }
        if (!anyToken) throw new JsonException("JSON_VALUE_REQUIRED");
    }

    // Called only for contiguous, grammar-validated UTF-8 string/name tokens. Unlike GetString,
    // this preserves escaped lone surrogates, which must not collide with each other or U+FFFD.
    internal static string DecodeString(ref Utf8JsonReader reader)
    {
        var encoded = StrictUtf8.GetString(reader.ValueSpan);
        if (!reader.ValueIsEscaped) return encoded;
        var decoded = new StringBuilder(encoded.Length);
        for (var index = 0; index < encoded.Length; index++)
        {
            var character = encoded[index];
            if (character != '\\') { decoded.Append(character); continue; }
            character = encoded[++index];
            if (character == 'u')
            {
                decoded.Append((char)ushort.Parse(encoded.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                index += 4;
            }
            else
            {
                decoded.Append(character switch
                {
                    '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f',
                    'n' => '\n', 'r' => '\r', 't' => '\t',
                    _ => throw new JsonException("INVALID_STRING_ESCAPE")
                });
            }
        }
        return decoded.ToString();
    }
}

/// <summary>Owns a validated document and its original bytes. Dispose after using its JsonElements.</summary>
public sealed class StrictJsonDocument : IDisposable
{
    private readonly byte[] original;
    private readonly JsonDocument document;
    private bool disposed;

    internal StrictJsonDocument(byte[] original, JsonDocument document)
    {
        this.original = original;
        this.document = document;
    }

    public JsonElement RootElement
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return document.RootElement; }
    }

    /// <summary>Returns a defensive copy for byte-based digest/signature/replay protocols; never reserializes JSON.</summary>
    public byte[] GetOriginalUtf8()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return original.ToArray();
    }

    /// <summary>Uses caller type/converter rules after strict validation. Does not mutate caller options.</summary>
    /// <remarks>A caller's explicit nonzero MaxDepth is respected; the serializer default of 64 is not imposed.</remarks>
    public T? Deserialize<T>(JsonSerializerOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var effective = options is null ? new JsonSerializerOptions() : new JsonSerializerOptions(options);
        if (effective.MaxDepth == 0) effective.MaxDepth = int.MaxValue;
        effective.AllowTrailingCommas = false;
        effective.ReadCommentHandling = JsonCommentHandling.Disallow;
        return JsonSerializer.Deserialize<T>(original, effective);
    }

    public void Dispose()
    {
        document.Dispose();
        disposed = true;
    }
}

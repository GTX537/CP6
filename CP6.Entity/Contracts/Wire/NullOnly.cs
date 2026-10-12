using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Entity.Contracts.Wire;

/// <summary>A JSON null value. Use required/JsonRequired on the containing property to require presence.</summary>
[JsonConverter(typeof(NullOnlyConverter))]
public readonly record struct NullOnly;

public sealed class NullOnlyConverter : JsonConverter<NullOnly>
{
    public override bool HandleNull => true;
    public override NullOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Null)
            throw new JsonException("NULL_ONLY_REQUIRED");
        return default;
    }

    public override void Write(Utf8JsonWriter writer, NullOnly value, JsonSerializerOptions options) => writer.WriteNullValue();
}

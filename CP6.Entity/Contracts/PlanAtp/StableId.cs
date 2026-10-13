using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CP6.Entity.Contracts.PlanAtp;

[JsonConverter(typeof(StableIdJsonConverter))]
public sealed partial record StableId
{
    public const string ProfileVersion = "ATP-STABLEID-LEXICAL/20261011-R1";

    public string Value { get; }

    private StableId(string value) => Value = value;

    public static StableId Parse(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!LexicalPattern().IsMatch(value))
            throw new ArgumentException("StableId must contain 1–128 ASCII characters, start with a letter or digit, and use only letters, digits, . _ : / -.", nameof(value));

        return new StableId(value);
    }

    public override string ToString() => Value;

    // Absolute end is required: '$' can accept a trailing LF in this profile.
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LexicalPattern();
}

public sealed class StableIdJsonConverter : JsonConverter<StableId>
{
    public override bool HandleNull => true;

    public override StableId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("ATP StableId requires a JSON string.");

        try
        {
            return StableId.Parse(reader.GetString());
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Invalid ATP StableId.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, StableId value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException("ATP StableId requires a non-null string.");

        writer.WriteStringValue(value.Value);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Entity.Contracts.FinCredit;

/// <summary>Converts a required amount scalar to/from a string using one explicit native field profile.</summary>
/// <remarks>
/// Register a configured instance in serializer options, or use CreditAmountFieldAttribute on each property.
/// The parameterless instance only supports metadata discovery and rejects actual reads/writes. For carriers,
/// use Wire.StrictJson.Parse/Deserialize to reject duplicates before conversion and retain original bytes separately.
/// Nullability and property presence belong to the enclosing contract, not this required-scalar converter.
/// </remarks>
public sealed class CreditAmountJsonConverter : JsonConverter<CreditAmount>
{
    private readonly CreditAmountProfile? profile;

    public CreditAmountJsonConverter() { }

    public CreditAmountJsonConverter(CreditAmountProfile profile)
    {
        CreditAmountProfiles.Validate(profile);
        this.profile = profile;
    }

    public override bool HandleNull => true;

    public override CreditAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var selected = RequireProfile();
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("FIN_CREDIT_AMOUNT_STRING_REQUIRED");
        try { return CreditAmount.Parse(reader.GetString()!, selected); }
        catch (FormatException error) { throw new JsonException(error.Message, error); }
    }

    public override void Write(Utf8JsonWriter writer, CreditAmount value, JsonSerializerOptions options)
    {
        var selected = RequireProfile();
        string text;
        try { text = value.ToFieldString(selected); }
        catch (FormatException error) { throw new JsonException(error.Message, error); }
        writer.WriteStringValue(text);
    }

    private CreditAmountProfile RequireProfile() => profile ?? throw new InvalidOperationException(
        "Configure CreditAmountJsonConverter with an explicit CreditAmountProfile, or annotate the field with CreditAmountFieldAttribute.");
}

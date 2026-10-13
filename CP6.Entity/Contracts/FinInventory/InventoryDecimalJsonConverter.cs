using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Entity.Contracts.FinInventory;

public class InventoryDecimalJsonConverter : JsonConverter<InventoryDecimal>
{
    private readonly InventoryDecimalProfile profile;

    /// <summary>Choose from the exact destination field; do not infer from a CLR property name.</summary>
    public InventoryDecimalJsonConverter(InventoryDecimalProfile profile)
    {
        InventoryDecimal.MaximumScale(profile);
        this.profile = profile;
    }

    // Required scalar null rejects instead of bypassing the converter. Nullable enclosing fields
    // need their own contract-aware boundary; this converter does not infer optionality/presence.
    public override bool HandleNull => true;

    public override InventoryDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("FIN-INV requires a JSON string");
        try { return InventoryDecimal.Parse(reader.GetString()!, profile); }
        catch (FormatException error) { throw new JsonException("Invalid FIN-INV field wire", error); }
    }

    public override void Write(Utf8JsonWriter writer, InventoryDecimal value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("FIN-INV scalar cannot be null");
        try { writer.WriteStringValue(value.ToWire(profile)); }
        catch (FormatException error) { throw new JsonException("FIN-INV value does not fit the destination field", error); }
    }
}
public sealed class Decimal18JsonConverter() : InventoryDecimalJsonConverter(InventoryDecimalProfile.Decimal18);
public sealed class Qty12JsonConverter() : InventoryDecimalJsonConverter(InventoryDecimalProfile.Qty12);
public sealed class NonNegativeQty12JsonConverter() : InventoryDecimalJsonConverter(InventoryDecimalProfile.NonNegativeQty12);

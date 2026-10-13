using System.Text.Json.Serialization;

namespace CP6.Entity.Contracts.FinCredit;

/// <summary>Selects the exact native profile for one amount property or field, including nullable enclosing fields.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CreditAmountFieldAttribute : JsonConverterAttribute
{
    private readonly CreditAmountProfile profile;

    public CreditAmountFieldAttribute(CreditAmountProfile profile)
    {
        CreditAmountProfiles.Validate(profile);
        this.profile = profile;
    }

    public override JsonConverter CreateConverter(Type typeToConvert)
    {
        if (typeToConvert != typeof(CreditAmount) && Nullable.GetUnderlyingType(typeToConvert) != typeof(CreditAmount))
            throw new InvalidOperationException("CreditAmountFieldAttribute requires a CreditAmount or nullable CreditAmount field.");
        return new CreditAmountJsonConverter(profile);
    }
}

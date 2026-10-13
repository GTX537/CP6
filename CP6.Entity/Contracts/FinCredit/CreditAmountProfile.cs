namespace CP6.Entity.Contracts.FinCredit;

/// <summary>Explicit native definitions in fin-credit-wire-r4.schema.json; never infer these from a field name.</summary>
public enum CreditAmountProfile
{
    /// <summary>/$defs/signedDecimal: negative, zero or positive.</summary>
    Signed = 1,
    /// <summary>/$defs/nonNegativeDecimal: zero or positive.</summary>
    NonNegative = 2,
    /// <summary>/$defs/positiveDecimal: strictly greater than zero.</summary>
    Positive = 3
}

internal static class CreditAmountProfiles
{
    internal static void Validate(CreditAmountProfile profile)
    {
        if (profile is not (CreditAmountProfile.Signed or CreditAmountProfile.NonNegative or CreditAmountProfile.Positive))
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "An explicit native FIN-CREDIT amount profile is required.");
    }
}

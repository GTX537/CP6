using System.Numerics;

namespace CP6.Entity.Contracts.FinInventory;

/// <summary>Internal exact rational arithmetic: signed numerator, positive denominator, gcd-reduced.</summary>
/// <remarks>
/// This is not the native Rational object (positive parts plus basisRef), nor a MAVG wire codec.
/// It does not retain original parts; consumers must preserve those strings/document bytes separately.
/// </remarks>
public sealed class InventoryRatio : IComparable<InventoryRatio>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    private InventoryRatio(BigInteger numerator, BigInteger denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public static InventoryRatio Create(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator), "Ratio denominator must be positive");
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return new InventoryRatio(numerator / gcd, denominator / gcd);
    }

    /// <summary>
    /// Divides the reduced numerator/denominator, truncating toward zero. Remainder has the
    /// numerator's sign or is zero; Numerator = Quotient * Denominator + Remainder.
    /// Remainder is in units of the reduced denominator, not the original unretained wire parts.
    /// </summary>
    public (BigInteger Quotient, BigInteger Remainder) DivRem()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return (quotient, remainder);
    }

    public InventoryRatio Add(InventoryRatio other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Numerator * other.Denominator + other.Numerator * Denominator,
            Denominator * other.Denominator);
    }

    public InventoryRatio Subtract(InventoryRatio other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Numerator * other.Denominator - other.Numerator * Denominator,
            Denominator * other.Denominator);
    }

    public InventoryRatio Multiply(InventoryRatio other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Numerator * other.Numerator, Denominator * other.Denominator);
    }

    public int CompareTo(InventoryRatio? other) => other is null ? 1
        : (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
}

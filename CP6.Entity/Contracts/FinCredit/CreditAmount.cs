using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace CP6.Entity.Contracts.FinCredit;

/// <summary>An exact normalized decimal coefficient and scale for FIN-CREDIT field values and intermediate arithmetic.</summary>
/// <remarks>
/// Parse validates the original canonical spelling against an explicit native profile. Arithmetic and
/// FromIntermediate permit values outside any field profile; ToFieldString or the configured JSON converter
/// must validate the final target field. No decimal, floating-point conversion, rounding or fixed scale is used.
/// Default is canonical zero, which still requires target-profile validation. Representation capacity is bounded
/// only by framework collection/scale types and available memory; no application coefficient or term-count cap is imposed.
/// </remarks>
[JsonConverter(typeof(CreditAmountJsonConverter))]
public readonly struct CreditAmount : IComparable<CreditAmount>, IEquatable<CreditAmount>
{
    private static readonly BigInteger FieldCoefficientLimit = BigInteger.Pow(10, 38);

    public BigInteger Coefficient { get; }
    public int Scale { get; }

    private CreditAmount(BigInteger coefficient, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        if (coefficient.IsZero) scale = 0;
        while (scale > 0)
        {
            var quotient = BigInteger.DivRem(coefficient, 10, out var remainder);
            if (!remainder.IsZero) break;
            coefficient = quotient;
            scale--;
        }
        Coefficient = coefficient;
        Scale = scale;
    }

    /// <summary>Accepts only the native canonical ASCII string for the selected field profile.</summary>
    /// <exception cref="FormatException">The original input violates its native lexical or sign profile.</exception>
    public static CreditAmount Parse(string text, CreditAmountProfile profile)
    {
        CreditAmountProfiles.Validate(profile);
        ArgumentNullException.ThrowIfNull(text);
        var magnitude = text.AsSpan();
        var negative = !magnitude.IsEmpty && magnitude[0] == '-';
        if (negative) magnitude = magnitude[1..];
        if (magnitude.IsEmpty || (negative && profile != CreditAmountProfile.Signed)) throw InvalidField();

        var point = magnitude.IndexOf('.');
        var integerLength = point < 0 ? magnitude.Length : point;
        var scale = point < 0 ? 0 : magnitude.Length - point - 1;
        // These counts reproduce each native pattern branch, including the integer zero before a fraction.
        if (integerLength == 0 || scale > 8 || (point >= 0 && scale == 0) || integerLength > 38 - scale)
            throw InvalidField();
        if (magnitude[0] == '0' && integerLength != 1) throw InvalidField();
        if (point >= 0 && magnitude[^1] is not (>= '1' and <= '9')) throw InvalidField();
        for (var index = 0; index < magnitude.Length; index++)
            if (index != point && magnitude[index] is not (>= '0' and <= '9')) throw InvalidField();
        if (magnitude.SequenceEqual("0") && (negative || profile == CreditAmountProfile.Positive)) throw InvalidField();

        var digits = magnitude.ToString().Replace(".", "", StringComparison.Ordinal);
        var coefficient = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return new CreditAmount(negative ? -coefficient : coefficient, scale);
    }

    /// <summary>Creates an unbounded intermediate, normalizing fractional trailing zeros without rounding.</summary>
    /// <remarks>This is explicitly not field validation. Scale must be nonnegative; emit through ToFieldString(profile).</remarks>
    public static CreditAmount FromIntermediate(BigInteger coefficient, int scale) => new(coefficient, scale);

    /// <summary>Adds exactly, without checking final-field precision or sign.</summary>
    public CreditAmount Add(CreditAmount other)
    {
        if (Coefficient.IsZero) return other;
        if (other.Coefficient.IsZero) return this;
        var scale = Math.Max(Scale, other.Scale);
        return new CreditAmount(AtScale(scale) + other.AtScale(scale), scale);
    }

    /// <summary>Subtracts exactly, permitting negative or oversized intermediate results.</summary>
    public CreditAmount Subtract(CreditAmount other)
    {
        if (other.Coefficient.IsZero) return this;
        if (Coefficient.IsZero) return new CreditAmount(-other.Coefficient, other.Scale);
        var scale = Math.Max(Scale, other.Scale);
        return new CreditAmount(AtScale(scale) - other.AtScale(scale), scale);
    }

    /// <summary>Streams all terms with unbounded exact intermediate arithmetic; an empty sequence yields zero.</summary>
    public static CreditAmount Sum(IEnumerable<CreditAmount> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var result = default(CreditAmount);
        foreach (var term in terms) result = result.Add(term);
        return result;
    }

    /// <summary>Validates the normalized final value against the selected native field profile, then emits its canonical string.</summary>
    /// <exception cref="FormatException">The final value is outside the target sign, 38-digit or 8-place field limits.</exception>
    public string ToFieldString(CreditAmountProfile profile)
    {
        CreditAmountProfiles.Validate(profile);
        if (Scale > 8 || BigInteger.Abs(Coefficient) >= FieldCoefficientLimit ||
            (Coefficient.Sign < 0 && profile != CreditAmountProfile.Signed) ||
            (Coefficient.IsZero && profile == CreditAmountProfile.Positive)) throw InvalidField();

        var digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
        if (Scale > 0)
        {
            digits = digits.PadLeft(Scale + 1, '0');
            digits = digits.Insert(digits.Length - Scale, ".");
        }
        return Coefficient.Sign < 0 ? "-" + digits : digits;
    }

    public int CompareTo(CreditAmount other)
    {
        if (Coefficient.Sign != other.Coefficient.Sign) return Coefficient.Sign.CompareTo(other.Coefficient.Sign);
        if (Scale == other.Scale) return Coefficient.CompareTo(other.Coefficient);
        var scale = Math.Max(Scale, other.Scale);
        return AtScale(scale).CompareTo(other.AtScale(scale));
    }

    public bool Equals(CreditAmount other) => Coefficient == other.Coefficient && Scale == other.Scale;
    public override bool Equals(object? obj) => obj is CreditAmount other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Coefficient, Scale);

    private BigInteger AtScale(int scale) => Coefficient * BigInteger.Pow(10, scale - Scale);
    private static FormatException InvalidField() => new("FIN_CREDIT_AMOUNT_INVALID_FOR_NATIVE_PROFILE");
}

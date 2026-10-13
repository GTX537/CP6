using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CP6.Entity.Contracts.FinInventory;

/// <summary>Native R10 scalar definitions; enclosing object/state constraints remain separate.</summary>
public enum InventoryDecimalProfile { Decimal18, Qty12, NonNegativeQty12 }

/// <summary>A validated FIN-INV field string with its independent normalized arithmetic value.</summary>
/// <remarks>OriginalWire is a decoded field string, not the original JSON document bytes.</remarks>
public sealed class InventoryDecimal
{
    private static readonly Regex WirePattern = new(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public InventoryDecimalValue Value { get; }
    public InventoryDecimalProfile Profile { get; }
    public string OriginalWire { get; }

    private InventoryDecimal(InventoryDecimalValue value, InventoryDecimalProfile profile, string originalWire)
    {
        Value = value;
        Profile = profile;
        OriginalWire = originalWire;
    }

    internal static int MaximumScale(InventoryDecimalProfile profile) => profile switch
    {
        InventoryDecimalProfile.Decimal18 => 18,
        InventoryDecimalProfile.Qty12 or InventoryDecimalProfile.NonNegativeQty12 => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown FIN-INV profile")
    };

    private static int Validate(string wire, InventoryDecimalProfile profile)
    {
        var maximumScale = MaximumScale(profile);
        ArgumentNullException.ThrowIfNull(wire);
        if (!WirePattern.IsMatch(wire)) throw new FormatException("Invalid FIN-INV decimal wire");
        var point = wire.IndexOf('.');
        var scale = point < 0 ? 0 : wire.Length - point - 1;
        if (scale > maximumScale) throw new FormatException($"FIN-INV field exceeds scale {maximumScale}");
        // This lexical overlay also excludes -0, although signed Qty12 permits it.
        if (profile == InventoryDecimalProfile.NonNegativeQty12 && wire[0] == '-')
            throw new FormatException("NonNegativeQty12 does not allow a minus sign");
        return scale;
    }

    public static InventoryDecimal Parse(string wire, InventoryDecimalProfile profile)
    {
        var scale = Validate(wire, profile);
        var coefficient = BigInteger.Parse(wire.Replace(".", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return new InventoryDecimal(InventoryDecimalValue.FromParts(coefficient, scale), profile, wire);
    }

    /// <summary>Creates new canonical wire after destination-field validation, without rounding.</summary>
    public static InventoryDecimal FromValue(InventoryDecimalValue value, InventoryDecimalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.ToString(), profile);
    }

    public string ToWire() => ToWire(Profile);

    /// <summary>Revalidates the unchanged original lexical form against the destination field.</summary>
    public string ToWire(InventoryDecimalProfile profile)
    {
        Validate(OriginalWire, profile);
        return OriginalWire;
    }

    public override string ToString() => OriginalWire;
}

/// <summary>Exact immutable intermediate value. No field precision/scale limit or rounding policy.</summary>
public sealed class InventoryDecimalValue : IComparable<InventoryDecimalValue>
{
    public BigInteger Coefficient { get; }
    public int Scale { get; }

    private InventoryDecimalValue(BigInteger coefficient, int scale)
    {
        Coefficient = coefficient;
        Scale = scale;
    }

    public static InventoryDecimalValue FromParts(BigInteger coefficient, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        if (coefficient.IsZero) return new InventoryDecimalValue(BigInteger.Zero, 0);
        while (scale > 0)
        {
            var quotient = BigInteger.DivRem(coefficient, 10, out var remainder);
            if (!remainder.IsZero) break;
            coefficient = quotient;
            scale--;
        }
        return new InventoryDecimalValue(coefficient, scale);
    }

    private (BigInteger Left, BigInteger Right, int Scale) Align(InventoryDecimalValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var scale = Math.Max(Scale, other.Scale);
        return (Coefficient * BigInteger.Pow(10, scale - Scale),
            other.Coefficient * BigInteger.Pow(10, scale - other.Scale), scale);
    }

    public InventoryDecimalValue Add(InventoryDecimalValue other)
    {
        var (left, right, scale) = Align(other);
        return FromParts(left + right, scale);
    }

    public InventoryDecimalValue Subtract(InventoryDecimalValue other)
    {
        var (left, right, scale) = Align(other);
        return FromParts(left - right, scale);
    }

    public InventoryDecimalValue Multiply(InventoryDecimalValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return FromParts(Coefficient * other.Coefficient, checked(Scale + other.Scale));
    }

    public int CompareTo(InventoryDecimalValue? other)
    {
        if (other is null) return 1;
        var (left, right, _) = Align(other);
        return left.CompareTo(right);
    }

    /// <summary>Canonical arithmetic text, not approval for a final field or original replay bytes.</summary>
    public override string ToString()
    {
        var digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
        if (Scale == 0) return Coefficient.Sign < 0 ? "-" + digits : digits;
        digits = digits.PadLeft(checked(Scale + 1), '0');
        var point = digits.Length - Scale;
        return (Coefficient.Sign < 0 ? "-" : "") + digits[..point] + "." + digits[point..];
    }
}

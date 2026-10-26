using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace CP6.Core.Services.Erp;

/// <summary>Native v1 exact strings. No CLR decimal, binary floating point or implicit conversion policy.</summary>
internal static class UomProfileValuesV1
{
    internal const string Owner = "Main.Erp.UomProfile.v1";
    internal const int MaxDigits = 1024;
    private static readonly Regex Integer = new(@"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Decimal = new(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);

    internal static long Revision(string? value)
    {
        if (value == null || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 1 || revision.ToString(CultureInfo.InvariantCulture) != value)
            throw Error("UOM_VERSION_INVALID", 400);
        return revision;
    }

    internal static void Reference(UomProfileRefV1? reference)
    {
        if (reference == null || reference.Owner != Owner || reference.Id == Guid.Empty
            || reference.Digest == null || reference.Digest.Length != 64
            || reference.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("UOM_REFERENCE_INVALID", 400);
        Revision(reference.Version);
    }

    private static void Key(string value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value != value.Trim()
            || value.Any(char.IsControl) || !ValidUnicode(value)) throw Error("UOM_IDENTITY_INVALID", 400);
    }

    private static bool ValidUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i]))) return false;
        return true;
    }

    internal static BigInteger PositiveInteger(string value)
    {
        if (value == null || value.Length > MaxDigits || !Integer.IsMatch(value)) throw Error("UOM_RATIO_INVALID");
        return BigInteger.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private readonly record struct ExactDecimal(BigInteger Units, int Scale);
    private static ExactDecimal Parse(string value, int scale)
    {
        if (value == null || value.Length > 2 * MaxDigits + 2 || !Decimal.IsMatch(value)) throw Error("UOM_QUANTITY_INVALID");
        var point = value.IndexOf('.');
        var decimals = point < 0 ? 0 : value.Length - point - 1;
        var whole = (point < 0 ? value.Length : point) - (value.StartsWith('-') ? 1 : 0);
        if (whole > MaxDigits || decimals > scale) throw Error("UOM_PRECISION_EXCEEDED");
        var units = BigInteger.Parse(value.Replace(".", "", StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (units.IsZero && value.StartsWith('-')) throw Error("UOM_QUANTITY_INVALID");
        return new(units, decimals);
    }

    internal static UomProfileInputV1 Validate(UomProfileInputV1 input)
    {
        Key(input.Code, 64); Key(input.DimensionKey, 128);
        if (input.Name == null || !ValidUnicode(input.Name) || string.IsNullOrWhiteSpace(input.Name)
            || input.Name.Length > 200 || input.Name.Any(char.IsControl)) throw Error("UOM_NAME_INVALID", 400);
        if (input.QuantityScale is not (>= 0 and <= MaxDigits) || input.Enabled == null) throw Error("UOM_PROFILE_INCOMPLETE", 400);
        var numerator = PositiveInteger(input.Numerator); var denominator = PositiveInteger(input.Denominator);
        if (input.Mode == "BASE")
        {
            if (input.BaseRef != null || numerator != BigInteger.One || denominator != BigInteger.One) throw Error("UOM_BASE_INVALID");
        }
        else if (input.Mode == "RATIO") Reference(input.BaseRef);
        else throw Error("UOM_MODE_INVALID", 400);
        if (input.PackMultiple != null && Parse(input.PackMultiple, input.QuantityScale.Value).Units <= 0) throw Error("UOM_PACK_MULTIPLE_INVALID");
        return input with { Name = input.Name.Trim().Normalize(NormalizationForm.FormC) };
    }

    private static void Pack(ExactDecimal value, UomProfileInputV1 profile)
    {
        if (profile.PackMultiple == null) return;
        var pack = Parse(profile.PackMultiple, profile.QuantityScale!.Value);
        if (value.Units * BigInteger.Pow(10, pack.Scale) % (pack.Units * BigInteger.Pow(10, value.Scale)) != 0)
            throw Error("UOM_PACK_MULTIPLE_VIOLATION");
    }

    internal static string ConvertExact(string quantity, UomProfileInputV1 from, UomProfileInputV1 to)
    {
        if (from.DimensionKey != to.DimensionKey) throw Error("UOM_DIMENSION_MISMATCH");
        var parsed = Parse(quantity, from.QuantityScale!.Value); Pack(parsed, from);
        var numerator = parsed.Units * PositiveInteger(from.Numerator) * PositiveInteger(to.Denominator)
            * BigInteger.Pow(10, to.QuantityScale!.Value);
        var denominator = BigInteger.Pow(10, parsed.Scale) * PositiveInteger(from.Denominator) * PositiveInteger(to.Numerator);
        var units = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (!remainder.IsZero) throw Error("UOM_CONVERSION_NOT_EXACT");
        Pack(new(units, to.QuantityScale.Value), to);
        var digits = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(to.QuantityScale.Value + 1, '0');
        if (digits.Length - to.QuantityScale.Value > MaxDigits) throw Error("UOM_PRECISION_EXCEEDED");
        return (units.Sign < 0 ? "-" : "") + (to.QuantityScale == 0 ? digits : digits.Insert(digits.Length - to.QuantityScale.Value, "."));
    }
}

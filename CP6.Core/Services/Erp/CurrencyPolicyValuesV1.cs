using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CP6.Core.Services.Erp;

internal static class CurrencyPolicyValuesV1
{
    internal const string Owner = "Main.Erp.CurrencyPolicy.v1";
    private static readonly Regex Decimal = new(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    internal static long Revision(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 1 || revision.ToString(CultureInfo.InvariantCulture) != value) throw Error("CURRENCY_VERSION_INVALID", 400);
        return revision;
    }
    internal static void Reference(CurrencyPolicyRefV1? reference)
    {
        if (reference == null || reference.Owner != Owner || reference.Id == Guid.Empty || reference.Digest == null
            || reference.Digest.Length != 64 || reference.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("CURRENCY_REFERENCE_INVALID", 400);
        Revision(reference.Version);
    }
    private static bool ValidText(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl)) return false;
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i]))) return false;
        return true;
    }
    internal static CurrencyPolicyInputV1 Validate(CurrencyPolicyInputV1 policy)
    {
        // Codes retain their original case/Unicode identity; no ISO/FX inference or default currency.
        if (!ValidText(policy.Code, 128) || policy.Code != policy.Code.Trim()) throw Error("CURRENCY_IDENTITY_INVALID", 400);
        if (!ValidText(policy.Name, 200)) throw Error("CURRENCY_NAME_INVALID", 400);
        if (policy.MinorDigits is not (>= 0 and <= 4) || policy.PriceDigits is not (>= 0 and <= 6)
            || policy.Rounding != "HALF_EVEN" || policy.Enabled == null) throw Error("CURRENCY_POLICY_INCOMPLETE", 400);
        return policy;
    }
    internal static string Quantize(string value, string kind, CurrencyPolicyInputV1 policy)
    {
        Validate(policy);
        var digits = kind switch { "AMOUNT" => policy.MinorDigits!.Value, "PRICE" => policy.PriceDigits!.Value, _ => throw Error("CURRENCY_QUANTIZATION_KIND_INVALID", 400) };
        if (value == null || value.Length > 2050 || !Decimal.IsMatch(value)) throw Error("CURRENCY_VALUE_INVALID");
        var point = value.IndexOf('.'); var scale = point < 0 ? 0 : value.Length - point - 1;
        var whole = (point < 0 ? value.Length : point) - (value.StartsWith('-') ? 1 : 0);
        if (whole > 1024 || scale > 1024) throw Error("CURRENCY_VALUE_PRECISION_EXCEEDED");
        var signed = BigInteger.Parse(value.Replace(".", "", StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (signed.IsZero && value.StartsWith('-')) throw Error("CURRENCY_VALUE_INVALID");
        var units = BigInteger.Abs(signed);
        if (scale > digits)
        {
            var divisor = BigInteger.Pow(10, scale - digits);
            var quotient = BigInteger.DivRem(units, divisor, out var remainder);
            var midpoint = (remainder * 2).CompareTo(divisor);
            units = midpoint > 0 || midpoint == 0 && !quotient.IsEven ? quotient + 1 : quotient;
        }
        else units *= BigInteger.Pow(10, digits - scale);
        var text = units.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        if (digits > 0) text = text.Insert(text.Length - digits, ".");
        return signed.Sign < 0 && !units.IsZero ? "-" + text : text;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Mes;

internal static class ProcessCostOwnerValuesV1
{
    internal static string? Format(decimal? value) => value?.ToString("0.############################", CultureInfo.InvariantCulture);
    internal static decimal Parse(string value)
    {
        // The explicit v1 wire representation preserves every existing nonnegative numeric(21,8) value.
        // Reject rounding and overflow before PostgreSQL can coerce it.
        if (value.Length > 64 || !Regex.IsMatch(value, @"\A[0-9]+(?:\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant)
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number >= 10000000000000m)
            throw Error("EXACT_DECIMAL_21_8_REQUIRED", 400);
        return number;
    }
    internal static void Version(byte[]? expected, byte[]? actual)
    {
        if (expected is not { Length: > 0 }) throw Error("ROW_VERSION_REQUIRED", 409);
        if (actual == null || !expected.SequenceEqual(actual)) throw Error("ROW_VERSION_CONFLICT", 409);
    }
    internal static void Code(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 10) throw Error("WORK_CENTER_CODE_INVALID", 400);
    }
    internal static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
}

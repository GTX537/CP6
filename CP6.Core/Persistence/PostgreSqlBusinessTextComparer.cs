using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CP6.Core.Persistence;

/// <summary>Key identity only; ordinary changes keep exact string value comparison.</summary>
public sealed class PostgreSqlBusinessTextComparer(bool binary) : ValueComparer<string>(
    (left, right) => Equal(left, right, binary),
    value => Hash(value, binary), value => value)
{
    private const CompareOptions LinguisticOptions =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    private static bool Equal(string? left, string? right, bool binary)
    {
        if (left is null || right is null) return left is null && right is null;
        left = left.TrimEnd(' ');
        right = right.TrimEnd(' ');
        return binary ? string.Equals(left, right, StringComparison.Ordinal)
            : CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, LinguisticOptions) == 0;
    }

    private static int Hash(string value, bool binary)
    {
        value = value.TrimEnd(' ');
        return binary ? StringComparer.Ordinal.GetHashCode(value)
            : CultureInfo.InvariantCulture.CompareInfo.GetHashCode(value, LinguisticOptions);
    }
}

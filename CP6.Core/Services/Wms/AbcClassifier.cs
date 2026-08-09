namespace CP6.Core.Services.Wms;

public enum AbcMetric
{
    Quantity,
    Frequency,
}

public sealed record AbcInputRow(string ProductCd, int OutCount, decimal OutQty);

public sealed record AbcClassifiedRow(
    string ProductCd,
    int OutCount,
    decimal OutQty,
    decimal Score,
    decimal CumulativeRatio,
    string AbcRank);

/// <summary>Shared deterministic ABC classifier used by reporting, slotting and Space.</summary>
public static class AbcClassifier
{
    public static IReadOnlyList<AbcClassifiedRow> Classify(
        IEnumerable<AbcInputRow> input,
        AbcMetric metric,
        decimal thresholdA = 0.80m,
        decimal thresholdB = 0.95m)
    {
        if (thresholdA <= 0m || thresholdA >= thresholdB || thresholdB > 1m)
            throw new ArgumentOutOfRangeException(nameof(thresholdA), "ABC thresholds must satisfy 0 < A < B <= 1.");

        var rows = input
            .Where(x => !string.IsNullOrWhiteSpace(x.ProductCd))
            .Select(x => new
            {
                Row = x,
                Score = metric == AbcMetric.Quantity ? Math.Abs(x.OutQty) : Math.Max(0, x.OutCount),
            })
            .Where(x => x.Score > 0m)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => Math.Abs(x.Row.OutQty))
            .ThenBy(x => x.Row.ProductCd, StringComparer.Ordinal)
            .ToList();

        var total = rows.Sum(x => x.Score);
        if (total <= 0m) return Array.Empty<AbcClassifiedRow>();

        decimal cumulative = 0m;
        var result = new List<AbcClassifiedRow>(rows.Count);
        foreach (var item in rows)
        {
            cumulative += item.Score;
            var ratio = cumulative / total;
            var rank = ratio <= thresholdA ? "A" : ratio <= thresholdB ? "B" : "C";
            result.Add(new AbcClassifiedRow(
                item.Row.ProductCd,
                item.Row.OutCount,
                item.Row.OutQty,
                item.Score,
                ratio,
                rank));
        }
        return result;
    }

    public static AbcMetric ParseMetric(string? value) =>
        string.Equals(value, "frequency", StringComparison.OrdinalIgnoreCase)
            ? AbcMetric.Frequency
            : AbcMetric.Quantity;

    public static string ToValue(AbcMetric metric) =>
        metric == AbcMetric.Frequency ? "frequency" : "quantity";
}

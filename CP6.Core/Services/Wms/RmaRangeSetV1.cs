using System.Globalization;
using System.Text.RegularExpressions;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

internal static class RmaRangeSetV1
{
    internal readonly record struct Segment(decimal From, decimal To);
    internal static decimal Input(string value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
            throw Error("RETURN_RANGE_INVALID", 400);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
    internal static Segment[] Input(RmaDeliveryRangeV1[] value, bool empty = false, int limit = 100)
    {
        if (value == null || value.Length > limit || !empty && value.Length == 0) throw Error("RETURN_RANGE_INVALID", 400);
        var result = value.Select(x => x == null ? throw Error("RETURN_RANGE_INVALID", 400) : new Segment(Input(x.From), Input(x.To))).OrderBy(x => x.From).ToArray();
        for (var i = 0; i < result.Length; i++)
            if (result[i].From >= result[i].To || i > 0 && result[i].From < result[i - 1].To) throw Error("RETURN_RANGE_INVALID", 400);
        return result;
    }
    // All remaining operations consume internal decimal ranges already validated by their Owner boundary.
    internal static Segment[] Read(IEnumerable<FulfilmentRangeV1> value) => value.Select(x => new Segment(decimal.Parse(x.From, CultureInfo.InvariantCulture), decimal.Parse(x.To, CultureInfo.InvariantCulture))).ToArray();
    internal static string Text(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    internal static FulfilmentRangeV1[] Wire(IEnumerable<Segment> value) => value.Select(x => new FulfilmentRangeV1(Text(x.From), Text(x.To))).ToArray();
    internal static Segment[] Union(IEnumerable<Segment> source)
    {
        var result = new List<Segment>();
        foreach (var item in source.OrderBy(x => x.From))
            if (result.Count > 0 && item.From <= result[^1].To) result[^1] = result[^1] with { To = Math.Max(result[^1].To, item.To) };
            else result.Add(item);
        return result.ToArray();
    }
    internal static Segment[] Except(IEnumerable<Segment> source, IEnumerable<Segment> removed)
    {
        var cuts = Union(removed); var result = new List<Segment>();
        foreach (var part in Union(source))
        {
            var start = part.From;
            foreach (var cut in cuts)
            {
                if (cut.To <= start) continue; if (cut.From >= part.To) break;
                if (cut.From > start) result.Add(new(start, cut.From)); start = Math.Max(start, cut.To);
                if (start >= part.To) break;
            }
            if (start < part.To) result.Add(new(start, part.To));
        }
        return result.ToArray();
    }
    internal static bool Subset(IEnumerable<Segment> inner, IEnumerable<Segment> outer) => Except(inner, outer).Length == 0;
    internal static bool Intersects(IEnumerable<Segment> left, IEnumerable<Segment> right) => left.Any(a => right.Any(b => a.From < b.To && b.From < a.To));
    internal static decimal Measure(IEnumerable<Segment> source) => Union(source).Sum(x => x.To - x.From);
}

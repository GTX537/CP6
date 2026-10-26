using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

internal static class OwnerPageV1
{
    // Both inputs must be derived from the same already-authorized and filtered query.
    // LEFT JOIN retains the count on an out-of-range page; the empty set has total zero.
    internal static async Task<PagedResult<T>> ReadAsync<T>(IQueryable<T> filtered,
        IOrderedQueryable<T> ordered, int page, int size, CancellationToken ct,
        Func<IQueryable<Row<T>>, IOrderedQueryable<Row<T>>> orderResult) where T : class
    {
        var slice = ordered.Skip((page - 1) * size).Take(size);
        var totals = filtered.GroupBy(_ => 1).Select(group => group.Count());
        var joined = totals.SelectMany(_ => slice.DefaultIfEmpty(),
            (total, item) => new Row<T> { Total = total, Item = item });
        // A subquery's ORDER BY selects the correct page but does not order a JOIN result.
        var result = await orderResult(joined).AsSingleQuery().ToArrayAsync(ct);
        return new() { Page = page, PageSize = size, Total = result.FirstOrDefault()?.Total ?? 0,
            Items = result.Where(row => row.Item is not null).Select(row => row.Item!).ToArray() };
    }

    internal sealed class Row<T> where T : class
    {
        public int Total { get; init; }
        public T? Item { get; init; }
    }
}

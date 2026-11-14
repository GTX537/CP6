using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

public partial class RfqService
{
    private async Task<List<string>> ConvertToPoPostgreSqlAsync(string rfqNo, string? userName,
        Func<CancellationToken, Task<Guid>>? currentActor, CancellationToken ct)
    {
        var (orderDate, selected) = await CommitAsync(async token =>
        {
            var rfq = await ReadHeadAsync(token);
            var quotes = await ReadSelectionsAsync(SelectedQuotes(), token);
            if (quotes.Length == 0) throw new InvalidOperationException("E-PUR-070");
            RequireUnexpired(quotes);
            return (rfq.RfqDate, quotes);
        });

        var orders = new List<string>();
        // The PO, its lines and its number commit together for each supplier.
        foreach (var group in selected.GroupBy(row => row.SupplierId))
        {
            var number = await CommitAsync(async token =>
            {
                var rfq = await ReadHeadAsync(token);
                var quotes = await ReadSelectionsAsync(SelectedQuotes().Where(row => row.SupplierId == group.Key), token);
                RequireSameSelection(rfq, group, quotes);
                RequireUnexpired(quotes);
                return await CreatePurchaseOrderAsync(rfq, group.Key, quotes.Select(row => new PoLineCreateDto
                {
                    ItemId = row.ItemId,
                    Qty = row.Quantity,
                    UnitPrice = row.Price,
                    RequiredDate = row.RequiredDate
                }), userName);
            });
            orders.Add(number);
        }

        await CommitAsync(async token =>
        {
            var rfq = await ReadHeadAsync(token);
            var quotes = await ReadSelectionsAsync(SelectedQuotes(), token);
            RequireSameSelection(rfq, selected, quotes);
            var lineNumbers = await _db.RfqLines.AsNoTracking().Where(row => row.RfqNo == rfqNo && !row.IsDeleted)
                .Select(row => row.LineNo).ToArrayAsync(token);
            var converted = quotes.Select(row => row.LineNumber).ToHashSet();
            if (lineNumbers.All(converted.Contains)) rfq.Status = RfqStatus.Converted;
            rfq.Modifier = userName;
            rfq.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(token);
            return true;
        });
        return orders;

        Task<T> CommitAsync<T>(Func<CancellationToken, Task<T>> body) =>
            CommitRfqEffectAsync("pur-rfq:convert", currentActor, body, ct);

        async Task<Rfq> ReadHeadAsync(CancellationToken token) =>
            await _db.Rfqs.SingleOrDefaultAsync(row => row.RfqNo == rfqNo && !row.IsDeleted, token)
                ?? throw new InvalidOperationException("E-PUR-061");

        IQueryable<RfqQuote> SelectedQuotes() => _db.RfqQuotes.AsNoTracking()
            .Where(row => row.RfqNo == rfqNo && row.IsSelected && !row.IsDeleted);

        void RequireSameSelection(Rfq rfq, IEnumerable<PurchaseSelection> original, PurchaseSelection[] current)
        {
            if (rfq.RfqDate != orderDate || !original.SequenceEqual(current))
                throw new IamAdministrationExceptionV1(409, "RFQ_CONVERSION_SELECTION_CHANGED");
        }
    }

    private async Task<PurchaseSelection[]> ReadSelectionsAsync(IQueryable<RfqQuote> selected, CancellationToken ct)
    {
        var selections = await (from quote in selected
                                join activeLine in _db.RfqLines.AsNoTracking().Where(row => !row.IsDeleted)
                                    on new { quote.RfqNo, quote.LineNo } equals new { activeLine.RfqNo, activeLine.LineNo }
                                    into lines
                                from line in lines.DefaultIfEmpty()
                                orderby quote.LineNo, quote.Id
                                select new PurchaseSelection(quote.Id, quote.LineNo, quote.SupplierId, quote.QuotedPrice,
                                    quote.CurrencyCd, quote.ValidUntil, (Guid?)line.Id, line.ItemId,
                                    (decimal?)line.Qty, line.RequiredDate)).ToArrayAsync(ct);
        if (selections.Any(row => row.RfqLineId == null))
            throw new InvalidOperationException("E-PUR-065");
        return selections;
    }

    private static void RequireUnexpired(PurchaseSelection[] quotes)
    {
        var now = DateTime.Now;
        if (quotes.Any(row => row.ValidUntil < now))
            throw new InvalidOperationException("E-PUR-068");
    }

    private sealed record PurchaseSelection(Guid QuoteId, int LineNumber, string SupplierId, decimal Price,
        string? Currency, DateTime? ValidUntil, Guid? RfqLineId, string ItemId, decimal? LineQuantity,
        DateTime? RequiredDate)
    {
        public decimal Quantity => LineQuantity!.Value;
    }
}

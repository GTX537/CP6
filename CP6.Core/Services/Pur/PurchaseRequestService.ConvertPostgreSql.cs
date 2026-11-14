using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

public partial class PurchaseRequestService
{
    private async Task<List<string>> ConvertToPoPostgreSqlAsync(string prNo, string? userName,
        Func<CancellationToken, Task<Guid>>? currentActor, CancellationToken ct)
    {
        if (_authority?.Owns(_db) != true || currentActor == null)
            throw new IamAdministrationExceptionV1(503, "AUTHORITY_FENCE_UNAVAILABLE");

        var suppliers = await CommitAsync(async token =>
        {
            await ReadApprovedRequestAsync(token);
            var selections = await ConvertibleLines().AsNoTracking().OrderBy(row => row.LineNo)
                .Select(row => row.SuggestSupplierId!).ToArrayAsync(token);
            if (selections.Length == 0) throw new InvalidOperationException("E-PUR-054");
            return selections.Distinct(StringComparer.Ordinal).ToArray();
        });

        var orders = new List<string>();
        foreach (var supplier in suppliers)
        {
            orders.Add(await CommitAsync(async token =>
            {
                var pr = await ReadApprovedRequestAsync(token);
                var lines = await ConvertibleLines().Where(row => row.SuggestSupplierId == supplier)
                    .OrderBy(row => row.LineNo).ToArrayAsync(token);
                if (lines.Length == 0)
                    throw new IamAdministrationExceptionV1(409, "PR_CONVERSION_SOURCE_CHANGED");

                var order = await CreateOrderAsync(pr, supplier, lines, userName);
                foreach (var line in lines)
                    line.ConvertedPoNo = order.PoNo;

                var converted = lines.Select(row => row.Id).ToArray();
                var hasRemaining = await _db.PurchaseRequestLines.AnyAsync(row => row.PrNo == prNo &&
                    !row.IsDeleted && row.Status == 0 && (row.ConvertedPoNo == null || row.ConvertedPoNo == "") &&
                    !converted.Contains(row.Id), token);
                if (!hasRemaining)
                    pr.Status = PrStatus.Converted;
                pr.Modifier = userName;
                pr.ModifyDate = DateTime.Now;
                await _db.SaveChangesAsync(token);
                return order.PoNo;
            }));
        }
        return orders;

        Task<T> CommitAsync<T>(Func<CancellationToken, Task<T>> body) =>
            _authority.ExecuteAsync("pur-pr:convert", currentActor, async (actor, token) =>
            {
                var result = await body(token);
                if (await currentActor(token) != actor)
                    throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                return result;
            }, ct);

        async Task<PurchaseRequest> ReadApprovedRequestAsync(CancellationToken token)
        {
            var pr = await _db.PurchaseRequests.SingleOrDefaultAsync(row => row.PrNo == prNo && !row.IsDeleted, token)
                ?? throw new InvalidOperationException("E-PUR-056");
            if (pr.Status != PrStatus.Approved)
                throw new InvalidOperationException("E-PUR-053");
            return pr;
        }

        IQueryable<PurchaseRequestLine> ConvertibleLines() => _db.PurchaseRequestLines.Where(row =>
            row.PrNo == prNo && !row.IsDeleted && row.Status == 0 &&
            !string.IsNullOrWhiteSpace(row.SuggestSupplierId) &&
            (row.ConvertedPoNo == null || row.ConvertedPoNo == ""));
    }
}

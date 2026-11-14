using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

public partial class RfqService
{
    private async Task<Rfq> WriteBackPostgreSqlAsync(string rfqNo, string? userName,
        Func<CancellationToken, Task<Guid>>? currentActor, CancellationToken ct)
    {
        var quotes = await CommitAsync(async token =>
        {
            if (!await _db.Rfqs.AsNoTracking().AnyAsync(row => row.RfqNo == rfqNo && !row.IsDeleted, token))
                throw new InvalidOperationException("E-PUR-061");
            return await _db.RfqQuotes.AsNoTracking()
                .Where(row => row.RfqNo == rfqNo && row.IsSelected && !row.IsDeleted)
                .OrderBy(row => row.LineNo).ThenBy(row => row.SupplierId)
                .Select(row => row.Id).ToArrayAsync(token);
        });

        var today = DateTime.Now.Date;
        // Each price retains its original commit boundary. A later denial preserves earlier prices.
        foreach (var quote in quotes)
        {
            await CommitAsync(async token =>
            {
                var selected = await (from row in _db.RfqQuotes.AsNoTracking()
                                      join line in _db.RfqLines.AsNoTracking()
                                          on new { row.RfqNo, row.LineNo } equals new { line.RfqNo, line.LineNo }
                                      where row.Id == quote && row.IsSelected && !row.IsDeleted && !line.IsDeleted
                                          && _db.Rfqs.Any(rfq => rfq.RfqNo == row.RfqNo && !rfq.IsDeleted)
                                      select new { Quote = row, line.ItemId }).SingleOrDefaultAsync(token);
                if (selected != null)
                    await _price.UpsertAsync(FromQuote(selected.Quote, selected.ItemId, today), userName);
                return selected != null;
            });
        }

        return await CommitAsync(async token =>
        {
            var rfq = await _db.Rfqs.SingleOrDefaultAsync(row => row.RfqNo == rfqNo && !row.IsDeleted, token)
                ?? throw new InvalidOperationException("E-PUR-061");
            return await CompleteWritebackAsync(rfq, userName, token);
        });

        Task<T> CommitAsync<T>(Func<CancellationToken, Task<T>> body) =>
            CommitRfqEffectAsync("pur-rfq:writeback", currentActor, body, ct);
    }

    private Task<T> CommitRfqEffectAsync<T>(string permission, Func<CancellationToken, Task<Guid>>? currentActor,
        Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (_authority?.Owns(_db) != true || currentActor == null)
            throw new IamAdministrationExceptionV1(503, "AUTHORITY_FENCE_UNAVAILABLE");

        return _authority.ExecuteAsync(permission, currentActor, async (actor, token) =>
        {
            var result = await body(token);
            if (await currentActor(token) != actor)
                throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
            return result;
        }, ct);
    }
}

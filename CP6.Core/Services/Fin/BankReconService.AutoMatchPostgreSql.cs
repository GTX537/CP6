using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public partial class BankReconService
{
    private async Task<FinResult> AutoMatchPostgreSqlAsync(Guid statementId, string? user,
        Func<CancellationToken, Task<Guid>>? currentActor, CancellationToken ct)
    {
        var authority = _authority;
        if (authority?.Owns(_db) != true || currentActor == null)
            throw new CurrentPermissionUnavailableException();

        var start = await CommitAsync(async token =>
        {
            var statement = await ReadStatement(token);
            var failure = StatementFailure(statement);
            if (failure != null) return (Failure: failure, Lines: Array.Empty<Guid>(), Candidates: Array.Empty<Guid>());
            var lines = await Unmatched().Select(l => l.Id).ToArrayAsync(token);
            var candidates = await LoadCandidateRowsAsync(statement!, DefaultWindowDays, token);
            return (Failure: (FinResult?)null, Lines: lines, Candidates: candidates.Select(c => c.JournalLineId).ToArray());
        });
        if (start.Failure != null) return start.Failure;

        // Each group's inputs are checked inside its own write transaction.
        foreach (var lineId in start.Lines)
        {
            var result = await CommitAsync<FinResult?>(async token =>
            {
                var statement = await ReadStatement(token);
                var failure = StatementFailure(statement);
                if (failure != null) return failure;

                var line = await Unmatched().SingleOrDefaultAsync(l => l.Id == lineId, token);
                if (line == null) return null;
                var candidates = await LoadCandidateRowsAsync(statement!, DefaultWindowDays, token,
                    statementLine: line, eligibleCandidateIds: start.Candidates);
                var group = FindUniqueJournalMatch(line, candidates);
                if (group != null)
                    await PersistMatchAsync(statement!, [line], group, BankReconMatchType.Auto, null, user, token);
                return null;
            });
            if (result != null) return result;
        }

        foreach (var candidateId in start.Candidates)
        {
            var result = await CommitAsync<FinResult?>(async token =>
            {
                var statement = await ReadStatement(token);
                var failure = StatementFailure(statement);
                if (failure != null) return failure;

                var candidates = await LoadCandidateRowsAsync(statement!, DefaultWindowDays, token, candidateId: candidateId);
                var candidate = candidates.SingleOrDefault();
                if (candidate == null) return null;
                var date = candidate.VoucherDate;
                var sign = Math.Sign(candidate.BankSignedAmount);
                var lines = await Unmatched()
                    .Where(l => start.Lines.Contains(l.Id)
                        && (sign > 0 ? l.SignedAmount > 0 : sign < 0 ? l.SignedAmount < 0 : l.SignedAmount == 0))
                    .OrderBy(l => Math.Abs((l.TxnDate - date).Days)).ThenBy(l => l.LineNo)
                    .Take(AutoMatchPoolSize).ToListAsync(token);
                var group = FindUniqueStatementMatch(candidate, lines);
                if (group != null)
                    await PersistMatchAsync(statement!, group, [candidate], BankReconMatchType.Auto, null, user, token);
                return null;
            });
            if (result != null) return result;
        }
        return FinResult.Pass();

        Task<BankStatement?> ReadStatement(CancellationToken token) =>
            _db.BankStatements.AsNoTracking().SingleOrDefaultAsync(s => s.Id == statementId, token);

        IOrderedQueryable<BankStatementLine> Unmatched() => _db.BankStatementLines
            .Where(l => l.StatementId == statementId && l.MatchStatus == BankLineMatchStatus.Unmatched)
            .OrderBy(l => l.LineNo);

        Task<T> CommitAsync<T>(Func<CancellationToken, Task<T>> body) =>
            authority.ExecuteAsync("fin-bank-reconciliation:match", currentActor, async (actor, token) =>
            {
                var result = await body(token);
                if (await currentActor(token) != actor)
                    throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                return result;
            }, ct);
    }

    private static FinResult? StatementFailure(BankStatement? statement)
    {
        if (statement == null) return FinResult.Fail("E-A4-MATCH-004");
        return statement.Status == BankStatementStatus.Open ? null : FinResult.Fail("E-A4-STATEMENT-LOCKED");
    }
}

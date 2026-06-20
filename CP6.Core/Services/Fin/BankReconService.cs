using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Fin;

public class BankReconService : IBankReconService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;    // used by C-2 AutoMatch
    private readonly IFiscalPeriodService _period;     // used by C-2 AutoMatch
    private const int DefaultWindowDays = 90;
    private const int SubsetSumK = 8;                  // used by C-2 AutoMatch
    // must exceed the max possible date-distance so amount-match always outranks date-closeness
    private const int AmountMismatchPenalty = 100_000;

    public BankReconService(CP6Context db, IJournalEntryService journal, IFiscalPeriodService period)
    { _db = db; _journal = journal; _period = period; }

    public async Task<List<BankCandidateLine>> GetCandidatesAsync(Guid statementId, Guid statementLineId, bool widen)
    {
        var stmt = await _db.BankStatements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        var line = await _db.BankStatementLines.AsNoTracking().FirstAsync(x => x.Id == statementLineId);
        var raw = await LoadCandidateRowsAsync(stmt, widen ? null : DefaultWindowDays);
        // 按 (金额接近 + 日期接近) 排序，金额完全相等优先
        raw.ForEach(c => c.Rank = Math.Abs((c.VoucherDate - line.TxnDate).Days)
            + (c.BankSignedAmount == line.SignedAmount ? 0 : AmountMismatchPenalty));
        return raw.OrderBy(c => c.Rank).ThenBy(c => c.VoucherDate).ToList();
    }

    /// <summary>账面侧候选来源（spec §4.2）：命中银行GL、Posted、未反转、未占用、VoucherDate≤PeriodEnd、窗口、外币原币规则。</summary>
    private async Task<List<BankCandidateLine>> LoadCandidateRowsAsync(BankStatement stmt, int? windowDays)
    {
        var acct = await _db.BankAccounts.AsNoTracking().FirstAsync(x => x.Id == stmt.BankAccountId);
        var isForeign = !FxConstants.IsBase(acct.CurrencyCd);
        var occupied = _db.BankReconJournalLinks.Select(x => x.JournalLineId);
        var lowerDate = windowDays is int w ? stmt.PeriodStart.AddDays(-w) : DateTime.MinValue;

        var rows = await (from jl in _db.JournalLines.AsNoTracking()
                          join je in _db.JournalEntries.AsNoTracking() on jl.EntryId equals je.Id
                          where jl.AccountId == acct.GlAccountId
                                && je.Status == JournalStatus.Posted
                                && je.Source != VoucherSource.Reversal
                                && je.VoucherDate <= stmt.PeriodEnd
                                && je.VoucherDate >= lowerDate
                                && !occupied.Contains(jl.Id)
                          select new { jl, je }).ToListAsync();

        var list = new List<BankCandidateLine>();
        foreach (var r in rows)
        {
            decimal bankSigned;
            if (isForeign)
            {
                if (r.jl.OrigAmount is not decimal orig || r.jl.CurrencyCd != acct.CurrencyCd)
                    continue;   // 外币：缺原币/币种不符 → 不进自动候选（§4.2/§6）
                bankSigned = r.jl.Debit > 0 ? orig : -orig;
            }
            else
            {
                bankSigned = r.jl.Debit - r.jl.Credit;   // 本位币 Debit=+,Credit=−
            }
            list.Add(new BankCandidateLine
            {
                JournalLineId = r.jl.Id, JournalEntryId = r.je.Id, EntryNo = r.je.No,
                VoucherDate = r.je.VoucherDate, BankSignedAmount = bankSigned,
                CurrencyCd = r.jl.CurrencyCd, PartnerId = r.jl.PartnerId, Memo = r.jl.Memo,
            });
        }
        return list;
    }

    // ── C-2: AutoMatchAsync ──

    public async Task<FinResult> AutoMatchAsync(Guid statementId, string? user)
    {
        var stmt = await _db.BankStatements.FirstOrDefaultAsync(x => x.Id == statementId);
        if (stmt == null) return FinResult.Fail("E-A4-MATCH-004");
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");

        var candidates = await LoadCandidateRowsAsync(stmt, DefaultWindowDays);
        var occupiedNow = new HashSet<Guid>();   // 本轮已被占用的凭证行（防同轮重复占用）

        var unmatched = await _db.BankStatementLines
            .Where(x => x.StatementId == statementId && x.MatchStatus == BankLineMatchStatus.Unmatched)
            .OrderBy(x => x.LineNo).ToListAsync();

        // ── Phase 1 + Phase 2（1:N）合并：有界子集和，唯一解（含 size==1 的精确 1:1 情况）──
        // 正确性要求：必须在所有子集大小上整体判断唯一性；将 Phase1 与 Phase2 拆开会导致
        // 「单行精确候选 + 多行组合候选同时满足」时错误地在 Phase1 自动匹配（多解场景）。
        foreach (var line in unmatched.ToList())
        {
            // 同方向且未被本轮占用的候选作为搜索池（按日期接近限 20 条，性能护栏）
            var pool = candidates
                .Where(c => !occupiedNow.Contains(c.JournalLineId)
                            && Math.Sign(c.BankSignedAmount) == Math.Sign(line.SignedAmount))
                .OrderBy(c => Math.Abs((c.VoucherDate - line.TxnDate).Days))
                .Take(20)
                .ToList();

            // 在池中找所有 ΣBankSignedAmount==target 的子集（size 1..K）
            var solutions = FindSubsetSums(pool, line.SignedAmount, SubsetSumK);

            if (solutions.Count == 1)
            {
                // 唯一解（无论 size 1 = Phase1 精确，还是 size ≥2 = Phase2 组合）→ 自动撮合
                await PersistMatchAsync(stmt, line, solutions[0], BankReconMatchType.Auto, null, user);
                foreach (var c in solutions[0]) occupiedNow.Add(c.JournalLineId);
                unmatched.Remove(line);
            }
            // 0 解或 ≥2 解 → 留人工
        }

        // ── Phase 2b：N:1（多流水 ↔ 单凭证，合并收款），唯一解（spec §4.4 / AC-004）──
        foreach (var cand in candidates.Where(c => !occupiedNow.Contains(c.JournalLineId)).ToList())
        {
            // 同方向、剔已占用、按日期接近排序的未匹配流水池
            var pool = unmatched
                .Where(l => Math.Sign(l.SignedAmount) == Math.Sign(cand.BankSignedAmount))
                .OrderBy(l => Math.Abs((l.TxnDate - cand.VoucherDate).Days))
                .Take(20)
                .ToList();   // 性能护栏

            var sols = FindStmtSubsetSums(pool, cand.BankSignedAmount, SubsetSumK);

            if (sols.Count == 1 && sols[0].Count >= 2)   // ≥2 才是 N:1（size==1 已由上方合并的 1:1/1:N 阶段覆盖）
            {
                await PersistMatchAsync(stmt, sols[0], new[] { cand }, BankReconMatchType.Auto, null, user);
                occupiedNow.Add(cand.JournalLineId);
                foreach (var l in sols[0]) unmatched.Remove(l);
            }
            // 多解/无解 → 留人工
        }

        return FinResult.Pass();
    }

    /// <summary>有界子集和：在 pool 中找 ΣBankSignedAmount==target、大小≤K 的所有子集（绝不无界）。返回全部解（>1 即早停判唯一）。</summary>
    private static List<List<BankCandidateLine>> FindSubsetSums(List<BankCandidateLine> pool, decimal target, int k)
    {
        var solutions = new List<List<BankCandidateLine>>();
        var current = new List<BankCandidateLine>();
        void Dfs(int start, decimal sum)
        {
            if (current.Count > k) return;
            if (current.Count >= 1 && sum == target) { solutions.Add(new List<BankCandidateLine>(current)); }
            if (solutions.Count > 1) return;   // 一旦 >1 解即可早停（只需判定唯一性）
            for (int i = start; i < pool.Count; i++)
            {
                current.Add(pool[i]);
                Dfs(i + 1, sum + pool[i].BankSignedAmount);
                current.RemoveAt(current.Count - 1);
                if (solutions.Count > 1) return;
            }
        }
        Dfs(0, 0m);
        return solutions;
    }

    /// <summary>有界子集和（流水侧，N:1 用）：在 pool 中找 ΣSignedAmount==target、大小≤K 的所有子集。返回全部解（>1 即早停判唯一）。</summary>
    private static List<List<BankStatementLine>> FindStmtSubsetSums(List<BankStatementLine> pool, decimal target, int k)
    {
        var solutions = new List<List<BankStatementLine>>();
        var current = new List<BankStatementLine>();
        void Dfs(int start, decimal sum)
        {
            if (current.Count > k) return;
            if (current.Count >= 1 && sum == target) solutions.Add(new List<BankStatementLine>(current));
            if (solutions.Count > 1) return;
            for (int i = start; i < pool.Count; i++)
            {
                current.Add(pool[i]);
                Dfs(i + 1, sum + pool[i].SignedAmount);
                current.RemoveAt(current.Count - 1);
                if (solutions.Count > 1) return;
            }
        }
        Dfs(0, 0m);
        return solutions;
    }

    /// <summary>单流水行重载：委托给多流水行版本。</summary>
    private async Task PersistMatchAsync(BankStatement stmt, BankStatementLine line, IReadOnlyList<BankCandidateLine> cands,
        BankReconMatchType type, string? note, string? user)
        => await PersistMatchAsync(stmt, new[] { line }, cands, type, note, user);

    /// <summary>把一组流水行 ↔ 一组凭证候选落库为 BankReconMatch + Link（自动撮合每组独立 SaveChanges）。</summary>
    private async Task PersistMatchAsync(BankStatement stmt, IReadOnlyList<BankStatementLine> lines,
        IReadOnlyList<BankCandidateLine> cands, BankReconMatchType type, string? note, string? user)
    {
        var stmtSum = lines.Sum(l => l.SignedAmount);
        var match = new BankReconMatch
        {
            Id = Guid.NewGuid(), StatementId = stmt.Id, MatchType = type, StmtSignedSum = stmtSum,
            MatchedAt = DateTime.Now, MatchedBy = user ?? "system", Note = note,
            Creator = user, CreateDate = DateTime.Now,
        };
        _db.BankReconMatches.Add(match);
        foreach (var c in cands)
            _db.BankReconJournalLinks.Add(new BankReconJournalLink
            {
                Id = Guid.NewGuid(), MatchGroupId = match.Id, JournalLineId = c.JournalLineId,
                JournalEntryId = c.JournalEntryId, BankSignedAmount = c.BankSignedAmount,
                Creator = user, CreateDate = DateTime.Now,
            });
        foreach (var l in lines)
        {
            var tracked = await _db.BankStatementLines.FirstAsync(x => x.Id == l.Id);
            tracked.MatchStatus = BankLineMatchStatus.Matched;
            tracked.MatchGroupId = match.Id;
            tracked.Modifier = user; tracked.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    // ── C-3: ManualMatchAsync + UnmatchAsync ──

    public async Task<FinResult> ManualMatchAsync(ManualMatchRequest req, byte[]? stmtLineRowVersion, string? user)
    {
        var stmt = await _db.BankStatements.FirstOrDefaultAsync(x => x.Id == req.StatementId);
        if (stmt == null) return FinResult.Fail("E-A4-MATCH-004");
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");
        if (req.StatementLineIds.Count == 0 || req.JournalLineIds.Count == 0) return FinResult.Fail("E-A4-MATCH-001");

        var acct = await _db.BankAccounts.AsNoTracking().FirstAsync(x => x.Id == stmt.BankAccountId);
        var isForeign = !FxConstants.IsBase(acct.CurrencyCd);   // Bug1 fix: 与 LoadCandidateRowsAsync 保持一致

        // 流水行：同一会话、未占用
        var lines = await _db.BankStatementLines
            .Where(x => req.StatementLineIds.Contains(x.Id)).ToListAsync();
        if (lines.Count != req.StatementLineIds.Count || lines.Any(l => l.StatementId != req.StatementId))
            return FinResult.Fail("E-A4-MATCH-004");
        if (lines.Any(l => l.MatchStatus == BankLineMatchStatus.Matched)) return FinResult.Fail("E-A4-MATCH-005");

        // 凭证行：命中银行GL、Posted未反转、未占用
        var jls = await (from jl in _db.JournalLines
                         join je in _db.JournalEntries on jl.EntryId equals je.Id
                         where req.JournalLineIds.Contains(jl.Id)
                         select new { jl, je }).ToListAsync();
        if (jls.Count != req.JournalLineIds.Count) return FinResult.Fail("E-A4-MATCH-004");
        if (jls.Any(x => x.jl.AccountId != acct.GlAccountId)) return FinResult.Fail("E-A4-MATCH-004");
        if (jls.Any(x => x.je.Status != JournalStatus.Posted || x.je.Source == VoucherSource.Reversal))
            return FinResult.Fail("E-A4-MATCH-003");
        var alreadyOccupied = await _db.BankReconJournalLinks
            .Where(x => req.JournalLineIds.Contains(x.JournalLineId)).AnyAsync();
        if (alreadyOccupied) return FinResult.Fail("E-A4-MATCH-002");

        // Σ 完全相等（外币按原币）；Bug2 fix: 用 foreach 替代 LINQ 投影，确保错误路径 return FinResult.Fail 而非 throw
        var cands = new List<BankCandidateLine>(jls.Count);
        decimal bookSum = 0m;
        foreach (var x in jls)
        {
            decimal signed;
            if (isForeign)
            {
                if (x.jl.OrigAmount is not decimal orig || x.jl.CurrencyCd != acct.CurrencyCd)
                    return FinResult.Fail("E-A4-MATCH-003");   // 缺原币/币种不符 → 优雅失败
                signed = x.jl.Debit > 0 ? orig : -orig;
            }
            else
            {
                signed = x.jl.Debit - x.jl.Credit;
            }
            cands.Add(new BankCandidateLine { JournalLineId = x.jl.Id, JournalEntryId = x.je.Id, BankSignedAmount = signed });
            bookSum += signed;
        }
        var stmtSum = lines.Sum(l => l.SignedAmount);
        if (stmtSum != bookSum) return FinResult.Fail("E-A4-MATCH-001");

        // RowVersion 乐观并发（前端带其中一条流水行版本）
        if (stmtLineRowVersion != null)
        {
            var primary = lines[0];
            _db.Entry(primary).Property(x => x.RowVersion).OriginalValue = stmtLineRowVersion;
        }
        try { await PersistMatchAsync(stmt, lines, cands, BankReconMatchType.Manual, req.Note, user); }
        catch (DbUpdateConcurrencyException) { return FinResult.Fail("E-A4-CONCURRENCY-001"); }
        catch (DbUpdateException) { return FinResult.Fail("E-A4-MATCH-002"); }   // 唯一约束(JL占用)兜底
        return FinResult.Pass();
    }

    public async Task<FinResult> UnmatchAsync(Guid groupId, string? user)
    {
        var match = await _db.BankReconMatches.FirstOrDefaultAsync(x => x.Id == groupId);
        if (match == null) return FinResult.Fail("E-A4-MATCH-004");
        var stmt = await _db.BankStatements.FirstAsync(x => x.Id == match.StatementId);
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");

        var lines = await _db.BankStatementLines.Where(x => x.MatchGroupId == groupId).ToListAsync();
        foreach (var l in lines)
        {
            l.MatchStatus = BankLineMatchStatus.Unmatched;
            l.MatchGroupId = null;
            l.Modifier = user; l.ModifyDate = DateTime.Now;
            // 若组关联了 BankRecon 自动凭证：不自动删凭证（走反冲，§4.5/§5.1）；GeneratedJournalEntryId 由 D-2 反冲流程清
        }
        var links = await _db.BankReconJournalLinks.Where(x => x.MatchGroupId == groupId).ToListAsync();
        _db.BankReconJournalLinks.RemoveRange(links);
        _db.BankReconMatches.Remove(match);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
    public async Task<List<BankOnlyLineResult>> GenerateBankOnlyVoucherAsync(Guid statementId, List<Guid> lineIds,
        Guid counterAccountId, string? counterRole, string? partnerId, string? user)
    {
        var results = new List<BankOnlyLineResult>();
        var stmt = await _db.BankStatements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == statementId);
        if (stmt == null) { foreach (var id in lineIds) results.Add(new() { LineId = id, Ok = false, Code = "E-A4-MATCH-004" }); return results; }
        if (stmt.Status != BankStatementStatus.Open)
        { foreach (var id in lineIds) results.Add(new() { LineId = id, Ok = false, Code = "E-A4-STATEMENT-LOCKED" }); return results; }

        var acct = await _db.BankAccounts.AsNoTracking().FirstAsync(x => x.Id == stmt.BankAccountId);
        // 对方科目：显式 Id 优先，否则按 Role 解析
        Guid? counterId = counterAccountId != Guid.Empty ? counterAccountId
            : (counterRole != null ? (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == counterRole && a.IsActive && a.IsLeaf))?.Id : null);

        foreach (var lineId in lineIds)
        {
            var line = await _db.BankStatementLines.FirstOrDefaultAsync(x => x.Id == lineId && x.StatementId == statementId);
            if (line == null) { results.Add(new() { LineId = lineId, Ok = false, Code = "E-A4-MATCH-004" }); continue; }
            if (line.MatchStatus == BankLineMatchStatus.Matched || line.GeneratedJournalEntryId != null)
            { results.Add(new() { LineId = lineId, Ok = false, Code = "E-A4-BANKONLY-DUP" }); continue; }   // 幂等
            if (counterId is not Guid cAcc)
            { results.Add(new() { LineId = lineId, Ok = false, Code = "E-A4-MATCH-003" }); continue; }

            // ── 单条事务（spec §5.1 点6）：过账→写回→建组→建Link→改状态 任一失败整体回滚 ──
            // InMemory provider 不支持真实事务 → 检测 ProviderName 后跳过 BeginTransactionAsync
            var isInMemory = _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            IDbContextTransaction? tx = isInMemory ? null : await _db.Database.BeginTransactionAsync();
            try
            {
                await _period.EnsureOpenAsync(line.TxnDate, user);
                // 凭证方向：Withdrawal 借对方/贷银行；Deposit 借银行/贷对方
                var bankLine = new JournalLine { AccountId = acct.GlAccountId, PartnerId = null };
                var counterLine = new JournalLine { AccountId = cAcc, PartnerId = partnerId };
                if (line.Direction == BankLineDirection.Withdrawal)
                { counterLine.Debit = line.Amount; bankLine.Credit = line.Amount; }
                else
                { bankLine.Debit = line.Amount; counterLine.Credit = line.Amount; }

                var entry = new JournalEntry
                {
                    Id = Guid.NewGuid(), VoucherDate = line.TxnDate, Source = VoucherSource.BankRecon,
                    SourceDocNo = stmt.No, Description = $"银行对账单边项 {stmt.No} 行{line.LineNo}：{line.Description}",
                    Lines = { bankLine, counterLine },
                };
                var post = await _journal.AutoPostAsync(entry);
                if (!post.Ok) { if (tx != null) await tx.RollbackAsync(); results.Add(new() { LineId = lineId, Ok = false, Code = post.Code }); continue; }

                // 重新取该凭证的银行GL行 Id
                var newBankJl = await _db.JournalLines.FirstAsync(l => l.EntryId == entry.Id && l.AccountId == acct.GlAccountId);

                line.GeneratedJournalEntryId = entry.Id;
                line.GeneratedAt = DateTime.Now; line.GeneratedBy = user;
                line.Category = line.Direction == BankLineDirection.Withdrawal ? BankLineCategory.BankCharge : BankLineCategory.InterestIncome;

                var match = new BankReconMatch { Id = Guid.NewGuid(), StatementId = statementId, MatchType = BankReconMatchType.Auto,
                    StmtSignedSum = line.SignedAmount, MatchedAt = DateTime.Now, MatchedBy = user ?? "system", Creator = user, CreateDate = DateTime.Now };
                _db.BankReconMatches.Add(match);
                _db.BankReconJournalLinks.Add(new BankReconJournalLink { Id = Guid.NewGuid(), MatchGroupId = match.Id,
                    JournalLineId = newBankJl.Id, JournalEntryId = entry.Id, BankSignedAmount = newBankJl.Debit - newBankJl.Credit, Creator = user, CreateDate = DateTime.Now });
                line.MatchStatus = BankLineMatchStatus.Matched; line.MatchGroupId = match.Id;
                line.Modifier = user; line.ModifyDate = DateTime.Now;

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                results.Add(new() { LineId = lineId, Ok = true, JournalEntryId = entry.Id });
            }
            catch (Exception)
            {
                if (tx != null) await tx.RollbackAsync();
                results.Add(new() { LineId = lineId, Ok = false, Code = "E-A4-BANKONLY-DUP" });
            }
            finally
            {
                tx?.Dispose();
            }
        }
        return results;
    }
    public Task<FinResult> MarkPendingAsync(Guid statementId, List<Guid> lineIds, BankLineCategory category, byte[]? rowVersion, string? user) => throw new NotImplementedException();
    public Task<ReconciliationStatementDto> GetReconciliationStatementAsync(Guid statementId) => throw new NotImplementedException();
    public Task<FinResult> LockAsync(Guid statementId, string? user) => throw new NotImplementedException();
    public Task<FinResult> UnlockAsync(Guid statementId, string reason, string? user) => throw new NotImplementedException();
}

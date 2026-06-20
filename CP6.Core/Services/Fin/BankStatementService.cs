using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BankStatementService : IBankStatementService
{
    private readonly CP6Context _db;
    private readonly IFiscalPeriodService _period;
    private readonly IFinSequenceService _seq;
    private readonly IBankStatementImporter _importer;

    public BankStatementService(CP6Context db, IFiscalPeriodService period,
        IFinSequenceService seq, IBankStatementImporter importer)
    { _db = db; _period = period; _seq = seq; _importer = importer; }

    // ── Profile ──
    public async Task<List<BankImportProfile>> ListProfilesAsync(Guid? bankAccountId = null)
    {
        var q = _db.BankImportProfiles.AsNoTracking().AsQueryable();
        if (bankAccountId is Guid b) q = q.Where(x => x.BankAccountId == null || x.BankAccountId == b);
        return await q.OrderBy(x => x.Name).ToListAsync();
    }

    public async Task UpsertProfileAsync(BankImportProfile dto, string? user)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("E-A4-IMPORT-001: 模板名必填");
        var existing = dto.Id != Guid.Empty
            ? await _db.BankImportProfiles.FirstOrDefaultAsync(x => x.Id == dto.Id) : null;
        if (existing == null)
        {
            dto.Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            dto.Creator = user; dto.CreateDate = DateTime.Now;
            _db.BankImportProfiles.Add(dto);
        }
        else
        {
            var keepTenant = existing.TenantId;
            var keepCreator = existing.Creator;
            var keepCreated = existing.CreateDate;
            _db.Entry(existing).CurrentValues.SetValues(dto);
            existing.TenantId = keepTenant;
            existing.Creator = keepCreator;
            existing.CreateDate = keepCreated;
            existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteProfileAsync(Guid id, string? user)
    {
        var row = await _db.BankImportProfiles.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("E-A4-IMPORT-001: 模板不存在");
        _db.BankImportProfiles.Remove(row);
        await _db.SaveChangesAsync();
    }

    // ── 会话 / 导入 / 手工行：B-2 实现 ──

    public async Task<List<BankStatement>> ListAsync(Guid? bankAccountId, Guid? fiscalPeriodId, BankStatementStatus? status)
    {
        var q = _db.BankStatements.AsNoTracking().AsQueryable();
        if (bankAccountId is Guid b) q = q.Where(x => x.BankAccountId == b);
        if (fiscalPeriodId is Guid f) q = q.Where(x => x.FiscalPeriodId == f);
        if (status is BankStatementStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.PeriodStart).ToListAsync();
    }

    public Task<BankStatement?> GetAsync(Guid id) => _db.BankStatements.FirstOrDefaultAsync(x => x.Id == id);

    public Task<List<BankStatementLine>> GetLinesAsync(Guid statementId) =>
        _db.BankStatementLines.AsNoTracking().Where(x => x.StatementId == statementId)
            .OrderBy(x => x.LineNo).ToListAsync();

    public async Task<FinResult> CreateAsync(BankStatement dto, string? user)
    {
        var acct = await _db.BankAccounts.FirstOrDefaultAsync(x => x.Id == dto.BankAccountId && x.IsActive);
        if (acct == null) return FinResult.Fail("E-A4-MATCH-004");
        var period = await _db.FiscalPeriods.FirstOrDefaultAsync(x => x.Id == dto.FiscalPeriodId);
        if (period == null) return FinResult.Fail("E-A4-RECON-002");
        // 每账户每期一个会话（DB 唯一索引兜底，先内存查）
        if (await _db.BankStatements.AnyAsync(x => x.BankAccountId == dto.BankAccountId && x.FiscalPeriodId == dto.FiscalPeriodId))
            return FinResult.Fail("E-A4-MATCH-004");
        dto.Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
        dto.No = await _seq.NextAsync("BKR", period.PeriodStart);
        dto.CurrencyCd = acct.CurrencyCd;
        dto.PeriodStart = period.PeriodStart; dto.PeriodEnd = period.PeriodEnd;
        dto.Status = BankStatementStatus.Open;
        dto.Creator = user; dto.CreateDate = DateTime.Now;
        _db.BankStatements.Add(dto);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<BankImportPreviewResult> PreviewAsync(Guid statementId, Guid profileId, Stream file, string fileName)
    {
        var profile = await _db.BankImportProfiles.AsNoTracking().FirstAsync(x => x.Id == profileId);
        var parsed = _importer.Parse(profile, file, fileName);
        var (existHash, existFp) = await ExistingHashSetsAsync(statementId);
        return BuildPreview(parsed, existHash, existFp);
    }

    public async Task<FinResult> ConfirmImportAsync(Guid statementId, Guid profileId, Stream file, string fileName, string? user)
    {
        var stmt = await _db.BankStatements.FirstOrDefaultAsync(x => x.Id == statementId);
        if (stmt == null) return FinResult.Fail("E-A4-IMPORT-002");
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");

        var profile = await _db.BankImportProfiles.AsNoTracking().FirstAsync(x => x.Id == profileId);
        var parsed = _importer.Parse(profile, file, fileName);
        if (parsed.HasFatalParseError) return FinResult.Fail("E-A4-IMPORT-001");   // 整批拒绝，无部分落库（§3.3）

        var (existHash, existFp) = await ExistingHashSetsAsync(statementId);
        var batchNo = await _seq.NextAsync("BKRIMP", DateTime.Today);
        var maxLineNo = await _db.BankStatementLines.Where(x => x.StatementId == statementId)
            .Select(x => (int?)x.LineNo).MaxAsync() ?? 0;

        foreach (var r in parsed.Rows)
        {
            if (existHash.Contains(r.RawRowHash) || existFp.Contains(r.Fingerprint)) continue;  // 强重复跳过
            var line = new BankStatementLine
            {
                Id = Guid.NewGuid(), StatementId = statementId, LineNo = ++maxLineNo,
                TxnDate = r.TxnDate, Direction = (BankLineDirection)r.Direction, Amount = r.Amount,
                CurrencyCd = r.CurrencyCd ?? stmt.CurrencyCd, Description = r.Description,
                CounterpartyName = r.CounterpartyName, RefNo = r.RefNo, BalanceAfter = r.BalanceAfter,
                Source = BankLineSource.Imported, MatchStatus = BankLineMatchStatus.Unmatched,
                ImportBatchNo = batchNo, RawRowJson = r.RawRowJson, RawRowHash = r.RawRowHash, Fingerprint = r.Fingerprint,
                Creator = user, CreateDate = DateTime.Now,
            };
            line.RecomputeSigned();
            _db.BankStatementLines.Add(line);
            existHash.Add(r.RawRowHash); existFp.Add(r.Fingerprint);
        }
        stmt.ImportFileName = fileName;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    // ── 手工行 ──
    public async Task<FinResult> AddLineAsync(Guid statementId, BankStatementLine line, string? user)
    {
        var stmt = await _db.BankStatements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == statementId);
        if (stmt == null) return FinResult.Fail("E-A4-MATCH-004");
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");
        var maxLineNo = await _db.BankStatementLines.Where(x => x.StatementId == statementId).Select(x => (int?)x.LineNo).MaxAsync() ?? 0;
        line.Id = Guid.NewGuid(); line.StatementId = statementId; line.LineNo = maxLineNo + 1;
        line.Source = BankLineSource.Manual; line.MatchStatus = BankLineMatchStatus.Unmatched;
        line.CurrencyCd ??= stmt.CurrencyCd;
        line.RecomputeSigned();
        line.Creator = user; line.CreateDate = DateTime.Now;
        _db.BankStatementLines.Add(line);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> UpdateLineAsync(Guid statementId, Guid lineId, BankStatementLine line, byte[]? rowVersion, string? user)
    {
        var existing = await _db.BankStatementLines.FirstOrDefaultAsync(x => x.Id == lineId && x.StatementId == statementId);
        if (existing == null) return FinResult.Fail("E-A4-MATCH-004");
        var stmt = await _db.BankStatements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");
        if (existing.MatchStatus == BankLineMatchStatus.Matched) return FinResult.Fail("E-A4-MATCH-005");   // 已匹配须先 Unmatch
        if (rowVersion != null) _db.Entry(existing).Property(x => x.RowVersion).OriginalValue = rowVersion;
        existing.TxnDate = line.TxnDate; existing.Direction = line.Direction; existing.Amount = line.Amount;
        existing.Description = line.Description; existing.CounterpartyName = line.CounterpartyName;
        existing.RefNo = line.RefNo; existing.BalanceAfter = line.BalanceAfter; existing.CurrencyCd = line.CurrencyCd ?? stmt.CurrencyCd;
        existing.RecomputeSigned();
        existing.Modifier = user; existing.ModifyDate = DateTime.Now;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return FinResult.Fail("E-A4-CONCURRENCY-001"); }
        return FinResult.Pass();
    }

    public async Task<FinResult> DeleteLineAsync(Guid statementId, Guid lineId, string? user)
    {
        var existing = await _db.BankStatementLines.FirstOrDefaultAsync(x => x.Id == lineId && x.StatementId == statementId);
        if (existing == null) return FinResult.Fail("E-A4-MATCH-004");
        var stmt = await _db.BankStatements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        if (stmt.Status != BankStatementStatus.Open) return FinResult.Fail("E-A4-STATEMENT-LOCKED");
        if (existing.MatchStatus == BankLineMatchStatus.Matched) return FinResult.Fail("E-A4-MATCH-005");
        _db.BankStatementLines.Remove(existing);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    // ── 私有助手 ──
    private async Task<(HashSet<string> Hash, HashSet<string> Fp)> ExistingHashSetsAsync(Guid statementId)
    {
        var rows = await _db.BankStatementLines.AsNoTracking().Where(x => x.StatementId == statementId)
            .Select(x => new { x.RawRowHash, x.Fingerprint }).ToListAsync();
        return (rows.Where(x => x.RawRowHash != null).Select(x => x.RawRowHash!).ToHashSet(),
                rows.Where(x => x.Fingerprint != null).Select(x => x.Fingerprint!).ToHashSet());
    }

    private static BankImportPreviewResult BuildPreview(BankImportParseResult parsed,
        HashSet<string> existHash, HashSet<string> existFp)
    {
        var res = new BankImportPreviewResult { Errors = parsed.Errors, FailedCount = parsed.Errors.Count };
        // 跨会话去重：分别持有 RawRowHash 集和 Fingerprint 集，与 ConfirmImportAsync 逻辑对称
        var seen = new HashSet<string>(existHash);          // RawRowHash（含已存在 + 批内累积）
        var seenFp = new HashSet<string>(existFp);         // Fingerprint（含已存在 + 批内累积）
        var seenKey = new HashSet<string>();               // (TxnDate+Direction+Amount+RefNo) 疑似重复键
        foreach (var r in parsed.Rows)
        {
            var key = $"{r.TxnDate:yyyyMMdd}|{r.Direction}|{r.Amount}|{r.RefNo}";
            if (seen.Contains(r.RawRowHash) || seenFp.Contains(r.Fingerprint))
            { r.DupKind = "Strong"; r.Importable = false; res.StrongDupCount++; }
            else if (seenKey.Contains(key))
            { r.DupKind = "Suspected"; r.Importable = true; res.SuspectedDupCount++; }
            else res.SuccessCount++;
            seen.Add(r.RawRowHash); seenFp.Add(r.Fingerprint); seenKey.Add(key);
            res.Rows.Add(r);
        }
        return res;
    }
}

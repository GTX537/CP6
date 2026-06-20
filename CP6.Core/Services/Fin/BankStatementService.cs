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
            _db.Entry(existing).CurrentValues.SetValues(dto);
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

    // ── 会话 / 导入 / 手工行：B-2 + C/F 实现 ──
    public Task<List<BankStatement>> ListAsync(Guid? bankAccountId, Guid? fiscalPeriodId, BankStatementStatus? status) => throw new NotImplementedException();
    public Task<BankStatement?> GetAsync(Guid id) => throw new NotImplementedException();
    public Task<List<BankStatementLine>> GetLinesAsync(Guid statementId) => throw new NotImplementedException();
    public Task<FinResult> CreateAsync(BankStatement dto, string? user) => throw new NotImplementedException();
    public Task<BankImportPreviewResult> PreviewAsync(Guid statementId, Guid profileId, Stream file, string fileName) => throw new NotImplementedException();
    public Task<FinResult> ConfirmImportAsync(Guid statementId, Guid profileId, Stream file, string fileName, string? user) => throw new NotImplementedException();
    public Task<FinResult> AddLineAsync(Guid statementId, BankStatementLine line, string? user) => throw new NotImplementedException();
    public Task<FinResult> UpdateLineAsync(Guid statementId, Guid lineId, BankStatementLine line, byte[]? rowVersion, string? user) => throw new NotImplementedException();
    public Task<FinResult> DeleteLineAsync(Guid statementId, Guid lineId, string? user) => throw new NotImplementedException();
}

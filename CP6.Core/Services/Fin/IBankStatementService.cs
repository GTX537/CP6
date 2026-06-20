using CP6.Entity.DomainModels.Fin;
namespace CP6.Core.Services.Fin;

public interface IBankStatementService
{
    // ── Profile（导入模板）──
    Task<List<BankImportProfile>> ListProfilesAsync(Guid? bankAccountId = null);
    Task<FinResult> UpsertProfileAsync(BankImportProfile dto, string? user);
    Task<FinResult> DeleteProfileAsync(Guid id, string? user);

    // ── 会话 ──
    Task<List<BankStatement>> ListAsync(Guid? bankAccountId, Guid? fiscalPeriodId, BankStatementStatus? status);
    Task<BankStatement?> GetAsync(Guid id);
    Task<List<BankStatementLine>> GetLinesAsync(Guid statementId);
    Task<FinResult> CreateAsync(BankStatement dto, string? user);

    // ── 导入 ──
    Task<BankImportPreviewResult> PreviewAsync(Guid statementId, Guid profileId, Stream file, string fileName);
    Task<FinResult> ConfirmImportAsync(Guid statementId, Guid profileId, Stream file, string fileName, string? user);

    // ── 手工行 ──
    Task<FinResult> AddLineAsync(Guid statementId, BankStatementLine line, string? user);
    Task<FinResult> UpdateLineAsync(Guid statementId, Guid lineId, BankStatementLine line, byte[]? rowVersion, string? user);
    Task<FinResult> DeleteLineAsync(Guid statementId, Guid lineId, string? user);
}

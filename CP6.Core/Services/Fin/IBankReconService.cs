using CP6.Entity.DomainModels.Fin;
namespace CP6.Core.Services.Fin;

public interface IBankReconService
{
    Task<List<BankCandidateLine>> GetCandidatesAsync(Guid statementId, Guid statementLineId, bool widen);
    Task<FinResult> AutoMatchAsync(Guid statementId, string? user);
    Task<FinResult> ManualMatchAsync(ManualMatchRequest req, byte[]? stmtLineRowVersion, string? user);
    Task<FinResult> UnmatchAsync(Guid groupId, string? user);

    // D 阶段
    Task<List<BankOnlyLineResult>> GenerateBankOnlyVoucherAsync(Guid statementId, List<Guid> lineIds, Guid counterAccountId, string? counterRole, string? partnerId, string? user);
    Task<FinResult> MarkPendingAsync(Guid statementId, List<Guid> lineIds, BankLineCategory category, byte[]? rowVersion, string? user);
    Task<ReconciliationStatementDto> GetReconciliationStatementAsync(Guid statementId);
    Task<FinResult> LockAsync(Guid statementId, string? user);
    Task<FinResult> UnlockAsync(Guid statementId, string reason, string? user);
}

using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IBudgetService
{
    Task<List<Budget>> ListBudgetsAsync();
    Task<FinResult<Budget>> CreateBudgetAsync(Budget dto, string user);
    Task<FinResult> UpdateBudgetAsync(Guid id, string name, string? description);
    Task<FinResult> DeactivateBudgetAsync(Guid id);

    Task<List<BudgetVersion>> ListVersionsAsync(Guid budgetId);
    Task<BudgetVersion?> GetVersionAsync(Guid versionId);
    Task<FinResult<BudgetVersion>> CreateVersionAsync(Guid budgetId, string name, string user,
        Guid? copyFromVersionId = null, int? copyFromActualFiscalYear = null);
    Task<FinResult> UpdateVersionAsync(Guid versionId, string name, BudgetControlMode mode, BudgetControlBasis basis);
    Task<FinResult> DeleteVersionAsync(Guid versionId);

    Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName);
    Task<FinResult> ActivateAsync(Guid versionId);
    Task ActivateFromApprovalAsync(Guid versionId, string decidedBy);   // OA 回调专用，不 SaveChanges
}

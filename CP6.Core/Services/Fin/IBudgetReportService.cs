using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IBudgetReportService
{
    Task<BudgetVsActualReport> BuildVsActualAsync(int fiscalYear, Guid? versionId, int periodFrom, int periodTo);
    Task<List<BudgetWarningDto>> PreCheckAsync(JournalEntry entry);   // F-3
    Task<Dictionary<(Guid acct, Guid cc, string coType, string coId), decimal[]>> AggregateActualByBucketAsync(int fiscalYear);   // F-2
}

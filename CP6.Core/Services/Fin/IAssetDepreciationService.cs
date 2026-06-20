using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IAssetDepreciationService
{
    Task<List<DepreciationEntryDto>> PreviewAsync(Guid periodId);
    Task<FinResult> RunAsync(Guid periodId, string userId, DepreciationRunMode mode);
    Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload);
    Task<FinResult> PostAsync(Guid runId, string userId);
    Task<FinResult> ReverseAsync(Guid runId, string userId, string reason);
    Task<FinResult> AccrueAsync(Guid periodId, string userId);
    Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId);
    Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid assetCardId, Guid periodId, string userId);
    Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId);
}

using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IAssetDisposalService
{
    Task<FinResult> CreateAsync(AssetDisposal d, string userId);
    Task<FinResult> ConfirmAsync(Guid id, string userId);
    Task<FinResult> ReverseAsync(Guid id, string userId, string reason);
    Task<AssetDisposal?> GetAsync(Guid id);
    Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId);
}

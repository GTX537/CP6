using CP6.Core.Services.Wf;

namespace CP6.Core.Services.Wms;

public interface IWmsFeatureFlagChangeService
{
    Task<WmsFeatureFlagChangeDto> SubmitAsync(
        CreateWmsFeatureFlagChangeRequest request,
        Guid requestedById,
        string? requestedBy,
        CancellationToken ct = default);

    Task<IReadOnlyList<WmsFeatureFlagChangeDto>> GetAsync(
        WmsFeatureFlagChangeQuery query,
        CancellationToken ct = default);

    Task CancelAsync(
        Guid id,
        Guid requestedById,
        string? requestedBy,
        CancellationToken ct = default);

    Task ApplyApprovedAsync(
        Guid id,
        ApprovalCallbackContext context,
        CancellationToken ct = default);

    Task ApplyRejectedAsync(
        Guid id,
        ApprovalCallbackContext context,
        CancellationToken ct = default);
}

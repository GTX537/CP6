using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceEditLeaseService
{
    Task<SpaceEditLeaseDto> GetAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default);

    Task<SpaceEditLeaseDto> AcquireAsync(
        Guid versionId,
        Guid floorLogicalId,
        AcquireSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceEditLeaseDto> RenewAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceEditLeaseDto> ReleaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        ContinueSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceEditLeaseDto> TakeoverAsync(
        Guid versionId,
        Guid floorLogicalId,
        TakeoverSpaceEditLeaseRequest request,
        CancellationToken cancellationToken = default);
}

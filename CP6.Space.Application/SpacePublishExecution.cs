using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpacePublishOrchestrator
{
    Task<CreateSpacePublishAttemptResponse> StartAsync(
        Guid versionId,
        CreateSpacePublishAttemptRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpacePublishAttemptDto> GetAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceRuntimeActivationRequest(
    Guid AttemptId,
    Guid SiteId,
    Guid TargetVersionId,
    Guid? BaseVersionId,
    string PlanHash,
    Guid ActorId);

public sealed record SpaceRuntimeActivationResult(
    string MaterializedHash,
    int FloorCount,
    int ZoneCount,
    int AisleCount,
    int RackCount,
    int LocationCount,
    int ElementCount);

public interface ISpaceRuntimeMaterializer
{
    Task<SpaceRuntimeActivationResult> ActivateAsync(
        SpaceRuntimeActivationRequest request,
        CancellationToken cancellationToken = default);
}

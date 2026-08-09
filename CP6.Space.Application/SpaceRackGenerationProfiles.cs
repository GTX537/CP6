using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceRackGenerationProfileService
{
    Task<SpacePage<SpaceRackGenerationProfileDto>> GetProfilesAsync(
        string? scope,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<SpaceRackGenerationProfileVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<CreateSpaceRackGenerationProfileResponse> CreateAsync(
        CreateSpaceRackGenerationProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

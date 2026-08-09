using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record UploadSpaceUnderlayRequest(
    SpaceSourceType SourceType,
    string OriginalName,
    string? DeclaredContentType);

public sealed record SpaceUnderlayContent(
    Stream Content,
    string ContentType,
    string FileName);

public sealed class SpaceUnderlayCalibrationOptions
{
    public decimal MinimumValidationErrorMillimeters { get; init; } = 50m;
    public decimal RelativeValidationErrorTolerance { get; init; } = 0.002m;
}

public interface ISpaceUnderlayV1Service
{
    Task<UploadSpaceUnderlayResponse> UploadAsync(
        Guid versionId,
        UploadSpaceUnderlayRequest request,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<SpaceFileDto> GetFileAsync(
        Guid versionId,
        Guid fileId,
        CancellationToken cancellationToken = default);

    Task<SpaceUnderlayContent> OpenContentAsync(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<AttachSpaceUnderlayResponse> AttachAsync(
        Guid versionId,
        Guid floorLogicalId,
        AttachSpaceUnderlayRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceUnderlayCalibrationDto> GetCalibrationAsync(
        Guid versionId,
        Guid sourceId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default);

    Task<SaveSpaceUnderlayCalibrationResponse> CalibrateAsync(
        Guid versionId,
        Guid sourceId,
        SaveSpaceUnderlayCalibrationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

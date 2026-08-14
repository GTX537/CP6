using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpaceCadParsePayloadVersions
{
    public const int LegacyBaseRevision = 2;
    public const int Current = 3;
}

public sealed record SpaceCadParseJobPayload(
    int SchemaVersion,
    Guid ModelVersionId,
    Guid SourceId,
    Guid FileId,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    Guid FloorLogicalId,
    SpaceCadUnit ConfirmedUnit,
    decimal ConfirmedScaleToMillimeters,
    string CoordinateMetadataJson,
    string CoordinateTransformSha256,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionSha256,
    string MappingPreviewSha256,
    long BaseContentRevision,
    string? BaseContentHash,
    string? PreferredProviderKey,
    string? ExpectedSemanticPreviewSha256);

public sealed record SpaceCadParseProviderRequest(
    Guid TenantId,
    Guid JobId,
    SpaceCadParseJobPayload Payload);

public sealed record SpaceCadGeneratedArtifact(
    SpaceArtifactType ArtifactType,
    string SchemaVersion,
    string FileName,
    string ContentType,
    string Extension,
    long SizeBytes,
    string Sha256,
    Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

public interface ISpaceCadParseProvider
{
    Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
        SpaceCadParseProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface ISpaceCadParseJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceCadParseService
{
    Task<UploadSpaceCadSourceResponse> UploadAsync(
        Guid versionId,
        SpaceCadSourceFormat sourceFormat,
        string originalName,
        string? declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<StartSpaceCadParseResponse> StartAsync(
        Guid versionId,
        Guid sourceId,
        StartSpaceCadParseRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceCadParseDto> GetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<SpaceCadReviewWorkspaceV1> GetReviewWorkspaceAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<ApplySpaceCadChangesetResponse> ApplyReviewChangesAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        ApplySpaceCadChangesetRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceCadParseActionResponse> CancelAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<SpaceCadParseActionResponse> RetryAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceCadParseJobProcessor(
    ISpaceCadParseJobStepExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-cad-parse-v1";
    public const string GenerateArtifacts = nameof(GenerateArtifacts);
    public const string FinalizePreview = nameof(FinalizePreview);

    private static readonly IReadOnlyList<string> Steps =
        [GenerateArtifacts, FinalizePreview];

    public SpaceJobType JobType => SpaceJobType.CadParse;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.ModelSource;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes => Steps;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        if ((execution.StepCode == GenerateArtifacts && execution.StepNo == 1) ||
            (execution.StepCode == FinalizePreview && execution.StepNo == 2))
        {
            return executor.ExecuteAsync(execution, cancellationToken);
        }

        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Bug,
            "SPACE_CAD_PARSE_STEP_INVALID",
            "The CAD parse Job step is invalid.");
    }
}

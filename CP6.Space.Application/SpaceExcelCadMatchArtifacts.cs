using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceExcelCadMatchJobPayload(
    int SchemaVersion,
    Guid ModelVersionId,
    Guid ExcelSourceId,
    Guid PreflightJobId,
    Guid CadSourceId,
    Guid CadParseJobId,
    Guid FloorLogicalId,
    long ExpectedContentRevision);

public interface ISpaceExcelCadMatchJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceExcelCadMatchService
{
    Task<StartSpaceExcelCadMatchResponse> StartAsync(
        Guid versionId,
        StartSpaceExcelCadMatchRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceExcelCadMatchDto> GetAsync(
        Guid versionId,
        Guid jobId,
        string? disposition,
        string? rackCode,
        string? sourceRef,
        bool onlyLocatable,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceExcelCadMatchJobProcessor(
    ISpaceExcelCadMatchJobStepExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-excel-cad-match-v1";
    public const string PersistMatchArtifact = nameof(PersistMatchArtifact);

    public SpaceJobType JobType => SpaceJobType.ExcelCadMatch;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.ModelSource;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes { get; } = [PersistMatchArtifact];

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        if (execution.StepNo == 1 &&
            execution.StepCode == PersistMatchArtifact)
        {
            return executor.ExecuteAsync(execution, cancellationToken);
        }

        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Bug,
            "SPACE_EXCEL_CAD_MATCH_STEP_INVALID",
            "The Excel/CAD match Job step is invalid.");
    }
}

public static class SpaceExcelCadMatchArtifact
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static SpaceExcelCadMatchArtifactV1 Create(
        Guid tenantId,
        Guid matchJobId,
        SpaceExcelCadMatchJobPayload payload,
        Guid cadPreviewSetArtifactId,
        Guid requestedBy,
        DateTime requestedAtUtc,
        SpaceExcelCadMatchPreviewV1 preview)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(preview);
        var withoutHash = new SpaceExcelCadMatchArtifactV1(
            SpaceExcelCadMatchArtifactVersions.SchemaVersion,
            IsAuthoritativeArtifact: true,
            tenantId,
            matchJobId,
            payload.ModelVersionId,
            payload.ExcelSourceId,
            payload.PreflightJobId,
            payload.CadSourceId,
            payload.CadParseJobId,
            cadPreviewSetArtifactId,
            payload.FloorLogicalId,
            payload.ExpectedContentRevision,
            requestedBy,
            requestedAtUtc,
            preview,
            ArtifactPayloadSha256: string.Empty);
        var result = withoutHash with
        {
            ArtifactPayloadSha256 = Hash(SerializeUnchecked(withoutHash)),
        };
        Validate(result);
        return result;
    }

    public static string Serialize(SpaceExcelCadMatchArtifactV1 artifact)
    {
        Validate(artifact);
        return SerializeUnchecked(artifact);
    }

    public static SpaceExcelCadMatchArtifactV1 Deserialize(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SpaceExcelCadMatchArtifactV1>(
                json,
                JsonOptions) ?? throw new JsonException();
            Validate(value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Excel/CAD Match Artifact is not valid JSON.",
                exception);
        }
    }

    public static void Validate(SpaceExcelCadMatchArtifactV1 artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Preview);
        SpaceExcelCadMatching.Validate(artifact.Preview);
        if (artifact.SchemaVersion != SpaceExcelCadMatchArtifactVersions.SchemaVersion ||
            !artifact.IsAuthoritativeArtifact ||
            artifact.TenantId == Guid.Empty ||
            artifact.MatchJobId == Guid.Empty ||
            artifact.ModelVersionId == Guid.Empty ||
            artifact.ExcelSourceId == Guid.Empty ||
            artifact.PreflightJobId == Guid.Empty ||
            artifact.CadSourceId == Guid.Empty ||
            artifact.CadParseJobId == Guid.Empty ||
            artifact.CadPreviewSetArtifactId == Guid.Empty ||
            artifact.FloorLogicalId == Guid.Empty ||
            artifact.ExpectedContentRevision < 0 ||
            artifact.RequestedBy == Guid.Empty ||
            artifact.RequestedAtUtc.Kind != DateTimeKind.Utc ||
            !IsSha256(artifact.ArtifactPayloadSha256) ||
            artifact.Preview.TenantId != artifact.TenantId ||
            artifact.Preview.ModelVersionId != artifact.ModelVersionId ||
            artifact.Preview.ExcelSourceId != artifact.ExcelSourceId ||
            artifact.Preview.PreflightJobId != artifact.PreflightJobId ||
            artifact.Preview.FloorLogicalId != artifact.FloorLogicalId ||
            artifact.Preview.EditorContentRevision !=
                artifact.ExpectedContentRevision)
        {
            throw new InvalidDataException(
                "The Excel/CAD Match Artifact identity is invalid.");
        }

        var expected = Hash(SerializeUnchecked(
            artifact with { ArtifactPayloadSha256 = string.Empty }));
        if (!artifact.ArtifactPayloadSha256.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Excel/CAD Match Artifact hash is invalid.");
        }
    }

    private static string SerializeUnchecked(SpaceExcelCadMatchArtifactV1 value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => Uri.IsHexDigit(character) && !char.IsUpper(character));
}

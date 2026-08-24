using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceExcelCadApplyJobPayload(
    int SchemaVersion,
    Guid ModelVersionId,
    Guid MatchJobId,
    Guid ArtifactId,
    string ArtifactPayloadSha256,
    Guid ExcelSourceId,
    Guid FloorLogicalId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision,
    Guid CommandBatchId);

public interface ISpaceExcelCadApplyJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public interface ISpaceExcelCadApplyService
{
    Task<ConfirmSpaceExcelCadMatchResponse> ConfirmAsync(
        Guid versionId,
        Guid matchJobId,
        ConfirmSpaceExcelCadMatchRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceExcelCadApplyDto> GetAsync(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CancellationToken cancellationToken = default);

    Task<CompensateSpaceExcelCadApplyResponse> CompensateAsync(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CompensateSpaceExcelCadApplyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceExcelCadApplyJobProcessor(
    ISpaceExcelCadApplyJobStepExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-excel-cad-apply-v3";
    public const string ApplyConfirmedArtifact = nameof(ApplyConfirmedArtifact);

    public SpaceJobType JobType => SpaceJobType.ExcelCadApply;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.ModelSource;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes { get; } = [ApplyConfirmedArtifact];

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        if (execution.StepNo == 1 &&
            execution.StepCode == ApplyConfirmedArtifact)
        {
            return executor.ExecuteAsync(execution, cancellationToken);
        }

        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Bug,
            "SPACE_EXCEL_CAD_APPLY_STEP_INVALID",
            "The Excel/CAD Apply Job step is invalid.");
    }
}

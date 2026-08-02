using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpaceExcelPreflightJobStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceExcelPreflightJobProcessor : ISpaceJobProcessor
{
    public const string Version = "space-excel-preflight-v1";
    public const string ValidateWorkbook = nameof(ValidateWorkbook);
    public const string PersistPreview = nameof(PersistPreview);

    private static readonly IReadOnlyList<string> Steps =
        [ValidateWorkbook, PersistPreview];

    private readonly ISpaceExcelPreflightJobStepExecutor _executor;

    public SpaceExcelPreflightJobProcessor(
        ISpaceExcelPreflightJobStepExecutor executor)
    {
        _executor = executor;
    }

    public SpaceJobType JobType => SpaceJobType.ExcelPreview;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.ModelSource;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes => Steps;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        if ((execution.StepCode == ValidateWorkbook && execution.StepNo == 1) ||
            (execution.StepCode == PersistPreview && execution.StepNo == 2))
        {
            return _executor.ExecuteAsync(execution, cancellationToken);
        }

        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Bug,
            "SPACE_EXCEL_PREFLIGHT_STEP_INVALID",
            "The Excel preflight Job step is invalid.");
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExcelPreflightJobStepExecutor(
    SpaceContext context,
    IServiceProvider services,
    ISpaceExcelWorkbookReader workbookReader,
    ISpaceExcelMappingService mappings,
    SpaceExcelPreflightValidator validator)
    : ISpaceExcelPreflightJobStepExecutor
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureLease(execution.Lease);
        return execution.StepCode switch
        {
            SpaceExcelPreflightJobProcessor.ValidateWorkbook =>
                await ValidateWorkbookAsync(execution, cancellationToken),
            SpaceExcelPreflightJobProcessor.PersistPreview =>
                await PersistPreviewAsync(execution, cancellationToken),
            _ => throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_PREFLIGHT_STEP_INVALID",
                "The Excel preflight Job step is invalid."),
        };
    }

    private async Task<SpaceJobStepOutput> ValidateWorkbookAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(execution.Lease, cancellationToken);
        SpaceExcelWorkbookData workbook;
        try
        {
            var files = services.GetService(typeof(ISpaceFileStore)) as
                ISpaceFileStore ?? throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorUnavailable,
                "Private Space file storage is not configured.");
            await using var content = await files.OpenQuarantinedReadAsync(
                input.File.TenantId,
                input.File.Id,
                input.File.StorageKey,
                cancellationToken);
            workbook = await workbookReader.ReadAsync(
                content,
                cancellationToken);
        }
        catch (SpaceExcelWorkbookException exception)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                exception.Code,
                exception.Message);
        }
        catch (IOException)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorFailed,
                "The Excel source could not be read from private storage.");
        }

        var result = validator.Validate(input.Profile.Definition, workbook);
        var pendingIssues = result.Findings.Select(finding =>
            SpaceModelIssue.Create(
                execution.Lease.TenantId,
                input.Source.ModelVersionId,
                input.Source.Id,
                execution.Lease.JobId,
                finding.Severity,
                finding.Code,
                SourceReference(finding),
                messageArgsJson: JsonSerializer.Serialize(
                    new
                    {
                        sheet = finding.Sheet,
                        row = finding.Row,
                        column = finding.Column,
                        targetField = finding.TargetField,
                    },
                    JsonOptions),
                suggestedActionCode: finding.SuggestedActionCode))
            .ToArray();
        var previousIssues = await context.Issues
            .AsNoTracking()
            .Where(issue => issue.JobId == execution.Lease.JobId)
            .ToListAsync(cancellationToken);
        if (previousIssues.Count == 0)
        {
            context.Issues.AddRange(pendingIssues);
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (!previousIssues.Select(IssueSignature)
                     .OrderBy(value => value, StringComparer.Ordinal)
                     .SequenceEqual(
                         pendingIssues.Select(IssueSignature)
                             .OrderBy(value => value, StringComparer.Ordinal),
                         StringComparer.Ordinal))
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_PREFLIGHT_RETRY_MISMATCH",
                "The immutable Excel preflight input produced different retry results.");
        }

        var checkpoint = Checkpoint(
            input.Payload,
            result.SheetCount,
            result.DataRowCount,
            result.ValidRowCount,
            result.Findings);
        return Output(checkpoint);
    }

    private async Task<SpaceJobStepOutput> PersistPreviewAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(execution.Lease, cancellationToken);
        var validateStep = await context.JobSteps
            .AsNoTracking()
            .SingleOrDefaultAsync(
                step =>
                    step.AttemptId == execution.Lease.AttemptId &&
                    step.StepCode ==
                    SpaceExcelPreflightJobProcessor.ValidateWorkbook,
                cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_PREFLIGHT_CHECKPOINT_MISSING",
                "The Excel validation checkpoint is missing.");
        if (string.IsNullOrWhiteSpace(validateStep.CheckpointJson) ||
            string.IsNullOrWhiteSpace(validateStep.OutputHash))
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_PREFLIGHT_CHECKPOINT_MISSING",
                "The Excel validation checkpoint is incomplete.");
        }

        if (input.Source.State == SpaceSourceState.Parsing)
        {
            input.Source.MarkPreviewReady();
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (input.Source.State != SpaceSourceState.PreviewReady)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightInvalid,
                "The Excel source is no longer in a preflight state.");
        }

        return new SpaceJobStepOutput(
            validateStep.CheckpointJson,
            validateStep.OutputHash);
    }

    private async Task<PreflightInput> LoadInputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var job = await context.Jobs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lease.JobId, cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightNotFound,
                "The Excel preflight Job was not found.");
        var payload = DeserializePayload(job.PayloadJson);
        if (payload.SchemaVersion != 1 ||
            payload.SourceId != lease.SubjectId ||
            payload.ModelVersionId == Guid.Empty ||
            payload.MappingProfileId == Guid.Empty ||
            payload.MappingProfileVersion <= 0)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightInvalid,
                "The Excel preflight Job payload is invalid.");
        }

        var source = await context.Sources
            .SingleOrDefaultAsync(
                item =>
                    item.Id == payload.SourceId &&
                    item.ModelVersionId == payload.ModelVersionId,
                cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.SourceNotFound,
                "The Excel source was not found.");
        if (source.SourceType != SpaceSourceType.Excel ||
            source.MappingProfileId != payload.MappingProfileId ||
            source.MappingProfileVersion != payload.MappingProfileVersion ||
            source.ParserVersion != SpaceExcelPreflightJobProcessor.Version ||
            source.FileId is null)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightInvalid,
                "The Excel source no longer matches the pinned preflight input.");
        }

        var file = await context.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == source.FileId, cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.FileNotFound,
                "The Excel source file was not found.");
        if (file.State != SpaceFileState.Clean || file.IsDeleted)
        {
            throw Failure(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.SourceUnsafe,
                "The Excel source file is not clean.");
        }

        SpaceExcelMappingProfileDto profile;
        try
        {
            profile = await mappings.GetProfileAsync(
                payload.MappingProfileId,
                payload.MappingProfileVersion,
                cancellationToken);
        }
        catch (SpaceProblemException)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelMappingProfileNotFound,
                "The pinned Excel mapping profile was not found.");
        }
        if (!string.Equals(
                profile.DefinitionHash,
                payload.MappingDefinitionHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightInvalid,
                "The pinned Excel mapping definition hash changed.");
        }
        return new PreflightInput(payload, source, file, profile);
    }

    private static SpaceExcelPreflightJobPayload DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceExcelPreflightJobPayload>(
                       json,
                       JsonOptions) ??
                   throw new JsonException();
        }
        catch (JsonException)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelPreflightInvalid,
                "The Excel preflight Job payload is invalid.");
        }
    }

    private static object Checkpoint(
        SpaceExcelPreflightJobPayload payload,
        int sheetCount,
        int dataRowCount,
        int validRowCount,
        IReadOnlyList<SpaceExcelPreflightFinding> findings) =>
        new
        {
            schemaVersion = 1,
            payload.ModelVersionId,
            payload.SourceId,
            payload.MappingProfileId,
            payload.MappingProfileVersion,
            payload.MappingDefinitionHash,
            parserVersion = SpaceExcelPreflightJobProcessor.Version,
            sheetCount,
            dataRowCount,
            validRowCount,
            infoCount = findings.Count(item =>
                item.Severity == SpaceIssueSeverity.Info),
            warningCount = findings.Count(item =>
                item.Severity == SpaceIssueSeverity.Warning),
            blockingCount = findings.Count(item =>
                item.Severity == SpaceIssueSeverity.Blocking),
        };

    private static SpaceJobStepOutput Output(object checkpoint)
    {
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
        return new SpaceJobStepOutput(json, hash);
    }

    private static string SourceReference(
        SpaceExcelPreflightFinding finding)
    {
        var sheet = Uri.EscapeDataString(finding.Sheet);
        return finding.Row.HasValue
            ? $"excel://{sheet}/{finding.Row.Value}/{finding.Column ?? string.Empty}"
            : $"excel://{sheet}";
    }

    private static string IssueSignature(SpaceModelIssue issue) =>
        string.Join(
            '\u001f',
            ((short)issue.Severity).ToString(),
            issue.Code,
            issue.SourceRef ?? string.Empty,
            issue.MessageArgsJson,
            issue.SuggestedActionCode ?? string.Empty);

    private static void EnsureLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.JobType != SpaceJobType.ExcelPreview ||
            lease.SubjectType != SpaceJobSubjectType.ModelSource ||
            lease.SubjectId == Guid.Empty)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_PREFLIGHT_LEASE_INVALID",
                "The Excel preflight Job lease is invalid.");
        }
    }

    private static SpaceJobProcessingException Failure(
        SpaceJobFailureKind kind,
        string code,
        string message) =>
        new(kind, code, message);

    private sealed record PreflightInput(
        SpaceExcelPreflightJobPayload Payload,
        SpaceModelSource Source,
        SpaceFile File,
        SpaceExcelMappingProfileDto Profile);
}

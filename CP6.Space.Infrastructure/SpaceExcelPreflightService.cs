using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExcelPreflightService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    SpaceFileUploadService uploads,
    SpaceSourceCoordinator sources,
    ISpaceExcelMappingService mappings,
    ISpaceClock clock) : ISpaceExcelPreflightService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<UploadSpaceExcelSourceResponse> UploadAsync(
        Guid versionId,
        string originalName,
        string? declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureExecutionContext();
        var (version, _) = await LoadWritableVersionAsync(
            versionId,
            cancellationToken);
        var upload = await uploads.UploadAsync(
            new SpaceFileUploadRequest(
                SpaceSourceType.Excel,
                originalName,
                declaredContentType,
                SpaceFileRetentionClass.Source),
            content,
            cancellationToken);

        var existing = await context.Sources.SingleOrDefaultAsync(
            source =>
                source.ModelVersionId == version.Id &&
                source.Sha256 == upload.File.Sha256 &&
                source.SourceType == SpaceSourceType.Excel,
            cancellationToken);
        SpaceModelSource source;
        if (existing is not null)
        {
            source = existing;
            if (source.State == SpaceSourceState.Rejected)
            {
                throw Conflict(
                    "The same Excel source was previously rejected.",
                    "remove-rejected-source-before-reupload");
            }
        }
        else
        {
            source = upload.File.State == SpaceFileState.Clean
                ? sources.AddFileSource(
                    version,
                    upload.File,
                    SpaceSourceType.Excel,
                    upload.File.OriginalName)
                : sources.AddPendingFileSource(
                    version,
                    upload.File,
                    SpaceSourceType.Excel,
                    upload.File.OriginalName);
            context.Sources.Add(source);
            await context.SaveChangesAsync(cancellationToken);
        }

        await SynchronizeTerminalFileStateAsync(
            source,
            upload.File.Id,
            cancellationToken);
        var currentFile = await context.Files.AsNoTracking().SingleAsync(
            file => file.Id == upload.File.Id,
            cancellationToken);
        var scanJobId = upload.ScanJobId ??
            await FindScanJobIdAsync(upload.File.Id, cancellationToken);
        return new UploadSpaceExcelSourceResponse(
            ToDto(currentFile),
            ToDto(source),
            scanJobId,
            scanJobId.HasValue
                ? $"/api/space/design/v1/jobs/{scanJobId.Value:D}"
                : null,
            upload.Reused || existing is not null);
    }

    public async Task<StartSpaceExcelPreflightResponse> StartAsync(
        Guid versionId,
        Guid sourceId,
        StartSpaceExcelPreflightRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (sourceId == Guid.Empty || request.MappingProfileId == Guid.Empty ||
            request.MappingProfileVersion <= 0)
        {
            throw Invalid(
                "Source, mapping profile and positive mapping version are required.");
        }
        await LoadWritableVersionAsync(versionId, cancellationToken);
        var profile = await mappings.GetProfileAsync(
            request.MappingProfileId,
            request.MappingProfileVersion,
            cancellationToken);
        var payload = new SpaceExcelPreflightJobPayload(
            1,
            versionId,
            sourceId,
            profile.Id,
            profile.Version,
            profile.DefinitionHash);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var inputHash = Hash(payloadJson);
        var enqueue = new SpaceJobEnqueueRequest(
            SpaceJobType.ExcelPreview,
            SpaceJobSubjectType.ModelSource,
            sourceId,
            inputHash,
            SpaceExcelPreflightJobProcessor.Version,
            VariantKey: $"{profile.Id:N}:{profile.Version}:{profile.DefinitionHash}",
            MaxAttempts: 3,
            PayloadJson: payloadJson);
        var operation = $"excel-preflight:{sourceId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var replay = await ReadReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var source = await context.Sources.SingleOrDefaultAsync(
                item =>
                    item.Id == sourceId &&
                    item.ModelVersionId == versionId,
                cancellationToken) ?? throw NotFound();
            if (source.SourceType != SpaceSourceType.Excel ||
                source.FileId is null)
            {
                throw Invalid("The selected source is not a file-backed Excel source.");
            }
            var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == source.FileId,
                cancellationToken) ?? throw NotFound();
            if (file.State != SpaceFileState.Clean || file.IsDeleted ||
                source.State is not (
                    SpaceSourceState.Ready or
                    SpaceSourceState.PreviewReady))
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.SourceUnsafe,
                    409,
                    "The Excel source is not ready for preflight.",
                    "Wait for the safety scan or the active preflight to finish.",
                    "wait-for-source-ready",
                    retryable: true);
            }

            source.ConfigureImport(
                SpaceExcelPreflightJobProcessor.Version,
                profile.Id,
                profile.Version,
                unit: null,
                scaleToMillimeters: null,
                transformJson: null);
            source.BeginParsing();
            var now = RequireUtcNow();
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                enqueue.JobType,
                enqueue.SubjectType,
                enqueue.SubjectId,
                SpaceJobBusinessKey.Create(enqueue),
                enqueue.InputHash,
                enqueue.Priority,
                enqueue.MaxAttempts,
                execution.ActorId,
                now,
                CorrelationId(),
                enqueue.PayloadJson);
            context.Jobs.Add(job);
            await context.SaveChangesAsync(cancellationToken);

            var response = Response(job, source, profile, replay: false);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                202,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                "The Excel source changed while preflight was starting.",
                "reload-source-and-retry");
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<SpaceExcelPreflightDto> GetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        int issueLimit,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        issueLimit = Math.Clamp(issueLimit, 1, 500);
        var input = await LoadReadablePreflightAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        var issueQuery = context.Issues.AsNoTracking()
            .Where(issue => issue.JobId == jobId);
        var counts = await issueQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Info = group.Count(issue =>
                    issue.Severity == SpaceIssueSeverity.Info),
                Warning = group.Count(issue =>
                    issue.Severity == SpaceIssueSeverity.Warning),
                Blocking = group.Count(issue =>
                    issue.Severity == SpaceIssueSeverity.Blocking),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var issues = await issueQuery
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.CreatedAtUtc)
            .ThenBy(issue => issue.Id)
            .Take(issueLimit)
            .ToArrayAsync(cancellationToken);
        var checkpoint = await ReadCheckpointAsync(jobId, cancellationToken);
        var total = counts?.Total ?? 0;
        return new SpaceExcelPreflightDto(
            input.Job.Id,
            input.Source.ModelVersionId,
            input.Source.Id,
            input.Job.Status.ToString(),
            input.Source.State.ToString(),
            input.Payload.MappingProfileId,
            input.Payload.MappingProfileVersion,
            input.Payload.MappingDefinitionHash,
            SpaceExcelPreflightJobProcessor.Version,
            input.Job.Status == SpaceJobStatus.Succeeded &&
            input.Source.State == SpaceSourceState.PreviewReady &&
            input.Source.MappingProfileId == input.Payload.MappingProfileId &&
            input.Source.MappingProfileVersion ==
                input.Payload.MappingProfileVersion &&
            input.Source.ParserVersion ==
                SpaceExcelPreflightJobProcessor.Version &&
            (counts?.Blocking ?? 0) == 0,
            counts?.Info ?? 0,
            counts?.Warning ?? 0,
            counts?.Blocking ?? 0,
            checkpoint?.SheetCount ?? 0,
            checkpoint?.DataRowCount ?? 0,
            checkpoint?.ValidRowCount ?? 0,
            issues.Length,
            total > issues.Length,
            ErrorReportUrl(versionId, sourceId, jobId),
            issues.Select(ToPreflightIssueDto).ToArray());
    }

    public async Task<SpaceExcelPreflightReport> OpenErrorReportAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        await LoadReadablePreflightAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        var issues = await context.Issues.AsNoTracking()
            .Where(issue => issue.JobId == jobId)
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.CreatedAtUtc)
            .ThenBy(issue => issue.Id)
            .ToArrayAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine(
            "Severity,Code,Sheet,Row,Column,TargetField,FixHint");
        foreach (var issue in issues)
        {
            var location = ReadLocation(issue.MessageArgsJson);
            AppendCsv(builder,
                issue.Severity.ToString(),
                issue.Code,
                location.Sheet,
                location.Row?.ToString(),
                location.Column,
                location.TargetField,
                issue.SuggestedActionCode);
        }
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(builder.ToString());
        return new SpaceExcelPreflightReport(
            new MemoryStream(bytes, writable: false),
            "text/csv; charset=utf-8",
            $"cp6-excel-preflight-{jobId:N}.csv");
    }

    private async Task<ReadablePreflight> LoadReadablePreflightAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty || sourceId == Guid.Empty || jobId == Guid.Empty)
            throw NotFound();
        var result = await (
                from source in context.Sources.AsNoTracking()
                join version in context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                join job in context.Jobs.AsNoTracking()
                    on source.Id equals job.SubjectId
                where version.Id == versionId &&
                      source.Id == sourceId &&
                      source.SourceType == SpaceSourceType.Excel &&
                      job.Id == jobId &&
                      job.JobType == SpaceJobType.ExcelPreview &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource
                select new { Source = source, Model = model, Job = job })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.Model);
        SpaceExcelPreflightJobPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SpaceExcelPreflightJobPayload>(
                          result.Job.PayloadJson,
                          JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("The stored Excel preflight payload is invalid.");
        }
        if (payload.SchemaVersion != 1 ||
            payload.ModelVersionId != versionId ||
            payload.SourceId != sourceId ||
            payload.MappingProfileId == Guid.Empty ||
            payload.MappingProfileVersion <= 0 ||
            payload.MappingDefinitionHash.Length != 64 ||
            payload.MappingDefinitionHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw Invalid("The stored Excel preflight payload is invalid.");
        }
        return new ReadablePreflight(result.Source, result.Job, payload);
    }

    private async Task<PreflightCheckpoint?> ReadCheckpointAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var json = await (
                from step in context.JobSteps.AsNoTracking()
                join attempt in context.JobAttempts.AsNoTracking()
                    on step.AttemptId equals attempt.Id
                where attempt.JobId == jobId &&
                      step.StepCode ==
                      SpaceExcelPreflightJobProcessor.PersistPreview &&
                      (step.Status == SpaceJobStepStatus.Succeeded ||
                       step.Status == SpaceJobStepStatus.Reused)
                orderby attempt.AttemptNo descending
                select step.CheckpointJson)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PreflightCheckpoint>(
                json,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(SpaceModelVersion Version, SpaceModel Model)>
        LoadWritableVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
            throw NotFound();
        var result = await (
                from version in context.Versions
                join model in context.Models
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureWritable(result.Model);
        if (result.Version.Status != SpaceVersionStatus.Draft)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can accept an Excel preflight.",
                recoveryAction: "open-or-create-draft");
        }
        return (result.Version, result.Model);
    }

    private async Task SynchronizeTerminalFileStateAsync(
        SpaceModelSource source,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (source.State != SpaceSourceState.Scanning)
            return;
        var file = await context.Files.SingleAsync(
            item => item.Id == fileId,
            cancellationToken);
        await context.Entry(file).ReloadAsync(cancellationToken);
        if (file.State is not (SpaceFileState.Clean or SpaceFileState.Rejected))
            return;
        source.CompleteFileScan(file);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<Guid?> FindScanJobIdAsync(
        Guid fileId,
        CancellationToken cancellationToken) =>
        context.Jobs.AsNoTracking()
            .Where(job =>
                job.SubjectType == SpaceJobSubjectType.File &&
                job.SubjectId == fileId &&
                job.JobType == SpaceJobType.FileScan)
            .OrderByDescending(job => job.RequestedAtUtc)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<StartSpaceExcelPreflightResponse?> ReadReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.PrincipalId == execution.ActorId &&
                    item.Operation == operation &&
                    item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<StartSpaceExcelPreflightResponse>(
                    record.ResponseJson,
                    JsonOptions) ?? throw new InvalidOperationException(
                    "The Excel preflight idempotency response is invalid."))
            with { IdempotentReplay = true };
    }

    private StartSpaceExcelPreflightResponse Response(
        SpaceJob job,
        SpaceModelSource source,
        SpaceExcelMappingProfileDto profile,
        bool replay) =>
        new(
            job.Id,
            job.Status.ToString(),
            $"/api/space/design/v1/jobs/{job.Id:D}",
            PreviewUrl(source.ModelVersionId, source.Id, job.Id),
            ErrorReportUrl(source.ModelVersionId, source.Id, job.Id),
            profile.Id,
            profile.Version,
            profile.DefinitionHash,
            ToDto(source),
            replay);

    private static SpaceExcelPreflightIssueDto ToPreflightIssueDto(
        SpaceModelIssue issue)
    {
        var location = ReadLocation(issue.MessageArgsJson);
        return new SpaceExcelPreflightIssueDto(
            issue.Id,
            issue.Severity.ToString(),
            issue.Code,
            location.Sheet,
            location.Row,
            location.Column,
            location.TargetField,
            issue.MessageArgsJson,
            issue.SuggestedActionCode,
            issue.CreatedAtUtc);
    }

    private static IssueLocation ReadLocation(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IssueLocation>(json, JsonOptions)
                   ?? new IssueLocation(null, null, null, null);
        }
        catch (JsonException)
        {
            return new IssueLocation(null, null, null, null);
        }
    }

    private static void AppendCsv(StringBuilder builder, params string?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
                builder.Append(',');
            var value = values[index] ?? string.Empty;
            builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }
        builder.AppendLine();
    }

    private static SpaceFileDto ToDto(SpaceFile file) =>
        new(
            file.Id,
            file.OriginalName,
            file.DetectedContentType ?? file.DeclaredContentType,
            file.Extension,
            file.SizeBytes,
            file.Sha256,
            file.State.ToString(),
            file.ScanResultCode,
            Convert.ToBase64String(file.RowVersion));

    private static SpaceSourceDto ToDto(SpaceModelSource source) =>
        new(
            source.Id,
            source.ModelVersionId,
            source.SourceType.ToString(),
            source.FileId,
            source.DisplayName,
            source.Sha256,
            source.State.ToString(),
            source.ParserVersion,
            source.MappingProfileId,
            source.MappingProfileVersion,
            source.Unit,
            source.ScaleToMillimeters,
            Convert.ToBase64String(source.RowVersion));

    private void EnsureReadable(SpaceModel model)
    {
        if (model.Mode != SpaceModelMode.DesignV1 ||
            model.CutoverState != SpaceModelCutoverState.DesignV1)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DesignApiDisabled,
                404,
                "The Design API is not enabled for this Site.",
                recoveryAction: "use-legacy-api");
        }
        access.EnsureSiteAccess(model.SiteId, write: false);
    }

    private void EnsureWritable(SpaceModel model)
    {
        EnsureReadable(model);
        access.EnsureSiteAccess(model.SiteId, write: true);
    }

    private void EnsureExecutionContext()
    {
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private Guid CorrelationId() =>
        execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : Guid.NewGuid();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string IdempotencyKeyHash(string operation, string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }
        return Hash($"{operation}\n{normalized}");
    }

    private static string PreviewUrl(Guid versionId, Guid sourceId, Guid jobId) =>
        $"/api/space/design/v1/versions/{versionId:D}/sources/{sourceId:D}/" +
        $"excel-preflights/{jobId:D}";

    private static string ErrorReportUrl(
        Guid versionId,
        Guid sourceId,
        Guid jobId) =>
        PreviewUrl(versionId, sourceId, jobId) + "/report";

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.ExcelPreflightInvalid,
            422,
            "The Excel preflight request is invalid.",
            detail,
            "correct-excel-preflight-request");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.ExcelPreflightNotFound,
            404,
            "The Excel preflight was not found.",
            recoveryAction: "reload-excel-sources");

    private static SpaceProblemException Conflict(
        string detail,
        string recovery) =>
        new(
            SpaceErrorCodes.SourceConflict,
            409,
            "The Excel source is not available for this operation.",
            detail,
            recovery,
            retryable: true);

    private sealed record ReadablePreflight(
        SpaceModelSource Source,
        SpaceJob Job,
        SpaceExcelPreflightJobPayload Payload);

    private sealed record PreflightCheckpoint(
        int SchemaVersion,
        int SheetCount,
        int DataRowCount,
        int ValidRowCount);

    private sealed record IssueLocation(
        string? Sheet,
        int? Row,
        string? Column,
        string? TargetField);
}

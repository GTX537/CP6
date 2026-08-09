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

public sealed class SpaceExcelCadMatchService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceCursorCodec cursorCodec,
    IServiceProvider services,
    ISpaceClock clock) : ISpaceExcelCadMatchService
{
    private const string CursorResource = "excel-cad-match-rows";
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<StartSpaceExcelCadMatchResponse> StartAsync(
        Guid versionId,
        StartSpaceExcelCadMatchRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateRequest(versionId, request);
        await ValidateStartAuthorityAsync(versionId, request, cancellationToken);

        var payload = new SpaceExcelCadMatchJobPayload(
            SpaceExcelCadMatchArtifactVersions.SchemaVersion,
            versionId,
            request.ExcelSourceId,
            request.PreflightJobId,
            request.CadSourceId,
            request.CadParseJobId,
            request.FloorLogicalId,
            request.ExpectedContentRevision);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var inputHash = Hash(payloadJson);
        var enqueue = new SpaceJobEnqueueRequest(
            SpaceJobType.ExcelCadMatch,
            SpaceJobSubjectType.ModelSource,
            request.ExcelSourceId,
            inputHash,
            SpaceExcelCadMatchJobProcessor.Version,
            VariantKey: $"{versionId:N}:{request.FloorLogicalId:N}",
            MaxAttempts: 5,
            PayloadJson: payloadJson);
        var businessKey = SpaceJobBusinessKey.Create(enqueue);
        var operation = $"excel-cad-match:{versionId:N}";
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

            await ValidateStartAuthorityAsync(versionId, request, cancellationToken);
            var job = await context.Jobs.SingleOrDefaultAsync(
                item =>
                    item.JobType == SpaceJobType.ExcelCadMatch &&
                    item.BusinessKey == businessKey &&
                    (item.Status == SpaceJobStatus.Queued ||
                     item.Status == SpaceJobStatus.Running),
                cancellationToken);
            var reused = job is not null;
            var now = RequireUtcNow();
            if (job is null)
            {
                job = SpaceJob.CreateQueued(
                    execution.TenantId,
                    enqueue.JobType,
                    enqueue.SubjectType,
                    enqueue.SubjectId,
                    businessKey,
                    enqueue.InputHash,
                    enqueue.Priority,
                    enqueue.MaxAttempts,
                    execution.ActorId,
                    now,
                    CorrelationId(),
                    enqueue.PayloadJson);
                context.Jobs.Add(job);
                await context.SaveChangesAsync(cancellationToken);
            }

            var response = Response(job, versionId, replay: reused);
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
                "The model or authoritative inputs changed while matching was queued.",
                "reload-match-inputs");
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

    public async Task<SpaceExcelCadMatchDto> GetAsync(
        Guid versionId,
        Guid jobId,
        string? disposition,
        string? rackCode,
        string? sourceRef,
        bool onlyLocatable,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty || jobId == Guid.Empty)
            throw NotFound();
        limit = NormalizeLimit(limit);
        var parsedDisposition = ParseDisposition(disposition);
        var scope = await LoadReadableJobAsync(
            versionId,
            jobId,
            cancellationToken);
        if (scope.Job.Status != SpaceJobStatus.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(cursor))
                throw Invalid("A cursor cannot be used before matching succeeds.");
            return Empty(scope.Job, scope.Payload);
        }

        var persistedRows = await (
                from storedArtifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on storedArtifact.FileId equals file.Id
                where storedArtifact.JobId == jobId &&
                      storedArtifact.ArtifactType ==
                          SpaceArtifactType.ExcelCadMatchPreview
                select new { Artifact = storedArtifact, File = file })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (persistedRows.Length > 1)
        {
            throw ArtifactInvalid(
                "The completed match Job has duplicate authoritative artifacts.");
        }
        var persisted = persistedRows.SingleOrDefault()
            ?? throw ArtifactInvalid(
                "The completed match Job has no authoritative artifact.");
        if (persisted.Artifact.ModelVersionId != versionId ||
            persisted.Artifact.SourceId != scope.Payload.ExcelSourceId ||
            persisted.Artifact.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.ArtifactSchema ||
            persisted.File.State != SpaceFileState.Clean ||
            persisted.File.IsDeleted ||
            persisted.File.SizeBytes is < 1 or > MaximumArtifactBytes ||
            !IsSha256(persisted.File.Sha256))
        {
            throw ArtifactInvalid(
                "The persisted Match Artifact identity is inconsistent.");
        }

        var artifact = await ReadArtifactAsync(
            persisted.File,
            cancellationToken);
        if (artifact.TenantId != execution.TenantId ||
            artifact.MatchJobId != jobId ||
            artifact.ModelVersionId != versionId ||
            artifact.ExcelSourceId != scope.Payload.ExcelSourceId ||
            artifact.PreflightJobId != scope.Payload.PreflightJobId ||
            artifact.CadSourceId != scope.Payload.CadSourceId ||
            artifact.CadParseJobId != scope.Payload.CadParseJobId ||
            artifact.FloorLogicalId != scope.Payload.FloorLogicalId ||
            artifact.ExpectedContentRevision !=
                scope.Payload.ExpectedContentRevision)
        {
            throw ArtifactInvalid(
                "The Match Artifact no longer matches its frozen Job input.");
        }

        var normalizedRack = NormalizeOptional(rackCode, 100, "rackCode");
        var normalizedSource = NormalizeOptional(sourceRef, 500, "sourceRef");
        var filterHash = Hash(string.Join(
            '\n',
            persisted.Artifact.Id.ToString("N"),
            parsedDisposition?.ToString() ?? string.Empty,
            normalizedRack ?? string.Empty,
            normalizedSource ?? string.Empty,
            onlyLocatable.ToString(),
            limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var offset = ReadOffset(cursor, filterHash);
        SpaceExcelCadMatchPageV1 page;
        try
        {
            page = SpaceExcelCadMatching.Query(
                artifact.Preview,
                new SpaceExcelCadMatchQueryV1(
                    parsedDisposition,
                    normalizedRack,
                    normalizedSource,
                    onlyLocatable,
                    offset,
                    limit));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw Invalid(exception.Message);
        }

        var nextCursor = offset + page.Items.Count < page.TotalCount
            ? cursorCodec.Encode(new SpaceCursorState(
                CursorResource,
                filterHash,
                checked(offset + page.Items.Count)))
            : null;
        var stillCurrent = scope.Version.Status == SpaceVersionStatus.Draft &&
                           scope.Version.ContentRevision ==
                           artifact.ExpectedContentRevision;
        return new SpaceExcelCadMatchDto(
            scope.Job.Id,
            versionId,
            scope.Job.Status.ToString(),
            SpaceExcelCadMatchJobProcessor.Version,
            artifact.ExcelSourceId,
            artifact.PreflightJobId,
            artifact.CadSourceId,
            artifact.CadParseJobId,
            artifact.FloorLogicalId,
            artifact.ExpectedContentRevision,
            persisted.Artifact.Id,
            artifact.ArtifactPayloadSha256,
            persisted.File.Sha256,
            artifact.Preview.CanConfirm && stillCurrent,
            artifact.Preview.Summary,
            page.TotalCount,
            page.Items.Count,
            nextCursor,
            page.Items,
            scope.Job.LastErrorCode,
            scope.Job.LastErrorSummary);
    }

    private async Task ValidateStartAuthorityAsync(
        Guid versionId,
        StartSpaceExcelCadMatchRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        EnsureWritable(scope.Model);
        if (scope.Version.Status != SpaceVersionStatus.Draft)
        {
            throw Conflict(
                "Only a Draft version can produce a Match Artifact.",
                "open-or-create-draft");
        }
        if (scope.Version.ContentRevision != request.ExpectedContentRevision)
        {
            throw Conflict(
                "The Draft content revision changed before matching started.",
                "reload-scene-and-retry");
        }

        var sources = await context.Sources.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                (item.Id == request.ExcelSourceId ||
                 item.Id == request.CadSourceId))
            .ToArrayAsync(cancellationToken);
        var excel = sources.SingleOrDefault(item =>
            item.Id == request.ExcelSourceId);
        var cad = sources.SingleOrDefault(item =>
            item.Id == request.CadSourceId);
        if (excel is null || cad is null)
            throw NotFound();
        if (excel.SourceType != SpaceSourceType.Excel ||
            excel.State != SpaceSourceState.PreviewReady ||
            excel.FileId is null ||
            excel.ParserVersion != SpaceExcelPreflightJobProcessor.Version ||
            cad.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            cad.State != SpaceSourceState.PreviewReady ||
            cad.FileId is null ||
            cad.ParserVersion != SpaceCadParseJobProcessor.Version)
        {
            throw Invalid(
                "The selected Excel and CAD sources are not authoritative PreviewReady inputs.");
        }

        var jobs = await context.Jobs.AsNoTracking()
            .Where(item =>
                item.Id == request.PreflightJobId ||
                item.Id == request.CadParseJobId)
            .ToArrayAsync(cancellationToken);
        var preflight = jobs.SingleOrDefault(item =>
            item.Id == request.PreflightJobId);
        var cadParse = jobs.SingleOrDefault(item =>
            item.Id == request.CadParseJobId);
        if (preflight is null || cadParse is null)
            throw NotFound();
        var preflightPayload = DeserializePreflight(preflight.PayloadJson);
        var cadPayload = DeserializeCadParse(cadParse.PayloadJson);
        if (preflight.JobType != SpaceJobType.ExcelPreview ||
            preflight.SubjectType != SpaceJobSubjectType.ModelSource ||
            preflight.SubjectId != excel.Id ||
            preflight.Status != SpaceJobStatus.Succeeded ||
            preflightPayload.ModelVersionId != versionId ||
            preflightPayload.SourceId != excel.Id ||
            excel.MappingProfileId != preflightPayload.MappingProfileId ||
            excel.MappingProfileVersion != preflightPayload.MappingProfileVersion ||
            cadParse.JobType != SpaceJobType.CadParse ||
            cadParse.SubjectType != SpaceJobSubjectType.ModelSource ||
            cadParse.SubjectId != cad.Id ||
            cadParse.Status != SpaceJobStatus.Succeeded ||
            cadPayload.ModelVersionId != versionId ||
            cadPayload.SourceId != cad.Id ||
            cadPayload.FloorLogicalId != request.FloorLogicalId)
        {
            throw Invalid(
                "The selected Jobs do not match the frozen Excel/CAD source chain.");
        }
        var blocking = await context.Issues.AsNoTracking().AnyAsync(
            item =>
                item.JobId == preflight.Id &&
                item.Severity == SpaceIssueSeverity.Blocking,
            cancellationToken);
        if (blocking)
        {
            throw Invalid(
                "The selected Excel preflight has blocking issues.");
        }

        var producerJobIds = cadParse.RetryOfJobId.HasValue
            ? new[] { cadParse.Id, cadParse.RetryOfJobId.Value }
            : new[] { cadParse.Id };
        var previewSetExists = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId.HasValue &&
                      producerJobIds.Contains(artifact.JobId.Value) &&
                      artifact.SourceId == cad.Id &&
                      artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                      artifact.SchemaVersion ==
                          SpaceCadPreviewSetVersions.ArtifactSchema &&
                      file.State == SpaceFileState.Clean
                select artifact.Id)
            .AnyAsync(cancellationToken);
        if (!previewSetExists)
        {
            throw Invalid(
                "The selected CAD parse has no authoritative PreviewSet artifact.");
        }
    }

    private async Task<ReadableJob> LoadReadableJobAsync(
        Guid versionId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var result = await (
                from job in context.Jobs.AsNoTracking()
                join source in context.Sources.AsNoTracking()
                    on job.SubjectId equals source.Id
                join version in context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where job.Id == jobId &&
                      job.JobType == SpaceJobType.ExcelCadMatch &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource &&
                      version.Id == versionId
                select new
                {
                    Job = job,
                    Source = source,
                    Version = version,
                    Model = model,
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        EnsureReadable(result.Model);
        var payload = DeserializeMatch(result.Job.PayloadJson);
        if (payload.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.SchemaVersion ||
            payload.ModelVersionId != versionId ||
            payload.ExcelSourceId != result.Source.Id)
        {
            throw ArtifactInvalid("The stored Match Job payload is invalid.");
        }
        return new ReadableJob(result.Job, result.Version, payload);
    }

    private async Task<SpaceExcelCadMatchArtifactV1> ReadArtifactAsync(
        SpaceFile file,
        CancellationToken cancellationToken)
    {
        var store = services.GetService(typeof(ISpaceFileStore)) as
            ISpaceFileStore ?? throw new SpaceProblemException(
                SpaceErrorCodes.JobProcessorUnavailable,
                503,
                "Private Space artifact storage is not configured.",
                recoveryAction: "configure-space-file-storage",
                retryable: true);
        try
        {
            await using var stream = await store.OpenQuarantinedReadAsync(
                file.TenantId,
                file.Id,
                file.StorageKey,
                cancellationToken);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 64 * 1024,
                leaveOpen: false);
            var json = await reader.ReadToEndAsync(cancellationToken);
            if (Encoding.UTF8.GetByteCount(json) != file.SizeBytes ||
                !Hash(json).Equals(file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Match Artifact file hash or size changed.");
            }
            return SpaceExcelCadMatchArtifact.Deserialize(json);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException)
        {
            throw ArtifactInvalid(exception.Message);
        }
    }

    private async Task<StartSpaceExcelCadMatchResponse?> ReadReplayAsync(
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
        if (!record.RequestHash.Equals(requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<StartSpaceExcelCadMatchResponse>(
                    record.ResponseJson,
                    JsonOptions) ?? throw new InvalidOperationException(
                    "The Match Job idempotency response is invalid."))
            with
        { IdempotentReplay = true };
    }

    private static SpaceExcelPreflightJobPayload DeserializePreflight(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceExcelPreflightJobPayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("The stored Excel preflight payload is invalid.");
        }
    }

    private static SpaceCadParseJobPayload DeserializeCadParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceCadParseJobPayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("The stored CAD parse payload is invalid.");
        }
    }

    private static SpaceExcelCadMatchJobPayload DeserializeMatch(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceExcelCadMatchJobPayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw ArtifactInvalid("The stored Match Job payload is invalid.");
        }
    }

    private static StartSpaceExcelCadMatchResponse Response(
        SpaceJob job,
        Guid versionId,
        bool replay) => new(
        job.Id,
        job.Status.ToString(),
        MatchUrl(versionId, job.Id),
        replay);

    private static SpaceExcelCadMatchDto Empty(
        SpaceJob job,
        SpaceExcelCadMatchJobPayload payload) => new(
        job.Id,
        payload.ModelVersionId,
        job.Status.ToString(),
        SpaceExcelCadMatchJobProcessor.Version,
        payload.ExcelSourceId,
        payload.PreflightJobId,
        payload.CadSourceId,
        payload.CadParseJobId,
        payload.FloorLogicalId,
        payload.ExpectedContentRevision,
        null,
        null,
        null,
        false,
        null,
        0,
        0,
        null,
        [],
        job.LastErrorCode,
        job.LastErrorSummary);

    private static string MatchUrl(Guid versionId, Guid jobId) =>
        $"/api/space/design/v1/versions/{versionId:D}/" +
        $"excel-cad-matches/{jobId:D}";

    private static void ValidateRequest(
        Guid versionId,
        StartSpaceExcelCadMatchRequest request)
    {
        if (versionId == Guid.Empty ||
            request.ExcelSourceId == Guid.Empty ||
            request.PreflightJobId == Guid.Empty ||
            request.CadSourceId == Guid.Empty ||
            request.CadParseJobId == Guid.Empty ||
            request.FloorLogicalId == Guid.Empty ||
            request.ExpectedContentRevision < 0 ||
            request.ExcelSourceId == request.CadSourceId ||
            request.PreflightJobId == request.CadParseJobId)
        {
            throw Invalid(
                "Version, distinct authoritative sources/Jobs, floor and content revision are required.");
        }
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return SpaceExcelCadMatchArtifactVersions.DefaultPageSize;
        if (limit is < 1 or > SpaceExcelCadMatchArtifactVersions.MaximumPageSize)
        {
            throw Invalid(
                $"limit must be between 1 and {SpaceExcelCadMatchArtifactVersions.MaximumPageSize}.");
        }
        return limit;
    }

    private static SpaceExcelCadMatchDisposition? ParseDisposition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<SpaceExcelCadMatchDisposition>(
                value.Trim(),
                ignoreCase: true,
                out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        throw Invalid("disposition is not supported.");
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = cursorCodec.Decode(cursor, CursorResource, filterHash);
        if (state.Offset < 0)
            throw Invalid("The cursor offset is invalid.");
        return state.Offset;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw Invalid($"{field} is too long.");
        return normalized;
    }

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
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot create or read Excel/CAD Match Artifacts.",
                recoveryAction: "use-internal-space-principal");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            Uri.IsHexDigit(character) && !char.IsUpper(character));

    private static SpaceProblemException Invalid(string detail) => new(
        SpaceErrorCodes.ExcelCadMatchInvalid,
        422,
        "The Excel/CAD match request is invalid.",
        detail,
        "correct-match-inputs");

    private static SpaceProblemException NotFound() => new(
        SpaceErrorCodes.ExcelCadMatchNotFound,
        404,
        "The Excel/CAD match was not found.",
        recoveryAction: "reload-match-inputs");

    private static SpaceProblemException Conflict(
        string detail,
        string recoveryAction) => new(
        SpaceErrorCodes.ConcurrencyConflict,
        409,
        "The Excel/CAD match input changed.",
        detail,
        recoveryAction,
        retryable: true);

    private static SpaceProblemException ArtifactInvalid(string detail) => new(
        SpaceErrorCodes.ExcelCadMatchArtifactInvalid,
        422,
        "The authoritative Excel/CAD Match Artifact is invalid.",
        detail,
        "rebuild-match-artifact");

    private sealed record ReadableJob(
        SpaceJob Job,
        SpaceModelVersion Version,
        SpaceExcelCadMatchJobPayload Payload);
}

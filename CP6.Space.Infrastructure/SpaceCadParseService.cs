using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadParseService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    SpaceFileUploadService uploads,
    SpaceSourceCoordinator sources,
    ISpaceClock clock) : ISpaceCadParseService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<UploadSpaceCadSourceResponse> UploadAsync(
        Guid versionId,
        SpaceCadSourceFormat sourceFormat,
        string originalName,
        string? declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureExecutionContext();
        var (version, _) = await LoadWritableVersionAsync(versionId, cancellationToken);
        var sourceType = sourceFormat == SpaceCadSourceFormat.Dwg
            ? SpaceSourceType.Dwg
            : SpaceSourceType.Dxf;
        var upload = await uploads.UploadAsync(
            new SpaceFileUploadRequest(
                sourceType,
                originalName,
                declaredContentType,
                SpaceFileRetentionClass.Source),
            content,
            cancellationToken);
        var existing = await context.Sources.SingleOrDefaultAsync(
            item =>
                item.ModelVersionId == versionId &&
                item.SourceType == sourceType &&
                item.Sha256 == upload.File.Sha256,
            cancellationToken);
        SpaceModelSource source;
        if (existing is not null)
        {
            source = existing;
            if (source.State == SpaceSourceState.Rejected)
                throw Conflict("The same CAD source was previously rejected.");
        }
        else
        {
            source = upload.File.State == SpaceFileState.Clean
                ? sources.AddFileSource(
                    version,
                    upload.File,
                    sourceType,
                    upload.File.OriginalName)
                : sources.AddPendingFileSource(
                    version,
                    upload.File,
                    sourceType,
                    upload.File.OriginalName);
            context.Sources.Add(source);
            await context.SaveChangesAsync(cancellationToken);
        }

        await SynchronizeTerminalFileStateAsync(
            source,
            upload.File.Id,
            cancellationToken);
        var currentFile = await context.Files.AsNoTracking().SingleAsync(
            item => item.Id == upload.File.Id,
            cancellationToken);
        var scanJobId = upload.ScanJobId ??
            await FindScanJobIdAsync(upload.File.Id, cancellationToken);
        return new UploadSpaceCadSourceResponse(
            ToDto(currentFile),
            ToDto(source),
            scanJobId,
            scanJobId.HasValue ? $"/api/space/design/v1/jobs/{scanJobId:D}" : null,
            upload.Reused || existing is not null);
    }

    public async Task<StartSpaceCadParseResponse> StartAsync(
        Guid versionId,
        Guid sourceId,
        StartSpaceCadParseRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateRequest(request);
        var operation = $"cad-parse:{sourceId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var replay = await ReadReplayAsync<StartSpaceCadParseResponse>(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay with { IdempotentReplay = true };

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireCadParseLockAsync(sourceId, cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync<StartSpaceCadParseResponse>(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay with { IdempotentReplay = true };
            }

            await LoadWritableVersionAsync(versionId, cancellationToken);
            var source = await context.Sources.SingleOrDefaultAsync(
                item => item.Id == sourceId && item.ModelVersionId == versionId,
                cancellationToken) ?? throw NotFound();
            if (source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
                source.FileId is null ||
                source.State is not (SpaceSourceState.Ready or SpaceSourceState.PreviewReady))
            {
                throw Conflict("The selected source is not a ready file-backed CAD source.");
            }
            var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == source.FileId,
                cancellationToken) ?? throw NotFound();
            if (file.State != SpaceFileState.Clean || file.IsDeleted ||
                !string.Equals(file.Sha256, source.Sha256, StringComparison.Ordinal))
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.SourceUnsafe,
                    409,
                    "The CAD source is not ready for parsing.",
                    "Wait for a clean scan result and retry.",
                    "wait-for-source-ready",
                    retryable: true);
            }
            ValidateCoordinateMetadata(request, source.Sha256);
            var payload = new SpaceCadParseJobPayload(
                1,
                versionId,
                sourceId,
                file.Id,
                source.Sha256,
                source.SourceType == SpaceSourceType.Dwg
                    ? SpaceCadSourceFormat.Dwg
                    : SpaceCadSourceFormat.Dxf,
                request.FloorLogicalId,
                request.ConfirmedUnit,
                request.ConfirmedScaleToMillimeters,
                request.CoordinateMetadataJson,
                NormalizeHash(request.CoordinateTransformSha256),
                request.MappingProfileId,
                request.MappingProfileVersion,
                NormalizeHash(request.MappingDefinitionSha256),
                NormalizeHash(request.MappingPreviewSha256));
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var inputHash = Hash(payloadJson);
            var enqueue = Enqueue(payload, inputHash, payloadJson);
            var businessKey = SpaceJobBusinessKey.Create(enqueue);
            var active = await context.Jobs.SingleOrDefaultAsync(
                job =>
                    job.JobType == SpaceJobType.CadParse &&
                    job.SubjectType == SpaceJobSubjectType.ModelSource &&
                    job.SubjectId == sourceId &&
                    (job.Status == SpaceJobStatus.Queued ||
                     job.Status == SpaceJobStatus.Running),
                cancellationToken);
            if (active is not null)
            {
                if (!active.BusinessKey.Equals(businessKey, StringComparison.Ordinal))
                    throw Conflict("A different CAD parse is already active for this source.");
                var activeResponse = Response(active, source, replay: true);
                AddIdempotency(
                    operation,
                    keyHash,
                    requestHash,
                    activeResponse,
                    RequireUtcNow());
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return activeResponse;
            }

            source.ConfigureImport(
                SpaceCadParseJobProcessor.Version,
                request.MappingProfileId,
                request.MappingProfileVersion,
                request.ConfirmedUnit.ToString(),
                request.ConfirmedScaleToMillimeters,
                request.CoordinateMetadataJson);
            var now = RequireUtcNow();
            var job = SpaceJob.CreateQueued(
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
            var response = Response(job, source, replay: false);
            AddIdempotency(operation, keyHash, requestHash, response, now);
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
            var concurrentReplay = await ReadReplayAsync<StartSpaceCadParseResponse>(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay with { IdempotentReplay = true };
            throw Conflict("The CAD source changed while parsing was being started.");
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

    public async Task<SpaceCadParseDto> GetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: false,
            cancellationToken);
        var artifacts = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == jobId ||
                      (input.Job.RetryOfJobId.HasValue &&
                       artifact.JobId == input.Job.RetryOfJobId.Value)
                orderby artifact.ArtifactType
                select new SpaceCadParseArtifactDto(
                    artifact.Id,
                    file.Id,
                    artifact.ArtifactType.ToString(),
                    artifact.SchemaVersion,
                    file.Sha256!,
                    file.SizeBytes))
            .ToArrayAsync(cancellationToken);
        return new SpaceCadParseDto(
            input.Job.Id,
            input.Source.ModelVersionId,
            input.Source.Id,
            input.Job.Status.ToString(),
            input.Source.State.ToString(),
            SpaceCadParseJobProcessor.Version,
            input.Payload.FloorLogicalId,
            input.Payload.CoordinateTransformSha256,
            input.Payload.MappingProfileId,
            input.Payload.MappingProfileVersion,
            input.Payload.MappingDefinitionSha256,
            input.Payload.MappingPreviewSha256,
            input.Job.RetryOfJobId,
            input.Job.CancellationRequestedAtUtc.HasValue,
            input.Job.LastErrorCode,
            input.Job.LastErrorSummary,
            artifacts);
    }

    public async Task<SpaceCadParseActionResponse> CancelAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: true,
            cancellationToken);
        access.EnsureSiteAccess(input.Model.SiteId, write: true);
        input.Job.RequestCancellation(execution.ActorId, RequireUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return Action(input.Job, versionId, sourceId);
    }

    public async Task<SpaceCadParseActionResponse> RetryAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var operation = $"cad-parse-retry:{jobId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash($"{versionId:N}\n{sourceId:N}\n{jobId:N}");
        var replay = await ReadReplayAsync<SpaceCadParseActionResponse>(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay with { IdempotentReplay = true };

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireCadParseLockAsync(sourceId, cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay =
                await ReadReplayAsync<SpaceCadParseActionResponse>(
                    operation,
                    keyHash,
                    requestHash,
                    cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay with { IdempotentReplay = true };
            }

            var input = await LoadReadableAsync(
                versionId,
                sourceId,
                jobId,
                tracked: true,
                cancellationToken);
            access.EnsureSiteAccess(input.Model.SiteId, write: true);
            var enqueue = Enqueue(
                input.Payload,
                input.Job.InputHash,
                input.Job.PayloadJson);
            var businessKey = SpaceJobBusinessKey.Create(enqueue);
            var active = await context.Jobs.SingleOrDefaultAsync(
                item =>
                    item.JobType == SpaceJobType.CadParse &&
                    item.BusinessKey == businessKey &&
                    (item.Status == SpaceJobStatus.Queued ||
                     item.Status == SpaceJobStatus.Running),
                cancellationToken);
            var retry = active ?? input.Job.CreateExplicitRetry(
                businessKey,
                input.Job.InputHash,
                execution.ActorId,
                RequireUtcNow(),
                CorrelationId(),
                input.Job.PayloadJson);
            if (active is null)
                context.Jobs.Add(retry);
            var response = Action(retry, versionId, sourceId);
            AddIdempotency(
                operation,
                keyHash,
                requestHash,
                response,
                RequireUtcNow());
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
            var concurrentReplay =
                await ReadReplayAsync<SpaceCadParseActionResponse>(
                    operation,
                    keyHash,
                    requestHash,
                    cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay with { IdempotentReplay = true };
            throw Conflict("The CAD parse changed while retry was being queued.");
        }
        catch (SpaceJobNotRetryableException exception)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw new SpaceProblemException(
                SpaceErrorCodes.JobNotRetryable,
                409,
                "The CAD parse Job cannot be retried.",
                exception.Message,
                "correct-input-or-start-new-parse");
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

    private async Task<ReadableParse> LoadReadableAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty || sourceId == Guid.Empty || jobId == Guid.Empty)
            throw NotFound();
        var sourcesQuery = tracked ? context.Sources : context.Sources.AsNoTracking();
        var versionsQuery = tracked ? context.Versions : context.Versions.AsNoTracking();
        var modelsQuery = tracked ? context.Models : context.Models.AsNoTracking();
        var jobsQuery = tracked ? context.Jobs : context.Jobs.AsNoTracking();
        var result = await (
                from source in sourcesQuery
                join version in versionsQuery on source.ModelVersionId equals version.Id
                join model in modelsQuery on version.ModelId equals model.Id
                join job in jobsQuery on source.Id equals job.SubjectId
                where version.Id == versionId &&
                      source.Id == sourceId &&
                      job.Id == jobId &&
                      job.JobType == SpaceJobType.CadParse &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource
                select new { source, model, job })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.model);
        var payload = DeserializePayload(result.job.PayloadJson);
        if (payload.ModelVersionId != versionId || payload.SourceId != sourceId)
            throw Invalid("The stored CAD parse payload does not match its route.");
        return new ReadableParse(result.source, result.model, result.job, payload);
    }

    private async Task<(SpaceModelVersion Version, SpaceModel Model)>
        LoadWritableVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken)
    {
        var result = await (
                from version in context.Versions
                join model in context.Models on version.ModelId equals model.Id
                where version.Id == versionId
                select new { version, model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.model);
        access.EnsureSiteAccess(result.model.SiteId, write: true);
        if (result.version.Status != SpaceVersionStatus.Draft)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can accept a CAD parse.",
                recoveryAction: "open-or-create-draft");
        }
        return (result.version, result.model);
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

    private static SpaceJobEnqueueRequest Enqueue(
        SpaceCadParseJobPayload payload,
        string inputHash,
        string payloadJson) =>
        new(
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            payload.SourceId,
            inputHash,
            SpaceCadParseJobProcessor.Version,
            VariantKey:
                $"{payload.FloorLogicalId:N}:{payload.CoordinateTransformSha256}:" +
                $"{payload.MappingProfileId:N}:{payload.MappingProfileVersion}:" +
                payload.MappingDefinitionSha256,
            MaxAttempts: 3,
            PayloadJson: payloadJson);

    private void AddIdempotency<T>(
        string operation,
        string keyHash,
        string requestHash,
        T response,
        DateTime nowUtc) =>
        context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
            execution.TenantId,
            execution.ActorId,
            operation,
            keyHash,
            requestHash,
            JsonSerializer.Serialize(response, JsonOptions),
            202,
            nowUtc.AddHours(24),
            nowUtc.AddDays(90)));

    private async Task<T?> ReadReplayAsync<T>(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken) where T : class
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
        return JsonSerializer.Deserialize<T>(record.ResponseJson, JsonOptions)
               ?? throw new InvalidOperationException(
                   "The CAD parse idempotency response is invalid.");
    }

    private async Task AcquireCadParseLockAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName !=
            "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter(
            "@resource",
            SqlDbType.NVarChar,
            255)
        {
            Value = $"cp6:space:cad-parse:{execution.TenantId:N}:{sourceId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw Conflict(
                "Another CAD parse request is currently updating this source.");
        }
    }

    private static void ValidateRequest(StartSpaceCadParseRequest request)
    {
        if (request.FloorLogicalId == Guid.Empty ||
            request.ConfirmedUnit == SpaceCadUnit.Unknown ||
            request.ConfirmedScaleToMillimeters <= 0 ||
            string.IsNullOrWhiteSpace(request.CoordinateMetadataJson) ||
            request.CoordinateMetadataJson.Length > 8_000 ||
            !IsSha256(request.CoordinateTransformSha256) ||
            request.MappingProfileId == Guid.Empty ||
            request.MappingProfileVersion <= 0 ||
            !IsSha256(request.MappingDefinitionSha256) ||
            !IsSha256(request.MappingPreviewSha256))
        {
            throw Invalid("CAD coordinate and mapping confirmations are required.");
        }
    }

    private static void ValidateCoordinateMetadata(
        StartSpaceCadParseRequest request,
        string sourceSha256)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<SpaceCadCoordinateMetadataV1>(
                               request.CoordinateMetadataJson,
                               JsonOptions) ?? throw new JsonException();
            if (metadata.SchemaVersion != SpaceCadCoordinateVersions.SchemaVersion ||
                !metadata.SourceSha256.Equals(sourceSha256, StringComparison.Ordinal) ||
                !metadata.UnitConfirmed ||
                metadata.ConfirmedUnit != request.ConfirmedUnit ||
                metadata.ConfirmedScaleToMillimeters !=
                    request.ConfirmedScaleToMillimeters ||
                metadata.TargetFloor.FloorLogicalId != request.FloorLogicalId ||
                metadata.TargetFloor.CoordinateSystem !=
                    SpaceCadCoordinateVersions.TargetCoordinateSystem ||
                !metadata.TransformSha256.Equals(
                    request.CoordinateTransformSha256,
                    StringComparison.Ordinal))
            {
                throw new JsonException();
            }
        }
        catch (JsonException)
        {
            throw Invalid(
                "CAD coordinate metadata does not match the source and confirmation.");
        }
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

    private static SpaceCadParseJobPayload DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceCadParseJobPayload>(json, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("The stored CAD parse payload is invalid.");
        }
    }

    private static StartSpaceCadParseResponse Response(
        SpaceJob job,
        SpaceModelSource source,
        bool replay) =>
        new(
            job.Id,
            job.Status.ToString(),
            $"/api/space/design/v1/jobs/{job.Id:D}",
            ParseUrl(source.ModelVersionId, source.Id, job.Id),
            ToDto(source),
            replay);

    private static SpaceCadParseActionResponse Action(
        SpaceJob job,
        Guid versionId,
        Guid sourceId) =>
        new(
            job.Id,
            job.Status.ToString(),
            $"/api/space/design/v1/jobs/{job.Id:D}",
            ParseUrl(versionId, sourceId, job.Id));

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

    private void EnsureExecutionContext()
    {
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
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

    private static string ParseUrl(Guid versionId, Guid sourceId, Guid jobId) =>
        $"/api/space/design/v1/versions/{versionId:D}/sources/{sourceId:D}/" +
        $"cad-parses/{jobId:D}";

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

    private static bool IsSha256(string value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string NormalizeHash(string value) => value.ToLowerInvariant();

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.CadParseInvalid,
            422,
            "The CAD parse request is invalid.",
            detail,
            "correct-cad-parse-request");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.CadParseNotFound,
            404,
            "The CAD parse was not found.",
            recoveryAction: "reload-cad-sources");

    private static SpaceProblemException Conflict(string detail) =>
        new(
            SpaceErrorCodes.SourceConflict,
            409,
            "The CAD source is not available for this operation.",
            detail,
            "reload-source-and-retry",
            retryable: true);

    private sealed record ReadableParse(
        SpaceModelSource Source,
        SpaceModel Model,
        SpaceJob Job,
        SpaceCadParseJobPayload Payload);
}

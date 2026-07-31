using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceUnderlayV1Service : ISpaceUnderlayV1Service
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly SpaceFileUploadService _uploads;
    private readonly SpaceSourceCoordinator _sources;
    private readonly ISpaceFileStore _files;
    private readonly ISpaceClock _clock;
    private readonly SpaceUnderlayCalibrationOptions _calibrationOptions;

    public SpaceUnderlayV1Service(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceDesignAccessEvaluator access,
        SpaceFileUploadService uploads,
        SpaceSourceCoordinator sources,
        ISpaceFileStore files,
        ISpaceClock clock,
        SpaceUnderlayCalibrationOptions? calibrationOptions = null)
    {
        _context = context;
        _execution = execution;
        _access = access;
        _uploads = uploads;
        _sources = sources;
        _files = files;
        _clock = clock;
        _calibrationOptions =
            calibrationOptions ?? new SpaceUnderlayCalibrationOptions();
        if (_calibrationOptions.MinimumValidationErrorMillimeters
            is <= 0 or > 10_000 ||
            _calibrationOptions.RelativeValidationErrorTolerance
            is <= 0 or > 0.1m)
        {
            throw new InvalidOperationException(
                "The underlay calibration error tolerance is invalid.");
        }
    }

    public async Task<UploadSpaceUnderlayResponse> UploadAsync(
        Guid versionId,
        UploadSpaceUnderlayRequest request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);
        EnsureExecutionContext();
        EnsureUnderlayType(request.SourceType);

        var (version, model) = await LoadWritableVersionAsync(
            versionId,
            cancellationToken);
        var upload = await _uploads.UploadAsync(
            new SpaceFileUploadRequest(
                request.SourceType,
                request.OriginalName,
                request.DeclaredContentType,
                SpaceFileRetentionClass.Source),
            content,
            cancellationToken);

        var existingSource = await _context.Sources
            .SingleOrDefaultAsync(
                source =>
                    source.ModelVersionId == version.Id &&
                    source.Sha256 == upload.File.Sha256 &&
                    source.SourceType == request.SourceType,
                cancellationToken);
        SpaceModelSource source;
        if (existingSource is not null)
        {
            source = existingSource;
            if (source.State == SpaceSourceState.Rejected)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.SourceConflict,
                    409,
                    "The same underlay source was previously rejected.",
                    recoveryAction: "remove-rejected-source-before-reupload");
            }
        }
        else
        {
            source = upload.File.State == SpaceFileState.Clean
                ? _sources.AddFileSource(
                    version,
                    upload.File,
                    request.SourceType,
                    request.OriginalName)
                : _sources.AddPendingFileSource(
                    version,
                    upload.File,
                    request.SourceType,
                    request.OriginalName);
            _context.Sources.Add(source);
            await _context.SaveChangesAsync(cancellationToken);
        }
        await SynchronizeTerminalFileStateAsync(
            source,
            upload.File.Id,
            cancellationToken);
        var currentFile = await _context.Files
            .AsNoTracking()
            .SingleAsync(
                file => file.Id == upload.File.Id,
                cancellationToken);

        var jobId = upload.ScanJobId ??
                    await FindScanJobIdAsync(
                        upload.File.Id,
                        cancellationToken);
        return new UploadSpaceUnderlayResponse(
            ToDto(currentFile),
            ToDto(source),
            jobId,
            jobId.HasValue
                ? $"/api/space/design/v1/jobs/{jobId.Value:D}"
                : null,
            upload.Reused || existingSource is not null);
    }

    public async Task<SpaceFileDto> GetFileAsync(
        Guid versionId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (fileId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.FileNotFound, "Space file");

        var result = await (
                from source in _context.Sources.AsNoTracking()
                join version in _context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in _context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                join file in _context.Files.AsNoTracking()
                    on source.FileId equals file.Id
                where version.Id == versionId && file.Id == fileId
                orderby source.CreatedAtUtc
                select new { File = file, Model = model })
            .FirstOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound(SpaceErrorCodes.FileNotFound, "Space file");

        EnsureReadable(result.Model);
        return ToDto(result.File);
    }

    public async Task<SpaceUnderlayContent> OpenContentAsync(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (sourceId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.SourceNotFound, "Space source");

        var result = await (
                from source in _context.Sources.AsNoTracking()
                join version in _context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in _context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                join file in _context.Files.AsNoTracking()
                    on source.FileId equals file.Id
                where version.Id == versionId && source.Id == sourceId
                select new
                {
                    Source = source,
                    File = file,
                    Model = model,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound(SpaceErrorCodes.SourceNotFound, "Space source");

        EnsureReadable(result.Model);
        EnsureUnderlayType(result.Source.SourceType);
        if (result.Source.State != SpaceSourceState.Ready ||
            result.File.State != SpaceFileState.Clean)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.FileQuarantined,
                409,
                "The underlay is not available until its safety scan succeeds.",
                recoveryAction: "wait-for-file-scan",
                retryable: true);
        }

        var contentType = result.File.DetectedContentType switch
        {
            "application/pdf" => "application/pdf",
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            _ => throw InvalidUnderlay(
                "The clean source does not have an allowed underlay content type."),
        };
        var stream = await _files.OpenQuarantinedReadAsync(
            result.File.TenantId,
            result.File.Id,
            result.File.StorageKey,
            cancellationToken);
        return new SpaceUnderlayContent(
            stream,
            contentType,
            result.File.OriginalName);
    }

    public async Task<AttachSpaceUnderlayResponse> AttachAsync(
        Guid versionId,
        Guid floorLogicalId,
        AttachSpaceUnderlayRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (floorLogicalId == Guid.Empty)
        {
            throw NotFound(
                SpaceErrorCodes.LogicalIdNotFound,
                "Space floor logical identity");
        }
        if (request.SourceId == Guid.Empty)
            throw InvalidUnderlay("A source identity is required.");
        if (request.ExpectedFloorRevision < 0)
        {
            throw InvalidUnderlay(
                "The expected floor revision cannot be negative.");
        }

        await LoadWritableVersionAsync(
            versionId,
            cancellationToken);
        var operation =
            $"attach-underlay:{versionId:D}:{floorLogicalId:D}";
        var requestHash = Hash(
            JsonSerializer.Serialize(request, JsonOptions));
        var keyHash = IdempotencyKeyHash(
            operation,
            idempotencyKey);

        var replay = await ReadAttachReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            var concurrentReplay = await ReadAttachReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var version = await _context.Versions
                              .SingleOrDefaultAsync(
                                  candidate => candidate.Id == versionId,
                                  cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            var floor = await _context.FloorRevisions
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.ModelVersionId == versionId &&
                                    candidate.LogicalId == floorLogicalId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.LogicalIdNotFound,
                            "Space floor logical identity");
            var source = await _context.Sources
                             .SingleOrDefaultAsync(
                                 candidate =>
                                     candidate.ModelVersionId == versionId &&
                                     candidate.Id == request.SourceId,
                                 cancellationToken)
                         ?? throw NotFound(
                             SpaceErrorCodes.SourceNotFound,
                             "Space source");
            EnsureUnderlayType(source.SourceType);
            if (source.State != SpaceSourceState.Ready ||
                !source.FileId.HasValue)
            {
                throw InvalidUnderlay(
                    "Only a ready scanned file source can be attached as an underlay.");
            }

            var fileIsClean = await _context.Files
                .AnyAsync(
                    file =>
                        file.Id == source.FileId &&
                        file.State == SpaceFileState.Clean,
                    cancellationToken);
            if (!fileIsClean)
            {
                throw InvalidUnderlay(
                    "The underlay source file is not clean.");
            }

            floor.AdvanceRevision(request.ExpectedFloorRevision);
            floor.AttachUnderlay(source);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var response = new AttachSpaceUnderlayResponse(
                ToDto(floor),
                IdempotentReplay: false);
            _context.IdempotencyRecords.Add(
                NewIdempotencyRecord(
                    operation,
                    keyHash,
                    requestHash,
                    JsonSerializer.Serialize(response, JsonOptions)));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var concurrentReplay = await ReadAttachReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw new SpaceProblemException(
                SpaceErrorCodes.ConcurrencyConflict,
                409,
                "The floor underlay changed concurrently.",
                recoveryAction: "reload-current-floor");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SpaceUnderlayCalibrationDto> GetCalibrationAsync(
        Guid versionId,
        Guid sourceId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (sourceId == Guid.Empty || floorLogicalId == Guid.Empty)
        {
            throw NotFound(
                SpaceErrorCodes.UnderlayCalibrationInvalid,
                "Underlay calibration");
        }
        var scope = await (
                from version in _context.Versions.AsNoTracking()
                join model in _context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
        EnsureReadable(scope.Model);

        var result = await (
                from floor in _context.FloorRevisions.AsNoTracking()
                join calibration in _context.UnderlayCalibrations.AsNoTracking()
                    on floor.UnderlayCalibrationId equals calibration.Id
                where floor.ModelVersionId == versionId &&
                      floor.LogicalId == floorLogicalId &&
                      floor.UnderlaySourceId == sourceId &&
                      calibration.SourceId == sourceId
                select calibration)
            .SingleOrDefaultAsync(cancellationToken);
        return result is null
            ? throw NotFound(
                SpaceErrorCodes.UnderlayCalibrationInvalid,
                "Underlay calibration")
            : ToDto(result);
    }

    public async Task<SaveSpaceUnderlayCalibrationResponse> CalibrateAsync(
        Guid versionId,
        Guid sourceId,
        SaveSpaceUnderlayCalibrationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (sourceId == Guid.Empty ||
            request.FloorLogicalId == Guid.Empty ||
            request.ExpectedFloorRevision < 0 ||
            request.Point1 is null ||
            request.Point2 is null ||
            request.ValidationPoint is null)
        {
            throw InvalidCalibration(
                "A source, floor, three control points and a current floor revision are required.");
        }

        await LoadWritableVersionAsync(versionId, cancellationToken);
        var operation =
            $"cal-underlay:{versionId:N}:{request.FloorLogicalId:N}";
        var requestHash = Hash(
            JsonSerializer.Serialize(
                new { SourceId = sourceId, Request = request },
                JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadCalibrationReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            var concurrentReplay = await ReadCalibrationReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var version = await _context.Versions
                              .SingleOrDefaultAsync(
                                  candidate => candidate.Id == versionId,
                                  cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            var floor = await _context.FloorRevisions
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.ModelVersionId == versionId &&
                                    candidate.LogicalId ==
                                    request.FloorLogicalId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.LogicalIdNotFound,
                            "Space floor logical identity");
            var source = await _context.Sources
                             .SingleOrDefaultAsync(
                                 candidate =>
                                     candidate.ModelVersionId == versionId &&
                                     candidate.Id == sourceId,
                                 cancellationToken)
                         ?? throw NotFound(
                             SpaceErrorCodes.SourceNotFound,
                             "Space source");
            EnsureUnderlayType(source.SourceType);
            if (source.SourceType != SpaceSourceType.Pdf &&
                request.PageNumber != 1)
            {
                throw InvalidCalibration(
                    "PNG and JPG underlays only support page 1.");
            }
            if (floor.UnderlaySourceId != sourceId)
            {
                throw InvalidCalibration(
                    "The source is not the floor's current underlay.");
            }
            if (source.State != SpaceSourceState.Ready ||
                !source.FileId.HasValue)
            {
                throw InvalidCalibration(
                    "Only a ready scanned underlay can be calibrated.");
            }
            var fileIsClean = await _context.Files.AnyAsync(
                file =>
                    file.Id == source.FileId &&
                    file.State == SpaceFileState.Clean,
                cancellationToken);
            if (!fileIsClean)
            {
                throw InvalidCalibration(
                    "The underlay source file is not clean.");
            }

            var calibration = SpaceUnderlayCalibration.Create(
                _execution.TenantId,
                versionId,
                request.FloorLogicalId,
                sourceId,
                request.PageNumber,
                request.PixelWidth,
                request.PixelHeight,
                ToDomain(request.Point1),
                ToDomain(request.Point2),
                ToDomain(request.ValidationPoint),
                _calibrationOptions.MinimumValidationErrorMillimeters,
                _calibrationOptions.RelativeValidationErrorTolerance);
            _context.UnderlayCalibrations.Add(calibration);
            floor.AdvanceRevision(request.ExpectedFloorRevision);
            floor.ApplyUnderlayCalibration(source, calibration);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var response = new SaveSpaceUnderlayCalibrationResponse(
                ToDto(floor),
                ToDto(calibration),
                IdempotentReplay: false);
            _context.IdempotencyRecords.Add(
                NewIdempotencyRecord(
                    operation,
                    keyHash,
                    requestHash,
                    JsonSerializer.Serialize(response, JsonOptions)));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (SpaceUnderlayCalibrationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw exception.ValidationErrorMillimeters.HasValue
                ? new SpaceProblemException(
                    SpaceErrorCodes.UnderlayCalibrationOutOfTolerance,
                    422,
                    "The underlay calibration is outside the accepted tolerance.",
                    exception.Message,
                    "select-a-better-validation-point")
                : InvalidCalibration(exception.Message);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var concurrentReplay = await ReadCalibrationReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw new SpaceProblemException(
                SpaceErrorCodes.ConcurrencyConflict,
                409,
                "The floor calibration changed concurrently.",
                recoveryAction: "reload-current-floor");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(SpaceModelVersion Version, SpaceModel Model)>
        LoadWritableVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
        var result = await (
                from version in _context.Versions
                join model in _context.Models
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
        EnsureWritable(result.Model);
        return (result.Version, result.Model);
    }

    private async Task SynchronizeTerminalFileStateAsync(
        SpaceModelSource source,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (source.State != SpaceSourceState.Scanning)
            return;
        var file = await _context.Files
            .SingleAsync(
                candidate => candidate.Id == fileId,
                cancellationToken);
        await _context.Entry(file).ReloadAsync(cancellationToken);
        if (file.State is not (
            SpaceFileState.Clean or SpaceFileState.Rejected))
        {
            return;
        }

        source.CompleteFileScan(file);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private Task<Guid?> FindScanJobIdAsync(
        Guid fileId,
        CancellationToken cancellationToken) =>
        _context.Jobs
            .AsNoTracking()
            .Where(job =>
                job.SubjectType == SpaceJobSubjectType.File &&
                job.SubjectId == fileId &&
                job.JobType == SpaceJobType.FileScan)
            .OrderByDescending(job => job.RequestedAtUtc)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<AttachSpaceUnderlayResponse?> ReadAttachReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await _context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.PrincipalId == _execution.ActorId &&
                    candidate.Operation == operation &&
                    candidate.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }

        return (JsonSerializer.Deserialize<AttachSpaceUnderlayResponse>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The underlay idempotency response is invalid."))
            with
        {
            IdempotentReplay = true,
        };
    }

    private async Task<SaveSpaceUnderlayCalibrationResponse?>
        ReadCalibrationReplayAsync(
            string operation,
            string keyHash,
            string requestHash,
            CancellationToken cancellationToken)
    {
        var record = await _context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.PrincipalId == _execution.ActorId &&
                    candidate.Operation == operation &&
                    candidate.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }

        return (JsonSerializer
                    .Deserialize<SaveSpaceUnderlayCalibrationResponse>(
                        record.ResponseJson,
                        JsonOptions)
                ?? throw new InvalidOperationException(
                    "The calibration idempotency response is invalid."))
            with
        {
            IdempotentReplay = true,
        };
    }

    private SpaceIdempotencyRecord NewIdempotencyRecord(
        string operation,
        string keyHash,
        string requestHash,
        string responseJson)
    {
        var now = RequireUtcNow();
        return SpaceIdempotencyRecord.Create(
            _execution.TenantId,
            _execution.ActorId,
            operation,
            keyHash,
            requestHash,
            responseJson,
            200,
            now.AddHours(24),
            now.AddDays(90));
    }

    private string IdempotencyKeyHash(
        string operation,
        string idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }
        return Hash(
            $"{_execution.TenantId:D}\n{operation}\n{normalized}");
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
        _access.EnsureSiteAccess(model.SiteId, write: false);
    }

    private void EnsureWritable(SpaceModel model)
    {
        EnsureReadable(model);
        _access.EnsureSiteAccess(model.SiteId, write: true);
    }

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "A verified Space tenant and actor are required.",
                recoveryAction: "refresh-session");
        }
    }

    private static void EnsureUnderlayType(SpaceSourceType sourceType)
    {
        if (sourceType is not (
            SpaceSourceType.Pdf or
            SpaceSourceType.Png or
            SpaceSourceType.Jpg))
        {
            throw InvalidUnderlay(
                "Only PDF, PNG and JPG sources can be used as underlays.");
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceFileDto ToDto(SpaceFile file) =>
        new(
            file.Id,
            file.OriginalName,
            file.DetectedContentType,
            file.Extension,
            file.SizeBytes,
            file.Sha256,
            file.State.ToString(),
            file.ScanResultCode,
            RowVersion(file.RowVersion));

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
            RowVersion(source.RowVersion));

    private static SpaceSceneFloorDto ToDto(SpaceFloorRevision floor) =>
        new(
            new SpaceSceneRevisionDto(
                floor.Id,
                floor.LogicalId,
                floor.SourceId,
                floor.SourceRef,
                floor.LifecycleState.ToString(),
                RowVersion(floor.RowVersion)),
            floor.SiteLogicalId,
            floor.Level,
            floor.FloorCode,
            floor.Name,
            floor.Elevation,
            floor.Height,
            floor.BoundaryJson,
            floor.CoordinateSystem,
            floor.UnderlaySourceId,
            floor.UnderlayCalibrationId,
            floor.UnderlayScale,
            floor.UnderlayOffsetX,
            floor.UnderlayOffsetY,
            floor.UnderlayRotationZ,
            floor.Revision);

    private static SpaceUnderlayCalibrationDto ToDto(
        SpaceUnderlayCalibration calibration) =>
        new(
            calibration.Id,
            calibration.ModelVersionId,
            calibration.FloorLogicalId,
            calibration.SourceId,
            calibration.PageNumber,
            calibration.PixelWidth,
            calibration.PixelHeight,
            new SpaceUnderlayCalibrationPointDto(
                calibration.Point1PixelX,
                calibration.Point1PixelY,
                calibration.Point1WorldX,
                calibration.Point1WorldY),
            new SpaceUnderlayCalibrationPointDto(
                calibration.Point2PixelX,
                calibration.Point2PixelY,
                calibration.Point2WorldX,
                calibration.Point2WorldY),
            new SpaceUnderlayCalibrationPointDto(
                calibration.ValidationPixelX,
                calibration.ValidationPixelY,
                calibration.ValidationWorldX,
                calibration.ValidationWorldY),
            calibration.MillimetersPerPixel,
            calibration.OffsetX,
            calibration.OffsetY,
            calibration.RotationZ,
            calibration.ValidationErrorMillimeters,
            calibration.ErrorThresholdMillimeters,
            calibration.CreatedAtUtc,
            calibration.CreatedBy);

    private static SpaceCalibrationPoint ToDomain(
        SpaceUnderlayCalibrationPointDto point) =>
        new(
            point.PixelX,
            point.PixelY,
            point.WorldX,
            point.WorldY);

    private static string RowVersion(byte[] value) =>
        Convert.ToBase64String(value);

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException NotFound(
        string code,
        string resource) =>
        new(
            code,
            404,
            $"{resource} was not found.",
            recoveryAction: "reload-resource");

    private static SpaceProblemException InvalidUnderlay(string detail) =>
        new(
            SpaceErrorCodes.UnderlaySourceInvalid,
            422,
            "The underlay source is invalid.",
            detail,
            "choose-clean-pdf-png-or-jpg");

    private static SpaceProblemException InvalidCalibration(string detail) =>
        new(
            SpaceErrorCodes.UnderlayCalibrationInvalid,
            422,
            "The underlay calibration is invalid.",
            detail,
            "select-valid-control-points");
}

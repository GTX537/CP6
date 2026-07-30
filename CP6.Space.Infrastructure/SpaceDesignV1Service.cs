using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDesignV1Service : ISpaceDesignV1Service
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const string PublishedVersionMode = "PublishedVersion";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly SpaceVersionCloneCoordinator _clone;
    private readonly SpaceSourceCoordinator _sources;

    public SpaceDesignV1Service(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceCursorCodec cursorCodec,
        ISpaceDesignAccessEvaluator access,
        SpaceVersionCloneCoordinator clone,
        SpaceSourceCoordinator sources)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _cursorCodec = cursorCodec;
        _access = access;
        _clone = clone;
        _sources = sources;
    }

    public async Task<SpaceModelDto> GetModelAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var model = await FindModelBySiteAsync(siteId, cancellationToken);
        EnsureReadable(model);
        return ToDto(model);
    }

    public async Task<SpacePage<SpaceVersionDto>> GetVersionsAsync(
        Guid siteId,
        string? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        limit = NormalizeLimit(limit);
        var parsedStatus = ParseOptionalEnum<SpaceVersionStatus>(
            status,
            nameof(status));
        var model = await FindModelBySiteAsync(siteId, cancellationToken);
        EnsureReadable(model);

        var filterHash = Hash(
            $"site={siteId:D}\nstatus={Normalize(status)}\nlimit={limit}");
        var offset = ReadOffset(cursor, "versions", filterHash);

        var query = _context.Versions
            .AsNoTracking()
            .Where(version => version.ModelId == model.Id);
        if (parsedStatus.HasValue)
            query = query.Where(version => version.Status == parsedStatus.Value);

        var rows = await query
            .OrderByDescending(version => version.VersionNo)
            .ThenBy(version => version.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return Page(
            rows,
            limit,
            offset,
            "versions",
            filterHash,
            version => ToDto(version, model.SiteId));
    }

    public async Task<SpaceVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var result = await (
                from version in _context.Versions.AsNoTracking()
                join model in _context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");

        EnsureReadable(result.Model);
        return ToDto(result.Version, result.Model.SiteId);
    }

    public async Task<CreateSpaceVersionResponse> CreateVersionAsync(
        Guid siteId,
        CreateSpaceVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        var model = await FindModelBySiteAsync(siteId, cancellationToken);
        EnsureWritable(model);

        var name = RequireText(request.Name, 200, "name");
        if (!string.Equals(
                request.CreateMode?.Trim(),
                PublishedVersionMode,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "createMode",
                "Only PublishedVersion is supported by the MVP create endpoint.");
        }
        if (!model.CurrentPublishedVersionId.HasValue)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "A current Published version is required.",
                "publish-or-bootstrap-version");
        }
        if (request.BasedOnVersionId.HasValue &&
            request.BasedOnVersionId != model.CurrentPublishedVersionId)
        {
            throw Conflict(
                SpaceErrorCodes.VersionConflict,
                "basedOnVersionId is not the current Published version.",
                "reload-current-published-version");
        }

        var operation = $"create-version:{siteId:D}";
        var normalizedRequest = new CreateSpaceVersionRequest(
            name,
            model.CurrentPublishedVersionId,
            PublishedVersionMode);
        var requestHash = Hash(
            JsonSerializer.Serialize(normalizedRequest, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);

        var replay = await ReadVersionReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        SpaceVersionCloneStartResult started;
        try
        {
            started = await _clone.StartAsync(
                new SpaceVersionCloneRequest(
                    model.Id,
                    name,
                    OperationId(keyHash)),
                cancellationToken);
        }
        catch (SpaceVersionConflictException exception)
            when (exception.Message.Contains(
                "operation ID",
                StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                SpaceErrorCodes.IdempotencyConflict,
                "The Idempotency-Key was already used with different input.",
                "use-new-idempotency-key");
        }

        _context.ChangeTracker.Clear();
        var version = await _context.Versions
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == started.ModelVersionId,
                cancellationToken);
        var response = new CreateSpaceVersionResponse(
            version.Id,
            siteId,
            FormatVersionNo(version.VersionNo),
            version.Status.ToString(),
            RowVersion(version.RowVersion),
            started.JobId,
            $"/api/space/design/v1/jobs/{started.JobId:D}",
            started.Reused);

        return await StoreVersionResultAsync(
            operation,
            keyHash,
            requestHash,
            response,
            cancellationToken);
    }

    public async Task<SpacePage<SpaceSourceDto>> GetSourcesAsync(
        Guid versionId,
        string? sourceType,
        string? state,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        limit = NormalizeLimit(limit);
        var parsedType = ParseOptionalEnum<SpaceSourceType>(
            sourceType,
            nameof(sourceType));
        var parsedState = ParseOptionalEnum<SpaceSourceState>(
            state,
            nameof(state));
        var model = await FindModelByVersionAsync(
            versionId,
            cancellationToken);
        EnsureReadable(model);

        var filterHash = Hash(
            $"version={versionId:D}\ntype={Normalize(sourceType)}" +
            $"\nstate={Normalize(state)}\nlimit={limit}");
        var offset = ReadOffset(cursor, "sources", filterHash);

        var query = _context.Sources
            .AsNoTracking()
            .Where(source => source.ModelVersionId == versionId);
        if (parsedType.HasValue)
            query = query.Where(source => source.SourceType == parsedType.Value);
        if (parsedState.HasValue)
            query = query.Where(source => source.State == parsedState.Value);

        var rows = await query
            .OrderByDescending(source => source.CreatedAtUtc)
            .ThenBy(source => source.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return Page(
            rows,
            limit,
            offset,
            "sources",
            filterHash,
            ToDto);
    }

    public async Task<CreateSpaceSourceResponse> CreateSourceAsync(
        Guid versionId,
        CreateSpaceSourceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        var model = await FindModelByVersionAsync(
            versionId,
            cancellationToken);
        EnsureWritable(model);

        if (request.FileId == Guid.Empty)
            throw Invalid("fileId", "A scanned file is required.");
        var sourceType = ParseRequiredEnum<SpaceSourceType>(
            request.SourceType,
            "sourceType");
        if (sourceType is SpaceSourceType.Editor or SpaceSourceType.Template)
        {
            throw Invalid(
                "sourceType",
                "The scanned-source endpoint accepts only file-backed source types.");
        }
        var displayName = RequireText(
            request.DisplayName,
            260,
            "displayName");

        var operation = $"create-source:{versionId:D}";
        var normalizedRequest = new CreateSpaceSourceRequest(
            request.FileId,
            sourceType.ToString(),
            displayName);
        var requestHash = Hash(
            JsonSerializer.Serialize(normalizedRequest, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadSourceReplayAsync(
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
            var concurrentReplay = await ReadSourceReplayAsync(
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
            var file = await _context.Files
                           .SingleOrDefaultAsync(
                               candidate => candidate.Id == request.FileId,
                               cancellationToken)
                       ?? throw new SpaceProblemException(
                           SpaceErrorCodes.SourceUnsafe,
                           422,
                           "The source file is unavailable.",
                           "The file is missing, outside the tenant, or not clean.",
                           "upload-and-scan-source");

            var source = _sources.AddFileSource(
                version,
                file,
                sourceType,
                displayName);
            _context.Sources.Add(source);
            await _context.SaveChangesAsync(cancellationToken);

            var response = new CreateSpaceSourceResponse(
                ToDto(source),
                IdempotentReplay: false);
            _context.IdempotencyRecords.Add(
                NewIdempotencyRecord(
                    operation,
                    keyHash,
                    requestHash,
                    JsonSerializer.Serialize(response, JsonOptions),
                    HttpCreated));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var concurrentReplay = await ReadSourceReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                SpaceErrorCodes.SourceConflict,
                "The source is already attached or changed concurrently.",
                "reload-version-sources");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SpaceJobDto> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var job = await _context.Jobs
                      .AsNoTracking()
                      .SingleOrDefaultAsync(
                          candidate => candidate.Id == jobId,
                          cancellationToken)
                  ?? throw NotFound(SpaceErrorCodes.JobNotFound, "Space Job");
        var siteId = await ResolveJobSiteAsync(job, cancellationToken);
        if (!siteId.HasValue)
            throw NotFound(SpaceErrorCodes.JobNotFound, "Space Job");
        _access.EnsureSiteAccess(siteId.Value, write: false);

        var openIssues = _context.Issues
            .AsNoTracking()
            .Where(issue =>
                issue.JobId == jobId &&
                issue.Status == SpaceIssueStatus.Open);
        return new SpaceJobDto(
            job.Id,
            job.JobType.ToString(),
            job.SubjectType.ToString(),
            job.SubjectId,
            job.Status.ToString(),
            job.ProgressDone,
            job.ProgressTotal,
            job.ProgressStage,
            job.AttemptCount,
            job.MaxAttempts,
            job.Status == SpaceJobStatus.Queued
                ? job.NextAttemptAtUtc
                : null,
            job.LockExpiresAtUtc,
            job.CancellationRequestedAtUtc.HasValue,
            job.LastErrorCode,
            job.LastErrorSummary,
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Info,
                cancellationToken),
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Warning,
                cancellationToken),
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Blocking,
                cancellationToken),
            job.RequestedAtUtc,
            job.StartedAtUtc,
            job.FinishedAtUtc,
            job.ResultSummaryJson,
            RowVersion(job.RowVersion));
    }

    public async Task<SpacePage<SpaceIssueDto>> GetIssuesAsync(
        Guid versionId,
        string? severity,
        string? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        limit = NormalizeLimit(limit);
        var parsedSeverity = ParseOptionalEnum<SpaceIssueSeverity>(
            severity,
            nameof(severity));
        var parsedStatus = ParseOptionalEnum<SpaceIssueStatus>(
            status,
            nameof(status));
        var model = await FindModelByVersionAsync(
            versionId,
            cancellationToken);
        EnsureReadable(model);

        var filterHash = Hash(
            $"version={versionId:D}\nseverity={Normalize(severity)}" +
            $"\nstatus={Normalize(status)}\nlimit={limit}");
        var offset = ReadOffset(cursor, "issues", filterHash);

        var query = _context.Issues
            .AsNoTracking()
            .Where(issue => issue.ModelVersionId == versionId);
        if (parsedSeverity.HasValue)
            query = query.Where(issue => issue.Severity == parsedSeverity.Value);
        if (parsedStatus.HasValue)
            query = query.Where(issue => issue.Status == parsedStatus.Value);

        var rows = await query
            .OrderByDescending(issue => issue.CreatedAtUtc)
            .ThenBy(issue => issue.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return Page(
            rows,
            limit,
            offset,
            "issues",
            filterHash,
            ToDto);
    }

    private async Task<SpaceModel> FindModelBySiteAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.ModelNotFound, "Space model");
        return await _context.Models
                   .AsNoTracking()
                   .SingleOrDefaultAsync(
                       model => model.SiteId == siteId,
                       cancellationToken)
               ?? throw NotFound(SpaceErrorCodes.ModelNotFound, "Space model");
    }

    private async Task<SpaceModel> FindModelByVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
        return await (
                   from version in _context.Versions.AsNoTracking()
                   join model in _context.Models.AsNoTracking()
                       on version.ModelId equals model.Id
                   where version.Id == versionId
                   select model)
               .SingleOrDefaultAsync(cancellationToken)
               ?? throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
    }

    private async Task<Guid?> ResolveJobSiteAsync(
        SpaceJob job,
        CancellationToken cancellationToken)
    {
        if (job.SubjectType == SpaceJobSubjectType.ModelVersion)
        {
            return await (
                    from version in _context.Versions.AsNoTracking()
                    join model in _context.Models.AsNoTracking()
                        on version.ModelId equals model.Id
                    where version.Id == job.SubjectId
                    select (Guid?)model.SiteId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (job.SubjectType == SpaceJobSubjectType.ModelSource)
        {
            return await (
                    from source in _context.Sources.AsNoTracking()
                    join version in _context.Versions.AsNoTracking()
                        on source.ModelVersionId equals version.Id
                    join model in _context.Models.AsNoTracking()
                        on version.ModelId equals model.Id
                    where source.Id == job.SubjectId
                    select (Guid?)model.SiteId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (job.SubjectType == SpaceJobSubjectType.File)
        {
            return await (
                    from source in _context.Sources.AsNoTracking()
                    join version in _context.Versions.AsNoTracking()
                        on source.ModelVersionId equals version.Id
                    join model in _context.Models.AsNoTracking()
                        on version.ModelId equals model.Id
                    where source.FileId == job.SubjectId
                    orderby source.CreatedAtUtc descending
                    select (Guid?)model.SiteId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        return null;
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

    private async Task<CreateSpaceVersionResponse?> ReadVersionReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await FindIdempotencyAsync(
            operation,
            keyHash,
            cancellationToken);
        if (record is null)
            return null;
        EnsureMatchingIdempotency(record, requestHash);
        return Deserialize<CreateSpaceVersionResponse>(record.ResponseJson)
            with
        { IdempotentReplay = true };
    }

    private async Task<CreateSpaceSourceResponse?> ReadSourceReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await FindIdempotencyAsync(
            operation,
            keyHash,
            cancellationToken);
        if (record is null)
            return null;
        EnsureMatchingIdempotency(record, requestHash);
        return Deserialize<CreateSpaceSourceResponse>(record.ResponseJson)
            with
        { IdempotentReplay = true };
    }

    private Task<SpaceIdempotencyRecord?> FindIdempotencyAsync(
        string operation,
        string keyHash,
        CancellationToken cancellationToken) =>
        _context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record =>
                    record.PrincipalId == _execution.ActorId &&
                    record.Operation == operation &&
                    record.IdempotencyKeyHash == keyHash,
                cancellationToken);

    private void EnsureMatchingIdempotency(
        SpaceIdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            throw Conflict(
                SpaceErrorCodes.IdempotencyConflict,
                "The Idempotency-Key was already used with different input.",
                "use-new-idempotency-key");
        }
        if (record.ReplayUntilUtc < RequireUtcNow())
        {
            throw Conflict(
                SpaceErrorCodes.IdempotencyConflict,
                "The Idempotency-Key replay window has expired.",
                "use-new-idempotency-key");
        }
    }

    private async Task<CreateSpaceVersionResponse> StoreVersionResultAsync(
        string operation,
        string keyHash,
        string requestHash,
        CreateSpaceVersionResponse response,
        CancellationToken cancellationToken)
    {
        _context.IdempotencyRecords.Add(
            NewIdempotencyRecord(
                operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(
                    response with { IdempotentReplay = false },
                    JsonOptions),
                HttpAccepted));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var replay = await ReadVersionReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (replay is not null)
                return replay;
            throw;
        }
    }

    private SpaceIdempotencyRecord NewIdempotencyRecord(
        string operation,
        string keyHash,
        string requestHash,
        string responseJson,
        int httpStatusCode)
    {
        var now = RequireUtcNow();
        return SpaceIdempotencyRecord.Create(
            _execution.TenantId,
            _execution.ActorId,
            operation,
            keyHash,
            requestHash,
            responseJson,
            httpStatusCode,
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
                "Use 1 to 128 UTF-8 bytes without control characters.",
                "supply-idempotency-key");
        }
        return Hash(
            $"{_execution.TenantId:D}\n{operation}\n{normalized}");
    }

    private int ReadOffset(
        string? cursor,
        string resource,
        string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(cursor, resource, filterHash);
        if (state.Offset < 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CursorInvalid,
                400,
                "The cursor is invalid.",
                recoveryAction: "restart-pagination");
        }
        return state.Offset;
    }

    private SpacePage<TDto> Page<TEntity, TDto>(
        IReadOnlyList<TEntity> rows,
        int limit,
        int offset,
        string resource,
        string filterHash,
        Func<TEntity, TDto> map)
    {
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).Select(map).ToArray();
        var next = hasMore
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    resource,
                    filterHash,
                    checked(offset + limit)))
            : null;
        return new SpacePage<TDto>(items, next);
    }

    private static SpaceModelDto ToDto(SpaceModel model) =>
        new(
            model.Id,
            model.SiteId,
            model.Mode.ToString(),
            model.CutoverState.ToString(),
            model.ActiveDraftVersionId,
            model.CurrentPublishedVersionId,
            RowVersion(model.RowVersion));

    private static SpaceVersionDto ToDto(
        SpaceModelVersion version,
        Guid siteId) =>
        new(
            version.Id,
            version.ModelId,
            siteId,
            FormatVersionNo(version.VersionNo),
            version.Name,
            version.Status.ToString(),
            version.BasedOnVersionId,
            version.ContentRevision,
            version.ContentHash,
            version.ValidatedHash,
            version.PublishedAtUtc,
            RowVersion(version.RowVersion));

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

    private static SpaceIssueDto ToDto(SpaceModelIssue issue) =>
        new(
            issue.Id,
            issue.ModelVersionId,
            issue.SourceId,
            issue.JobId,
            issue.Severity.ToString(),
            issue.Code,
            issue.SourceRef,
            issue.TargetLogicalId,
            issue.MessageArgsJson,
            issue.SuggestedActionCode,
            issue.Status.ToString(),
            issue.ResolutionCommandBatchId,
            issue.AcknowledgedBy,
            issue.AcknowledgedAtUtc,
            issue.AcknowledgementReason,
            issue.CreatedAtUtc);

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty ||
            _execution.TenantId != _context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return DefaultPageSize;
        if (limit is < 1 or > MaxPageSize)
        {
            throw Invalid(
                "limit",
                $"limit must be between 1 and {MaxPageSize}.");
        }
        return limit;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(
        string? value,
        string field)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseRequiredEnum<TEnum>(value, field);

    private static TEnum ParseRequiredEnum<TEnum>(
        string? value,
        string field)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value?.Trim(), ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw Invalid(field, $"'{value}' is not a supported {field}.");
        }
        return parsed;
    }

    private static string RequireText(
        string? value,
        int maxLength,
        string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maxLength)
        {
            throw Invalid(
                field,
                $"{field} is required and cannot exceed {maxLength} characters.");
        }
        return normalized;
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static Guid OperationId(string keyHash)
    {
        var bytes = Convert.FromHexString(keyHash)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException(
            "The persisted Space idempotency response is invalid.");

    private static string FormatVersionNo(long versionNo) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"V{versionNo:000000}");

    private static string RowVersion(byte[] rowVersion) =>
        Convert.ToBase64String(rowVersion ?? []);

    private static SpaceProblemException Invalid(
        string field,
        string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The request is invalid.",
            $"{field}: {detail}",
            "correct-request");

    private static SpaceProblemException NotFound(
        string code,
        string resource) =>
        new(
            code,
            404,
            $"{resource} was not found.",
            recoveryAction: "reload-resource");

    private static SpaceProblemException Conflict(
        string code,
        string detail,
        string recoveryAction) =>
        new(
            code,
            409,
            "The Space request conflicts with current state.",
            detail,
            recoveryAction);

    private const int HttpCreated = 201;
    private const int HttpAccepted = 202;
}

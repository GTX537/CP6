using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDesignV1Service :
    ISpaceDesignV1Service,
    ISpaceDesignCodingService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const string BlankVersionMode = "Blank";
    private const string PublishedVersionMode = "PublishedVersion";
    private const string CodingDecisionModify = "modify";
    private const string CodingDecisionUnchanged = "unchanged";
    private const string CodingDecisionProtected = "protected";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly SpaceVersionCloneCoordinator _clone;
    private readonly SpaceSourceCoordinator _sources;
    private readonly ISpaceLocationCodeRuleProvider _codingRules;

    public SpaceDesignV1Service(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceCursorCodec cursorCodec,
        ISpaceDesignAccessEvaluator access,
        SpaceVersionCloneCoordinator clone,
        SpaceSourceCoordinator sources,
        ISpaceLocationCodeRuleProvider? codingRules = null)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _cursorCodec = cursorCodec;
        _access = access;
        _clone = clone;
        _sources = sources;
        _codingRules = codingRules ?? EmptySpaceLocationCodeRuleProvider.Instance;
    }

    private sealed class EmptySpaceLocationCodeRuleProvider :
        ISpaceLocationCodeRuleProvider
    {
        public static readonly EmptySpaceLocationCodeRuleProvider Instance = new();

        public Task<SpaceLocationCodingCatalog> GetCatalogAsync(
            Guid siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SpaceLocationCodingCatalog(null, []));
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
            .Where(version =>
                version.ModelId == model.Id &&
                version.Purpose == SpaceModelVersionPurpose.Production);
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

    public async Task<IReadOnlyList<SpaceSceneFloorDto>> GetFloorsAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        EnsureInternalEditor();
        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureReadable(model);

        var floors = await _context.FloorRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(candidate => candidate.Level)
            .ThenBy(candidate => candidate.FloorCode)
            .ThenBy(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);
        return floors.Select(ToSceneDto).ToArray();
    }

    public async Task<CreateSpaceFloorResponse> CreateFloorAsync(
        Guid versionId,
        CreateSpaceFloorRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        EnsureInternalEditor();
        ArgumentNullException.ThrowIfNull(request);
        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureWritable(model);

        var normalizedRequest = new CreateSpaceFloorRequest(
            RequireText(request.FloorCode, 100, "floorCode"),
            RequireText(request.Name, 200, "name"),
            request.Level,
            request.Elevation,
            request.Height,
            request.ExpectedContentRevision);
        if (normalizedRequest.Height < 0)
        {
            throw Invalid(
                "height",
                "height must be greater than or equal to zero.");
        }
        if (normalizedRequest.ExpectedContentRevision < 0)
        {
            throw Invalid(
                "expectedContentRevision",
                "expectedContentRevision must be greater than or equal to zero.");
        }

        var operation = $"create-floor:{versionId:D}";
        var requestHash = Hash(
            JsonSerializer.Serialize(normalizedRequest, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadFloorReplayAsync(
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
            await AcquireVersionFloorInitializationLockAsync(
                versionId,
                cancellationToken);
            replay = await ReadFloorReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var version = await _context.Versions
                              .SingleOrDefaultAsync(
                                  candidate => candidate.Id == versionId,
                                  cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            if (version.Purpose != SpaceModelVersionPurpose.Production ||
                version.Status != SpaceVersionStatus.Draft)
            {
                throw Conflict(
                    SpaceErrorCodes.VersionStateInvalid,
                    "Only a production Draft version accepts new floors.",
                    "open-or-create-draft");
            }
            if (version.ContentRevision !=
                normalizedRequest.ExpectedContentRevision)
            {
                throw Conflict(
                    SpaceErrorCodes.ConcurrencyConflict,
                    $"Expected content revision " +
                    $"{normalizedRequest.ExpectedContentRevision}, but the " +
                    $"current revision is {version.ContentRevision}.",
                    "reload-design-project");
            }
            if (await _context.FloorRevisions.AnyAsync(
                    candidate =>
                        candidate.ModelVersionId == versionId &&
                        candidate.FloorCode == normalizedRequest.FloorCode,
                    cancellationToken))
            {
                throw Conflict(
                    SpaceErrorCodes.VersionConflict,
                    "The Draft already contains that floor code.",
                    "choose-another-floor-code");
            }

            var floor = SpaceFloorRevision.Create(
                _execution.TenantId,
                versionId,
                Guid.NewGuid(),
                model.SiteId,
                normalizedRequest.Level,
                normalizedRequest.FloorCode,
                normalizedRequest.Name,
                normalizedRequest.Elevation,
                normalizedRequest.Height);
            _context.FloorRevisions.Add(floor);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var response = new CreateSpaceFloorResponse(
                ToSceneDto(floor),
                version.ContentRevision,
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
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SpaceDesignSceneDto> GetSceneAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty)
            throw NotFound(SpaceErrorCodes.VersionNotFound, "Space version");
        if (floorLogicalId == Guid.Empty)
        {
            throw NotFound(
                SpaceErrorCodes.LogicalIdNotFound,
                "Space floor logical identity");
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

        var floor = await _context.FloorRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.LogicalId == floorLogicalId,
                cancellationToken);
        if (floor is null)
        {
            throw NotFound(
                SpaceErrorCodes.LogicalIdNotFound,
                "Space floor logical identity");
        }

        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId)
            .OrderBy(candidate => candidate.ZoneCode)
            .ThenBy(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);
        var zoneLogicalIds = zones
            .Select(candidate => candidate.LogicalId)
            .ToArray();
        var aisles = zoneLogicalIds.Length == 0
            ? []
            : await _context.AisleRevisions
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    zoneLogicalIds.Contains(candidate.ZoneLogicalId))
                .OrderBy(candidate => candidate.AisleCode)
                .ThenBy(candidate => candidate.LogicalId)
                .ToArrayAsync(cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId)
            .OrderBy(candidate => candidate.RackCode)
            .ThenBy(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);
        var rackLogicalIds = racks
            .Select(candidate => candidate.LogicalId)
            .ToArray();
        var rackLevels = rackLogicalIds.Length == 0
            ? []
            : await _context.RackLevelRevisions
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    rackLogicalIds.Contains(candidate.RackLogicalId))
                .OrderBy(candidate => candidate.RackLogicalId)
                .ThenBy(candidate => candidate.LevelNo)
                .ThenBy(candidate => candidate.LogicalId)
                .ToArrayAsync(cancellationToken);
        var locations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId)
            .OrderBy(candidate => candidate.LocationCode)
            .ThenBy(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);
        var elements = await _context.ElementRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId)
            .OrderBy(candidate => candidate.ElementType)
            .ThenBy(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);
        var elementRevisionIds = elements
            .Select(candidate => candidate.Id)
            .ToArray();
        var attributes = elementRevisionIds.Length == 0
            ? []
            : await _context.ElementAttributes
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    elementRevisionIds.Contains(candidate.ElementRevisionId))
                .OrderBy(candidate => candidate.ElementRevisionId)
                .ThenBy(candidate => candidate.Namespace)
                .ThenBy(candidate => candidate.Key)
                .ToArrayAsync(cancellationToken);
        var locationLogicalIds = locations
            .Select(candidate => candidate.LogicalId)
            .ToArray();
        var locationExternalBindings = locationLogicalIds.Length == 0
            ? []
            : await _context.LocationExternalBindings
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    locationLogicalIds.Contains(candidate.LocationLogicalId))
                .OrderBy(candidate => candidate.LocationLogicalId)
                .ThenBy(candidate => candidate.BindingMode)
                .ThenBy(candidate => candidate.ExternalLocationId)
                .ToArrayAsync(cancellationToken);
        var designTargetIds = rackLogicalIds
            .Concat(rackLevels.Select(candidate => candidate.LogicalId))
            .Concat(locationLogicalIds)
            .Distinct()
            .ToArray();
        var designAttributes = designTargetIds.Length == 0
            ? []
            : await _context.DesignAttributes
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    designTargetIds.Contains(candidate.ObjectLogicalId))
                .OrderBy(candidate => candidate.ObjectType)
                .ThenBy(candidate => candidate.ObjectLogicalId)
                .ThenBy(candidate => candidate.Namespace)
                .ThenBy(candidate => candidate.Key)
                .ToArrayAsync(cancellationToken);

        return new SpaceDesignSceneDto(
            SpaceDesignSceneContract.SchemaVersion,
            SpaceDesignSceneContract.Authority,
            RuntimeOverlayIncluded: false,
            scope.Version.Id,
            scope.Model.SiteId,
            scope.Version.Status.ToString(),
            scope.Version.ContentRevision,
            scope.Version.ContentHash,
            ToSceneDto(floor),
            zones.Select(ToSceneDto).ToArray(),
            aisles.Select(ToSceneDto).ToArray(),
            racks.Select(ToSceneDto).ToArray(),
            rackLevels.Select(ToSceneDto).ToArray(),
            locations.Select(ToSceneDto).ToArray(),
            elements.Select(ToSceneDto).ToArray(),
            attributes.Select(ToSceneDto).ToArray(),
            locationExternalBindings.Select(ToSceneDto).ToArray(),
            designAttributes.Select(ToSceneDto).ToArray());
    }

    public async Task<SpacePublishedViewerSceneDto> GetPublishedSceneAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var model = await FindModelBySiteAsync(siteId, cancellationToken);
        EnsureReadable(model);

        if (model.CurrentPublishedVersionId is not Guid publishedVersionId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionNotFound,
                404,
                "Current Published space version",
                recoveryAction: "publish-version");
        }

        var published = await _context.Versions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == publishedVersionId &&
                    candidate.ModelId == model.Id &&
                    candidate.Purpose == SpaceModelVersionPurpose.Production &&
                    candidate.Status == SpaceVersionStatus.Published,
                cancellationToken);
        if (published is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionNotFound,
                404,
                "Current Published space version",
                recoveryAction: "reconcile-published-version");
        }

        var floorLogicalIds = await _context.FloorRevisions
            .AsNoTracking()
            .Where(candidate =>
                candidate.ModelVersionId == publishedVersionId &&
                candidate.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(candidate => candidate.Level)
            .ThenBy(candidate => candidate.FloorCode)
            .ThenBy(candidate => candidate.LogicalId)
            .Select(candidate => candidate.LogicalId)
            .ToArrayAsync(cancellationToken);

        var floors = new List<SpaceDesignSceneDto>(floorLogicalIds.Length);
        foreach (var floorLogicalId in floorLogicalIds)
        {
            floors.Add(await GetSceneAsync(
                publishedVersionId,
                floorLogicalId,
                cancellationToken));
        }

        if (floors.Any(scene =>
                scene.ModelVersionId != publishedVersionId ||
                scene.SiteId != siteId ||
                scene.VersionStatus != SpaceVersionStatus.Published.ToString() ||
                scene.ContentRevision != published.ContentRevision ||
                !string.Equals(
                    scene.ContentHash,
                    published.ContentHash,
                    StringComparison.Ordinal) ||
                scene.RuntimeOverlayIncluded))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PublishedVersionChanged,
                409,
                "Published scene authority changed while it was being read.",
                recoveryAction: "reload-published-scene",
                retryable: true);
        }

        var publishedPointerIsCurrent = await _context.Models
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.Id == model.Id &&
                    candidate.CurrentPublishedVersionId == publishedVersionId,
                cancellationToken);
        var publishedAuthorityIsCurrent = await _context.Versions
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.Id == publishedVersionId &&
                    candidate.ModelId == model.Id &&
                    candidate.Purpose == SpaceModelVersionPurpose.Production &&
                    candidate.Status == SpaceVersionStatus.Published &&
                    candidate.ContentRevision == published.ContentRevision &&
                    candidate.ContentHash == published.ContentHash,
                cancellationToken);
        if (!publishedPointerIsCurrent || !publishedAuthorityIsCurrent)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PublishedVersionChanged,
                409,
                "Published scene authority changed while it was being read.",
                recoveryAction: "reload-published-scene",
                retryable: true);
        }

        return new SpacePublishedViewerSceneDto(
            SpaceDesignSceneContract.SchemaVersion,
            SpaceDesignSceneContract.Authority,
            RuntimeOverlayIncluded: false,
            siteId,
            publishedVersionId,
            published.PublishedAtUtc,
            published.ContentRevision,
            published.ContentHash,
            floors);
    }

    public async Task<ApplySpaceElementCommandBatchResponse>
        ApplyElementCommandsAsync(
            Guid versionId,
            Guid floorLogicalId,
            ApplySpaceElementCommandBatchRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();

        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureWritable(model);
        await EnsureActiveEditLeaseAsync(
            versionId,
            floorLogicalId,
            request.LeaseId,
            request.ClientInstanceId,
            cancellationToken);
        var requestHash = Hash(
            $"{versionId:D}\n{floorLogicalId:D}\n" +
            JsonSerializer.Serialize(request, JsonOptions));

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                floorLogicalId,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                floorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);
            var version = await _context.Versions
                              .SingleOrDefaultAsync(
                                  candidate => candidate.Id == versionId,
                                  cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            if (version.Status != SpaceVersionStatus.Draft)
            {
                throw Conflict(
                    SpaceErrorCodes.VersionStateInvalid,
                    "Only a Draft version accepts editor commands.",
                    "open-or-create-draft");
            }
            if (request.ExpectedContentRevision.HasValue &&
                (version.ContentRevision != request.ExpectedContentRevision.Value ||
                 !string.Equals(
                     version.ContentHash,
                     request.ExpectedContentHash,
                     StringComparison.Ordinal)))
            {
                var completedReplay = await ReadElementCommandReplayAsync(
                    request.CommandBatchId,
                    requestHash,
                    cancellationToken);
                if (completedReplay is not null &&
                    completedReplay.VersionContentRevision == version.ContentRevision)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return completedReplay;
                }
                throw Conflict(
                    SpaceErrorCodes.ParseChangesetStale,
                    "The CAD changeset no longer matches the current Draft revision.",
                    "start-new-cad-parse");
            }

            var floor = await _context.FloorRevisions
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.ModelVersionId == versionId &&
                                    candidate.LogicalId == floorLogicalId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.LogicalIdNotFound,
                            "Space floor logical identity");
            if (floor.Revision != request.ExpectedFloorRevision)
            {
                var completedReplay = await ReadElementCommandReplayAsync(
                    request.CommandBatchId,
                    requestHash,
                    cancellationToken);
                if (completedReplay is not null &&
                    completedReplay.FloorRevision == floor.Revision)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return completedReplay;
                }
                throw Conflict(
                    SpaceErrorCodes.FloorRevisionConflict,
                    $"Expected floor revision {request.ExpectedFloorRevision}, " +
                    $"but the current revision is {floor.Revision}.",
                    "reload-floor-scene");
            }

            ValidateElementCommandBatch(request);
            var concurrentReplay = await ReadElementCommandReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var targetIds = request.Commands
                .Where(command => command.Type !=
                    SpaceElementCommandContract.CreateElement)
                .Select(command => command.TargetLogicalId)
                .ToArray();
            var createIds = request.Commands
                .Where(command => command.Type ==
                    SpaceElementCommandContract.CreateElement)
                .Select(command => command.TargetLogicalId)
                .ToArray();
            var elements = await _context.ElementRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId &&
                    targetIds.Contains(candidate.LogicalId))
                .ToDictionaryAsync(
                    candidate => candidate.LogicalId,
                    cancellationToken);
            var racks = await _context.RackRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId &&
                    targetIds.Contains(candidate.LogicalId))
                .ToDictionaryAsync(
                    candidate => candidate.LogicalId,
                    cancellationToken);
            if (elements.Keys.Intersect(racks.Keys).Any() ||
                elements.Count + racks.Count != targetIds.Length)
            {
                throw NotFound(
                    SpaceErrorCodes.LogicalIdNotFound,
                    "Space editor object logical identity");
            }
            if (createIds.Length > 0 &&
                await LogicalIdsExistAsync(versionId, createIds, cancellationToken))
            {
                throw Conflict(
                    SpaceErrorCodes.CommandConflict,
                    "A CreateElement target logical identity already exists.",
                    "create-new-command-batch");
            }

            var elementRevisionIds = elements.Values
                .Select(element => element.Id)
                .ToArray();
            var loadedAttributes = await _context.ElementAttributes
                .Where(attribute =>
                    attribute.ModelVersionId == versionId &&
                    elementRevisionIds.Contains(attribute.ElementRevisionId))
                .ToListAsync(cancellationToken);
            var attributesByElement = loadedAttributes
                .GroupBy(attribute => attribute.ElementRevisionId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());
            var rackIds = racks.Keys.ToArray();
            var rackLevels = await _context.RackLevelRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    rackIds.Contains(candidate.RackLogicalId))
                .ToListAsync(cancellationToken);

            var sourceIds = request.Commands
                .Where(command => command.CreateElement?.SourceId is not null)
                .Select(command => command.CreateElement!.SourceId!.Value)
                .Distinct()
                .ToArray();
            var sources = sourceIds.Length == 0
                ? new Dictionary<Guid, SpaceModelSource>()
                : await _context.Sources
                    .Where(candidate =>
                        candidate.ModelVersionId == versionId &&
                        sourceIds.Contains(candidate.Id))
                    .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
            if (sources.Count != sourceIds.Length)
            {
                throw NotFound(
                    SpaceErrorCodes.SourceNotFound,
                    "Space CAD source");
            }
            var locations = await _context.LocationRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId &&
                    candidate.RackLogicalId.HasValue &&
                    rackIds.Contains(candidate.RackLogicalId.Value))
                .ToListAsync(cancellationToken);

            ValidateCommandTargets(
                request.Commands,
                elements,
                racks,
                rackLevels,
                locations,
                request.ChangesetSha256 is not null);
            await ValidateRackArrayCodesAsync(
                versionId,
                request.Commands,
                racks,
                cancellationToken);

            var batch = SpaceElementCommandBatch.Create(
                _execution.TenantId,
                request.CommandBatchId,
                versionId,
                floorLogicalId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                request.ExpectedContentHash,
                request.ChangesetSha256,
                requestHash,
                _execution.ActorId,
                RequireUtcNow());
            _context.ElementCommandBatches.Add(batch);

            var affectedElementCommands =
                new Dictionary<Guid, SpaceElementCommandDto>();
            var beforeElements =
                new Dictionary<Guid, SpaceSceneElementDto>();
            var beforeElementAttributes =
                new Dictionary<Guid,
                    IReadOnlyList<SpaceSceneElementAttributeDto>>();
            var affectedRacks = new Dictionary<Guid, SpaceRackRevision>();
            var affectedRackLevels =
                new Dictionary<Guid, SpaceRackLevelRevision>();
            var affectedLocations =
                new Dictionary<Guid, SpaceLocationRevision>();
            for (var index = 0; index < request.Commands.Count; index++)
            {
                var command = request.Commands[index];
                string beforeJson;
                string afterJson;
                string payloadJson;
                if (command.Type == SpaceElementCommandContract.CreateElement)
                {
                    var payload = command.CreateElement!;
                    var element = SpaceElementRevision.Create(
                        _execution.TenantId,
                        versionId,
                        command.TargetLogicalId,
                        floorLogicalId,
                        payload.ElementType,
                        payload.GeometryJson,
                        payload.ParentLogicalId);
                    element.ConfigurePlacement(
                        payload.X,
                        payload.Y,
                        payload.Z,
                        payload.RotationZ,
                        payload.Width,
                        payload.Height,
                        payload.Depth);
                    element.ConfigureBusinessLink(
                        payload.BusinessCode,
                        payload.LinkedEntityType,
                        payload.LinkedLogicalId);
                    if (payload.SourceId.HasValue)
                    {
                        element.AttachSource(
                            sources[payload.SourceId.Value],
                            payload.SourceRef);
                    }
                    _context.ElementRevisions.Add(element);
                    var attributes = new List<SpaceElementAttribute>();
                    ApplyElementAttributes(element, attributes, payload.Attributes);
                    elements[element.LogicalId] = element;
                    attributesByElement[element.Id] = attributes;
                    beforeJson = "{}";
                    payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                    afterJson = ElementAuditJson(element, attributes);
                    affectedElementCommands[element.LogicalId] = command;
                }
                else if (elements.TryGetValue(
                        command.TargetLogicalId,
                        out var element))
                {
                    if (!attributesByElement.TryGetValue(
                            element.Id,
                            out var attributes))
                    {
                        attributes = [];
                        attributesByElement[element.Id] = attributes;
                    }

                    if (!beforeElements.ContainsKey(element.LogicalId))
                    {
                        beforeElements[element.LogicalId] = ToSceneDto(element);
                        beforeElementAttributes[element.LogicalId] = attributes
                            .Where(attribute => !attribute.IsDeleted)
                            .OrderBy(attribute => attribute.Namespace)
                            .ThenBy(attribute => attribute.Key)
                            .Select(ToSceneDto)
                            .ToArray();
                    }

                    beforeJson = ElementAuditJson(element, attributes);
                    payloadJson = ApplyElementCommand(
                        command,
                        element,
                        attributes);
                    afterJson = ElementAuditJson(element, attributes);
                    affectedElementCommands[element.LogicalId] = command;
                }
                else
                {
                    var rack = racks[command.TargetLogicalId];
                    var relatedLevels = rackLevels
                        .Where(level => level.RackLogicalId == rack.LogicalId)
                        .ToList();
                    var relatedLocations = locations
                        .Where(location =>
                            location.RackLogicalId == rack.LogicalId)
                        .ToList();
                    beforeJson = RackAuditJson(
                        rack,
                        relatedLevels,
                        relatedLocations);
                    if (command.Type ==
                        SpaceElementCommandContract.GenerateRackArray)
                    {
                        var generated = GenerateRackArray(
                            versionId,
                            floorLogicalId,
                            rack,
                            relatedLevels,
                            relatedLocations,
                            command.GenerateRackArray!);
                        foreach (var generatedRack in generated.Racks)
                        {
                            racks[generatedRack.LogicalId] = generatedRack;
                            affectedRacks[generatedRack.LogicalId] =
                                generatedRack;
                        }
                        foreach (var level in generated.Levels)
                        {
                            rackLevels.Add(level);
                            affectedRackLevels[level.LogicalId] = level;
                        }
                        foreach (var location in generated.Locations)
                        {
                            locations.Add(location);
                            affectedLocations[location.LogicalId] = location;
                        }
                        payloadJson = JsonSerializer.Serialize(
                            command.GenerateRackArray,
                            JsonOptions);
                        afterJson = JsonSerializer.Serialize(
                            new
                            {
                                Source = JsonSerializer.Deserialize<JsonElement>(
                                    RackAuditJson(
                                        rack,
                                        relatedLevels,
                                        relatedLocations)),
                                GeneratedRacks = generated.Racks.Select(
                                    generatedRack =>
                                        JsonSerializer.Deserialize<JsonElement>(
                                            RackAuditJson(
                                                generatedRack,
                                                generated.Levels.Where(level =>
                                                    level.RackLogicalId ==
                                                    generatedRack.LogicalId),
                                                generated.Locations.Where(
                                                    location =>
                                                        location.RackLogicalId ==
                                                        generatedRack.LogicalId))))
                                    .ToArray(),
                            },
                            JsonOptions);
                    }
                    else
                    {
                        payloadJson = ApplyRackCommand(
                            command,
                            rack,
                            relatedLevels,
                            relatedLocations);
                        afterJson = RackAuditJson(
                            rack,
                            relatedLevels,
                            relatedLocations);
                        affectedRacks[rack.LogicalId] = rack;
                        foreach (var level in relatedLevels)
                            affectedRackLevels[level.LogicalId] = level;
                        foreach (var location in relatedLocations)
                            affectedLocations[location.LogicalId] = location;
                    }
                }

                _context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        _execution.TenantId,
                        command.CommandId,
                        batch,
                        index,
                        command.Type,
                        command.TargetLogicalId,
                        payloadJson,
                        beforeJson,
                        afterJson));
            }

            floor.AdvanceRevision(request.ExpectedFloorRevision);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var affected = affectedElementCommands
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    var element = elements[pair.Key];
                    var attributes = attributesByElement[element.Id];
                    return new SpaceElementCommandResultDto(
                        pair.Value.CommandId,
                        pair.Value.Type,
                        pair.Key,
                        ToSceneDto(element),
                        attributes
                        .Where(attribute => !attribute.IsDeleted)
                        .OrderBy(attribute => attribute.Namespace)
                        .ThenBy(attribute => attribute.Key)
                        .Select(ToSceneDto)
                        .ToArray(),
                        beforeElements.GetValueOrDefault(pair.Key),
                        beforeElementAttributes.GetValueOrDefault(pair.Key));
                })
                .ToArray();
            var response = new ApplySpaceElementCommandBatchResponse(
                request.CommandBatchId,
                floor.Revision,
                version.ContentRevision,
                affected,
                IdempotentReplay: false,
                affectedRacks.Values
                    .OrderBy(candidate => candidate.RackCode)
                    .ThenBy(candidate => candidate.LogicalId)
                    .Select(ToSceneDto)
                    .ToArray(),
                affectedRackLevels.Values
                    .OrderBy(candidate => candidate.RackLogicalId)
                    .ThenBy(candidate => candidate.LevelNo)
                    .Select(ToSceneDto)
                    .ToArray(),
                affectedLocations.Values
                    .OrderBy(candidate => candidate.RackLogicalId)
                    .ThenBy(candidate => candidate.LevelNo)
                    .ThenBy(candidate => candidate.ColumnNo)
                    .ThenBy(candidate => candidate.DepthNo)
                    .Select(ToSceneDto)
                    .ToArray());
            batch.Complete(
                floor.Revision,
                version.ContentRevision,
                JsonSerializer.Serialize(response, JsonOptions));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var concurrentReplay = await ReadElementCommandReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The command batch conflicted with a concurrent editor write.",
                "reload-floor-scene");
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw Invalid("commands", exception.Message);
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw Invalid(
                "commands",
                "The command result exceeds the supported coordinate or sequence range.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ApplySpaceLayoutCommandBatchResponse>
        ApplyLayoutCommandsAsync(
            Guid versionId,
            Guid floorLogicalId,
            ApplySpaceLayoutCommandBatchRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();

        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureWritable(model);
        await EnsureActiveEditLeaseAsync(
            versionId,
            floorLogicalId,
            request.LeaseId,
            request.ClientInstanceId,
            cancellationToken);
        var requestHash = Hash(
            $"layout\n{versionId:D}\n{floorLogicalId:D}\n" +
            JsonSerializer.Serialize(request, JsonOptions));

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                floorLogicalId,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                floorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);

            var version = await _context.Versions
                              .SingleOrDefaultAsync(
                                  candidate => candidate.Id == versionId,
                                  cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            if (version.Status != SpaceVersionStatus.Draft)
            {
                throw Conflict(
                    SpaceErrorCodes.VersionStateInvalid,
                    "Only a Draft version accepts layout commands.",
                    "open-or-create-draft");
            }

            var floor = await _context.FloorRevisions
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.ModelVersionId == versionId &&
                                    candidate.LogicalId == floorLogicalId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.LogicalIdNotFound,
                            "Space floor logical identity");
            if (floor.Revision != request.ExpectedFloorRevision ||
                version.ContentRevision != request.ExpectedContentRevision)
            {
                var completedReplay = await ReadLayoutCommandReplayAsync(
                    request.CommandBatchId,
                    requestHash,
                    cancellationToken);
                if (completedReplay is not null &&
                    completedReplay.FloorRevision == floor.Revision &&
                    completedReplay.VersionContentRevision ==
                        version.ContentRevision)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return completedReplay;
                }

                if (floor.Revision != request.ExpectedFloorRevision)
                {
                    throw Conflict(
                        SpaceErrorCodes.FloorRevisionConflict,
                        $"Expected floor revision {request.ExpectedFloorRevision}, " +
                        $"but the current revision is {floor.Revision}.",
                        "reload-floor-scene");
                }
                throw Conflict(
                    SpaceErrorCodes.VersionConflict,
                    $"Expected content revision {request.ExpectedContentRevision}, " +
                    $"but the current revision is {version.ContentRevision}.",
                    "reload-floor-scene");
            }

            ValidateLayoutCommandBatch(request);
            var concurrentReplay = await ReadLayoutCommandReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var newLogicalIds = ExpandCreatedLayoutLogicalIds(request.Commands);
            if (newLogicalIds.Count != newLogicalIds.Distinct().Count() ||
                await LogicalIdsExistAsync(
                    versionId,
                    newLogicalIds.ToArray(),
                    cancellationToken))
            {
                throw Conflict(
                    SpaceErrorCodes.CommandConflict,
                    "A layout command logical identity already exists.",
                    "create-new-command-batch");
            }

            var persistedZones = await _context.ZoneRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId)
                .ToListAsync(cancellationToken);
            var persistedZoneIds = persistedZones
                .Where(candidate =>
                    candidate.LifecycleState == SpaceLifecycleState.Active)
                .Select(candidate => candidate.LogicalId)
                .ToHashSet();
            var zoneCodes = persistedZones
                .Where(candidate =>
                    candidate.LifecycleState == SpaceLifecycleState.Active)
                .Select(candidate => candidate.ZoneCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var persistedAisles = await _context.AisleRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    persistedZoneIds.Contains(candidate.ZoneLogicalId))
                .ToListAsync(cancellationToken);
            var aisleZones = persistedAisles
                .Where(candidate =>
                    candidate.LifecycleState == SpaceLifecycleState.Active)
                .ToDictionary(
                candidate => candidate.LogicalId,
                candidate => candidate.ZoneLogicalId);
            var aisleCodes = persistedAisles
                .Where(candidate =>
                    candidate.LifecycleState == SpaceLifecycleState.Active)
                .Select(candidate => (candidate.ZoneLogicalId, candidate.AisleCode))
                .ToHashSet();
            var persistedRacks = await _context.RackRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId)
                .ToListAsync(cancellationToken);
            var rackCodes = persistedRacks
                .Where(candidate =>
                    candidate.LifecycleState == SpaceLifecycleState.Active)
                .Select(candidate => (candidate.ZoneLogicalId, candidate.RackCode))
                .ToHashSet();
            var persistedLevels = await _context.RackLevelRevisions
                .Where(candidate => candidate.ModelVersionId == versionId)
                .ToListAsync(cancellationToken);
            var persistedLocations = await _context.LocationRevisions
                .Where(candidate =>
                    candidate.ModelVersionId == versionId &&
                    candidate.FloorLogicalId == floorLogicalId)
                .ToListAsync(cancellationToken);
            var locationCodes = (await _context.LocationRevisions
                    .Where(candidate =>
                        candidate.ModelVersionId == versionId &&
                        candidate.LocationCode != null)
                    .Select(candidate => candidate.LocationCode!)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var zones = new Dictionary<Guid, SpaceZoneRevision>();
            var aisles = new Dictionary<Guid, SpaceAisleRevision>();
            var racks = new Dictionary<Guid, SpaceRackRevision>();
            var levels = new List<SpaceRackLevelRevision>();
            var locations = new List<SpaceLocationRevision>();
            var commandResults = new List<SpaceLayoutCommandResultDto>();
            var batch = SpaceElementCommandBatch.Create(
                _execution.TenantId,
                request.CommandBatchId,
                versionId,
                floorLogicalId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                expectedContentHash: null,
                changesetSha256: null,
                requestHash,
                _execution.ActorId,
                RequireUtcNow());
            _context.ElementCommandBatches.Add(batch);

            void EnsureRackParentIsValid(
                Guid zoneLogicalId,
                Guid? aisleLogicalId)
            {
                if (!persistedZoneIds.Contains(zoneLogicalId))
                {
                    throw NotFound(
                        SpaceErrorCodes.LogicalIdNotFound,
                        "Space zone logical identity");
                }
                if (aisleLogicalId.HasValue &&
                    (!aisleZones.TryGetValue(
                         aisleLogicalId.Value,
                         out var aisleZoneId) ||
                     aisleZoneId != zoneLogicalId))
                {
                    throw Invalid(
                        "commands.layoutRack.aisleLogicalId",
                        "The aisle must exist in the selected zone.");
                }
            }

            for (var index = 0; index < request.Commands.Count; index++)
            {
                var command = request.Commands[index];
                string afterJson;
                var beforeJson = "{}";
                string payloadJson;
                switch (command.Type)
                {
                    case SpaceLayoutCommandContract.CreateZone:
                    {
                        var payload = command.CreateZone!;
                        if (!zoneCodes.Add(payload.ZoneCode.Trim()))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Zone code '{payload.ZoneCode}' is already active on the floor.",
                                "choose-unique-layout-code");
                        }
                        var zone = SpaceZoneRevision.Create(
                            _execution.TenantId,
                            versionId,
                            command.TargetLogicalId,
                            floorLogicalId,
                            payload.ZoneCode,
                            payload.ZoneType,
                            payload.Name);
                        zone.ConfigureShape(
                            payload.PolygonJson,
                            payload.Color,
                            payload.CapabilityFlags);
                        zones.Add(zone.LogicalId, zone);
                        persistedZoneIds.Add(zone.LogicalId);
                        _context.ZoneRevisions.Add(zone);
                        payloadJson = JsonSerializer.Serialize(
                            payload,
                            JsonOptions);
                        afterJson = JsonSerializer.Serialize(
                            ToSceneDto(zone),
                            JsonOptions);
                        break;
                    }
                    case SpaceLayoutCommandContract.CreateAisle:
                    {
                        var payload = command.CreateAisle!;
                        if (!persistedZoneIds.Contains(payload.ZoneLogicalId))
                        {
                            throw NotFound(
                                SpaceErrorCodes.LogicalIdNotFound,
                                "Space zone logical identity");
                        }
                        if (!AddScopedCode(
                                aisleCodes,
                                payload.ZoneLogicalId,
                                payload.AisleCode))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Aisle code '{payload.AisleCode}' is already active in the zone.",
                                "choose-unique-layout-code");
                        }
                        var aisle = SpaceAisleRevision.Create(
                            _execution.TenantId,
                            versionId,
                            command.TargetLogicalId,
                            payload.ZoneLogicalId,
                            payload.AisleCode,
                            payload.Direction,
                            payload.Name);
                        aisle.ConfigureShape(
                            payload.PolygonJson,
                            payload.CenterlineJson);
                        aisles.Add(aisle.LogicalId, aisle);
                        aisleZones.Add(aisle.LogicalId, aisle.ZoneLogicalId);
                        _context.AisleRevisions.Add(aisle);
                        payloadJson = JsonSerializer.Serialize(
                            payload,
                            JsonOptions);
                        afterJson = JsonSerializer.Serialize(
                            ToSceneDto(aisle),
                            JsonOptions);
                        break;
                    }
                    case SpaceLayoutCommandContract.CreateRack:
                    {
                        var payload = command.CreateRack!;
                        if (!persistedZoneIds.Contains(payload.ZoneLogicalId))
                        {
                            throw NotFound(
                                SpaceErrorCodes.LogicalIdNotFound,
                                "Space zone logical identity");
                        }
                        if (payload.AisleLogicalId.HasValue &&
                            (!aisleZones.TryGetValue(
                                 payload.AisleLogicalId.Value,
                                 out var aisleZoneId) ||
                             aisleZoneId != payload.ZoneLogicalId))
                        {
                            throw Invalid(
                                "commands.createRack.aisleLogicalId",
                                "The aisle must exist in the selected zone.");
                        }
                        if (!AddScopedCode(
                                rackCodes,
                                payload.ZoneLogicalId,
                                payload.RackCode))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Rack code '{payload.RackCode}' is already active in the zone.",
                                "choose-unique-layout-code");
                        }

                        var rack = SpaceRackRevision.Create(
                            _execution.TenantId,
                            versionId,
                            command.TargetLogicalId,
                            floorLogicalId,
                            payload.ZoneLogicalId,
                            payload.RackCode,
                            payload.AisleLogicalId,
                            payload.Name,
                            payload.RackType);
                        rack.ConfigureGeometry(
                            payload.X,
                            payload.Y,
                            payload.Z,
                            payload.RotationZ,
                            payload.Width,
                            payload.Depth,
                            payload.Height,
                            payload.TemplateVersionId);
                        racks.Add(rack.LogicalId, rack);
                        _context.RackRevisions.Add(rack);

                        foreach (var levelPayload in payload.Levels
                                     .OrderBy(candidate => candidate.LevelNo))
                        {
                            var level = SpaceRackLevelRevision.Create(
                                _execution.TenantId,
                                versionId,
                                WarehouseDeterministicIdentity
                                    .CreateRackLevelLogicalId(
                                        rack.LogicalId,
                                        levelPayload.LevelNo),
                                rack.LogicalId,
                                levelPayload.LevelNo,
                                levelPayload.BottomZ,
                                levelPayload.ClearHeight,
                                levelPayload.BinCount,
                                levelPayload.DepthCount,
                                levelPayload.CellWidth,
                                levelPayload.CellDepth,
                                levelPayload.MaxLoad,
                                levelPayload.BeamHeight);
                            levels.Add(level);
                            _context.RackLevelRevisions.Add(level);

                            for (var columnNo = 1;
                                 columnNo <= levelPayload.BinCount;
                                 columnNo++)
                            {
                                for (var depthNo = 1;
                                     depthNo <= levelPayload.DepthCount;
                                     depthNo++)
                                {
                                    var locationCode = CreateManualLocationCode(
                                        levelPayload.LocationCodePrefix,
                                        levelPayload.LevelNo,
                                        columnNo,
                                        depthNo);
                                    if (locationCode is not null &&
                                        !locationCodes.Add(locationCode))
                                    {
                                        throw Conflict(
                                            SpaceErrorCodes.CommandConflict,
                                            $"Generated location code '{locationCode}' already exists.",
                                            "choose-unique-layout-code");
                                    }
                                    var location = SpaceLocationRevision.Create(
                                        _execution.TenantId,
                                        versionId,
                                        WarehouseDeterministicIdentity
                                            .CreateLocationLogicalId(
                                                rack.LogicalId,
                                                levelPayload.LevelNo,
                                                columnNo,
                                                depthNo),
                                        floorLogicalId,
                                        rack.LogicalId,
                                        locationCode,
                                        columnNo,
                                        levelPayload.LevelNo,
                                        depthNo,
                                        levelPayload.CellWidth,
                                        levelPayload.ClearHeight,
                                        levelPayload.CellDepth,
                                        levelPayload.MaxLoad,
                                        SpaceLocationCodeOrigin.Generated);
                                    locations.Add(location);
                                    _context.LocationRevisions.Add(location);
                                }
                            }
                        }
                        payloadJson = JsonSerializer.Serialize(
                            payload,
                            JsonOptions);
                        afterJson = RackAuditJson(
                            rack,
                            levels.Where(candidate =>
                                candidate.RackLogicalId == rack.LogicalId),
                            locations.Where(candidate =>
                                candidate.RackLogicalId == rack.LogicalId));
                        break;
                    }
                    case SpaceLayoutCommandContract.UpdateZone:
                    {
                        var payload = command.UpdateZone!;
                        var zone = FindActiveLayoutTarget(
                            zones,
                            persistedZones,
                            command.TargetLogicalId,
                            "Space zone logical identity");
                        beforeJson = JsonSerializer.Serialize(ToSceneDto(zone), JsonOptions);
                        zoneCodes.Remove(zone.ZoneCode);
                        if (!zoneCodes.Add(payload.ZoneCode.Trim()))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Zone code '{payload.ZoneCode}' is already active on the floor.",
                                "choose-unique-layout-code");
                        }
                        zone.UpdateDefinition(
                            floorLogicalId,
                            payload.ZoneCode,
                            payload.ZoneType,
                            payload.Name);
                        zone.ConfigureShape(
                            payload.PolygonJson,
                            payload.Color,
                            payload.CapabilityFlags);
                        zones[zone.LogicalId] = zone;
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = JsonSerializer.Serialize(ToSceneDto(zone), JsonOptions);
                        break;
                    }
                    case SpaceLayoutCommandContract.UpdateAisle:
                    {
                        var payload = command.UpdateAisle!;
                        var aisle = FindActiveLayoutTarget(
                            aisles,
                            persistedAisles,
                            command.TargetLogicalId,
                            "Space aisle logical identity");
                        if (!persistedZoneIds.Contains(payload.ZoneLogicalId))
                        {
                            throw NotFound(
                                SpaceErrorCodes.LogicalIdNotFound,
                                "Space zone logical identity");
                        }
                        if (aisle.ZoneLogicalId != payload.ZoneLogicalId &&
                            ActiveRacks(persistedRacks, racks.Values).Any(candidate =>
                                candidate.AisleLogicalId == aisle.LogicalId))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                "An aisle with active racks cannot be moved to another zone.",
                                "create-new-aisle-and-move-racks");
                        }
                        beforeJson = JsonSerializer.Serialize(ToSceneDto(aisle), JsonOptions);
                        RemoveScopedCode(aisleCodes, aisle.ZoneLogicalId, aisle.AisleCode);
                        if (!AddScopedCode(
                                aisleCodes,
                                payload.ZoneLogicalId,
                                payload.AisleCode))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Aisle code '{payload.AisleCode}' is already active in the zone.",
                                "choose-unique-layout-code");
                        }
                        aisle.UpdateDefinition(
                            payload.ZoneLogicalId,
                            payload.AisleCode,
                            payload.Direction,
                            payload.Name);
                        aisle.ConfigureShape(payload.PolygonJson, payload.CenterlineJson);
                        aisles[aisle.LogicalId] = aisle;
                        aisleZones[aisle.LogicalId] = aisle.ZoneLogicalId;
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = JsonSerializer.Serialize(ToSceneDto(aisle), JsonOptions);
                        break;
                    }
                    case SpaceLayoutCommandContract.UpdateRack:
                    {
                        var payload = command.UpdateRack!;
                        var rack = FindActiveLayoutTarget(
                            racks,
                            persistedRacks,
                            command.TargetLogicalId,
                            "Space rack logical identity");
                        EnsureRackParentIsValid(payload.ZoneLogicalId, payload.AisleLogicalId);
                        beforeJson = RackAuditJson(
                            rack,
                            RackLevelsFor(rack.LogicalId, persistedLevels, levels),
                            LocationsFor(rack.LogicalId, persistedLocations, locations));
                        RemoveScopedCode(rackCodes, rack.ZoneLogicalId, rack.RackCode);
                        if (!AddScopedCode(rackCodes, payload.ZoneLogicalId, payload.RackCode))
                        {
                            throw Conflict(
                                SpaceErrorCodes.CommandConflict,
                                $"Rack code '{payload.RackCode}' is already active in the zone.",
                                "choose-unique-layout-code");
                        }
                        rack.UpdateDefinition(
                            floorLogicalId,
                            payload.ZoneLogicalId,
                            payload.RackCode,
                            payload.AisleLogicalId,
                            payload.Name,
                            payload.RackType);
                        rack.ConfigureGeometry(
                            payload.X,
                            payload.Y,
                            payload.Z,
                            payload.RotationZ,
                            payload.Width,
                            payload.Depth,
                            payload.Height,
                            payload.TemplateVersionId);
                        racks[rack.LogicalId] = rack;
                        ReconcileRackLayout(
                            versionId,
                            floorLogicalId,
                            rack,
                            payload.Levels,
                            persistedLevels,
                            persistedLocations,
                            levels,
                            locations);
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = RackAuditJson(
                            rack,
                            RackLevelsFor(rack.LogicalId, persistedLevels, levels),
                            LocationsFor(rack.LogicalId, persistedLocations, locations));
                        break;
                    }
                    case SpaceLayoutCommandContract.DeleteRack:
                    {
                        var payload = command.DeleteObject!;
                        var rack = FindActiveLayoutTarget(
                            racks,
                            persistedRacks,
                            command.TargetLogicalId,
                            "Space rack logical identity");
                        var rackLevels = RackLevelsFor(
                            rack.LogicalId,
                            persistedLevels,
                            levels).ToArray();
                        var rackLocations = LocationsFor(
                            rack.LogicalId,
                            persistedLocations,
                            locations).ToArray();
                        RequireExplicitCascade(
                            payload,
                            rackLevels.Any(candidate => candidate.LifecycleState == SpaceLifecycleState.Active) ||
                            rackLocations.Any(candidate => candidate.LifecycleState == SpaceLifecycleState.Active),
                            "Rack",
                            "rack levels or locations");
                        beforeJson = RackAuditJson(rack, rackLevels, rackLocations);
                        ChangeRackLifecycle(
                            rack,
                            rackLevels,
                            rackLocations,
                            SpaceLifecycleState.RemoveRequested);
                        racks[rack.LogicalId] = rack;
                        AddAffected(levels, rackLevels);
                        AddAffected(locations, rackLocations);
                        RemoveScopedCode(rackCodes, rack.ZoneLogicalId, rack.RackCode);
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = RackAuditJson(rack, rackLevels, rackLocations);
                        break;
                    }
                    case SpaceLayoutCommandContract.DeleteAisle:
                    {
                        var payload = command.DeleteObject!;
                        var aisle = FindActiveLayoutTarget(
                            aisles,
                            persistedAisles,
                            command.TargetLogicalId,
                            "Space aisle logical identity");
                        var childRacks = ActiveRacks(persistedRacks, racks.Values)
                            .Where(candidate => candidate.AisleLogicalId == aisle.LogicalId)
                            .ToArray();
                        RequireExplicitCascade(
                            payload,
                            childRacks.Length > 0,
                            "Aisle",
                            "racks");
                        beforeJson = JsonSerializer.Serialize(ToSceneDto(aisle), JsonOptions);
                        foreach (var childRack in childRacks)
                        {
                            CascadeRackRemoval(
                                childRack,
                                persistedLevels,
                                persistedLocations,
                                racks,
                                levels,
                                locations);
                            RemoveScopedCode(
                                rackCodes,
                                childRack.ZoneLogicalId,
                                childRack.RackCode);
                        }
                        aisle.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
                        aisles[aisle.LogicalId] = aisle;
                        aisleZones.Remove(aisle.LogicalId);
                        RemoveScopedCode(aisleCodes, aisle.ZoneLogicalId, aisle.AisleCode);
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = JsonSerializer.Serialize(ToSceneDto(aisle), JsonOptions);
                        break;
                    }
                    case SpaceLayoutCommandContract.DeleteZone:
                    {
                        var payload = command.DeleteObject!;
                        var zone = FindActiveLayoutTarget(
                            zones,
                            persistedZones,
                            command.TargetLogicalId,
                            "Space zone logical identity");
                        var childAisles = ActiveAisles(persistedAisles, aisles.Values)
                            .Where(candidate => candidate.ZoneLogicalId == zone.LogicalId)
                            .ToArray();
                        var childRacks = ActiveRacks(persistedRacks, racks.Values)
                            .Where(candidate => candidate.ZoneLogicalId == zone.LogicalId)
                            .ToArray();
                        RequireExplicitCascade(
                            payload,
                            childAisles.Length > 0 || childRacks.Length > 0,
                            "Zone",
                            "aisles or racks");
                        beforeJson = JsonSerializer.Serialize(ToSceneDto(zone), JsonOptions);
                        foreach (var childRack in childRacks)
                        {
                            CascadeRackRemoval(
                                childRack,
                                persistedLevels,
                                persistedLocations,
                                racks,
                                levels,
                                locations);
                            RemoveScopedCode(
                                rackCodes,
                                childRack.ZoneLogicalId,
                                childRack.RackCode);
                        }
                        foreach (var childAisle in childAisles)
                        {
                            childAisle.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
                            aisles[childAisle.LogicalId] = childAisle;
                            aisleZones.Remove(childAisle.LogicalId);
                            RemoveScopedCode(
                                aisleCodes,
                                childAisle.ZoneLogicalId,
                                childAisle.AisleCode);
                        }
                        zone.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
                        zones[zone.LogicalId] = zone;
                        persistedZoneIds.Remove(zone.LogicalId);
                        zoneCodes.Remove(zone.ZoneCode);
                        payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                        afterJson = JsonSerializer.Serialize(ToSceneDto(zone), JsonOptions);
                        break;
                    }
                    default:
                        throw new InvalidOperationException(
                            "The validated layout command type is invalid.");
                }

                commandResults.Add(
                    new SpaceLayoutCommandResultDto(
                        command.CommandId,
                        command.Type,
                        command.TargetLogicalId));
                _context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        _execution.TenantId,
                        command.CommandId,
                        batch,
                        index,
                        command.Type,
                        command.TargetLogicalId,
                        payloadJson,
                        beforeJson,
                        afterJson));
            }

            floor.AdvanceRevision(request.ExpectedFloorRevision);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var response = new ApplySpaceLayoutCommandBatchResponse(
                request.CommandBatchId,
                floor.Revision,
                version.ContentRevision,
                commandResults,
                zones.Values.OrderBy(candidate => candidate.ZoneCode)
                    .Select(ToSceneDto).ToArray(),
                aisles.Values.OrderBy(candidate => candidate.AisleCode)
                    .Select(ToSceneDto).ToArray(),
                racks.Values.OrderBy(candidate => candidate.RackCode)
                    .Select(ToSceneDto).ToArray(),
                levels.OrderBy(candidate => candidate.RackLogicalId)
                    .ThenBy(candidate => candidate.LevelNo)
                    .Select(ToSceneDto).ToArray(),
                locations.OrderBy(candidate => candidate.RackLogicalId)
                    .ThenBy(candidate => candidate.LevelNo)
                    .ThenBy(candidate => candidate.ColumnNo)
                    .ThenBy(candidate => candidate.DepthNo)
                    .Select(ToSceneDto).ToArray(),
                IdempotentReplay: false);
            batch.Complete(
                floor.Revision,
                version.ContentRevision,
                JsonSerializer.Serialize(response, JsonOptions));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var concurrentReplay = await ReadLayoutCommandReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The layout command batch conflicted with a concurrent editor write.",
                "reload-floor-scene");
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw Invalid("commands", exception.Message);
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw Invalid(
                "commands",
                "The layout exceeds the supported level or location count.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<PreviewSpaceLocationCodesResponse>
        PreviewLocationCodesAsync(
            Guid versionId,
            Guid floorLogicalId,
            PreviewSpaceLocationCodesRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        EnsureInternalEditor();
        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureReadable(model);
        ValidateCodingPreviewRequest(request);
        return await BuildLocationCodeProposalAsync(
            model,
            versionId,
            floorLogicalId,
            request.Mode,
            request.ScopeZoneLogicalId,
            request.ExpectedFloorRevision,
            request.ExpectedContentRevision,
            cancellationToken);
    }

    public async Task<ApplySpaceLocationCodesResponse>
        ApplyLocationCodesAsync(
            Guid versionId,
            Guid floorLogicalId,
            ApplySpaceLocationCodesRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        EnsureInternalEditor();
        var model = await FindModelByVersionAsync(versionId, cancellationToken);
        EnsureWritable(model);
        await EnsureActiveEditLeaseAsync(
            versionId,
            floorLogicalId,
            request.LeaseId,
            request.ClientInstanceId,
            cancellationToken);
        ValidateCodingApplyRequest(request);
        var requestHash = Hash(
            $"location-coding\n{versionId:D}\n{floorLogicalId:D}\n" +
            JsonSerializer.Serialize(request, JsonOptions));

        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                floorLogicalId,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                floorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);

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
            if (version.Status != SpaceVersionStatus.Draft)
            {
                throw Conflict(
                    SpaceErrorCodes.VersionStateInvalid,
                    "Only a Draft version accepts generated location codes.",
                    "open-or-create-draft");
            }
            if (floor.Revision != request.ExpectedFloorRevision ||
                version.ContentRevision != request.ExpectedContentRevision)
            {
                var replay = await ReadLocationCodingReplayAsync(
                    request.CommandBatchId,
                    requestHash,
                    cancellationToken);
                if (replay is not null &&
                    replay.FloorRevision == floor.Revision &&
                    replay.VersionContentRevision == version.ContentRevision)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return replay;
                }
                throw Conflict(
                    SpaceErrorCodes.CodingProposalStale,
                    "The location coding preview no longer matches the current Draft revision.",
                    "preview-location-codes-again");
            }

            var concurrentReplay = await ReadLocationCodingReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var proposal = await BuildLocationCodeProposalAsync(
                model,
                versionId,
                floorLogicalId,
                request.Mode,
                request.ScopeZoneLogicalId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                cancellationToken);
            if (!string.Equals(
                    proposal.ProposalHash,
                    request.ProposalHash,
                    StringComparison.Ordinal))
            {
                throw Conflict(
                    SpaceErrorCodes.CodingProposalStale,
                    "The location coding rules or Draft inputs changed after preview.",
                    "preview-location-codes-again");
            }

            var changedItems = proposal.Items
                .Where(item => item.Decision == CodingDecisionModify)
                .ToArray();
            if (changedItems.Length == 0)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.CodingRuleInvalid,
                    422,
                    "The coding proposal contains no changes.",
                    recoveryAction: "adjust-coding-scope-or-mode");
            }

            var changedIds = changedItems
                .Select(item => item.LocationLogicalId)
                .ToArray();
            var locations = await _context.LocationRevisions
                .Where(location =>
                    location.ModelVersionId == versionId &&
                    location.FloorLogicalId == floorLogicalId &&
                    changedIds.Contains(location.LogicalId))
                .ToDictionaryAsync(
                    location => location.LogicalId,
                    cancellationToken);
            if (locations.Count != changedItems.Length)
            {
                throw Conflict(
                    SpaceErrorCodes.CodingProposalStale,
                    "A location from the coding preview is no longer available.",
                    "preview-location-codes-again");
            }

            var batch = SpaceElementCommandBatch.Create(
                _execution.TenantId,
                request.CommandBatchId,
                versionId,
                floorLogicalId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                expectedContentHash: null,
                proposal.ProposalHash,
                requestHash,
                _execution.ActorId,
                RequireUtcNow());
            _context.ElementCommandBatches.Add(batch);

            var beforeByLocation = changedItems.ToDictionary(
                item => item.LocationLogicalId,
                item => JsonSerializer.Serialize(
                    ToSceneDto(locations[item.LocationLogicalId]),
                    JsonOptions));
            if (request.Mode == SpaceDesignCodingContract.Rebuild)
            {
                foreach (var item in changedItems)
                    locations[item.LocationLogicalId].ClearGeneratedLocationCode();
                await _context.SaveChangesAsync(cancellationToken);
            }

            for (var index = 0; index < changedItems.Length; index++)
            {
                var item = changedItems[index];
                var location = locations[item.LocationLogicalId];
                location.ApplyGeneratedLocationCode(item.ProposedCode!);
                var afterJson = JsonSerializer.Serialize(
                    ToSceneDto(location),
                    JsonOptions);
                _context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        _execution.TenantId,
                        OperationId(Hash(
                            $"location-coding-command\n{request.CommandBatchId:D}\n" +
                            $"{item.LocationLogicalId:D}")),
                        batch,
                        index,
                        "ApplyGeneratedLocationCode",
                        item.LocationLogicalId,
                        JsonSerializer.Serialize(item, JsonOptions),
                        beforeByLocation[item.LocationLogicalId],
                        afterJson));
            }

            floor.AdvanceRevision(request.ExpectedFloorRevision);
            version.TouchContent();
            await _context.SaveChangesAsync(cancellationToken);

            var response = new ApplySpaceLocationCodesResponse(
                request.CommandBatchId,
                floor.Revision,
                version.ContentRevision,
                proposal.ProposalHash,
                changedItems.Length,
                changedItems,
                IdempotentReplay: false);
            batch.Complete(
                floor.Revision,
                version.ContentRevision,
                JsonSerializer.Serialize(response, JsonOptions));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            var replay = await ReadLocationCodingReplayAsync(
                request.CommandBatchId,
                requestHash,
                cancellationToken);
            if (replay is not null)
                return replay;
            throw Conflict(
                SpaceErrorCodes.CodingConflict,
                "The coding proposal conflicted with another Draft write.",
                "preview-location-codes-again");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<SpacePage<SpaceAssetDto>> GetAssetsAsync(
        string? scope,
        string? category,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        limit = NormalizeLimit(limit);
        var parsedScope = ParseOptionalEnum<SpaceAssetScope>(
            scope,
            nameof(scope));
        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? null
            : RequireText(category, 100, nameof(category));
        var filterHash = Hash(
            $"scope={Normalize(scope)}\ncategory={Normalize(normalizedCategory)}" +
            $"\nlimit={limit}");
        var offset = ReadOffset(cursor, "assets", filterHash);

        var query = _context.Assets.AsNoTracking();
        if (parsedScope.HasValue)
            query = query.Where(asset => asset.Scope == parsedScope.Value);
        if (normalizedCategory is not null)
            query = query.Where(asset => asset.Category == normalizedCategory);

        var assets = await query
            .OrderBy(asset => asset.Scope)
            .ThenBy(asset => asset.AssetCode)
            .ThenBy(asset => asset.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var assetIds = assets.Select(asset => asset.Id).ToArray();
        var versions = assetIds.Length == 0
            ? []
            : await _context.AssetVersions
                .AsNoTracking()
                .Where(version =>
                    assetIds.Contains(version.AssetId) &&
                    version.Status == SpaceAssetVersionStatus.Ready)
                .OrderByDescending(version => version.VersionNo)
                .ThenBy(version => version.Id)
                .ToArrayAsync(cancellationToken);
        var latestByAsset = versions
            .GroupBy(version => version.AssetId)
            .ToDictionary(group => group.Key, group => group.First());

        return Page(
            assets,
            limit,
            offset,
            "assets",
            filterHash,
            asset => ToDto(
                asset,
                latestByAsset.TryGetValue(asset.Id, out var latest)
                    ? latest
                    : throw new InvalidOperationException(
                        "A visible asset is missing its ready immutable version.")));
    }

    public async Task<CreateSpaceAssetResponse> CreateAssetAsync(
        CreateSpaceAssetRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (!string.Equals(
                request.Scope?.Trim(),
                SpaceAssetScope.Tenant.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.AssetScopeDenied,
                403,
                "System asset writes are not available through the tenant API.",
                "Only scope=Tenant can be created through this endpoint.",
                "use-tenant-scope");
        }

        var assetCode = RequireText(request.AssetCode, 100, "assetCode");
        var name = RequireText(request.Name, 200, "name");
        var category = RequireText(request.Category, 100, "category");
        var format = ParseRequiredEnum<SpaceAssetFormat>(
            request.Format,
            "format");
        var now = RequireUtcNow();
        SpaceAsset asset;
        SpaceAssetVersion version;
        try
        {
            asset = SpaceAsset.CreateTenant(
                _execution.TenantId,
                assetCode,
                name,
                category,
                request.Description,
                _execution.ActorId,
                now);
            version = SpaceAssetVersion.CreateReady(
                asset,
                1,
                format,
                request.ParameterSchemaJson,
                request.PreviewRef,
                request.RenderArtifactRef,
                request.ContentHash,
                _execution.ActorId,
                now);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("asset", exception.Message);
        }

        const string operation = "create-asset";
        var normalizedRequest = new CreateSpaceAssetRequest(
            asset.AssetCode,
            asset.Name,
            asset.Category,
            version.Format.ToString(),
            version.ParameterSchemaJson,
            version.ContentHash,
            asset.Description,
            version.PreviewRef,
            version.RenderArtifactRef,
            SpaceAssetScope.Tenant.ToString());
        var requestHash = Hash(
            JsonSerializer.Serialize(normalizedRequest, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadAssetReplayAsync(
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
            var concurrentReplay = await ReadAssetReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            _context.Assets.Add(asset);
            _context.AssetVersions.Add(version);
            await _context.SaveChangesAsync(cancellationToken);

            var response = new CreateSpaceAssetResponse(
                ToDto(asset, version),
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
            var concurrentReplay = await ReadAssetReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                SpaceErrorCodes.AssetConflict,
                "An asset with this code already exists or changed concurrently.",
                "choose-another-asset-code");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<IReadOnlyList<SpaceWarehouseTemplateDto>>
        GetWarehouseTemplatesAsync(
            string? scope,
            CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        EnsureInternalEditor();
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedScope = scope?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedScope) &&
            !string.Equals(
                normalizedScope,
                "System",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                normalizedScope,
                "Tenant",
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "scope",
                "Supported warehouse template scopes are System and Tenant.");
        }

        IReadOnlyList<SpaceWarehouseTemplateDto> result =
            SpaceBuiltInWarehouseTemplates.List()
                .Where(template =>
                    string.IsNullOrWhiteSpace(normalizedScope) ||
                    string.Equals(
                        template.Scope,
                        normalizedScope,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        return Task.FromResult(result);
    }

    public Task<SpaceWarehouseTemplateInstantiationPreviewDto>
        PreviewWarehouseTemplateAsync(
            Guid templateId,
            PreviewSpaceWarehouseTemplateRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        EnsureInternalEditor();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (templateId == Guid.Empty || request.TemplateVersionId == Guid.Empty)
        {
            throw Invalid(
                "templateVersionId",
                "Template and template version identities are required.");
        }

        var template = SpaceBuiltInWarehouseTemplates.List()
            .SingleOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WarehouseTemplateNotFound,
                404,
                "Warehouse template not found.",
                recoveryAction: "reload-template-catalog");
        }
        if (template.LatestVersion.Id != request.TemplateVersionId)
        {
            throw Conflict(
                SpaceErrorCodes.WarehouseTemplateVersionConflict,
                "The requested warehouse template version is not current.",
                "reload-template-catalog");
        }
        if (!SpaceBuiltInWarehouseTemplates.TryPreview(
                templateId,
                request.TemplateVersionId,
                out var preview) ||
            preview is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WarehouseTemplateNotFound,
                404,
                "Warehouse template preview not found.",
                recoveryAction: "reload-template-catalog");
        }
        return Task.FromResult(preview);
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
        var createMode = request.CreateMode?.Trim();
        var isBlank = string.Equals(
            createMode,
            BlankVersionMode,
            StringComparison.Ordinal);
        var isPublishedClone = string.Equals(
            createMode,
            PublishedVersionMode,
            StringComparison.Ordinal);
        if (!isBlank && !isPublishedClone)
        {
            throw Invalid(
                "createMode",
                "Supported values are Blank and PublishedVersion.");
        }
        if (isBlank && request.BasedOnVersionId.HasValue)
        {
            throw Invalid(
                "basedOnVersionId",
                "Blank drafts cannot specify a base version.");
        }
        if (isPublishedClone && !model.CurrentPublishedVersionId.HasValue)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "A current Published version is required.",
                "publish-or-bootstrap-version");
        }
        if (isPublishedClone &&
            request.BasedOnVersionId.HasValue &&
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
            isBlank ? null : model.CurrentPublishedVersionId,
            isBlank ? BlankVersionMode : PublishedVersionMode);
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

        Guid startedVersionId;
        Guid startedJobId;
        bool startedReused;
        try
        {
            if (isBlank)
            {
                var started = await _clone.StartBlankAsync(
                    new SpaceBlankVersionRequest(
                        model.Id,
                        name,
                        OperationId(keyHash),
                        requestHash),
                    cancellationToken);
                startedVersionId = started.ModelVersionId;
                startedJobId = started.JobId;
                startedReused = started.Reused;
            }
            else
            {
                var started = await _clone.StartAsync(
                    new SpaceVersionCloneRequest(
                        model.Id,
                        name,
                        OperationId(keyHash)),
                    cancellationToken);
                startedVersionId = started.ModelVersionId;
                startedJobId = started.JobId;
                startedReused = started.Reused;
            }
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
                candidate => candidate.Id == startedVersionId,
                cancellationToken);
        var response = new CreateSpaceVersionResponse(
            version.Id,
            siteId,
            FormatVersionNo(version.VersionNo),
            version.Status.ToString(),
            RowVersion(version.RowVersion),
            startedJobId,
            $"/api/space/design/v1/jobs/{startedJobId:D}",
            startedReused);

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
            var file = await SpaceFileReferenceLock.LoadAsync(
                           _context,
                           _context.CurrentTenantId,
                           request.FileId,
                           includeDeleted: false,
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

    private static void ValidateElementCommandBatch(
        ApplySpaceElementCommandBatchRequest request)
    {
        if (request.SchemaVersion != SpaceElementCommandContract.SchemaVersion)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CommandSchemaUnsupported,
                422,
                "The command schema is not supported.",
                $"schemaVersion must be {SpaceElementCommandContract.SchemaVersion}.",
                "upgrade-client");
        }
        if (request.CommandBatchId == Guid.Empty)
            throw Invalid("commandBatchId", "A non-empty identity is required.");
        if (request.ClientInstanceId == Guid.Empty)
            throw Invalid("clientInstanceId", "A non-empty identity is required.");
        if (request.LeaseId == Guid.Empty)
            throw Invalid("leaseId", "A non-empty identity is required.");
        if (request.ExpectedFloorRevision < 0)
        {
            throw Invalid(
                "expectedFloorRevision",
                "A non-negative revision is required.");
        }
        if (request.ExpectedContentRevision < 0 ||
            !request.ExpectedContentRevision.HasValue &&
                request.ExpectedContentHash is not null ||
            request.ExpectedContentHash is not null &&
                !IsSha256(request.ExpectedContentHash) ||
            request.ChangesetSha256 is not null &&
                !IsSha256(request.ChangesetSha256))
        {
            throw Invalid(
                "expectedContentRevision",
                "A complete CAD changeset content fence is required.");
        }
        if (request.Commands is null ||
            request.Commands.Count is < 1 or > 100)
        {
            throw Invalid(
                "commands",
                "A command batch must contain between 1 and 100 commands.");
        }
        if (request.Commands.Any(command => command is null))
            throw Invalid("commands", "Command entries cannot be null.");
        if (request.Commands.Any(command =>
                command.CommandId == Guid.Empty ||
                command.TargetLogicalId == Guid.Empty))
        {
            throw Invalid(
                "commands",
                "Every command and target must have a non-empty identity.");
        }
        if (request.Commands.Select(command => command.CommandId).Distinct().Count()
            != request.Commands.Count)
        {
            throw Invalid("commands", "Command identities must be unique.");
        }
        if (request.Commands
                .Select(command => command.TargetLogicalId)
                .Distinct()
                .Count() != request.Commands.Count)
        {
            throw Invalid(
                "commands",
                "A target can appear only once in an editor command batch.");
        }

        foreach (var command in request.Commands)
        {
            var payloadCount =
                (command.UpdateProperties is null ? 0 : 1) +
                (command.MoveObject is null ? 0 : 1) +
                (command.RotateObject is null ? 0 : 1) +
                (command.GenerateRackArray is null ? 0 : 1) +
                (command.CreateElement is null ? 0 : 1);
            switch (command.Type)
            {
                case SpaceElementCommandContract.UpdateProperties
                    when command.UpdateProperties is not null &&
                         payloadCount == 1:
                    ValidateOptionalElementType(
                        command.UpdateProperties.ElementType);
                    if (command.UpdateProperties.Attributes is null ||
                        command.UpdateProperties.Attributes.Count > 100)
                    {
                        throw Invalid(
                            "commands.updateProperties.attributes",
                            "At most 100 attributes are allowed.");
                    }
                    break;
                case SpaceElementCommandContract.MoveObject
                    when command.MoveObject is not null &&
                         payloadCount == 1:
                    break;
                case SpaceElementCommandContract.RotateObject
                    when command.RotateObject is not null &&
                         payloadCount == 1:
                    break;
                case SpaceElementCommandContract.GenerateRackArray
                    when command.GenerateRackArray is not null &&
                         payloadCount == 1:
                    ValidateRackArray(command.GenerateRackArray);
                    break;
                case SpaceElementCommandContract.CreateElement
                    when command.CreateElement is not null && payloadCount == 1:
                    ValidateCreateElement(command.CreateElement);
                    break;
                case SpaceElementCommandContract.DeleteObject
                    when payloadCount == 0:
                    break;
                case SpaceElementCommandContract.RestoreLogicalObject
                    when payloadCount == 0:
                    break;
                case SpaceElementCommandContract.UpdateProperties:
                    throw Invalid(
                        "commands.updateProperties",
                        "UpdateProperties requires only its strongly typed payload.");
                case SpaceElementCommandContract.MoveObject:
                    throw Invalid(
                        "commands.moveObject",
                        "MoveObject requires only its strongly typed payload.");
                case SpaceElementCommandContract.RotateObject:
                    throw Invalid(
                        "commands.rotateObject",
                        "RotateObject requires only its strongly typed payload.");
                case SpaceElementCommandContract.GenerateRackArray:
                    throw Invalid(
                        "commands.generateRackArray",
                        "GenerateRackArray requires only its strongly typed payload.");
                case SpaceElementCommandContract.CreateElement:
                    throw Invalid(
                        "commands.createElement",
                        "CreateElement requires only its strongly typed payload.");
                case SpaceElementCommandContract.DeleteObject:
                case SpaceElementCommandContract.RestoreLogicalObject:
                    throw Invalid(
                        "commands",
                        $"{command.Type} must not contain a payload.");
                default:
                    throw new SpaceProblemException(
                        SpaceErrorCodes.CommandSchemaUnsupported,
                        422,
                        "The command type is not supported.",
                        $"Unsupported command type '{command.Type}'.",
                        "upgrade-client");
            }
        }
    }

    private static void ValidateLayoutCommandBatch(
        ApplySpaceLayoutCommandBatchRequest request)
    {
        if (request.SchemaVersion != SpaceLayoutCommandContract.SchemaVersion)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CommandSchemaUnsupported,
                422,
                "The layout command schema is not supported.",
                $"schemaVersion must be {SpaceLayoutCommandContract.SchemaVersion}.",
                "upgrade-client");
        }
        if (request.CommandBatchId == Guid.Empty)
            throw Invalid("commandBatchId", "A non-empty identity is required.");
        if (request.ClientInstanceId == Guid.Empty)
            throw Invalid("clientInstanceId", "A non-empty identity is required.");
        if (request.LeaseId == Guid.Empty)
            throw Invalid("leaseId", "A non-empty identity is required.");
        if (request.ExpectedFloorRevision < 0)
        {
            throw Invalid(
                "expectedFloorRevision",
                "A non-negative revision is required.");
        }
        if (request.ExpectedContentRevision < 0)
        {
            throw Invalid(
                "expectedContentRevision",
                "A non-negative revision is required.");
        }
        if (request.Commands is null || request.Commands.Count is < 1 or > 100)
        {
            throw Invalid(
                "commands",
                "A layout command batch must contain between 1 and 100 commands.");
        }
        if (request.Commands.Any(command => command is null))
            throw Invalid("commands", "Command entries cannot be null.");
        if (request.Commands.Any(command =>
                command.CommandId == Guid.Empty ||
                command.TargetLogicalId == Guid.Empty))
        {
            throw Invalid(
                "commands",
                "Every command and target must have a non-empty identity.");
        }
        if (request.Commands.Select(command => command.CommandId).Distinct().Count()
            != request.Commands.Count ||
            request.Commands.Select(command => command.TargetLogicalId).Distinct().Count()
            != request.Commands.Count)
        {
            throw Invalid(
                "commands",
                "Command and target identities must be unique in a batch.");
        }

        long generatedLocationCount = 0;
        var createdZoneIds = new HashSet<Guid>();
        var createdAisles = new Dictionary<Guid, Guid>();
        foreach (var command in request.Commands)
        {
            var payloadCount =
                (command.CreateZone is null ? 0 : 1) +
                (command.CreateAisle is null ? 0 : 1) +
                (command.CreateRack is null ? 0 : 1) +
                (command.UpdateZone is null ? 0 : 1) +
                (command.UpdateAisle is null ? 0 : 1) +
                (command.UpdateRack is null ? 0 : 1) +
                (command.DeleteObject is null ? 0 : 1);
            switch (command.Type)
            {
                case SpaceLayoutCommandContract.CreateZone
                    when command.CreateZone is not null && payloadCount == 1:
                    createdZoneIds.Add(command.TargetLogicalId);
                    break;
                case SpaceLayoutCommandContract.CreateAisle
                    when command.CreateAisle is not null && payloadCount == 1:
                    if (command.CreateAisle.ZoneLogicalId == Guid.Empty)
                    {
                        throw Invalid(
                            "commands.createAisle.zoneLogicalId",
                            "A non-empty zone identity is required.");
                    }
                    createdAisles.Add(
                        command.TargetLogicalId,
                        command.CreateAisle.ZoneLogicalId);
                    break;
                case SpaceLayoutCommandContract.CreateRack
                    when command.CreateRack is not null && payloadCount == 1:
                    ValidateCreateLayoutRack(command.CreateRack);
                    foreach (var level in command.CreateRack.Levels)
                    {
                        generatedLocationCount = checked(
                            generatedLocationCount +
                            (long)level.BinCount * level.DepthCount);
                    }
                    break;
                case SpaceLayoutCommandContract.UpdateZone
                    when command.UpdateZone is not null && payloadCount == 1:
                    break;
                case SpaceLayoutCommandContract.UpdateAisle
                    when command.UpdateAisle is not null && payloadCount == 1:
                    if (command.UpdateAisle.ZoneLogicalId == Guid.Empty)
                    {
                        throw Invalid(
                            "commands.updateAisle.zoneLogicalId",
                            "A non-empty zone identity is required.");
                    }
                    break;
                case SpaceLayoutCommandContract.UpdateRack
                    when command.UpdateRack is not null && payloadCount == 1:
                    ValidateUpdateLayoutRack(command.UpdateRack);
                    foreach (var level in command.UpdateRack.Levels)
                    {
                        generatedLocationCount = checked(
                            generatedLocationCount +
                            (long)level.BinCount * level.DepthCount);
                    }
                    break;
                case SpaceLayoutCommandContract.DeleteZone:
                case SpaceLayoutCommandContract.DeleteAisle:
                case SpaceLayoutCommandContract.DeleteRack:
                    if (command.DeleteObject is null || payloadCount != 1)
                    {
                        throw Invalid(
                            "commands.deleteObject",
                            $"{command.Type} requires only its strongly typed payload.");
                    }
                    break;
                case SpaceLayoutCommandContract.CreateZone:
                case SpaceLayoutCommandContract.CreateAisle:
                case SpaceLayoutCommandContract.CreateRack:
                case SpaceLayoutCommandContract.UpdateZone:
                case SpaceLayoutCommandContract.UpdateAisle:
                case SpaceLayoutCommandContract.UpdateRack:
                    throw Invalid(
                        "commands",
                        $"{command.Type} requires only its strongly typed payload.");
                default:
                    throw new SpaceProblemException(
                        SpaceErrorCodes.CommandSchemaUnsupported,
                        422,
                        "The layout command type is not supported.",
                        $"Unsupported command type '{command.Type}'.",
                        "upgrade-client");
            }
        }
        if (generatedLocationCount > 5_000)
        {
            throw Invalid(
                "commands.layoutRack.levels",
                "A layout batch can generate at most 5,000 locations.");
        }

        var seenZones = new HashSet<Guid>();
        var seenAisles = new HashSet<Guid>();
        foreach (var command in request.Commands)
        {
            if (command.Type == SpaceLayoutCommandContract.CreateZone)
                seenZones.Add(command.TargetLogicalId);
            if (command.Type == SpaceLayoutCommandContract.CreateAisle)
            {
                if (createdZoneIds.Contains(command.CreateAisle!.ZoneLogicalId) &&
                    !seenZones.Contains(command.CreateAisle.ZoneLogicalId))
                {
                    throw Invalid(
                        "commands",
                        "A CreateAisle command must follow the CreateZone command it references.");
                }
                seenAisles.Add(command.TargetLogicalId);
            }
            if (command.Type != SpaceLayoutCommandContract.CreateRack)
                continue;
            var rack = command.CreateRack!;
            if (createdZoneIds.Contains(rack.ZoneLogicalId) &&
                !seenZones.Contains(rack.ZoneLogicalId))
            {
                throw Invalid(
                    "commands",
                    "A CreateRack command must follow the CreateZone command it references.");
            }
            if (rack.AisleLogicalId.HasValue &&
                createdAisles.ContainsKey(rack.AisleLogicalId.Value) &&
                !seenAisles.Contains(rack.AisleLogicalId.Value))
            {
                throw Invalid(
                    "commands",
                    "A CreateRack command must follow the CreateAisle command it references.");
            }
        }
    }

    private static void ValidateCreateLayoutRack(
        SpaceCreateLayoutRackDto payload)
    {
        if (payload.ZoneLogicalId == Guid.Empty ||
            payload.AisleLogicalId == Guid.Empty ||
            payload.TemplateVersionId == Guid.Empty)
        {
            throw Invalid(
                "commands.createRack",
                "Zone, aisle and template identities cannot be empty.");
        }
        if (payload.Levels is null || payload.Levels.Count is < 1 or > 50)
        {
            throw Invalid(
                "commands.createRack.levels",
                "A rack must contain between 1 and 50 level specifications.");
        }
        if (payload.Levels.Any(level => level is null))
        {
            throw Invalid(
                "commands.createRack.levels",
                "Rack level entries cannot be null.");
        }
        if (payload.Levels.Select(level => level.LevelNo).Distinct().Count() !=
            payload.Levels.Count)
        {
            throw Invalid(
                "commands.createRack.levels",
                "Rack level numbers must be unique.");
        }
        foreach (var level in payload.Levels)
        {
            if (level.LevelNo <= 0 ||
                level.BottomZ < 0 ||
                level.ClearHeight <= 0 ||
                level.BinCount is < 1 or > 500 ||
                level.DepthCount is < 1 or > 20 ||
                level.CellWidth <= 0 ||
                level.CellDepth <= 0 ||
                level.BeamHeight < 0 ||
                level.MaxLoad < 0 ||
                level.LocationCodePrefix?.Trim().Length > 150)
            {
                throw Invalid(
                    "commands.createRack.levels",
                    "Rack level dimensions, counts or load are outside the supported range.");
            }
        }
        ValidateRackEnvelope(
            payload.Width,
            payload.Depth,
            payload.Height,
            payload.RotationZ,
            payload.Levels.Select(level => new RackEnvelopeLevel(
                level.LevelNo,
                level.BottomZ,
                level.ClearHeight,
                level.BinCount,
                level.DepthCount,
                level.CellWidth,
                level.CellDepth,
                level.BeamHeight)),
            "commands.createRack");
    }

    private static void ValidateUpdateLayoutRack(
        SpaceUpdateLayoutRackDto payload)
    {
        if (payload.ZoneLogicalId == Guid.Empty ||
            payload.AisleLogicalId == Guid.Empty ||
            payload.TemplateVersionId == Guid.Empty)
        {
            throw Invalid(
                "commands.updateRack",
                "Zone, aisle and template identities cannot be empty.");
        }
        if (payload.Levels is null || payload.Levels.Count is < 1 or > 50)
        {
            throw Invalid(
                "commands.updateRack.levels",
                "A rack must contain between 1 and 50 level specifications.");
        }
        if (payload.Levels.Any(level => level is null) ||
            payload.Levels.Select(level => level.LevelNo).Distinct().Count() !=
            payload.Levels.Count)
        {
            throw Invalid(
                "commands.updateRack.levels",
                "Rack level entries and level numbers must be non-null and unique.");
        }
        foreach (var level in payload.Levels)
        {
            if (level.LevelNo <= 0 ||
                level.BottomZ < 0 ||
                level.ClearHeight <= 0 ||
                level.BinCount is < 1 or > 500 ||
                level.DepthCount is < 1 or > 20 ||
                level.CellWidth <= 0 ||
                level.CellDepth <= 0 ||
                level.BeamHeight < 0 ||
                level.MaxLoad < 0)
            {
                throw Invalid(
                    "commands.updateRack.levels",
                    "Rack level dimensions, counts or load are outside the supported range.");
            }
        }
        ValidateRackEnvelope(
            payload.Width,
            payload.Depth,
            payload.Height,
            payload.RotationZ,
            payload.Levels.Select(level => new RackEnvelopeLevel(
                level.LevelNo,
                level.BottomZ,
                level.ClearHeight,
                level.BinCount,
                level.DepthCount,
                level.CellWidth,
                level.CellDepth,
                level.BeamHeight)),
            "commands.updateRack");
    }

    private static void ValidateRackEnvelope(
        int width,
        int depth,
        int height,
        decimal rotationZ,
        IEnumerable<RackEnvelopeLevel> sourceLevels,
        string fieldPath)
    {
        if (width <= 0 || depth <= 0 || height <= 0 ||
            rotationZ is < 0 or >= 360)
        {
            throw Invalid(
                fieldPath,
                "Rack dimensions must be positive and rotation must be in [0, 360).");
        }
        var previousTop = 0L;
        foreach (var level in sourceLevels.OrderBy(candidate => candidate.BottomZ))
        {
            var levelTop = checked(
                (long)level.BottomZ + level.BeamHeight + level.ClearHeight);
            if ((long)level.BinCount * level.CellWidth > width ||
                (long)level.DepthCount * level.CellDepth > depth ||
                levelTop > height ||
                level.BottomZ < previousTop)
            {
                throw Invalid(
                    $"{fieldPath}.levels",
                    $"Level {level.LevelNo} does not fit the rack envelope or overlaps another level.");
            }
            previousTop = levelTop;
        }
    }

    private sealed record RackEnvelopeLevel(
        int LevelNo,
        int BottomZ,
        int ClearHeight,
        int BinCount,
        int DepthCount,
        int CellWidth,
        int CellDepth,
        int BeamHeight);

    private static List<Guid> ExpandCreatedLayoutLogicalIds(
        IReadOnlyList<SpaceLayoutCommandDto> commands)
    {
        var result = commands
            .Where(command => command.Type is
                SpaceLayoutCommandContract.CreateZone or
                SpaceLayoutCommandContract.CreateAisle or
                SpaceLayoutCommandContract.CreateRack)
            .Select(command => command.TargetLogicalId)
            .ToList();
        foreach (var command in commands.Where(command =>
                     command.Type == SpaceLayoutCommandContract.CreateRack))
        {
            foreach (var level in command.CreateRack!.Levels)
            {
                result.Add(
                    WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                        command.TargetLogicalId,
                        level.LevelNo));
                for (var columnNo = 1; columnNo <= level.BinCount; columnNo++)
                for (var depthNo = 1; depthNo <= level.DepthCount; depthNo++)
                {
                    result.Add(
                        WarehouseDeterministicIdentity.CreateLocationLogicalId(
                            command.TargetLogicalId,
                            level.LevelNo,
                            columnNo,
                            depthNo));
                }
            }
        }
        return result;
    }

    private static bool AddScopedCode(
        HashSet<(Guid ScopeLogicalId, string Code)> codes,
        Guid scopeLogicalId,
        string code)
    {
        var normalized = code?.Trim() ?? string.Empty;
        if (codes.Any(candidate =>
                candidate.ScopeLogicalId == scopeLogicalId &&
                string.Equals(
                    candidate.Code,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        codes.Add((scopeLogicalId, normalized));
        return true;
    }

    private static void RemoveScopedCode(
        HashSet<(Guid ScopeLogicalId, string Code)> codes,
        Guid scopeLogicalId,
        string code)
    {
        var match = codes.FirstOrDefault(candidate =>
            candidate.ScopeLogicalId == scopeLogicalId &&
            string.Equals(
                candidate.Code,
                code?.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (match != default)
            codes.Remove(match);
    }

    private static TRevision FindActiveLayoutTarget<TRevision>(
        IReadOnlyDictionary<Guid, TRevision> affected,
        IEnumerable<TRevision> persisted,
        Guid logicalId,
        string resourceName)
        where TRevision : SpaceRevisionEntity
    {
        var target = affected.GetValueOrDefault(logicalId) ??
                     persisted.SingleOrDefault(candidate =>
                         candidate.LogicalId == logicalId);
        if (target is null)
            throw NotFound(SpaceErrorCodes.LogicalIdNotFound, resourceName);
        if (target.LifecycleState != SpaceLifecycleState.Active)
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The layout command requires an active target.",
                "reload-floor-scene");
        }
        return target;
    }

    private static IEnumerable<SpaceAisleRevision> ActiveAisles(
        IEnumerable<SpaceAisleRevision> persisted,
        IEnumerable<SpaceAisleRevision> affected) =>
        persisted.Concat(affected)
            .GroupBy(candidate => candidate.LogicalId)
            .Select(group => group.Last())
            .Where(candidate =>
                candidate.LifecycleState == SpaceLifecycleState.Active);

    private static IEnumerable<SpaceRackRevision> ActiveRacks(
        IEnumerable<SpaceRackRevision> persisted,
        IEnumerable<SpaceRackRevision> affected) =>
        persisted.Concat(affected)
            .GroupBy(candidate => candidate.LogicalId)
            .Select(group => group.Last())
            .Where(candidate =>
                candidate.LifecycleState == SpaceLifecycleState.Active);

    private static IEnumerable<SpaceRackLevelRevision> RackLevelsFor(
        Guid rackLogicalId,
        IEnumerable<SpaceRackLevelRevision> persisted,
        IEnumerable<SpaceRackLevelRevision> affected) =>
        persisted.Concat(affected)
            .Where(candidate => candidate.RackLogicalId == rackLogicalId)
            .GroupBy(candidate => candidate.LogicalId)
            .Select(group => group.Last());

    private static IEnumerable<SpaceLocationRevision> LocationsFor(
        Guid rackLogicalId,
        IEnumerable<SpaceLocationRevision> persisted,
        IEnumerable<SpaceLocationRevision> affected) =>
        persisted.Concat(affected)
            .Where(candidate => candidate.RackLogicalId == rackLogicalId)
            .GroupBy(candidate => candidate.LogicalId)
            .Select(group => group.Last());

    private void ReconcileRackLayout(
        Guid versionId,
        Guid floorLogicalId,
        SpaceRackRevision rack,
        IReadOnlyList<SpaceUpdateLayoutRackLevelDto> specifications,
        List<SpaceRackLevelRevision> persistedLevels,
        List<SpaceLocationRevision> persistedLocations,
        List<SpaceRackLevelRevision> affectedLevels,
        List<SpaceLocationRevision> affectedLocations)
    {
        var rackLevels = RackLevelsFor(
                rack.LogicalId,
                persistedLevels,
                affectedLevels)
            .ToDictionary(candidate => candidate.LevelNo);
        var rackLocations = LocationsFor(
                rack.LogicalId,
                persistedLocations,
                affectedLocations)
            .ToDictionary(
                candidate => (candidate.LevelNo, candidate.ColumnNo, candidate.DepthNo));
        var desiredLevels = specifications
            .Select(candidate => candidate.LevelNo)
            .ToHashSet();

        foreach (var level in rackLevels.Values.Where(candidate =>
                     candidate.LifecycleState == SpaceLifecycleState.Active &&
                     !desiredLevels.Contains(candidate.LevelNo)))
        {
            level.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
            AddAffected(affectedLevels, [level]);
            foreach (var location in rackLocations.Values.Where(candidate =>
                         candidate.LevelNo == level.LevelNo &&
                         candidate.LifecycleState == SpaceLifecycleState.Active))
            {
                location.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
                AddAffected(affectedLocations, [location]);
            }
        }

        foreach (var specification in specifications.OrderBy(candidate => candidate.LevelNo))
        {
            if (!rackLevels.TryGetValue(specification.LevelNo, out var level))
            {
                level = SpaceRackLevelRevision.Create(
                    _execution.TenantId,
                    versionId,
                    WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                        rack.LogicalId,
                        specification.LevelNo),
                    rack.LogicalId,
                    specification.LevelNo,
                    specification.BottomZ,
                    specification.ClearHeight,
                    specification.BinCount,
                    specification.DepthCount,
                    specification.CellWidth,
                    specification.CellDepth,
                    specification.MaxLoad,
                    specification.BeamHeight);
                persistedLevels.Add(level);
                _context.RackLevelRevisions.Add(level);
            }
            else
            {
                level.UpdateSpecification(
                    specification.LevelNo,
                    specification.BottomZ,
                    specification.ClearHeight,
                    specification.BinCount,
                    specification.DepthCount,
                    specification.CellWidth,
                    specification.CellDepth,
                    specification.MaxLoad,
                    specification.BeamHeight);
                level.Restore();
            }
            AddAffected(affectedLevels, [level]);

            for (var columnNo = 1; columnNo <= specification.BinCount; columnNo++)
            for (var depthNo = 1; depthNo <= specification.DepthCount; depthNo++)
            {
                if (!rackLocations.TryGetValue(
                        (specification.LevelNo, columnNo, depthNo),
                        out var location))
                {
                    location = SpaceLocationRevision.Create(
                        _execution.TenantId,
                        versionId,
                        WarehouseDeterministicIdentity.CreateLocationLogicalId(
                            rack.LogicalId,
                            specification.LevelNo,
                            columnNo,
                            depthNo),
                        floorLogicalId,
                        rack.LogicalId,
                        locationCode: null,
                        columnNo,
                        specification.LevelNo,
                        depthNo,
                        specification.CellWidth,
                        specification.ClearHeight,
                        specification.CellDepth,
                        specification.MaxLoad,
                        SpaceLocationCodeOrigin.Generated);
                    persistedLocations.Add(location);
                    _context.LocationRevisions.Add(location);
                }
                else
                {
                    location.UpdateGeneratedSpecification(
                        floorLogicalId,
                        rack.LogicalId,
                        columnNo,
                        specification.LevelNo,
                        depthNo,
                        specification.CellWidth,
                        specification.ClearHeight,
                        specification.CellDepth,
                        specification.MaxLoad);
                }
                AddAffected(affectedLocations, [location]);
            }

            foreach (var location in rackLocations.Values.Where(candidate =>
                         candidate.LevelNo == specification.LevelNo &&
                         candidate.LifecycleState == SpaceLifecycleState.Active &&
                         (candidate.ColumnNo > specification.BinCount ||
                          candidate.DepthNo > specification.DepthCount)))
            {
                location.ChangeLifecycle(SpaceLifecycleState.RemoveRequested);
                AddAffected(affectedLocations, [location]);
            }
        }
    }

    private static void RequireExplicitCascade(
        SpaceDeleteLayoutObjectDto payload,
        bool hasActiveChildren,
        string parentKind,
        string childDescription)
    {
        if (hasActiveChildren && !payload.Cascade)
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                $"{parentKind} has active {childDescription}; explicit cascade is required.",
                "confirm-layout-cascade");
        }
    }

    private static void CascadeRackRemoval(
        SpaceRackRevision rack,
        List<SpaceRackLevelRevision> persistedLevels,
        List<SpaceLocationRevision> persistedLocations,
        IDictionary<Guid, SpaceRackRevision> affectedRacks,
        List<SpaceRackLevelRevision> affectedLevels,
        List<SpaceLocationRevision> affectedLocations)
    {
        var rackLevels = RackLevelsFor(
            rack.LogicalId,
            persistedLevels,
            affectedLevels).ToArray();
        var rackLocations = LocationsFor(
            rack.LogicalId,
            persistedLocations,
            affectedLocations).ToArray();
        ChangeRackLifecycle(
            rack,
            rackLevels,
            rackLocations,
            SpaceLifecycleState.RemoveRequested);
        affectedRacks[rack.LogicalId] = rack;
        AddAffected(affectedLevels, rackLevels);
        AddAffected(affectedLocations, rackLocations);
    }

    private static void AddAffected<TRevision>(
        List<TRevision> affected,
        IEnumerable<TRevision> candidates)
        where TRevision : SpaceRevisionEntity
    {
        foreach (var candidate in candidates)
        {
            if (affected.All(existing => existing.LogicalId != candidate.LogicalId))
                affected.Add(candidate);
        }
    }

    private static string? CreateManualLocationCode(
        string? prefix,
        int levelNo,
        int columnNo,
        int depthNo) =>
        string.IsNullOrWhiteSpace(prefix)
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix.Trim()}-L{levelNo:00}-C{columnNo:000}-D{depthNo:00}");

    private async Task<ApplySpaceLayoutCommandBatchResponse?>
        ReadLayoutCommandReplayAsync(
            Guid commandBatchId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        var batch = await _context.ElementCommandBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == commandBatchId,
                cancellationToken);
        if (batch is null)
            return null;
        if (!string.Equals(batch.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The commandBatchId was already used with different input.",
                "create-new-command-batch");
        }
        if (batch.ResponseJson is null)
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The layout command batch has not reached a replayable state.",
                "reload-floor-scene");
        }
        return Deserialize<ApplySpaceLayoutCommandBatchResponse>(
                batch.ResponseJson)
            with
        {
            IdempotentReplay = true,
        };
    }

    private async Task<ApplySpaceLocationCodesResponse?>
        ReadLocationCodingReplayAsync(
            Guid commandBatchId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        var batch = await _context.ElementCommandBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == commandBatchId,
                cancellationToken);
        if (batch is null)
            return null;
        if (!string.Equals(batch.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Conflict(
                SpaceErrorCodes.CodingConflict,
                "The commandBatchId was already used with different coding input.",
                "create-new-command-batch");
        }
        if (batch.ResponseJson is null)
        {
            throw Conflict(
                SpaceErrorCodes.CodingConflict,
                "The coding command batch is not replayable yet.",
                "reload-floor-scene");
        }
        return Deserialize<ApplySpaceLocationCodesResponse>(batch.ResponseJson) with
        {
            IdempotentReplay = true,
        };
    }

    private async Task<PreviewSpaceLocationCodesResponse>
        BuildLocationCodeProposalAsync(
            SpaceModel model,
            Guid versionId,
            Guid floorLogicalId,
            string mode,
            Guid? scopeZoneLogicalId,
            long expectedFloorRevision,
            long expectedContentRevision,
            CancellationToken cancellationToken)
    {
        var normalizedMode = NormalizeCodingMode(mode);
        var version = await _context.Versions
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              candidate => candidate.Id == versionId,
                              cancellationToken)
                      ?? throw NotFound(
                          SpaceErrorCodes.VersionNotFound,
                          "Space version");
        if (version.Status != SpaceVersionStatus.Draft)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "Only a Draft version can preview generated location codes.",
                "open-or-create-draft");
        }
        var floor = await _context.FloorRevisions
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            candidate =>
                                candidate.ModelVersionId == versionId &&
                                candidate.LogicalId == floorLogicalId,
                            cancellationToken)
                    ?? throw NotFound(
                        SpaceErrorCodes.LogicalIdNotFound,
                        "Space floor logical identity");
        if (floor.Revision != expectedFloorRevision ||
            version.ContentRevision != expectedContentRevision)
        {
            throw Conflict(
                SpaceErrorCodes.CodingProposalStale,
                "The requested coding base revision is stale.",
                "reload-floor-scene");
        }

        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(zone =>
                zone.ModelVersionId == versionId &&
                zone.FloorLogicalId == floorLogicalId &&
                zone.LifecycleState == SpaceLifecycleState.Active &&
                (!scopeZoneLogicalId.HasValue ||
                 zone.LogicalId == scopeZoneLogicalId.Value))
            .OrderBy(zone => zone.ZoneCode)
            .ThenBy(zone => zone.LogicalId)
            .ToListAsync(cancellationToken);
        if (scopeZoneLogicalId.HasValue && zones.Count == 0)
        {
            throw NotFound(
                SpaceErrorCodes.LogicalIdNotFound,
                "Space zone logical identity");
        }
        var zoneIds = zones.Select(zone => zone.LogicalId).ToHashSet();
        var aisles = await _context.AisleRevisions
            .AsNoTracking()
            .Where(aisle =>
                aisle.ModelVersionId == versionId &&
                zoneIds.Contains(aisle.ZoneLogicalId) &&
                aisle.LifecycleState == SpaceLifecycleState.Active)
            .ToDictionaryAsync(aisle => aisle.LogicalId, cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(rack =>
                rack.ModelVersionId == versionId &&
                rack.FloorLogicalId == floorLogicalId &&
                zoneIds.Contains(rack.ZoneLogicalId) &&
                rack.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(rack => rack.ZoneLogicalId)
            .ThenBy(rack => rack.X)
            .ThenBy(rack => rack.Y)
            .ThenBy(rack => rack.LogicalId)
            .ToListAsync(cancellationToken);
        var rackIds = racks.Select(rack => rack.LogicalId).ToHashSet();
        var locations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(location =>
                location.ModelVersionId == versionId &&
                location.FloorLogicalId == floorLogicalId &&
                location.RackLogicalId.HasValue &&
                rackIds.Contains(location.RackLogicalId.Value) &&
                location.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(location => location.RackLogicalId)
            .ThenBy(location => location.LevelNo)
            .ThenBy(location => location.ColumnNo)
            .ThenBy(location => location.DepthNo)
            .ThenBy(location => location.LogicalId)
            .ToListAsync(cancellationToken);
        if (locations.Count > 10_000)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "The coding preview exceeds the 10,000-location floor limit.",
                recoveryAction: "select-a-zone-scope");
        }

        var catalog = await _codingRules.GetCatalogAsync(
            model.SiteId,
            cancellationToken);
        var selectedRules = zones.ToDictionary(
            zone => zone.LogicalId,
            zone => PickCodingRule(catalog.Rules, floor, zone));
        var zoneSequences = zones
            .Select((zone, index) => (zone.LogicalId, Sequence: index + 1))
            .ToDictionary(item => item.LogicalId, item => item.Sequence);
        var rackSequences = racks
            .GroupBy(rack => rack.ZoneLogicalId)
            .SelectMany(group => group.Select(
                (rack, index) => (rack.LogicalId, Sequence: index + 1)))
            .ToDictionary(item => item.LogicalId, item => item.Sequence);
        var rackById = racks.ToDictionary(rack => rack.LogicalId);
        var zoneById = zones.ToDictionary(zone => zone.LogicalId);
        var candidateItems = new List<SpaceLocationCodeProposalItemDto>();

        foreach (var location in locations)
        {
            var rack = rackById[location.RackLogicalId!.Value];
            var zone = zoneById[rack.ZoneLogicalId];
            var rule = selectedRules[zone.LogicalId];
            var protectedReason = CodingProtectionReason(location);
            if (protectedReason is not null)
            {
                candidateItems.Add(ToCodingProposalItem(
                    location,
                    rack,
                    location.LocationCode,
                    CodingDecisionProtected,
                    protectedReason,
                    rule.RuleId));
                continue;
            }
            if (normalizedMode == SpaceDesignCodingContract.FillEmpty &&
                location.LocationCode is not null)
            {
                candidateItems.Add(ToCodingProposalItem(
                    location,
                    rack,
                    location.LocationCode,
                    CodingDecisionUnchanged,
                    "already-coded",
                    rule.RuleId));
                continue;
            }

            aisles.TryGetValue(rack.AisleLogicalId ?? Guid.Empty, out var aisle);
            var code = AssembleDesignLocationCode(
                rule.Segments,
                catalog.SiteCode,
                floor,
                zone,
                aisle,
                rack,
                location,
                zoneSequences,
                rackSequences);
            if (string.IsNullOrWhiteSpace(code) || code.Length > 200)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.CodingRuleInvalid,
                    422,
                    "A generated location code is empty or exceeds 200 characters.",
                    $"Rule {rule.RuleName} generated an invalid code for {location.LogicalId:D}.",
                    "repair-coding-rule");
            }
            candidateItems.Add(ToCodingProposalItem(
                location,
                rack,
                code,
                string.Equals(
                    location.LocationCode,
                    code,
                    StringComparison.Ordinal)
                    ? CodingDecisionUnchanged
                    : CodingDecisionModify,
                string.Equals(
                    location.LocationCode,
                    code,
                    StringComparison.Ordinal)
                    ? "matches-rule"
                    : normalizedMode,
                rule.RuleId));
        }

        var changingCodes = candidateItems
            .Where(item => item.Decision == CodingDecisionModify)
            .Select(item => item.ProposedCode!)
            .ToArray();
        if (changingCodes
            .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingConflict,
                409,
                "The coding proposal contains duplicate location codes.",
                recoveryAction: "repair-coding-rule");
        }
        var changedLogicalIds = candidateItems
            .Where(item => item.Decision == CodingDecisionModify)
            .Select(item => item.LocationLogicalId)
            .ToHashSet();
        var reservedCodes = await _context.LocationRevisions
            .AsNoTracking()
            .Where(location =>
                location.ModelVersionId == versionId &&
                location.LocationCode != null &&
                !changedLogicalIds.Contains(location.LogicalId))
            .Select(location => location.LocationCode!)
            .ToListAsync(cancellationToken);
        var reservedSet = reservedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changingCodes.Any(reservedSet.Contains))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingConflict,
                409,
                "A generated code conflicts with an existing Draft location.",
                recoveryAction: "repair-coding-rule-or-change-scope");
        }

        var usedRules = selectedRules.Values
            .DistinctBy(rule => rule.RuleId)
            .OrderBy(rule => rule.ScopeType)
            .ThenBy(rule => rule.RuleName)
            .ThenBy(rule => rule.RuleId)
            .Select(ToCodingRuleDto)
            .ToArray();
        var ruleSetHash = Hash(JsonSerializer.Serialize(usedRules, JsonOptions));
        var proposalHash = Hash(JsonSerializer.Serialize(
            new
            {
                schemaVersion = SpaceDesignCodingContract.SchemaVersion,
                modelVersionId = versionId,
                floorLogicalId,
                mode = normalizedMode,
                scopeZoneLogicalId,
                baseFloorRevision = floor.Revision,
                baseContentRevision = version.ContentRevision,
                ruleSetHash,
                items = candidateItems,
            },
            JsonOptions));
        return new PreviewSpaceLocationCodesResponse(
            SpaceDesignCodingContract.SchemaVersion,
            versionId,
            floorLogicalId,
            normalizedMode,
            scopeZoneLogicalId,
            floor.Revision,
            version.ContentRevision,
            proposalHash,
            ruleSetHash,
            candidateItems.Count(item => item.Decision == CodingDecisionModify),
            candidateItems.Count(item => item.Decision == CodingDecisionUnchanged),
            candidateItems.Count(item => item.Decision == CodingDecisionProtected),
            usedRules,
            candidateItems);
    }

    private static void ValidateCodingPreviewRequest(
        PreviewSpaceLocationCodesRequest request)
    {
        if (request.SchemaVersion != SpaceDesignCodingContract.SchemaVersion)
            throw Invalid("schemaVersion", "The coding schema is not supported.");
        _ = NormalizeCodingMode(request.Mode);
        if (request.ScopeZoneLogicalId == Guid.Empty ||
            request.ExpectedFloorRevision < 0 ||
            request.ExpectedContentRevision < 0)
        {
            throw Invalid(
                "codingPreview",
                "Zone identity and expected revisions are invalid.");
        }
    }

    private static void ValidateCodingApplyRequest(
        ApplySpaceLocationCodesRequest request)
    {
        if (request.SchemaVersion != SpaceDesignCodingContract.SchemaVersion)
            throw Invalid("schemaVersion", "The coding schema is not supported.");
        _ = NormalizeCodingMode(request.Mode);
        if (request.CommandBatchId == Guid.Empty ||
            request.ClientInstanceId == Guid.Empty ||
            request.LeaseId == Guid.Empty ||
            request.ScopeZoneLogicalId == Guid.Empty ||
            request.ExpectedFloorRevision < 0 ||
            request.ExpectedContentRevision < 0 ||
            !IsSha256(request.ProposalHash))
        {
            throw Invalid(
                "codingApply",
                "Command, lease, revision and proposal hash fields are invalid.");
        }
    }

    private static string NormalizeCodingMode(string mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            SpaceDesignCodingContract.FillEmpty =>
                SpaceDesignCodingContract.FillEmpty,
            SpaceDesignCodingContract.Rebuild =>
                SpaceDesignCodingContract.Rebuild,
            _ => throw Invalid(
                "mode",
                "mode must be fill-empty or rebuild."),
        };

    private static SpaceLocationCodingRuleDefinition PickCodingRule(
        IReadOnlyList<SpaceLocationCodingRuleDefinition> rules,
        SpaceFloorRevision floor,
        SpaceZoneRevision zone)
    {
        var candidates = rules.Where(rule =>
                rule.ScopeType == 2 &&
                string.Equals(
                    rule.ScopeFloorCode,
                    floor.FloorCode,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    rule.ScopeZoneCode,
                    zone.ZoneCode,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = rules.Where(rule =>
                    rule.ScopeType == 1 &&
                    string.Equals(
                        rule.ScopeFloorCode,
                        floor.FloorCode,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        if (candidates.Length == 0)
            candidates = rules.Where(rule => rule.ScopeType == 0).ToArray();
        if (candidates.Length == 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleNotFound,
                422,
                "No coding rule applies to a selected zone.",
                $"No rule applies to zone {zone.ZoneCode} on floor {floor.FloorCode}.",
                "configure-coding-rule");
        }
        var selected = candidates.FirstOrDefault(rule => rule.IsDefault) ??
                       (candidates.Length == 1 ? candidates[0] : null);
        if (selected is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "More than one coding rule applies without a default.",
                $"Zone {zone.ZoneCode} matches {candidates.Length} rules.",
                "choose-default-coding-rule");
        }
        ValidateCodingSegments(selected);
        return selected;
    }

    private static void ValidateCodingSegments(
        SpaceLocationCodingRuleDefinition rule)
    {
        var segments = rule.Segments;
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "fixed", "site-code", "floor-level", "zone-code", "zone-seq",
            "aisle-code", "aisle-seq", "rack-code", "rack-seq", "col",
            "level", "depth",
        };
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (segments.Count is < 1 or > 32 ||
            segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment.Key) ||
                string.IsNullOrWhiteSpace(segment.Source) ||
                segment.Pad is null ||
                segment.Separator is null ||
                segment.FixedValue is null ||
                !keys.Add(segment.Key.Trim()) ||
                !supported.Contains(segment.Source) ||
                segment.Width is < 0 or > 200 ||
                segment.Pad.Length > 1 ||
                segment.Start < 0 ||
                segment.Step <= 0 ||
                segment.Separator.Length > 10 ||
                segment.FixedValue.Length > 200))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "A coding rule segment is invalid.",
                $"Rule {rule.RuleName} has an invalid or unsupported segment.",
                "repair-coding-rule");
        }
        var hasZone = segments.Any(segment =>
            segment.Source is "zone-code" or "zone-seq");
        var hasSiteFloor = segments.Any(segment => segment.Source == "site-code") &&
                           segments.Any(segment => segment.Source == "floor-level");
        if ((!hasZone && !hasSiteFloor) ||
            segments.Any(segment =>
                segment.Source is "aisle-code" or "aisle-seq" &&
                !segment.Optional) ||
            !segments.Any(segment =>
                segment.Source is "col" or "level" or "depth"))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "A coding rule cannot produce safely unique location codes.",
                $"Rule {rule.RuleName} is missing required zone/location or optional aisle semantics.",
                "repair-coding-rule");
        }
    }

    private static string? CodingProtectionReason(
        SpaceLocationRevision location)
    {
        if (location.ExternalBindingState != SpaceExternalBindingState.Unbound)
            return "wms-bound";
        return location.CodeOrigin switch
        {
            SpaceLocationCodeOrigin.Imported => "imported-code",
            SpaceLocationCodeOrigin.Adopted => "adopted-code",
            SpaceLocationCodeOrigin.Manual => "manual-code",
            _ => null,
        };
    }

    private static SpaceLocationCodeProposalItemDto ToCodingProposalItem(
        SpaceLocationRevision location,
        SpaceRackRevision rack,
        string? proposedCode,
        string decision,
        string reason,
        Guid? ruleId) =>
        new(
            location.LogicalId,
            rack.LogicalId,
            rack.RackCode,
            location.ColumnNo,
            location.LevelNo,
            location.DepthNo,
            location.LocationCode,
            proposedCode,
            decision,
            reason,
            ruleId);

    private static SpaceLocationCodingRuleDto ToCodingRuleDto(
        SpaceLocationCodingRuleDefinition rule) =>
        new(
            rule.RuleId,
            rule.RuleName,
            rule.ScopeType,
            rule.ScopeId,
            Hash(JsonSerializer.Serialize(rule, JsonOptions)));

    private static string AssembleDesignLocationCode(
        IReadOnlyList<SpaceLocationCodeSegmentDto> segments,
        string? siteCode,
        SpaceFloorRevision floor,
        SpaceZoneRevision zone,
        SpaceAisleRevision? aisle,
        SpaceRackRevision rack,
        SpaceLocationRevision location,
        IReadOnlyDictionary<Guid, int> zoneSequences,
        IReadOnlyDictionary<Guid, int> rackSequences)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            var aisleSegment = segment.Source is "aisle-code" or "aisle-seq";
            if (aisleSegment && aisle is null && segment.Optional)
                continue;
            var value = segment.Source switch
            {
                "fixed" => segment.FixedValue,
                "site-code" => siteCode ?? string.Empty,
                "floor-level" => floor.Level.ToString(CultureInfo.InvariantCulture),
                "zone-code" => zone.ZoneCode,
                "zone-seq" => CodingSequence(segment, zoneSequences[zone.LogicalId]),
                "aisle-code" => aisle?.AisleCode ?? string.Empty,
                "aisle-seq" => CodingSequence(segment, 1),
                "rack-code" => rack.RackCode,
                "rack-seq" => CodingSequence(segment, rackSequences[rack.LogicalId]),
                "col" => CodingSequence(segment, location.ColumnNo),
                "level" => CodingSequence(segment, location.LevelNo),
                "depth" => CodingSequence(segment, location.DepthNo),
                _ => string.Empty,
            };
            if (segment.Upper)
                value = value.ToUpperInvariant();
            if (segment.Width > 0)
            {
                value = value.PadLeft(
                    segment.Width,
                    string.IsNullOrEmpty(segment.Pad) ? '0' : segment.Pad[0]);
            }
            builder.Append(value);
            builder.Append(segment.Separator);
        }
        return builder.ToString().TrimEnd('-', '_', '.', '/', ' ');
    }

    private static string CodingSequence(
        SpaceLocationCodeSegmentDto segment,
        int sequence)
    {
        try
        {
            return checked(segment.Start + (sequence - 1) * segment.Step)
                .ToString(CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CodingRuleInvalid,
                422,
                "A coding rule sequence exceeds the supported range.",
                $"Segment {segment.Key} overflowed for sequence {sequence}.",
                "repair-coding-rule");
        }
    }

    private static void ValidateRackArray(SpaceGenerateRackArrayDto payload)
    {
        int total;
        try
        {
            total = checked(payload.Rows * payload.Columns);
        }
        catch (OverflowException)
        {
            throw Invalid(
                "commands.generateRackArray",
                "The rack array dimensions are too large.");
        }
        if (payload.Rows is < 1 or > 100 ||
            payload.Columns is < 1 or > 100 ||
            total is < 2 or > 100)
        {
            throw Invalid(
                "commands.generateRackArray",
                "The rack array must contain between 2 and 100 total racks, including the template rack.");
        }
        if (payload.RowGap < 0 ||
            payload.ColumnGap < 0 ||
            payload.StaggerOffset < 0)
        {
            throw Invalid(
                "commands.generateRackArray",
                "Rack array gaps and stagger offset cannot be negative.");
        }
        if (string.IsNullOrWhiteSpace(payload.CodePrefix) ||
            payload.CodePrefix.Trim().Length > 90)
        {
            throw Invalid(
                "commands.generateRackArray.codePrefix",
                "A code prefix of at most 90 characters is required.");
        }
        if (payload.StartNumber < 0 ||
            payload.CodeDigits is < 1 or > 8)
        {
            throw Invalid(
                "commands.generateRackArray",
                "The code start number must be non-negative and codeDigits must be between 1 and 8.");
        }
    }

    private async Task<ApplySpaceElementCommandBatchResponse?>
        ReadElementCommandReplayAsync(
            Guid commandBatchId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        var batch = await _context.ElementCommandBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == commandBatchId,
                cancellationToken);
        if (batch is null)
            return null;
        if (!string.Equals(
                batch.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The commandBatchId was already used with different input.",
                "create-new-command-batch");
        }
        if (batch.ResponseJson is null)
        {
            throw Conflict(
                SpaceErrorCodes.CommandConflict,
                "The command batch has not reached a replayable state.",
                "reload-floor-scene");
        }
        return Deserialize<ApplySpaceElementCommandBatchResponse>(
                batch.ResponseJson)
            with
        {
            IdempotentReplay = true,
        };
    }

    private static void ValidateCommandTargets(
        IReadOnlyList<SpaceElementCommandDto> commands,
        IReadOnlyDictionary<Guid, SpaceElementRevision> elements,
        IReadOnlyDictionary<Guid, SpaceRackRevision> racks,
        IReadOnlyCollection<SpaceRackLevelRevision> rackLevels,
        IReadOnlyCollection<SpaceLocationRevision> locations,
        bool isCadChangeset)
    {
        foreach (var command in commands)
        {
            if (command.Type == SpaceElementCommandContract.CreateElement)
                continue;
            var isElement = elements.TryGetValue(
                command.TargetLogicalId,
                out var element);
            var rack = isElement
                ? null
                : racks[command.TargetLogicalId];
            var lifecycle = isElement
                ? element!.LifecycleState
                : rack!.LifecycleState;
            var expectedLifecycle =
                command.Type ==
                SpaceElementCommandContract.RestoreLogicalObject
                    ? SpaceLifecycleState.RemoveRequested
                    : SpaceLifecycleState.Active;
            if (lifecycle != expectedLifecycle)
            {
                throw Conflict(
                    SpaceErrorCodes.CommandConflict,
                    $"{command.Type} requires a {expectedLifecycle} target.",
                    "reload-floor-scene");
            }
            if (command.Type ==
                    SpaceElementCommandContract.UpdateProperties &&
                !isElement)
            {
                throw Invalid(
                    "commands.updateProperties",
                    "UpdateProperties can target only a common element.");
            }
            if (isCadChangeset && isElement &&
                element!.IsManualCorrectionLocked)
            {
                throw Conflict(
                    SpaceErrorCodes.CadManualCorrectionLocked,
                    "A CAD changeset cannot replace a locked manual correction.",
                    "keep-manual-correction-or-unlock");
            }
            if (command.Type ==
                    SpaceElementCommandContract.UpdateProperties &&
                command.UpdateProperties!.ManualCorrectionLocked.HasValue)
            {
                if (element!.SourceId is null ||
                    string.IsNullOrWhiteSpace(element.SourceRef))
                {
                    throw Invalid(
                        "commands.updateProperties.manualCorrectionLocked",
                        "Only a source-backed element can lock a manual correction.");
                }
                if (element.IsManualCorrectionLocked ==
                    command.UpdateProperties.ManualCorrectionLocked.Value)
                {
                    throw Conflict(
                        SpaceErrorCodes.CommandConflict,
                        "The manual correction already has the requested lock state.",
                        "reload-floor-scene");
                }
            }
            if (command.Type ==
                    SpaceElementCommandContract.UpdateProperties &&
                element?.ModelAssetId is not null &&
                !string.IsNullOrWhiteSpace(
                    command.UpdateProperties!.ElementType) &&
                !string.Equals(
                    command.UpdateProperties.ElementType.Trim(),
                    element.ElementType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(
                    "commands.updateProperties.elementType",
                    "An asset-backed element cannot be retyped.");
            }
            if (command.Type ==
                    SpaceElementCommandContract.GenerateRackArray &&
                isElement)
            {
                throw Invalid(
                    "commands.generateRackArray",
                    "GenerateRackArray requires a rack template.");
            }
            if (command.Type !=
                    SpaceElementCommandContract.GenerateRackArray ||
                rack is null)
            {
                continue;
            }

            var generatedRackCount =
                checked(
                    command.GenerateRackArray!.Rows *
                    command.GenerateRackArray.Columns) - 1;
            var activeLevelCount = rackLevels.Count(candidate =>
                candidate.RackLogicalId == rack.LogicalId &&
                candidate.LifecycleState == SpaceLifecycleState.Active);
            var activeLocationCount = locations.Count(candidate =>
                candidate.RackLogicalId == rack.LogicalId &&
                candidate.LifecycleState == SpaceLifecycleState.Active);
            if (activeLevelCount == 0)
            {
                throw Invalid(
                    "commands.generateRackArray",
                    "The rack template must have at least one active design level.");
            }
            if (generatedRackCount * activeLevelCount > 2_000 ||
                generatedRackCount * activeLocationCount > 5_000)
            {
                throw Invalid(
                    "commands.generateRackArray",
                    "The array would exceed the 2,000-level or 5,000-location command limit.");
            }
        }
    }

    private async Task ValidateRackArrayCodesAsync(
        Guid versionId,
        IReadOnlyList<SpaceElementCommandDto> commands,
        IReadOnlyDictionary<Guid, SpaceRackRevision> racks,
        CancellationToken cancellationToken)
    {
        var arrayCommands = commands
            .Where(command =>
                command.Type ==
                SpaceElementCommandContract.GenerateRackArray)
            .ToArray();
        if (arrayCommands.Length == 0)
            return;

        var existing = await _context.RackRevisions
            .AsNoTracking()
            .Where(candidate => candidate.ModelVersionId == versionId)
            .Select(candidate => new
            {
                candidate.ZoneLogicalId,
                candidate.RackCode,
            })
            .ToListAsync(cancellationToken);
        var reserved = existing
            .Select(candidate =>
                RackCodeKey(candidate.ZoneLogicalId, candidate.RackCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var command in arrayCommands)
        {
            var source = racks[command.TargetLogicalId];
            var payload = command.GenerateRackArray!;
            var generatedCount =
                checked(payload.Rows * payload.Columns) - 1;
            for (var index = 0; index < generatedCount; index++)
            {
                int number;
                try
                {
                    number = checked(payload.StartNumber + index);
                }
                catch (OverflowException)
                {
                    throw Invalid(
                        "commands.generateRackArray.startNumber",
                        "The generated rack code sequence is too large.");
                }
                var code = RackArrayCode(payload, number);
                if (code.Length > 100)
                {
                    throw Invalid(
                        "commands.generateRackArray.codePrefix",
                        "A generated rack code exceeds 100 characters.");
                }
                if (!reserved.Add(
                        RackCodeKey(source.ZoneLogicalId, code)))
                {
                    throw Conflict(
                        SpaceErrorCodes.CommandConflict,
                        $"Rack code '{code}' already exists in the target zone.",
                        "change-rack-code-preview");
                }
            }
        }
    }

    private string ApplyElementCommand(
        SpaceElementCommandDto command,
        SpaceElementRevision element,
        List<SpaceElementAttribute> attributes)
    {
        switch (command.Type)
        {
            case SpaceElementCommandContract.UpdateProperties:
                var update = command.UpdateProperties!;
                var updateJson = ApplyElementProperties(
                    element,
                    attributes,
                    update);
                if (update.ManualCorrectionLocked.HasValue)
                {
                    element.SetManualCorrectionLock(
                        update.ManualCorrectionLocked.Value,
                        _execution.ActorId,
                        RequireUtcNow());
                }
                else
                {
                    element.MarkLockedManualCorrectionChanged(
                        _execution.ActorId,
                        RequireUtcNow());
                }
                return updateJson;
            case SpaceElementCommandContract.MoveObject:
                var move = command.MoveObject!;
                element.ConfigurePlacement(
                    move.X,
                    move.Y,
                    move.Z,
                    element.RotationZ,
                    element.Width,
                    element.Height,
                    element.Depth);
                element.MarkLockedManualCorrectionChanged(
                    _execution.ActorId,
                    RequireUtcNow());
                return JsonSerializer.Serialize(move, JsonOptions);
            case SpaceElementCommandContract.RotateObject:
                var rotation = command.RotateObject!;
                element.ConfigurePlacement(
                    element.X,
                    element.Y,
                    element.Z,
                    rotation.RotationZ,
                    element.Width,
                    element.Height,
                    element.Depth);
                element.MarkLockedManualCorrectionChanged(
                    _execution.ActorId,
                    RequireUtcNow());
                return JsonSerializer.Serialize(rotation, JsonOptions);
            case SpaceElementCommandContract.DeleteObject:
                element.ChangeLifecycle(
                    SpaceLifecycleState.RemoveRequested);
                element.MarkLockedManualCorrectionChanged(
                    _execution.ActorId,
                    RequireUtcNow());
                return "{}";
            case SpaceElementCommandContract.RestoreLogicalObject:
                element.ChangeLifecycle(SpaceLifecycleState.Active);
                element.MarkLockedManualCorrectionChanged(
                    _execution.ActorId,
                    RequireUtcNow());
                return "{}";
            default:
                throw new UnreachableException();
        }
    }

    private static string ApplyRackCommand(
        SpaceElementCommandDto command,
        SpaceRackRevision rack,
        IReadOnlyCollection<SpaceRackLevelRevision> rackLevels,
        IReadOnlyCollection<SpaceLocationRevision> locations)
    {
        switch (command.Type)
        {
            case SpaceElementCommandContract.MoveObject:
                var move = command.MoveObject!;
                rack.ConfigureGeometry(
                    move.X,
                    move.Y,
                    move.Z,
                    rack.RotationZ,
                    rack.Width,
                    rack.Depth,
                    rack.Height,
                    rack.TemplateVersionId);
                return JsonSerializer.Serialize(move, JsonOptions);
            case SpaceElementCommandContract.RotateObject:
                var rotation = command.RotateObject!;
                rack.ConfigureGeometry(
                    rack.X,
                    rack.Y,
                    rack.Z,
                    rotation.RotationZ,
                    rack.Width,
                    rack.Depth,
                    rack.Height,
                    rack.TemplateVersionId);
                return JsonSerializer.Serialize(rotation, JsonOptions);
            case SpaceElementCommandContract.DeleteObject:
                ChangeRackLifecycle(
                    rack,
                    rackLevels,
                    locations,
                    SpaceLifecycleState.RemoveRequested);
                return "{}";
            case SpaceElementCommandContract.RestoreLogicalObject:
                ChangeRackLifecycle(
                    rack,
                    rackLevels,
                    locations,
                    SpaceLifecycleState.Active);
                return "{}";
            default:
                throw new UnreachableException();
        }
    }

    private static void ChangeRackLifecycle(
        SpaceRackRevision rack,
        IEnumerable<SpaceRackLevelRevision> rackLevels,
        IEnumerable<SpaceLocationRevision> locations,
        SpaceLifecycleState lifecycle)
    {
        rack.ChangeLifecycle(lifecycle);
        var sourceLifecycle =
            lifecycle == SpaceLifecycleState.RemoveRequested
                ? SpaceLifecycleState.Active
                : SpaceLifecycleState.RemoveRequested;
        foreach (var level in rackLevels.Where(candidate =>
                     candidate.LifecycleState == sourceLifecycle))
        {
            level.ChangeLifecycle(lifecycle);
        }
        foreach (var location in locations.Where(candidate =>
                     candidate.LifecycleState == sourceLifecycle))
        {
            location.ChangeLifecycle(lifecycle);
        }
    }

    private RackArrayGeneration GenerateRackArray(
        Guid versionId,
        Guid floorLogicalId,
        SpaceRackRevision source,
        IReadOnlyCollection<SpaceRackLevelRevision> sourceLevels,
        IReadOnlyCollection<SpaceLocationRevision> sourceLocations,
        SpaceGenerateRackArrayDto payload)
    {
        var generatedRacks = new List<SpaceRackRevision>();
        var generatedLevels = new List<SpaceRackLevelRevision>();
        var generatedLocations = new List<SpaceLocationRevision>();
        var radians = (double)source.RotationZ * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var sequence = 0;
        for (var row = 0; row < payload.Rows; row++)
        {
            for (var column = 0; column < payload.Columns; column++)
            {
                if (row == 0 && column == 0)
                    continue;

                var localX =
                    column * ((long)source.Width + payload.ColumnGap) +
                    (row % 2 == 1 ? payload.StaggerOffset : 0);
                var localY =
                    row * ((long)source.Depth + payload.RowGap);
                var x = checked(
                    source.X +
                    (int)Math.Round(
                        localX * cosine - localY * sine,
                        MidpointRounding.AwayFromZero));
                var y = checked(
                    source.Y +
                    (int)Math.Round(
                        localX * sine + localY * cosine,
                        MidpointRounding.AwayFromZero));
                var rack = SpaceRackRevision.Create(
                    _execution.TenantId,
                    versionId,
                    Guid.NewGuid(),
                    floorLogicalId,
                    source.ZoneLogicalId,
                    RackArrayCode(
                        payload,
                        checked(payload.StartNumber + sequence)),
                    source.AisleLogicalId);
                rack.ConfigureGeometry(
                    x,
                    y,
                    source.Z,
                    source.RotationZ,
                    source.Width,
                    source.Depth,
                    source.Height,
                    source.TemplateVersionId);
                generatedRacks.Add(rack);
                _context.RackRevisions.Add(rack);

                foreach (var sourceLevel in sourceLevels.Where(level =>
                             level.LifecycleState ==
                             SpaceLifecycleState.Active))
                {
                    var level = SpaceRackLevelRevision.Create(
                        _execution.TenantId,
                        versionId,
                        Guid.NewGuid(),
                        rack.LogicalId,
                        sourceLevel.LevelNo,
                        sourceLevel.BottomZ,
                        sourceLevel.ClearHeight,
                        sourceLevel.BinCount,
                        sourceLevel.DepthCount,
                        sourceLevel.CellWidth,
                        sourceLevel.CellDepth,
                        sourceLevel.MaxLoad,
                        sourceLevel.BeamHeight);
                    generatedLevels.Add(level);
                    _context.RackLevelRevisions.Add(level);
                }
                foreach (var sourceLocation in sourceLocations.Where(
                             location =>
                                 location.LifecycleState ==
                                 SpaceLifecycleState.Active))
                {
                    var location = SpaceLocationRevision.Create(
                        _execution.TenantId,
                        versionId,
                        Guid.NewGuid(),
                        floorLogicalId,
                        rack.LogicalId,
                        locationCode: null,
                        sourceLocation.ColumnNo,
                        sourceLocation.LevelNo,
                        sourceLocation.DepthNo,
                        sourceLocation.Width,
                        sourceLocation.Height,
                        sourceLocation.Depth,
                        sourceLocation.MaxLoad,
                        SpaceLocationCodeOrigin.Generated,
                        SpaceExternalBindingState.Unbound);
                    generatedLocations.Add(location);
                    _context.LocationRevisions.Add(location);
                }
                sequence++;
            }
        }
        return new RackArrayGeneration(
            generatedRacks,
            generatedLevels,
            generatedLocations);
    }

    private static string RackArrayCode(
        SpaceGenerateRackArrayDto payload,
        int number) =>
        $"{payload.CodePrefix.Trim()}" +
        number.ToString(
            $"D{payload.CodeDigits}",
            CultureInfo.InvariantCulture);

    private static string RackCodeKey(Guid zoneLogicalId, string rackCode) =>
        $"{zoneLogicalId:D}\u001f{rackCode}";

    private sealed record RackArrayGeneration(
        IReadOnlyList<SpaceRackRevision> Racks,
        IReadOnlyList<SpaceRackLevelRevision> Levels,
        IReadOnlyList<SpaceLocationRevision> Locations);

    private string ApplyElementProperties(
        SpaceElementRevision element,
        List<SpaceElementAttribute> attributes,
        SpaceUpdateElementPropertiesDto payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.ElementType) &&
            !string.Equals(
                payload.ElementType.Trim(),
                element.ElementType,
                StringComparison.OrdinalIgnoreCase))
        {
            element.Retype(payload.ElementType);
        }
        element.UpdateGeometry(payload.GeometryJson);
        element.ConfigurePlacement(
            payload.X,
            payload.Y,
            payload.Z,
            payload.RotationZ,
            payload.Width,
            payload.Height,
            payload.Depth);
        element.ConfigureBusinessLink(
            payload.BusinessCode,
            payload.LinkedEntityType,
            payload.LinkedLogicalId);

        ApplyElementAttributes(element, attributes, payload.Attributes);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static void ValidateOptionalElementType(string? elementType)
    {
        if (elementType is null)
            return;
        var normalized = elementType.Trim();
        if (normalized.Length == 0 ||
            !SpaceElementTypes.Supported.Contains(
                normalized,
                StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid(
                "commands.updateProperties.elementType",
                "A supported Space element type is required when retyping an element.");
        }
    }

    private async Task<bool> LogicalIdsExistAsync(
        Guid versionId,
        Guid[] logicalIds,
        CancellationToken cancellationToken)
    {
        if (logicalIds.Length == 0)
            return false;
        return await _context.FloorRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.ZoneRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.AisleRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.RackRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.RackLevelRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.LocationRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken) ||
               await _context.ElementRevisions.AnyAsync(item =>
                   item.ModelVersionId == versionId &&
                   logicalIds.Contains(item.LogicalId), cancellationToken);
    }

    private void ApplyElementAttributes(
        SpaceElementRevision element,
        List<SpaceElementAttribute> attributes,
        IReadOnlyList<SpaceElementAttributeWriteDto> requestedAttributes)
    {
        var existing = attributes.ToDictionary(
            AttributeKey,
            StringComparer.OrdinalIgnoreCase);
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requested in requestedAttributes)
        {
            if (requested is null)
            {
                throw new ArgumentException(
                    "Attribute entries cannot be null.",
                    nameof(requestedAttributes));
            }
            var candidate = SpaceElementAttribute.Create(
                _execution.TenantId,
                element,
                requested.Namespace,
                requested.Key,
                requested.ValueType,
                requested.Value,
                requested.Unit);
            var key = AttributeKey(candidate);
            if (!retained.Add(key))
            {
                throw new ArgumentException(
                    "Element attribute namespace and key pairs must be unique.",
                    nameof(requestedAttributes));
            }
            if (existing.TryGetValue(key, out var current))
            {
                current.UpdateValue(
                    requested.ValueType,
                    requested.Value,
                    requested.Unit);
                continue;
            }

            attributes.Add(candidate);
            _context.ElementAttributes.Add(candidate);
        }

        foreach (var attribute in attributes.Where(attribute =>
                     !attribute.IsDeleted &&
                     !retained.Contains(AttributeKey(attribute))))
        {
            attribute.Remove();
        }
    }

    private static string ElementAuditJson(
        SpaceElementRevision element,
        IEnumerable<SpaceElementAttribute> attributes) =>
        JsonSerializer.Serialize(
            new
            {
                element.LogicalId,
                element.ElementType,
                element.GeometryJson,
                element.X,
                element.Y,
                element.Z,
                element.RotationZ,
                element.Width,
                element.Height,
                element.Depth,
                element.BusinessCode,
                element.LinkedEntityType,
                element.LinkedLogicalId,
                element.IsManualCorrectionLocked,
                element.UserCorrectionVersion,
                element.ManualCorrectionUpdatedBy,
                element.ManualCorrectionUpdatedAtUtc,
                LifecycleState = element.LifecycleState.ToString(),
                Attributes = attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .OrderBy(attribute => attribute.Namespace)
                    .ThenBy(attribute => attribute.Key)
                    .Select(attribute => new
                    {
                        attribute.Namespace,
                        attribute.Key,
                        attribute.ValueType,
                        attribute.Value,
                        attribute.Unit,
                    })
                    .ToArray(),
            },
            JsonOptions);

    private static string RackAuditJson(
        SpaceRackRevision rack,
        IEnumerable<SpaceRackLevelRevision> rackLevels,
        IEnumerable<SpaceLocationRevision> locations) =>
        JsonSerializer.Serialize(
            new
            {
                rack.LogicalId,
                rack.FloorLogicalId,
                rack.ZoneLogicalId,
                rack.AisleLogicalId,
                rack.RackCode,
                rack.TemplateVersionId,
                rack.X,
                rack.Y,
                rack.Z,
                rack.RotationZ,
                rack.Width,
                rack.Depth,
                rack.Height,
                LifecycleState = rack.LifecycleState.ToString(),
                Levels = rackLevels
                    .OrderBy(level => level.LevelNo)
                    .Select(level => new
                    {
                        level.LogicalId,
                        level.LevelNo,
                        level.BottomZ,
                        level.ClearHeight,
                        level.BinCount,
                        level.DepthCount,
                        level.CellWidth,
                        level.CellDepth,
                        level.BeamHeight,
                        level.MaxLoad,
                        LifecycleState = level.LifecycleState.ToString(),
                    })
                    .ToArray(),
                Locations = locations
                    .OrderBy(location => location.LevelNo)
                    .ThenBy(location => location.ColumnNo)
                    .ThenBy(location => location.DepthNo)
                    .Select(location => new
                    {
                        location.LogicalId,
                        location.LocationCode,
                        location.ColumnNo,
                        location.LevelNo,
                        location.DepthNo,
                        location.Width,
                        location.Height,
                        location.Depth,
                        location.MaxLoad,
                        CodeOrigin = location.CodeOrigin.ToString(),
                        ExternalBindingState =
                            location.ExternalBindingState.ToString(),
                        LifecycleState =
                            location.LifecycleState.ToString(),
                    })
                    .ToArray(),
            },
            JsonOptions);

    private static string AttributeKey(SpaceElementAttribute attribute) =>
        $"{attribute.Namespace}\u001f{attribute.Key}";

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

    private async Task EnsureActiveEditLeaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        Guid clientInstanceId,
        CancellationToken cancellationToken)
    {
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
        var lease = await _context.EditLeases
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId,
                cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != _execution.ActorId ||
            lease.ClientInstanceId != clientInstanceId ||
            lease.IsExpired(now))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseLost,
                409,
                "The edit lease is no longer valid.",
                recoveryAction: "export-recovery-draft-or-reacquire",
                retryable: true);
        }
    }

    private static void ValidateCreateElement(SpaceCreateElementDto payload)
    {
        if (payload.Attributes is null || payload.Attributes.Count > 100)
        {
            throw Invalid(
                "commands.createElement.attributes",
                "At most 100 attributes are allowed.");
        }
        if (payload.ParentLogicalId == Guid.Empty)
        {
            throw Invalid(
                "commands.createElement.parentLogicalId",
                "Parent identity cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(payload.ElementType) ||
            payload.ElementType.Trim().Length > 64)
        {
            throw Invalid(
                "commands.createElement.elementType",
                "A bounded element type is required.");
        }
        if (payload.Width <= 0 || payload.Height <= 0 || payload.Depth <= 0)
        {
            throw Invalid(
                "commands.createElement",
                "Positive element dimensions are required.");
        }
        if (payload.SourceRef is not null &&
            (string.IsNullOrWhiteSpace(payload.SourceRef) ||
             payload.SourceRef.Trim().Length > 500))
        {
            throw Invalid(
                "commands.createElement.sourceRef",
                "Source reference is invalid.");
        }
        if (payload.SourceId == Guid.Empty ||
            payload.SourceId.HasValue != (payload.SourceRef is not null))
        {
            throw Invalid(
                "commands.createElement.sourceId",
                "Source identity and source reference must be supplied together.");
        }
        if (payload.LinkedLogicalId == Guid.Empty ||
            payload.LinkedLogicalId.HasValue !=
            !string.IsNullOrWhiteSpace(payload.LinkedEntityType))
        {
            throw Invalid(
                "commands.createElement.linkedLogicalId",
                "Linked entity type and logical identity must be supplied together.");
        }
        if (payload.LinkedEntityType?.Trim().Length > 100)
        {
            throw Invalid(
                "commands.createElement.linkedEntityType",
                "Linked entity type cannot exceed 100 characters.");
        }
    }


    private async Task<DateTime> ReadAuthoritativeUtcNowAsync(
        CancellationToken cancellationToken)
    {
        var now = _context.Database.IsSqlServer()
            ? await _context.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
            : RequireUtcNow();
        return now.Kind == DateTimeKind.Utc
            ? now
            : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    private async Task AcquireFloorEditLockAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"cp6:space:floor-edit:{_execution.TenantId:N}:" +
                    $"{versionId:N}:{floorLogicalId:N}",
        };
        await _context.Database.ExecuteSqlRawAsync(
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
                SpaceErrorCodes.CommandConflict,
                "The floor edit session is busy.",
                "retry-command-batch");
        }
    }

    private async Task AcquireVersionFloorInitializationLockAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"cp6:space:version-floor-init:{_execution.TenantId:N}:" +
                    $"{versionId:N}",
        };
        await _context.Database.ExecuteSqlRawAsync(
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
                SpaceErrorCodes.ConcurrencyConflict,
                "The version floor initialization session is busy.",
                "retry-floor-creation");
        }
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

    private async Task<CreateSpaceFloorResponse?> ReadFloorReplayAsync(
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
        return Deserialize<CreateSpaceFloorResponse>(record.ResponseJson)
            with
        { IdempotentReplay = true };
    }

    private async Task<CreateSpaceAssetResponse?> ReadAssetReplayAsync(
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
        return Deserialize<CreateSpaceAssetResponse>(record.ResponseJson)
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

    private static SpaceSceneRevisionDto ToSceneRevision(
        SpaceRevisionEntity revision) =>
        new(
            revision.Id,
            revision.LogicalId,
            revision.SourceId,
            revision.SourceRef,
            revision.LifecycleState.ToString(),
            RowVersion(revision.RowVersion));

    private static SpaceSceneFloorDto ToSceneDto(
        SpaceFloorRevision floor) =>
        new(
            ToSceneRevision(floor),
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

    private static SpaceSceneZoneDto ToSceneDto(
        SpaceZoneRevision zone) =>
        new(
            ToSceneRevision(zone),
            zone.FloorLogicalId,
            zone.ZoneCode,
            zone.Name,
            zone.ZoneType,
            zone.PolygonJson,
            zone.Color,
            zone.CapabilityFlags);

    private static SpaceSceneAisleDto ToSceneDto(
        SpaceAisleRevision aisle) =>
        new(
            ToSceneRevision(aisle),
            aisle.ZoneLogicalId,
            aisle.AisleCode,
            aisle.Name,
            aisle.PolygonJson,
            aisle.CenterlineJson,
            aisle.Direction);

    private static SpaceSceneRackDto ToSceneDto(
        SpaceRackRevision rack) =>
        new(
            ToSceneRevision(rack),
            rack.FloorLogicalId,
            rack.ZoneLogicalId,
            rack.AisleLogicalId,
            rack.RackCode,
            rack.Name,
            rack.RackType,
            rack.TemplateVersionId,
            rack.X,
            rack.Y,
            rack.Z,
            rack.RotationZ,
            rack.Width,
            rack.Depth,
            rack.Height);

    private static SpaceSceneRackLevelDto ToSceneDto(
        SpaceRackLevelRevision level) =>
        new(
            ToSceneRevision(level),
            level.RackLogicalId,
            level.LevelNo,
            level.BottomZ,
            level.ClearHeight,
            level.BinCount,
            level.DepthCount,
            level.CellWidth,
            level.CellDepth,
            level.BeamHeight,
            level.MaxLoad);

    private static SpaceSceneLocationDto ToSceneDto(
        SpaceLocationRevision location) =>
        new(
            ToSceneRevision(location),
            location.FloorLogicalId,
            location.RackLogicalId,
            location.LocationCode,
            location.ColumnNo,
            location.LevelNo,
            location.DepthNo,
            location.Width,
            location.Height,
            location.Depth,
            location.MaxLoad,
            location.CodeOrigin.ToString(),
            location.ExternalBindingState.ToString(),
            location.LocationType);

    private static SpaceSceneLocationExternalBindingDto ToSceneDto(
        SpaceLocationExternalBinding binding) =>
        new(
            binding.Id,
            binding.LocationLogicalId,
            binding.AdapterId,
            binding.WarehouseCode,
            binding.ExternalLocationId,
            binding.BindingMode.ToString(),
            binding.SourceId,
            binding.SourceRef);

    private static SpaceSceneDesignAttributeDto ToSceneDto(
        SpaceDesignAttribute attribute) =>
        new(
            attribute.Id,
            attribute.ObjectType,
            attribute.ObjectLogicalId,
            attribute.Namespace,
            attribute.Key,
            attribute.Value,
            attribute.Unit,
            attribute.SourceId,
            attribute.SourceRef);

    private static SpaceSceneElementDto ToSceneDto(
        SpaceElementRevision element) =>
        new(
            ToSceneRevision(element),
            element.FloorLogicalId,
            element.ParentLogicalId,
            element.ElementType,
            element.GeometryJson,
            element.ModelAssetId,
            element.ModelAssetScope?.ToString(),
            element.X,
            element.Y,
            element.Z,
            element.RotationZ,
            element.Width,
            element.Height,
            element.Depth,
            element.BusinessCode,
            element.LinkedEntityType,
            element.LinkedLogicalId,
            element.IsManualCorrectionLocked,
            element.UserCorrectionVersion,
            element.ManualCorrectionUpdatedBy,
            element.ManualCorrectionUpdatedAtUtc);

    private static SpaceAssetDto ToDto(
        SpaceAsset asset,
        SpaceAssetVersion latestVersion) =>
        new(
            asset.Id,
            asset.Scope.ToString(),
            asset.AssetCode,
            asset.Name,
            asset.Category,
            asset.Description,
            asset.Status.ToString(),
            new SpaceAssetVersionDto(
                latestVersion.Id,
                latestVersion.VersionNo,
                latestVersion.Format.ToString(),
                latestVersion.ParameterSchemaJson,
                latestVersion.PreviewRef,
                latestVersion.RenderArtifactRef,
                latestVersion.ContentHash,
                latestVersion.Status.ToString(),
                RowVersion(latestVersion.RowVersion)),
            RowVersion(asset.RowVersion));

    private static SpaceSceneElementAttributeDto ToSceneDto(
        SpaceElementAttribute attribute) =>
        new(
            attribute.Id,
            attribute.ElementRevisionId,
            attribute.Namespace,
            attribute.Key,
            attribute.ValueType,
            attribute.Value,
            attribute.Unit);

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
            RowVersion(version.RowVersion),
            version.Purpose.ToString());

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

    private void EnsureInternalEditor()
    {
        if (!_execution.IsExternal)
            return;
        throw new SpaceProblemException(
            SpaceErrorCodes.ExternalSubjectDenied,
            403,
            "External principals cannot access Draft design APIs.",
            recoveryAction: "use-internal-space-editor");
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

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

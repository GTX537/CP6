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

public sealed class SpaceWmsAdoptionService : ISpaceWmsAdoptionService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MaxBatchSize = 1_000;
    private const string CursorResource = "wms-adoption-locations";

    private static readonly HashSet<string> DifferenceCodes =
    [
        SpaceErrorCodes.WmsLocationUnbound,
        SpaceErrorCodes.WmsLocationCodeDuplicate,
        SpaceErrorCodes.WmsBindingGeometryMissing,
        SpaceErrorCodes.WmsBindingCodeMismatch,
        SpaceErrorCodes.WmsLocationMissing,
    ];

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly ISpaceWmsAdapter _adapter;
    private readonly ISpaceWarehouseResolver _warehouses;

    public SpaceWmsAdoptionService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceCursorCodec cursorCodec,
        ISpaceWmsAdapter adapter,
        ISpaceWarehouseResolver warehouses)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _cursorCodec = cursorCodec;
        _adapter = adapter;
        _warehouses = warehouses;
    }

    public async Task<RefreshSpaceWmsAdoptionResponse> RefreshAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var scope = await LoadScopeAsync(
            versionId,
            write: true,
            cancellationToken);
        var warehouse = await ResolveWarehouseAsync(
            scope.Model.SiteId,
            cancellationToken);
        var wmsContext = CreateWmsContext(scope.Model.SiteId, warehouse);
        SpaceWmsCapabilitySnapshot capabilities;
        SpaceWmsHealth health;
        SpaceWmsLocationResult catalog;
        try
        {
            capabilities = await _adapter.GetCapabilitiesAsync(
                wmsContext,
                cancellationToken);
            health = await _adapter.CheckHealthAsync(
                wmsContext,
                cancellationToken);
            catalog = await _adapter.QueryLocationsAsync(
                new SpaceWmsLocationQuery(wmsContext, []),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                503,
                "The WMS location catalog is unavailable.",
                $"The adapter failed with {exception.GetType().Name}.",
                "retry-wms-refresh",
                retryable: true);
        }
        if (capabilities.DataSourceKind == SpaceWmsDataSourceKind.Unavailable ||
            health.State == SpaceWmsHealthState.Unavailable ||
            !string.Equals(
                capabilities.AdapterId,
                health.AdapterId,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                503,
                "The WMS location catalog is unavailable.",
                recoveryAction: "retry-wms-refresh",
                retryable: true);
        }

        if (!catalog.Source.IsAvailable ||
            catalog.Source.Kind != capabilities.DataSourceKind)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                503,
                "The WMS location catalog is unavailable.",
                recoveryAction: "retry-wms-refresh",
                retryable: true);
        }

        EnsureCatalogIdentity(catalog.Items);
        var observedAtUtc = catalog.Source.ObservedAtUtc.UtcDateTime;
        var dataSource = catalog.Source.DataSourceId;

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        var existing = await _context.WmsAdoptions
            .Where(value =>
                value.SiteId == scope.Model.SiteId &&
                value.AdapterId == capabilities.AdapterId)
            .ToListAsync(cancellationToken);
        var byLogicalId = existing.ToDictionary(value => value.WmsLogicalId);
        var seen = new HashSet<Guid>();
        var updatedCount = 0;

        foreach (var state in catalog.Items.OrderBy(value => value.LogicalId))
        {
            seen.Add(state.LogicalId);
            if (byLogicalId.TryGetValue(state.LogicalId, out var adoption))
            {
                adoption.Observe(
                    dataSource,
                    catalog.Source.Kind.ToString(),
                    state.ExternalLocationId,
                    state.LocationCode,
                    state.IsActive,
                    state.ExternalVersion,
                    state.StateHash,
                    observedAtUtc);
                updatedCount++;
                continue;
            }

            adoption = SpaceWmsAdoption.Discover(
                _execution.TenantId,
                scope.Model.SiteId,
                capabilities.AdapterId,
                dataSource,
                catalog.Source.Kind.ToString(),
                state.LogicalId,
                state.ExternalLocationId,
                state.LocationCode,
                state.IsActive,
                state.ExternalVersion,
                state.StateHash,
                observedAtUtc);
            _context.WmsAdoptions.Add(adoption);
            existing.Add(adoption);
        }

        foreach (var adoption in existing.Where(
                     value => !seen.Contains(value.WmsLogicalId)))
        {
            adoption.MarkMissing(observedAtUtc);
        }

        await SyncIssuesAsync(
            scope.Version.Id,
            existing,
            Guid.NewGuid(),
            cancellationToken);
        await SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        var states = await BuildStatesAsync(
            scope.Version.Id,
            existing,
            cancellationToken);
        return new RefreshSpaceWmsAdoptionResponse(
            scope.Model.SiteId,
            capabilities.AdapterId,
            dataSource,
            capabilities.DataSourceKind.ToString(),
            observedAtUtc,
            catalog.Items.Count,
            updatedCount,
            states.Count(value =>
                value.Adoption.Status == SpaceWmsAdoptionStatus.MissingInWms),
            states.Count(value =>
                value.Adoption.Status == SpaceWmsAdoptionStatus.Unbound),
            states.Count(value =>
                value.Adoption.Status == SpaceWmsAdoptionStatus.Bound),
            states.Count(value => value.DifferenceCode is not null));
    }

    public async Task<SpacePage<SpaceWmsAdoptionDto>> GetLocationsAsync(
        Guid versionId,
        string? status,
        string? differenceCode,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var scope = await LoadScopeAsync(
            versionId,
            write: false,
            cancellationToken);
        limit = NormalizeLimit(limit);
        var parsedStatus = ParseStatus(status);
        var normalizedDifference = NormalizeDifferenceCode(differenceCode);
        var filterHash = Hash(
            $"version={versionId:D}\nstatus={Normalize(status)}" +
            $"\ndifference={Normalize(normalizedDifference)}\nlimit={limit}");
        var offset = ReadOffset(cursor, filterHash);

        var adoptions = await _context.WmsAdoptions
            .AsNoTracking()
            .Where(value => value.SiteId == scope.Model.SiteId)
            .OrderBy(value => value.WmsLocationCode)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);
        var states = await BuildStatesAsync(
            versionId,
            adoptions,
            cancellationToken);
        var filtered = states
            .Where(value =>
                !parsedStatus.HasValue ||
                value.Adoption.Status == parsedStatus.Value)
            .Where(value =>
                normalizedDifference is null ||
                string.Equals(
                    value.DifferenceCode,
                    normalizedDifference,
                    StringComparison.Ordinal))
            .Skip(offset)
            .Take(limit + 1)
            .ToArray();
        var hasMore = filtered.Length > limit;
        var items = filtered
            .Take(limit)
            .Select(ToDto)
            .ToArray();
        var next = hasMore
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    CursorResource,
                    filterHash,
                    checked(offset + limit)))
            : null;
        return new SpacePage<SpaceWmsAdoptionDto>(items, next);
    }

    public Task<SpaceWmsAdoptionCommandResponse> BindAsync(
        Guid versionId,
        Guid adoptionId,
        BindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BindBatchCoreAsync(
            versionId,
            [
                new BatchBindSpaceWmsAdoptionItem(
                    adoptionId,
                    request.LocationLogicalId,
                    request.ExpectedRowVersion),
            ],
            cancellationToken);
    }

    public Task<SpaceWmsAdoptionCommandResponse> BindBatchAsync(
        Guid versionId,
        BatchBindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BindBatchCoreAsync(
            versionId,
            request.Items,
            cancellationToken);
    }

    public async Task<SpaceWmsAdoptionCommandResponse> PlaceAsync(
        Guid versionId,
        Guid adoptionId,
        PlaceSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        var scope = await LoadScopeAsync(
            versionId,
            write: true,
            cancellationToken);
        EnsureEditable(scope.Version);

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        var adoption = await FindAdoptionAsync(
            scope.Model.SiteId,
            adoptionId,
            cancellationToken);
        EnsureExpectedRowVersion(adoption, request.ExpectedRowVersion);
        EnsureBindable(adoption);

        var rack = await _context.RackRevisions.SingleOrDefaultAsync(
                       value =>
                           value.ModelVersionId == versionId &&
                           value.LogicalId == request.RackLogicalId &&
                           value.LifecycleState ==
                           SpaceLifecycleState.Active,
                       cancellationToken)
                   ?? throw NotFound(
                       SpaceErrorCodes.WmsBindingGeometryMissing,
                       "The target rack geometry was not found.");
        var floorExists = await _context.FloorRevisions.AnyAsync(
            value =>
                value.ModelVersionId == versionId &&
                value.LogicalId == request.FloorLogicalId &&
                value.LifecycleState == SpaceLifecycleState.Active,
            cancellationToken);
        if (!floorExists || rack.FloorLogicalId != request.FloorLogicalId)
        {
            throw Conflict(
                SpaceErrorCodes.WmsBindingGeometryMissing,
                "The rack does not belong to the selected floor.",
                "select-valid-rack-cell");
        }

        var level = await _context.RackLevelRevisions.SingleOrDefaultAsync(
                        value =>
                            value.ModelVersionId == versionId &&
                            value.RackLogicalId == request.RackLogicalId &&
                            value.LevelNo == request.Level &&
                            value.LifecycleState ==
                            SpaceLifecycleState.Active,
                        cancellationToken)
                    ?? throw NotFound(
                        SpaceErrorCodes.WmsBindingGeometryMissing,
                        "The target rack level geometry was not found.");
        if (request.Column is < 1 ||
            request.Column > level.BinCount ||
            request.Depth is < 1 ||
            request.Depth > level.DepthCount)
        {
            throw Invalid(
                "cell",
                "The selected column, level, or depth is outside the rack.");
        }

        var cellOccupied = await _context.LocationRevisions.AnyAsync(
            value =>
                value.ModelVersionId == versionId &&
                value.RackLogicalId == request.RackLogicalId &&
                value.ColumnNo == request.Column &&
                value.LevelNo == request.Level &&
                value.DepthNo == request.Depth &&
                value.LifecycleState == SpaceLifecycleState.Active,
            cancellationToken);
        if (cellOccupied)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The selected rack cell already contains a Space location.",
                "select-empty-rack-cell");
        }

        await EnsureCodeAvailableAsync(
            versionId,
            adoption,
            exceptLogicalId: null,
            cancellationToken);
        var logicalIdExists = await _context.LocationRevisions.AnyAsync(
            value =>
                value.ModelVersionId == versionId &&
                value.LogicalId == adoption.WmsLogicalId,
            cancellationToken);
        if (logicalIdExists)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The WMS logical identity already has geometry in this version.",
                "bind-existing-geometry");
        }

        var location = SpaceLocationRevision.Create(
            _execution.TenantId,
            versionId,
            adoption.WmsLogicalId,
            request.FloorLogicalId,
            request.RackLogicalId,
            adoption.WmsLocationCode,
            request.Column,
            request.Level,
            request.Depth,
            level.CellWidth,
            level.ClearHeight,
            level.CellDepth,
            level.MaxLoad,
            SpaceLocationCodeOrigin.Adopted,
            SpaceExternalBindingState.Bound);
        _context.LocationRevisions.Add(location);
        adoption.Bind(versionId, location.LogicalId, RequireUtcNow());
        scope.Version.TouchContent();

        var all = await LoadSiteAdoptionsAsync(
            scope.Model.SiteId,
            cancellationToken);
        await SyncIssuesAsync(
            versionId,
            all,
            Guid.NewGuid(),
            cancellationToken);
        await SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return await BuildCommandResponseAsync(
            scope.Version,
            [adoption.Id],
            cancellationToken);
    }

    private async Task<SpaceWmsAdoptionCommandResponse> BindBatchCoreAsync(
        Guid versionId,
        IReadOnlyList<BatchBindSpaceWmsAdoptionItem> items,
        CancellationToken cancellationToken)
    {
        EnsureExecutionContext();
        if (items is null || items.Count is < 1 or > MaxBatchSize)
        {
            throw Invalid(
                "items",
                $"items must contain between 1 and {MaxBatchSize} bindings.");
        }
        if (items.Select(value => value.AdoptionId).Distinct().Count() !=
            items.Count)
        {
            throw Invalid("items", "Adoption identities must be unique.");
        }
        if (items.Select(value => value.LocationLogicalId).Distinct().Count() !=
            items.Count)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "A Space location cannot be bound more than once in a batch.",
                "select-unique-locations");
        }

        var scope = await LoadScopeAsync(
            versionId,
            write: true,
            cancellationToken);
        EnsureEditable(scope.Version);
        await using var transaction = await BeginTransactionAsync(
            cancellationToken);

        var adoptionIds = items.Select(value => value.AdoptionId).ToArray();
        var adoptions = await _context.WmsAdoptions
            .Where(value =>
                value.SiteId == scope.Model.SiteId &&
                adoptionIds.Contains(value.Id))
            .ToListAsync(cancellationToken);
        if (adoptions.Count != items.Count)
        {
            throw NotFound(
                SpaceErrorCodes.WmsAdoptionNotFound,
                "One or more WMS adoption records were not found.");
        }
        var adoptionById = adoptions.ToDictionary(value => value.Id);
        foreach (var item in items)
        {
            var adoption = adoptionById[item.AdoptionId];
            EnsureExpectedRowVersion(adoption, item.ExpectedRowVersion);
            EnsureBindable(adoption);
        }

        var locationIds = items
            .Select(value => value.LocationLogicalId)
            .ToArray();
        var locations = await _context.LocationRevisions
            .Where(value =>
                value.ModelVersionId == versionId &&
                locationIds.Contains(value.LogicalId) &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .ToListAsync(cancellationToken);
        if (locations.Count != items.Count)
        {
            throw NotFound(
                SpaceErrorCodes.WmsBindingGeometryMissing,
                "One or more target Space locations were not found.");
        }
        var locationById = locations.ToDictionary(value => value.LogicalId);
        var all = await LoadSiteAdoptionsAsync(
            scope.Model.SiteId,
            cancellationToken);
        var requestedAdoptionIds = adoptionIds.ToHashSet();
        foreach (var item in items)
        {
            var adoption = adoptionById[item.AdoptionId];
            var location = locationById[item.LocationLogicalId];
            EnsureUniqueBinding(
                all,
                adoption,
                item.LocationLogicalId,
                requestedAdoptionIds);
            if (location.ExternalBindingState ==
                    SpaceExternalBindingState.Bound &&
                !string.Equals(
                    location.LocationCode,
                    adoption.WmsLocationCode,
                    StringComparison.Ordinal))
            {
                throw Conflict(
                    SpaceErrorCodes.WmsBindingCodeMismatch,
                    "The target geometry is bound to another WMS code.",
                    "select-unbound-geometry");
            }
            await EnsureCodeAvailableAsync(
                versionId,
                adoption,
                location.LogicalId,
                cancellationToken);
        }

        var now = RequireUtcNow();
        foreach (var item in items)
        {
            var adoption = adoptionById[item.AdoptionId];
            var location = locationById[item.LocationLogicalId];
            location.BindAdoptedLocationCode(adoption.WmsLocationCode);
            adoption.Bind(versionId, location.LogicalId, now);
        }
        scope.Version.TouchContent();
        await SyncIssuesAsync(
            versionId,
            all,
            Guid.NewGuid(),
            cancellationToken);
        await SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return await BuildCommandResponseAsync(
            scope.Version,
            adoptionIds,
            cancellationToken);
    }

    private async Task<SpaceWmsAdoptionCommandResponse> BuildCommandResponseAsync(
        SpaceModelVersion version,
        IReadOnlyCollection<Guid> adoptionIds,
        CancellationToken cancellationToken)
    {
        var adoptions = await _context.WmsAdoptions
            .AsNoTracking()
            .Where(value => adoptionIds.Contains(value.Id))
            .OrderBy(value => value.WmsLocationCode)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);
        var states = await BuildStatesAsync(
            version.Id,
            adoptions,
            cancellationToken);
        var issueCounts = await _context.Issues
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == version.Id &&
                DifferenceCodes.Contains(value.Code) &&
                value.Status == SpaceIssueStatus.Open)
            .GroupBy(value => value.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                value => value.Severity,
                value => value.Count,
                cancellationToken);
        return new SpaceWmsAdoptionCommandResponse(
            states.Select(ToDto).ToArray(),
            version.ContentRevision,
            issueCounts.GetValueOrDefault(SpaceIssueSeverity.Warning),
            issueCounts.GetValueOrDefault(SpaceIssueSeverity.Blocking));
    }

    private async Task SyncIssuesAsync(
        Guid versionId,
        IReadOnlyCollection<SpaceWmsAdoption> adoptions,
        Guid commandBatchId,
        CancellationToken cancellationToken)
    {
        var existing = await _context.Issues
            .Where(value =>
                value.ModelVersionId == versionId &&
                DifferenceCodes.Contains(value.Code) &&
                value.Status == SpaceIssueStatus.Open)
            .ToListAsync(cancellationToken);
        var states = await BuildStatesAsync(
            versionId,
            adoptions,
            cancellationToken);
        var desired = states
            .Where(value => value.DifferenceCode is not null)
            .ToDictionary(
                value => new IssueKey(
                    value.DifferenceCode!,
                    value.Adoption.LocationLogicalId ??
                    value.Adoption.WmsLogicalId));
        foreach (var issue in existing)
        {
            var key = new IssueKey(
                issue.Code,
                issue.TargetLogicalId ?? Guid.Empty);
            if (desired.Remove(key))
                continue;
            issue.Resolve(commandBatchId);
        }

        foreach (var state in desired.Values)
        {
            var code = state.DifferenceCode!;
            var severity = code == SpaceErrorCodes.WmsLocationUnbound
                ? SpaceIssueSeverity.Warning
                : SpaceIssueSeverity.Blocking;
            _context.Issues.Add(
                SpaceModelIssue.Create(
                    _execution.TenantId,
                    versionId,
                    sourceId: null,
                    jobId: null,
                    severity,
                    code,
                    sourceRef: state.Adoption.ExternalLocationId,
                    targetLogicalId:
                        state.Adoption.LocationLogicalId ??
                        state.Adoption.WmsLogicalId,
                    messageArgsJson: JsonSerializer.Serialize(
                        new
                        {
                            adoptionId = state.Adoption.Id,
                            wmsLocationCode =
                                state.Adoption.WmsLocationCode,
                        }),
                    suggestedActionCode: SuggestedAction(code)));
        }
    }

    private async Task<IReadOnlyList<AdoptionState>> BuildStatesAsync(
        Guid versionId,
        IReadOnlyCollection<SpaceWmsAdoption> adoptions,
        CancellationToken cancellationToken)
    {
        if (adoptions.Count == 0)
            return [];
        var logicalIds = adoptions
            .Where(value => value.LocationLogicalId.HasValue)
            .Select(value => value.LocationLogicalId!.Value)
            .Distinct()
            .ToArray();
        var persistedLocations = logicalIds.Length == 0
            ? []
            : await _context.LocationRevisions
                .AsNoTracking()
                .Where(value =>
                    value.ModelVersionId == versionId &&
                    logicalIds.Contains(value.LogicalId) &&
                    value.LifecycleState == SpaceLifecycleState.Active)
                .ToListAsync(cancellationToken);
        var trackedLocations = _context.ChangeTracker
            .Entries<SpaceLocationRevision>()
            .Where(entry =>
                entry.State is not (
                    EntityState.Detached or EntityState.Deleted) &&
                entry.Entity.ModelVersionId == versionId &&
                logicalIds.Contains(entry.Entity.LogicalId) &&
                entry.Entity.LifecycleState == SpaceLifecycleState.Active)
            .Select(entry => entry.Entity)
            .ToArray();
        var trackedIds = trackedLocations
            .Select(value => value.LogicalId)
            .ToHashSet();
        var locations = persistedLocations
            .Where(value => !trackedIds.Contains(value.LogicalId))
            .Concat(trackedLocations)
            .ToArray();
        var locationById = locations.ToDictionary(value => value.LogicalId);
        var duplicateCodes = adoptions
            .Where(value =>
                value.Status != SpaceWmsAdoptionStatus.MissingInWms &&
                value.WmsIsActive)
            .GroupBy(
                value => new
                {
                    value.AdapterId,
                    value.WmsLocationCode,
                })
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key.AdapterId}\n{group.Key.WmsLocationCode}")
            .ToHashSet(StringComparer.Ordinal);

        return adoptions
            .OrderBy(value => value.WmsLocationCode)
            .ThenBy(value => value.Id)
            .Select(adoption =>
            {
                locationById.TryGetValue(
                    adoption.LocationLogicalId ?? Guid.Empty,
                    out var location);
                return new AdoptionState(
                    adoption,
                    location?.LocationCode,
                    location is not null,
                    DifferenceCode(adoption, location, duplicateCodes));
            })
            .ToArray();
    }

    private static string? DifferenceCode(
        SpaceWmsAdoption adoption,
        SpaceLocationRevision? location,
        IReadOnlySet<string> duplicateCodes)
    {
        if (adoption.Status == SpaceWmsAdoptionStatus.MissingInWms ||
            !adoption.WmsIsActive)
            return SpaceErrorCodes.WmsLocationMissing;
        if (duplicateCodes.Contains(
                $"{adoption.AdapterId}\n{adoption.WmsLocationCode}"))
            return SpaceErrorCodes.WmsLocationCodeDuplicate;
        if (!adoption.LocationLogicalId.HasValue)
            return SpaceErrorCodes.WmsLocationUnbound;
        if (location is null)
            return SpaceErrorCodes.WmsBindingGeometryMissing;
        if (adoption.Status == SpaceWmsAdoptionStatus.Diverged ||
            !string.Equals(
                adoption.WmsLocationCode,
                adoption.BoundLocationCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                adoption.WmsLocationCode,
                location.LocationCode,
                StringComparison.Ordinal))
        {
            return SpaceErrorCodes.WmsBindingCodeMismatch;
        }
        return null;
    }

    private static SpaceWmsAdoptionDto ToDto(AdoptionState state) =>
        new(
            state.Adoption.Id,
            state.Adoption.SiteId,
            state.Adoption.AdapterId,
            state.Adoption.DataSource,
            state.Adoption.DataSourceKind,
            state.Adoption.WmsLogicalId,
            state.Adoption.ExternalLocationId,
            state.Adoption.WmsLocationCode,
            state.Adoption.WmsIsActive,
            state.Adoption.ExternalVersion,
            state.Adoption.WmsStateHash,
            state.Adoption.LastObservedAtUtc,
            state.Adoption.Status.ToString(),
            state.Adoption.ModelVersionId,
            state.Adoption.LocationLogicalId,
            state.SpaceLocationCode,
            state.HasGeometry,
            state.DifferenceCode,
            state.Adoption.BoundAtUtc,
            Convert.ToBase64String(state.Adoption.RowVersion ?? []));

    private async Task<IReadOnlyList<SpaceWmsAdoption>> LoadSiteAdoptionsAsync(
        Guid siteId,
        CancellationToken cancellationToken) =>
        await _context.WmsAdoptions
            .Where(value => value.SiteId == siteId)
            .OrderBy(value => value.WmsLocationCode)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);

    private async Task<SpaceWmsAdoption> FindAdoptionAsync(
        Guid siteId,
        Guid adoptionId,
        CancellationToken cancellationToken) =>
        await _context.WmsAdoptions.SingleOrDefaultAsync(
            value => value.Id == adoptionId && value.SiteId == siteId,
            cancellationToken)
        ?? throw NotFound(
            SpaceErrorCodes.WmsAdoptionNotFound,
            "The WMS adoption record was not found.");

    private async Task EnsureCodeAvailableAsync(
        Guid versionId,
        SpaceWmsAdoption adoption,
        Guid? exceptLogicalId,
        CancellationToken cancellationToken)
    {
        var duplicateWmsCode = await _context.WmsAdoptions.AnyAsync(
            value =>
                value.SiteId == adoption.SiteId &&
                value.AdapterId == adoption.AdapterId &&
                value.Id != adoption.Id &&
                value.Status != SpaceWmsAdoptionStatus.MissingInWms &&
                value.WmsIsActive &&
                value.WmsLocationCode == adoption.WmsLocationCode,
            cancellationToken);
        if (duplicateWmsCode)
        {
            throw Conflict(
                SpaceErrorCodes.WmsLocationCodeDuplicate,
                "The WMS catalog contains a duplicate active location code.",
                "resolve-wms-location-code");
        }

        var duplicateSpaceCode = await _context.LocationRevisions.AnyAsync(
            value =>
                value.ModelVersionId == versionId &&
                value.LogicalId != exceptLogicalId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                value.LocationCode == adoption.WmsLocationCode,
            cancellationToken);
        if (duplicateSpaceCode)
        {
            throw Conflict(
                SpaceErrorCodes.WmsLocationCodeDuplicate,
                "Another Space location already uses the WMS location code.",
                "select-matching-geometry");
        }
    }

    private static void EnsureUniqueBinding(
        IReadOnlyCollection<SpaceWmsAdoption> all,
        SpaceWmsAdoption adoption,
        Guid locationLogicalId,
        IReadOnlySet<Guid> requestedAdoptionIds)
    {
        if (adoption.LocationLogicalId.HasValue &&
            adoption.LocationLogicalId != locationLogicalId)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The WMS object is already bound to another Space location.",
                "use-existing-binding");
        }
        var duplicate = all.Any(value =>
            value.Id != adoption.Id &&
            value.AdapterId == adoption.AdapterId &&
            value.LocationLogicalId == locationLogicalId &&
            (!requestedAdoptionIds.Contains(value.Id) ||
             value.Status != SpaceWmsAdoptionStatus.MissingInWms));
        if (duplicate)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The Space location is already bound to another WMS object.",
                "select-unbound-geometry");
        }
    }

    private static void EnsureBindable(SpaceWmsAdoption adoption)
    {
        if (adoption.Status == SpaceWmsAdoptionStatus.MissingInWms ||
            !adoption.WmsIsActive)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionMissing,
                "A missing or inactive WMS object cannot be bound.",
                "refresh-wms-catalog");
        }
    }

    private void EnsureExpectedRowVersion(
        SpaceWmsAdoption adoption,
        string expectedRowVersion)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedRowVersion ?? string.Empty);
        }
        catch (FormatException)
        {
            throw Invalid(
                "expectedRowVersion",
                "expectedRowVersion must be a base64 rowversion.");
        }
        if (!_context.Database.IsRelational() &&
            expected.Length == 0 &&
            (adoption.RowVersion?.Length ?? 0) == 0)
        {
            return;
        }
        if (expected.Length == 0 ||
            !expected.AsSpan().SequenceEqual(adoption.RowVersion))
        {
            throw Conflict(
                SpaceErrorCodes.ConcurrencyConflict,
                "The WMS adoption record changed after it was loaded.",
                "reload-resource");
        }
    }

    private static void EnsureCatalogIdentity(
        IReadOnlyCollection<SpaceWmsLocationState> items)
    {
        if (items.Any(value =>
                value.LogicalId == Guid.Empty ||
                string.IsNullOrWhiteSpace(value.LocationCode) ||
                value.LocationCode.Trim().Length > 200 ||
                string.IsNullOrWhiteSpace(value.ExternalVersion) ||
                value.ExternalVersion.Trim().Length > 100 ||
                value.StateHash is null ||
                value.StateHash.Length != 64 ||
                value.StateHash.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.WmsUnavailable,
                502,
                "The WMS location catalog violated the adapter contract.",
                recoveryAction: "repair-wms-adapter");
        }
        if (items.GroupBy(value => value.LogicalId).Any(group => group.Count() > 1))
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The WMS catalog returned duplicate logical identities.",
                "repair-wms-catalog");
        }
        if (items
            .Where(value => !string.IsNullOrWhiteSpace(value.ExternalLocationId))
            .GroupBy(
                value => value.ExternalLocationId!,
                StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The WMS catalog returned duplicate external identities.",
                "repair-wms-catalog");
        }
    }

    private async Task<Scope> LoadScopeAsync(
        Guid versionId,
        bool write,
        CancellationToken cancellationToken)
    {
        var result = await (
                from version in _context.Versions
                join model in _context.Models on version.ModelId equals model.Id
                where version.Id == versionId
                select new Scope(version, model))
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
        {
            throw NotFound(
                SpaceErrorCodes.VersionNotFound,
                "The Space version was not found.");
        }
        if (result.Model.Mode != SpaceModelMode.DesignV1 ||
            result.Model.CutoverState != SpaceModelCutoverState.DesignV1)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DesignApiDisabled,
                404,
                "The Design API is not enabled for this Site.",
                recoveryAction: "use-legacy-api");
        }
        _access.EnsureSiteAccess(result.Model.SiteId, write);
        return result;
    }

    private static void EnsureEditable(SpaceModelVersion version)
    {
        if (version.Status != SpaceVersionStatus.Draft)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                $"Version state {version.Status} is not editable.",
                "select-editable-version");
        }
    }

    private async Task<SpaceWarehouseIdentity> ResolveWarehouseAsync(
        Guid siteId,
        CancellationToken cancellationToken) =>
        await _warehouses.ResolveAsync(siteId, cancellationToken)
        ?? throw NotFound(
            SpaceErrorCodes.ModelNotFound,
            "The CP6 runtime site was not found.");

    private SpaceWmsContext CreateWmsContext(
        Guid siteId,
        SpaceWarehouseIdentity warehouse) =>
        new(
            _execution.TenantId,
            siteId,
            warehouse.WarehouseCode,
            _execution is ISpaceCorrelationContext correlation &&
            correlation.CorrelationId != Guid.Empty
                ? correlation.CorrelationId
                : Guid.NewGuid());

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict(
                SpaceErrorCodes.ConcurrencyConflict,
                "A WMS adoption record changed during the operation.",
                "reload-resource");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException?.Message.Contains(
                "UX_Space_WmsAdoption",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            throw Conflict(
                SpaceErrorCodes.WmsAdoptionDuplicate,
                "The WMS adoption identity or geometry binding is duplicated.",
                "refresh-and-retry");
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
            return null;
        return await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

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

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(cursor, CursorResource, filterHash);
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

    private static int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return DefaultPageSize;
        if (limit is < 1 or > MaxPageSize)
            throw Invalid("limit", $"limit must be between 1 and {MaxPageSize}.");
        return limit;
    }

    private static SpaceWmsAdoptionStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;
        if (!Enum.TryParse<SpaceWmsAdoptionStatus>(
                status.Trim(),
                ignoreCase: true,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw Invalid("status", $"'{status}' is not a supported status.");
        }
        return parsed;
    }

    private static string? NormalizeDifferenceCode(string? differenceCode)
    {
        if (string.IsNullOrWhiteSpace(differenceCode))
            return null;
        var normalized = differenceCode.Trim().ToUpperInvariant();
        if (!DifferenceCodes.Contains(normalized))
        {
            throw Invalid(
                "differenceCode",
                $"'{differenceCode}' is not a supported difference code.");
        }
        return normalized;
    }

    private static string SuggestedAction(string code) =>
        code switch
        {
            SpaceErrorCodes.WmsLocationUnbound => "bind-wms-location",
            SpaceErrorCodes.WmsLocationCodeDuplicate =>
                "resolve-wms-location-code",
            SpaceErrorCodes.WmsBindingGeometryMissing =>
                "place-wms-location",
            SpaceErrorCodes.WmsBindingCodeMismatch =>
                "reconcile-wms-binding",
            SpaceErrorCodes.WmsLocationMissing => "refresh-wms-catalog",
            _ => "review-wms-adoption",
        };

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

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
        string detail) =>
        new(
            code,
            404,
            detail,
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

    private sealed record Scope(
        SpaceModelVersion Version,
        SpaceModel Model);

    private sealed record AdoptionState(
        SpaceWmsAdoption Adoption,
        string? SpaceLocationCode,
        bool HasGeometry,
        string? DifferenceCode);

    private sealed record IssueKey(
        string Code,
        Guid TargetLogicalId);
}

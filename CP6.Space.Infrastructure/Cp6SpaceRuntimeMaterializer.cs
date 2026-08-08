using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Space;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceRuntimeMaterializer : ISpaceRuntimeMaterializer
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _space;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;

    public Cp6SpaceRuntimeMaterializer(
        SpaceContext space,
        ISpaceExecutionContext execution,
        ISpaceClock clock)
    {
        _space = space;
        _execution = execution;
        _clock = clock;
    }

    public async Task<SpaceRuntimeActivationResult> ActivateAsync(
        SpaceRuntimeActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await using var transaction =
            await _space.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var model = await _space.Models.SingleOrDefaultAsync(
                        value => value.SiteId == request.SiteId,
                        cancellationToken)
                    ?? throw NotFound(
                        SpaceErrorCodes.ModelNotFound,
                        "The model to activate was not found.");
        var target = await _space.Versions.SingleOrDefaultAsync(
                         value => value.Id == request.TargetVersionId,
                         cancellationToken)
                     ?? throw NotFound(
                         SpaceErrorCodes.VersionNotFound,
                         "The version to activate was not found.");
        var attempt = await _space.PublishAttempts
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              value => value.Id == request.AttemptId,
                              cancellationToken)
                      ?? throw new SpaceTenantScopeException(
                          "The runtime activation attempt is unavailable in the verified tenant scope.");
        var plan = await _space.PublishPlans
                       .AsNoTracking()
                       .SingleOrDefaultAsync(
                           value => value.Id == attempt.PublishPlanId,
                           cancellationToken)
                   ?? throw new SpaceTenantScopeException(
                       "The runtime activation plan is unavailable in the verified tenant scope.");
        if (attempt.SiteId != request.SiteId ||
            attempt.TargetVersionId != request.TargetVersionId ||
            attempt.BaseVersionId != request.BaseVersionId ||
            attempt.RequestedBy != request.ActorId ||
            attempt.Status != SpacePublishAttemptStatus.ActivatingRuntime ||
            attempt.CurrentStep != SpacePublishStep.ActivateRuntime ||
            plan.SiteId != request.SiteId ||
            plan.TargetVersionId != request.TargetVersionId ||
            plan.BaseVersionId != request.BaseVersionId ||
            !string.Equals(
                plan.PlanHash,
                request.PlanHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceTenantScopeException(
                "The runtime activation request does not match its persisted publish evidence.");
        }
        if (model.CurrentPublishedVersionId != request.BaseVersionId)
        {
            throw Conflict(
                SpaceErrorCodes.PublishedVersionChanged,
                "The published version changed before runtime activation.");
        }
        if (target.ModelId != model.Id ||
            target.Status != SpaceVersionStatus.Publishing)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "Only the publishing version for this model can activate.");
        }

        SpaceModelVersion? previous = null;
        if (request.BaseVersionId.HasValue)
        {
            previous = await _space.Versions.SingleOrDefaultAsync(
                           value =>
                               value.Id == request.BaseVersionId.Value,
                           cancellationToken)
                       ?? throw Conflict(
                           SpaceErrorCodes.PublishedVersionChanged,
                           "The base version no longer exists.");
            if (previous.Status != SpaceVersionStatus.Published)
            {
                throw Conflict(
                    SpaceErrorCodes.PublishedVersionChanged,
                    "The base version is no longer Published.");
            }
        }

        var snapshot = await LoadSnapshotAsync(
            target.Id,
            cancellationToken);
        var connection = _space.Database.GetDbConnection();
        var cp6Options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connection)
            .Options;
        var tenant = new TenantContext
        {
            CurrentTenantId = _execution.TenantId,
        };
        await using var runtime = new CP6Context(cp6Options, tenant);
        await runtime.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);

        var projection = await MaterializeCp6Async(
            runtime,
            model.SiteId,
            target,
            snapshot,
            cancellationToken);
        var expectedHash = ComputeHash(projection);
        var actualProjection = await ReadProjectionAsync(
            runtime,
            snapshot,
            cancellationToken);
        var actualHash = ComputeHash(actualProjection);
        if (!string.Equals(
                expectedHash,
                actualHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SPACE_RUNTIME_MATERIALIZED_HASH_MISMATCH");
        }

        await MaterializeElementsAsync(
            model.SiteId,
            target.Id,
            snapshot.Elements,
            cancellationToken);

        var materializedHash = Hash(
            string.Join(
                "\n",
                request.PlanHash.ToLowerInvariant(),
                actualHash,
                ComputeElementHash(snapshot.Elements)));
        previous?.MarkSuperseded();
        target.MarkPublished(request.ActorId, RequireUtcNow());
        model.SetPublishedVersion(target, materializedHash);
        await _space.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SpaceRuntimeActivationResult(
            materializedHash,
            snapshot.Floors.Length,
            snapshot.Zones.Length,
            snapshot.Aisles.Length,
            snapshot.Racks.Length,
            snapshot.Locations.Length,
            snapshot.Elements.Length);
    }

    private async Task<IReadOnlyList<RuntimeProjection>> MaterializeCp6Async(
        CP6Context runtime,
        Guid siteId,
        SpaceModelVersion target,
        RuntimeSnapshot source,
        CancellationToken cancellationToken)
    {
        var siteExists = await runtime.Space_Sites
            .AnyAsync(value => value.Id == siteId, cancellationToken);
        if (!siteExists)
        {
            throw NotFound(
                SpaceErrorCodes.ModelNotFound,
                "The CP6 runtime site was not found.");
        }

        var floorIds = source.Floors.Select(value => value.LogicalId).ToArray();
        var zoneIds = source.Zones.Select(value => value.LogicalId).ToArray();
        var aisleIds = source.Aisles.Select(value => value.LogicalId).ToArray();
        var rackIds = source.Racks.Select(value => value.LogicalId).ToArray();
        var locationIds =
            source.Locations.Select(value => value.LogicalId).ToArray();

        var floors = await runtime.Space_Floors
            .Where(value => floorIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var zones = await runtime.Space_Zones
            .Where(value => zoneIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var aisles = await runtime.Space_Aisles
            .Where(value => aisleIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var racks = await runtime.Space_Racks
            .Where(value => rackIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var locations = await runtime.Space_Locations
            .Where(value => locationIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        TemporarilyReleaseUniqueCodes(
            source,
            floors,
            zones,
            aisles,
            racks,
            locations);
        if (runtime.ChangeTracker.HasChanges())
            await runtime.SaveChangesAsync(cancellationToken);

        foreach (var value in source.Floors)
        {
            var row = GetOrAdd(
                floors,
                value.LogicalId,
                () => new Space_Floor { Id = value.LogicalId },
                runtime.Space_Floors);
            row.SiteId = siteId;
            row.Level = value.Level;
            row.FloorCode = Fit(value.FloorCode, 50, "FloorCode");
            row.FloorName = Fit(value.Name, 100, "FloorName");
            row.Height = value.Height;
            row.UnderlayScale = value.UnderlayScale is null
                ? null
                : decimal.ToDouble(value.UnderlayScale.Value);
            row.UnderlayOffsetX = value.UnderlayOffsetX;
            row.UnderlayOffsetY = value.UnderlayOffsetY;
        }

        foreach (var value in source.Zones)
        {
            var row = GetOrAdd(
                zones,
                value.LogicalId,
                () => new Space_Zone { Id = value.LogicalId },
                runtime.Space_Zones);
            row.FloorId = value.FloorLogicalId;
            row.ZoneCode = Fit(value.ZoneCode, 50, "ZoneCode");
            row.ZoneName = Fit(value.ZoneCode, 100, "ZoneName");
            row.ZoneType = value.ZoneType;
            row.Polygon = value.PolygonJson;
            row.Color = FitOptional(value.Color, 20, "Zone.Color");
            row.Enable = value.LifecycleState == SpaceLifecycleState.Active;
        }

        foreach (var value in source.Aisles)
        {
            var row = GetOrAdd(
                aisles,
                value.LogicalId,
                () => new Space_Aisle { Id = value.LogicalId },
                runtime.Space_Aisles);
            row.ZoneId = value.ZoneLogicalId;
            row.AisleCode = Fit(value.AisleCode, 50, "AisleCode");
            row.Polygon = value.PolygonJson;
            row.Centerline = value.CenterlineJson;
        }

        var levels = source.Levels
            .GroupBy(value => value.RackLogicalId)
            .ToDictionary(value => value.Key, value => value.ToArray());
        foreach (var value in source.Racks)
        {
            var row = GetOrAdd(
                racks,
                value.LogicalId,
                () => new Space_Rack { Id = value.LogicalId },
                runtime.Space_Racks);
            var rackLevels =
                levels.GetValueOrDefault(value.LogicalId) ?? [];
            row.ZoneId = value.ZoneLogicalId;
            row.AisleId = value.AisleLogicalId;
            row.FloorId = value.FloorLogicalId;
            row.TemplateId = null;
            row.RackCode = Fit(value.RackCode, 50, "RackCode");
            row.X = value.X;
            row.Y = value.Y;
            row.Z = value.Z;
            row.RotationZ = decimal.ToDouble(value.RotationZ);
            row.Cols = rackLevels.Length == 0
                ? 1
                : rackLevels.Max(level => level.BinCount);
            row.Levels = rackLevels.Length == 0
                ? 1
                : rackLevels.Max(level => level.LevelNo);
            row.DepthCount = rackLevels.Length == 0
                ? 1
                : rackLevels.Max(level => level.DepthCount);
            row.CellW = rackLevels.FirstOrDefault()?.CellWidth ??
                        Math.Max(1, value.Width);
            row.CellH = rackLevels.FirstOrDefault()?.ClearHeight ??
                        Math.Max(1, value.Height);
            row.CellD = rackLevels.FirstOrDefault()?.CellDepth ??
                        Math.Max(1, value.Depth);
            row.Enable = value.LifecycleState == SpaceLifecycleState.Active;
        }

        var rackLookup = source.Racks.ToDictionary(
            value => value.LogicalId);
        var levelLookup = source.Levels
            .GroupBy(value => (value.RackLogicalId, value.LevelNo))
            .ToDictionary(value => value.Key, value => value.First());
        foreach (var value in source.Locations)
        {
            var row = GetOrAdd(
                locations,
                value.LogicalId,
                () => new Space_Location { Id = value.LogicalId },
                runtime.Space_Locations);
            row.RackId = value.RackLogicalId;
            row.FloorId = value.FloorLogicalId;
            row.LocationCode = Fit(
                value.LocationCode ??
                throw new InvalidOperationException(
                    "A published location requires a code."),
                100,
                "LocationCode");
            row.CodeOrigin =
                value.CodeOrigin == SpaceLocationCodeOrigin.Generated
                    ? 1
                    : 2;
            row.Col = value.ColumnNo;
            row.Level = value.LevelNo;
            row.Depth = value.DepthNo;
            var absolute = Coordinates(
                value,
                rackLookup,
                levelLookup);
            row.AbsX = absolute.X;
            row.AbsY = absolute.Y;
            row.AbsZ = absolute.Z;
            row.SizeW = value.Width;
            row.SizeH = value.Height;
            row.SizeD = value.Depth;
            row.LoadLimit = value.MaxLoad.HasValue
                ? checked((int)decimal.Round(value.MaxLoad.Value))
                : null;
            row.Placed = value.RackLogicalId.HasValue;
            row.Status =
                value.LifecycleState == SpaceLifecycleState.Active ? 1 : 2;
            row.Version = target.VersionNo;
        }

        await DisableMissingRuntimeRowsAsync(
            runtime,
            siteId,
            source,
            cancellationToken);
        await runtime.SaveChangesAsync(cancellationToken);
        return Project(floors, zones, aisles, racks, locations);
    }

    private static void TemporarilyReleaseUniqueCodes(
        RuntimeSnapshot source,
        IReadOnlyDictionary<Guid, Space_Floor> floors,
        IReadOnlyDictionary<Guid, Space_Zone> zones,
        IReadOnlyDictionary<Guid, Space_Aisle> aisles,
        IReadOnlyDictionary<Guid, Space_Rack> racks,
        IReadOnlyDictionary<Guid, Space_Location> locations)
    {
        foreach (var value in source.Floors)
        {
            if (floors.TryGetValue(value.LogicalId, out var row) &&
                !string.Equals(
                    row.FloorCode,
                    value.FloorCode,
                    StringComparison.Ordinal))
            {
                row.FloorCode = TemporaryCode(row.Id, 50);
            }
        }
        foreach (var value in source.Zones)
        {
            if (zones.TryGetValue(value.LogicalId, out var row) &&
                !string.Equals(
                    row.ZoneCode,
                    value.ZoneCode,
                    StringComparison.Ordinal))
            {
                row.ZoneCode = TemporaryCode(row.Id, 50);
            }
        }
        foreach (var value in source.Aisles)
        {
            if (aisles.TryGetValue(value.LogicalId, out var row) &&
                !string.Equals(
                    row.AisleCode,
                    value.AisleCode,
                    StringComparison.Ordinal))
            {
                row.AisleCode = TemporaryCode(row.Id, 50);
            }
        }
        foreach (var value in source.Racks)
        {
            if (racks.TryGetValue(value.LogicalId, out var row) &&
                !string.Equals(
                    row.RackCode,
                    value.RackCode,
                    StringComparison.Ordinal))
            {
                row.RackCode = TemporaryCode(row.Id, 50);
            }
        }
        foreach (var value in source.Locations)
        {
            if (locations.TryGetValue(value.LogicalId, out var row) &&
                !string.Equals(
                    row.LocationCode,
                    value.LocationCode,
                    StringComparison.Ordinal))
            {
                row.LocationCode = TemporaryCode(row.Id, 100);
            }
        }
    }

    private static async Task DisableMissingRuntimeRowsAsync(
        CP6Context runtime,
        Guid siteId,
        RuntimeSnapshot source,
        CancellationToken cancellationToken)
    {
        var activeFloorIds = source.Floors
            .Where(value =>
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => value.LogicalId)
            .ToHashSet();
        var activeZoneIds = source.Zones
            .Where(value =>
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => value.LogicalId)
            .ToHashSet();
        var activeRackIds = source.Racks
            .Where(value =>
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => value.LogicalId)
            .ToHashSet();
        var activeLocationIds = source.Locations
            .Where(value =>
                value.LifecycleState == SpaceLifecycleState.Active)
            .Select(value => value.LogicalId)
            .ToHashSet();

        var siteFloorIds = await runtime.Space_Floors
            .Where(value => value.SiteId == siteId)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var staleZones = await runtime.Space_Zones
            .Where(value =>
                siteFloorIds.Contains(value.FloorId) &&
                !activeZoneIds.Contains(value.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var value in staleZones)
            value.Enable = false;

        var staleRacks = await runtime.Space_Racks
            .Where(value =>
                siteFloorIds.Contains(value.FloorId) &&
                !activeRackIds.Contains(value.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var value in staleRacks)
            value.Enable = false;

        var staleLocations = await runtime.Space_Locations
            .Where(value =>
                value.FloorId.HasValue &&
                siteFloorIds.Contains(value.FloorId.Value) &&
                !activeLocationIds.Contains(value.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var value in staleLocations)
            value.Status = 2;

        _ = activeFloorIds;
    }

    private async Task MaterializeElementsAsync(
        Guid siteId,
        Guid targetVersionId,
        IReadOnlyList<SpaceElementRevision> source,
        CancellationToken cancellationToken)
    {
        var ids = source.Select(value => value.LogicalId).ToArray();
        var existing = await _space.RuntimeElements
            .Where(value =>
                value.SiteId == siteId &&
                ids.Contains(value.LogicalId))
            .ToDictionaryAsync(value => value.LogicalId, cancellationToken);
        foreach (var value in source)
        {
            var payload = ElementPayload(value);
            var payloadHash = Hash(payload);
            if (existing.TryGetValue(value.LogicalId, out var row))
            {
                row.Update(
                    siteId,
                    targetVersionId,
                    value.LogicalId,
                    value.FloorLogicalId,
                    value.LifecycleState == SpaceLifecycleState.Active,
                    payload,
                    payloadHash);
            }
            else
            {
                _space.RuntimeElements.Add(
                    SpaceRuntimeElement.Create(
                        _execution.TenantId,
                        siteId,
                        targetVersionId,
                        value.LogicalId,
                        value.FloorLogicalId,
                        value.LifecycleState ==
                        SpaceLifecycleState.Active,
                        payload,
                        payloadHash));
            }
        }

        var stale = await _space.RuntimeElements
            .Where(value =>
                value.SiteId == siteId &&
                !ids.Contains(value.LogicalId) &&
                value.IsActive)
            .ToArrayAsync(cancellationToken);
        foreach (var value in stale)
            value.Deactivate(targetVersionId);
    }

    private async Task<RuntimeSnapshot> LoadSnapshotAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        return new RuntimeSnapshot(
            await _space.FloorRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.ZoneRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.AisleRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.RackRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.RackLevelRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.LocationRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken),
            await _space.ElementRevisions
                .Where(value => value.ModelVersionId == versionId)
                .OrderBy(value => value.LogicalId)
                .ToArrayAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<RuntimeProjection>>
        ReadProjectionAsync(
            CP6Context runtime,
            RuntimeSnapshot source,
            CancellationToken cancellationToken)
    {
        var floorIds = source.Floors.Select(value => value.LogicalId).ToArray();
        var zoneIds = source.Zones.Select(value => value.LogicalId).ToArray();
        var aisleIds = source.Aisles.Select(value => value.LogicalId).ToArray();
        var rackIds = source.Racks.Select(value => value.LogicalId).ToArray();
        var locationIds =
            source.Locations.Select(value => value.LogicalId).ToArray();
        var floors = await runtime.Space_Floors.AsNoTracking()
            .Where(value => floorIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var zones = await runtime.Space_Zones.AsNoTracking()
            .Where(value => zoneIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var aisles = await runtime.Space_Aisles.AsNoTracking()
            .Where(value => aisleIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var racks = await runtime.Space_Racks.AsNoTracking()
            .Where(value => rackIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var locations = await runtime.Space_Locations.AsNoTracking()
            .Where(value => locationIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        return Project(floors, zones, aisles, racks, locations);
    }

    private static IReadOnlyList<RuntimeProjection> Project(
        IReadOnlyDictionary<Guid, Space_Floor> floors,
        IReadOnlyDictionary<Guid, Space_Zone> zones,
        IReadOnlyDictionary<Guid, Space_Aisle> aisles,
        IReadOnlyDictionary<Guid, Space_Rack> racks,
        IReadOnlyDictionary<Guid, Space_Location> locations)
    {
        var values = new List<RuntimeProjection>();
        values.AddRange(floors.Values.Select(value => new RuntimeProjection(
            "Floor",
            value.Id,
            Serialize(new
            {
                value.SiteId,
                value.Level,
                value.FloorCode,
                value.FloorName,
                value.Height,
                value.UnderlayScale,
                value.UnderlayOffsetX,
                value.UnderlayOffsetY,
            }))));
        values.AddRange(zones.Values.Select(value => new RuntimeProjection(
            "Zone",
            value.Id,
            Serialize(new
            {
                value.FloorId,
                value.ZoneCode,
                value.ZoneName,
                value.ZoneType,
                value.Polygon,
                value.Color,
                value.Enable,
            }))));
        values.AddRange(aisles.Values.Select(value => new RuntimeProjection(
            "Aisle",
            value.Id,
            Serialize(new
            {
                value.ZoneId,
                value.AisleCode,
                value.Polygon,
                value.Centerline,
            }))));
        values.AddRange(racks.Values.Select(value => new RuntimeProjection(
            "Rack",
            value.Id,
            Serialize(new
            {
                value.ZoneId,
                value.AisleId,
                value.FloorId,
                value.RackCode,
                value.X,
                value.Y,
                value.Z,
                value.RotationZ,
                value.Cols,
                value.Levels,
                value.DepthCount,
                value.CellW,
                value.CellH,
                value.CellD,
                value.Enable,
            }))));
        values.AddRange(locations.Values.Select(value =>
            new RuntimeProjection(
                "Location",
                value.Id,
                Serialize(new
                {
                    value.RackId,
                    value.FloorId,
                    value.LocationCode,
                    value.CodeOrigin,
                    value.Col,
                    value.Level,
                    value.Depth,
                    value.AbsX,
                    value.AbsY,
                    value.AbsZ,
                    value.SizeW,
                    value.SizeH,
                    value.SizeD,
                    value.LoadLimit,
                    value.Placed,
                    value.Status,
                    value.Version,
                }))));
        return values
            .OrderBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Id)
            .ToArray();
    }

    private static (int X, int Y, int Z) Coordinates(
        SpaceLocationRevision location,
        IReadOnlyDictionary<Guid, SpaceRackRevision> racks,
        IReadOnlyDictionary<(Guid RackId, int LevelNo),
            SpaceRackLevelRevision> levels)
    {
        if (!location.RackLogicalId.HasValue ||
            !racks.TryGetValue(
                location.RackLogicalId.Value,
                out var rack))
        {
            return (0, 0, 0);
        }

        var localX = checked((location.ColumnNo - 1) * location.Width);
        var localY = checked((location.DepthNo - 1) * location.Depth);
        var radians = decimal.ToDouble(rack.RotationZ) * Math.PI / 180d;
        var x = rack.X +
                (int)Math.Round(
                    localX * Math.Cos(radians) -
                    localY * Math.Sin(radians));
        var y = rack.Y +
                (int)Math.Round(
                    localX * Math.Sin(radians) +
                    localY * Math.Cos(radians));
        var z = rack.Z +
                (levels.TryGetValue(
                    (rack.LogicalId, location.LevelNo),
                    out var level)
                    ? level.BottomZ
                    : checked(
                        (location.LevelNo - 1) * location.Height));
        return (x, y, z);
    }

    private static T GetOrAdd<T>(
        IDictionary<Guid, T> values,
        Guid id,
        Func<T> factory,
        DbSet<T> set)
        where T : class
    {
        if (values.TryGetValue(id, out var existing))
            return existing;
        var created = factory();
        values.Add(id, created);
        set.Add(created);
        return created;
    }

    private static string TemporaryCode(Guid id, int maximumLength)
    {
        var value = $"__sp_{id:N}";
        return value.Length <= maximumLength
            ? value
            : value[..maximumLength];
    }

    private static string Fit(
        string value,
        int maximumLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{field} cannot be materialized into the CP6 runtime.");
        }
        return value;
    }

    private static string? FitOptional(
        string? value,
        int maximumLength,
        string field)
    {
        if (value is null)
            return null;
        if (value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{field} cannot be materialized into the CP6 runtime.");
        }
        return value;
    }

    private static string ComputeHash(
        IEnumerable<RuntimeProjection> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values
                     .OrderBy(value => value.Type, StringComparer.Ordinal)
                     .ThenBy(value => value.Id))
        {
            builder
                .Append(value.Type).Append('|')
                .Append(value.Id.ToString("D")).Append('|')
                .Append(value.Payload).Append('\n');
        }
        return Hash(builder.ToString());
    }

    private static string ComputeElementHash(
        IEnumerable<SpaceElementRevision> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values.OrderBy(value => value.LogicalId))
        {
            builder
                .Append(value.LogicalId.ToString("D")).Append('|')
                .Append(Hash(ElementPayload(value))).Append('\n');
        }
        return Hash(builder.ToString());
    }

    private static string ElementPayload(SpaceElementRevision value) =>
        Serialize(new
        {
            value.FloorLogicalId,
            value.ParentLogicalId,
            value.ElementType,
            value.GeometryJson,
            value.ModelAssetId,
            value.ModelAssetScope,
            value.ModelAssetOwnerTenantId,
            value.X,
            value.Y,
            value.Z,
            value.RotationZ,
            value.Width,
            value.Height,
            value.Depth,
            value.BusinessCode,
            value.LinkedEntityType,
            value.LinkedLogicalId,
        });

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, Json);

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Space clock must return UTC.");
        return now;
    }

    private void ValidateRequest(SpaceRuntimeActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty ||
            request.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified tenant and actor are required.");
        }
        if (request.AttemptId == Guid.Empty ||
            request.SiteId == Guid.Empty ||
            request.TargetVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Runtime activation identities are required.",
                nameof(request));
        }
        if (request.BaseVersionId == Guid.Empty)
            throw new ArgumentException(
                "Base version identity cannot be empty.",
                nameof(request));
        _ = SpaceWmsContract.RequireSha256(
            request.PlanHash,
            nameof(request.PlanHash));
    }

    private static SpaceProblemException NotFound(
        string code,
        string detail) =>
        new(
            code,
            404,
            "The requested Space resource was not found.",
            detail,
            "refresh-resource");

    private static SpaceProblemException Conflict(
        string code,
        string detail) =>
        new(
            code,
            409,
            "The runtime activation precondition changed.",
            detail,
            "refresh-publish-preview");

    private sealed record RuntimeSnapshot(
        SpaceFloorRevision[] Floors,
        SpaceZoneRevision[] Zones,
        SpaceAisleRevision[] Aisles,
        SpaceRackRevision[] Racks,
        SpaceRackLevelRevision[] Levels,
        SpaceLocationRevision[] Locations,
        SpaceElementRevision[] Elements);

    private sealed record RuntimeProjection(
        string Type,
        Guid Id,
        string Payload);
}

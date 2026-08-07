using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePublishPreviewService : ISpacePublishPreviewService
{
    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceValidationProfileProvider _profiles;
    private readonly SpaceValidationEngine _validationEngine;
    private readonly SpacePublishPlanEngine _planEngine;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly EfSpaceValidationSnapshotReader _validationSnapshots;
    private readonly EfSpacePublishSnapshotReader _publishSnapshots;

    public SpacePublishPreviewService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceDesignAccessEvaluator access,
        ISpaceValidationProfileProvider profiles,
        SpaceValidationEngine validationEngine,
        SpacePublishPlanEngine planEngine,
        ISpaceCursorCodec cursorCodec)
    {
        _context = context;
        _execution = execution;
        _access = access;
        _profiles = profiles;
        _validationEngine = validationEngine;
        _planEngine = planEngine;
        _cursorCodec = cursorCodec;
        _validationSnapshots = new EfSpaceValidationSnapshotReader(context);
        _publishSnapshots = new EfSpacePublishSnapshotReader(context);
    }

    public async Task<SpacePublishPreviewDto> GetPreviewAsync(
        Guid versionId,
        Guid? floorLogicalId,
        string? objectType,
        string? action,
        string? impactCode,
        bool includeNoOp,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty)
            throw Invalid("A non-empty versionId is required.");
        if (floorLogicalId == Guid.Empty)
            throw Invalid("floorLogicalId cannot be empty.");
        limit = NormalizeLimit(limit);
        var normalizedObjectType = NormalizeFilter(
            objectType,
            SpacePublishObjectTypes.All,
            nameof(objectType));
        var normalizedAction = NormalizeFilter(
            action,
            SpacePublishActions.All,
            nameof(action));
        var normalizedImpact = NormalizeFilter(
            impactCode,
            SpacePublishImpactCodes.All,
            nameof(impactCode));

        var target = await _context.Versions
                         .SingleOrDefaultAsync(
                             value => value.Id == versionId,
                             cancellationToken)
                     ?? throw NotFound(
                         SpaceErrorCodes.VersionNotFound,
                         "The target model version was not found.");
        var model = await _context.Models
                        .SingleOrDefaultAsync(
                            value => value.Id == target.ModelId,
                            cancellationToken)
                    ?? throw NotFound(
                        SpaceErrorCodes.ModelNotFound,
                        "The target model was not found.");
        _access.EnsureSiteAccess(model.SiteId, write: false);
        if (target.Status is not (
                SpaceVersionStatus.Ready or SpaceVersionStatus.Draft))
        {
            throw Stale(
                $"Version state {target.Status} cannot produce a " +
                "publish preview.");
        }
        var initialContentRevision = target.ContentRevision;
        var initialTargetStatus = target.Status;
        var initialPublishedVersionId = model.CurrentPublishedVersionId;

        var correlationId =
            _execution is ISpaceCorrelationContext correlation &&
            correlation.CorrelationId != Guid.Empty
                ? correlation.CorrelationId
                : Guid.NewGuid();
        var profile = await _profiles.GetProfileAsync(
            _execution.TenantId,
            model.SiteId,
            correlationId,
            cancellationToken);
        var validationSnapshot = await _validationSnapshots.ReadAsync(
            model,
            target,
            cancellationToken);
        var contentHash =
            _validationEngine.ComputeContentHash(validationSnapshot);
        var validation = await _context.ValidationRuns
            .AsNoTracking()
            .Where(run =>
                run.ModelVersionId == versionId &&
                run.ContentHash == contentHash &&
                run.RuleSetVersion == SpaceValidationRuleSet.Version &&
                run.AdapterId == profile.AdapterId &&
                run.CapabilityHash == profile.CapabilityHash &&
                (run.Status == SpaceValidationStatus.Passed ||
                 run.Status == SpaceValidationStatus.Blocked))
            .OrderByDescending(run => run.FinishedAtUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (validation is null)
        {
            throw Stale(
                "No completed ValidationRun matches the current content, " +
                "rules, adapter, and capability snapshot.");
        }
        EnsureValidationBinding(target, validation);

        SpaceModelVersion? baseVersion = null;
        if (model.CurrentPublishedVersionId.HasValue)
        {
            baseVersion = await _context.Versions
                              .AsNoTracking()
                              .SingleOrDefaultAsync(
                                  value =>
                                      value.Id ==
                                      model.CurrentPublishedVersionId.Value,
                                  cancellationToken)
                          ?? throw Stale(
                              "The current Published version pointer is stale.");
            if (baseVersion.ModelId != model.Id ||
                baseVersion.Status != SpaceVersionStatus.Published)
            {
                throw Stale(
                    "The current Published version is outside the target " +
                    "model or is not active.");
            }
        }

        var targetObjects = await _publishSnapshots.ReadAsync(
            target.Id,
            profile.AdapterId,
            cancellationToken);
        var baseObjects = baseVersion is null
            ? []
            : await _publishSnapshots.ReadAsync(
                baseVersion.Id,
                profile.AdapterId,
                cancellationToken);
        var plan = _planEngine.Build(
            new SpacePublishPlanInput(
                target.Id,
                baseVersion?.Id,
                validation.Id,
                validation.Status.ToString(),
                validation.BlockingCount,
                contentHash,
                profile.AdapterId,
                profile.CapabilityHash,
                targetObjects,
                baseObjects));
        var currentBinding = await (
                from currentTarget in _context.Versions.AsNoTracking()
                join currentModel in _context.Models.AsNoTracking()
                    on currentTarget.ModelId equals currentModel.Id
                where currentTarget.Id == target.Id
                select new
                {
                    currentTarget.ContentRevision,
                    currentTarget.Status,
                    currentModel.CurrentPublishedVersionId,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (currentBinding is null ||
            currentBinding.ContentRevision != initialContentRevision ||
            currentBinding.Status != initialTargetStatus ||
            currentBinding.CurrentPublishedVersionId !=
            initialPublishedVersionId)
        {
            throw Stale(
                "The target content, version state, or current Published " +
                "pointer changed while the preview was being generated.");
        }

        var filtered = plan.Items
            .Where(item =>
                includeNoOp ||
                item.Action != SpacePublishActions.NoOp)
            .Where(item =>
                !floorLogicalId.HasValue ||
                item.FloorLogicalId == floorLogicalId)
            .Where(item =>
                normalizedObjectType is null ||
                item.ObjectType == normalizedObjectType)
            .Where(item =>
                normalizedAction is null ||
                item.Action == normalizedAction)
            .Where(item =>
                normalizedImpact is null ||
                item.ImpactCode == normalizedImpact)
            .ToArray();
        var filterHash = Hash(
            string.Join(
                "\n",
                $"plan={plan.PlanHash}",
                $"floor={floorLogicalId?.ToString("D") ?? "-"}",
                $"objectType={normalizedObjectType ?? "-"}",
                $"action={normalizedAction ?? "-"}",
                $"impact={normalizedImpact ?? "-"}",
                $"includeNoOp={includeNoOp}",
                $"limit={limit}"));
        var offset = ReadOffset(cursor, filterHash);
        if (offset > filtered.Length)
            throw InvalidCursor();
        var page = filtered.Skip(offset).Take(limit).ToArray();
        var nextCursor = offset + page.Length < filtered.Length
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    "publish-preview",
                    filterHash,
                    checked(offset + page.Length)))
            : null;
        var publishable =
            validation.Status == SpaceValidationStatus.Passed &&
            validation.BlockingCount == 0 &&
            target.Status == SpaceVersionStatus.Ready &&
            !plan.HasBlockingImpact;
        return new SpacePublishPreviewDto(
            target.Id,
            baseVersion?.Id,
            validation.Id,
            validation.Status.ToString(),
            validation.BlockingCount,
            contentHash,
            SpacePublishPlanRuleSet.Version,
            profile.AdapterId,
            profile.CapabilityHash,
            plan.PlanHash,
            publishable,
            plan.Items.Count,
            plan.ChangeCount,
            filtered.Length,
            new SpacePublishChangeSummaryDto(
                plan.Changes.CreateCount,
                plan.Changes.UpdateMasterCount,
                plan.Changes.UpdateGeometryOnlyCount,
                plan.Changes.DisableCount,
                plan.Changes.RestoreCount,
                plan.Changes.NoOpCount),
            new SpacePublishImpactSummaryDto(
                plan.WmsImpact.WmsCreateCount,
                plan.WmsImpact.WmsUpdateCount,
                plan.WmsImpact.WmsDisableCount,
                plan.WmsImpact.WmsRestoreCount,
                plan.WmsImpact.WmsNoOpCount,
                plan.WmsImpact.RuntimeOnlyCount,
                plan.WmsImpact.BlockingCount),
            page.Select(ToDto).ToArray(),
            nextCursor);
    }

    private static SpacePublishPreviewItemDto ToDto(
        SpacePublishPlanItem item) =>
        new(
            item.SequenceNo,
            item.ObjectType,
            item.LogicalId,
            item.FloorLogicalId,
            item.Action,
            item.BeforeHash,
            item.AfterHash,
            item.BeforeCode,
            item.AfterCode,
            item.ExternalBindingId,
            item.PayloadHash,
            item.ImpactCode,
            item.MasterChanged,
            item.GeometryChanged,
            item.ProvenanceChanged,
            item.WmsChanged,
            item.Blocking);

    private static void EnsureValidationBinding(
        SpaceModelVersion target,
        SpaceValidationRun validation)
    {
        if (validation.Status == SpaceValidationStatus.Passed)
        {
            if (target.Status != SpaceVersionStatus.Ready ||
                !string.Equals(
                    target.ContentHash,
                    validation.ContentHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    target.ValidatedHash,
                    validation.ContentHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    target.RuleSetVersion,
                    validation.RuleSetVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    target.WmsCapabilityHash,
                    validation.CapabilityHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Stale(
                    "Ready version evidence does not match the selected " +
                    "ValidationRun.");
            }
            return;
        }

        if (target.Status != SpaceVersionStatus.Draft ||
            validation.Status != SpaceValidationStatus.Blocked)
        {
            throw Stale(
                "Blocked validation evidence no longer matches the target " +
                "version state.");
        }
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(
            cursor,
            "publish-preview",
            filterHash);
        if (state.Offset < 0)
            throw InvalidCursor();
        return state.Offset;
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return 100;
        if (limit is < 1 or > 500)
            throw Invalid("limit must be between 1 and 500.");
        return limit;
    }

    private static string? NormalizeFilter(
        string? value,
        IReadOnlySet<string> supported,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        var match = supported.SingleOrDefault(candidate =>
            string.Equals(
                candidate,
                normalized,
                StringComparison.OrdinalIgnoreCase));
        return match ?? throw Invalid(
            $"{parameterName} is not a supported value.");
    }

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The publish preview request is invalid.",
            detail,
            "correct-request");

    private static SpaceProblemException InvalidCursor() =>
        new(
            SpaceErrorCodes.CursorInvalid,
            400,
            "The publish preview cursor is invalid.",
            recoveryAction: "restart-pagination");

    private static SpaceProblemException NotFound(
        string code,
        string detail) =>
        new(
            code,
            404,
            "The requested Space resource was not found.",
            detail,
            "refresh-resource");

    private static SpaceProblemException Stale(string detail) =>
        new(
            SpaceErrorCodes.ValidationStale,
            409,
            "The publish preview validation evidence is stale.",
            detail,
            "run-validation");
}

internal sealed class EfSpacePublishSnapshotReader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;

    public EfSpacePublishSnapshotReader(SpaceContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SpacePublishObjectSnapshot>> ReadAsync(
        Guid versionId,
        string adapterId,
        CancellationToken cancellationToken)
    {
        var siteId = await (
                from version in _context.Versions.AsNoTracking()
                join model in _context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select model.SiteId)
            .SingleAsync(cancellationToken);
        var adoptions = await _context.WmsAdoptions
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.AdapterId == adapterId &&
                value.LocationLogicalId.HasValue &&
                value.Status == SpaceWmsAdoptionStatus.Bound)
            .ToDictionaryAsync(
                value => value.LocationLogicalId!.Value,
                cancellationToken);
        var floors = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var aisles = await _context.AisleRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var levels = await _context.RackLevelRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var locations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var elements = await _context.ElementRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToArrayAsync(cancellationToken);
        var sources = await _context.Sources
            .AsNoTracking()
            .Where(value => value.ModelVersionId == versionId)
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var elementIds = elements.Select(value => value.Id).ToArray();
        var attributes = elementIds.Length == 0
            ? []
            : await _context.ElementAttributes
                .AsNoTracking()
                .Where(value =>
                    value.ModelVersionId == versionId &&
                    elementIds.Contains(value.ElementRevisionId))
                .ToArrayAsync(cancellationToken);
        var attributesByElement = attributes
            .GroupBy(value => value.ElementRevisionId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(value => value.Namespace, StringComparer.Ordinal)
                    .ThenBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => new
                    {
                        value.Namespace,
                        value.Key,
                        value.ValueType,
                        value.Value,
                        value.Unit,
                    })
                    .ToArray());
        var zoneFloors = zones.ToDictionary(
            value => value.LogicalId,
            value => value.FloorLogicalId);
        var rackFloors = racks.ToDictionary(
            value => value.LogicalId,
            value => value.FloorLogicalId);

        var result = new List<SpacePublishObjectSnapshot>(
            floors.Length +
            zones.Length +
            aisles.Length +
            racks.Length +
            levels.Length +
            locations.Length +
            elements.Length);
        result.AddRange(floors.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Floor,
                value.LogicalId,
                value.LogicalId,
                value.LifecycleState,
                value.FloorCode,
                Json(new
                {
                    value.SiteLogicalId,
                    value.Level,
                    value.FloorCode,
                    value.Name,
                    value.CoordinateSystem,
                    UnderlaySource = SourceFingerprint(
                        value.UnderlaySourceId,
                        sources),
                }),
                Json(new
                {
                    value.Elevation,
                    value.Height,
                    Boundary = Canonical(value.BoundaryJson),
                    value.UnderlayScale,
                    value.UnderlayOffsetX,
                    value.UnderlayOffsetY,
                    value.UnderlayRotationZ,
                }),
                EmptyJson(),
                Provenance(value, sources))));
        result.AddRange(zones.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Zone,
                value.LogicalId,
                value.FloorLogicalId,
                value.LifecycleState,
                value.ZoneCode,
                Json(new
                {
                    value.FloorLogicalId,
                    value.ZoneCode,
                    value.ZoneType,
                    value.Color,
                    value.CapabilityFlags,
                }),
                Json(new
                {
                    Polygon = Canonical(value.PolygonJson),
                }),
                EmptyJson(),
                Provenance(value, sources))));
        result.AddRange(aisles.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Aisle,
                value.LogicalId,
                TryGetFloor(zoneFloors, value.ZoneLogicalId),
                value.LifecycleState,
                value.AisleCode,
                Json(new
                {
                    value.ZoneLogicalId,
                    value.AisleCode,
                    value.Direction,
                }),
                Json(new
                {
                    Polygon = Canonical(value.PolygonJson),
                    Centerline = Canonical(value.CenterlineJson),
                }),
                EmptyJson(),
                Provenance(value, sources))));
        result.AddRange(racks.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Rack,
                value.LogicalId,
                value.FloorLogicalId,
                value.LifecycleState,
                value.RackCode,
                Json(new
                {
                    value.FloorLogicalId,
                    value.ZoneLogicalId,
                    value.AisleLogicalId,
                    value.RackCode,
                    value.TemplateVersionId,
                }),
                Json(new
                {
                    value.X,
                    value.Y,
                    value.Z,
                    value.RotationZ,
                    value.Width,
                    value.Depth,
                    value.Height,
                }),
                Json(new
                {
                    value.FloorLogicalId,
                    value.ZoneLogicalId,
                    value.AisleLogicalId,
                    value.RackCode,
                }),
                Provenance(value, sources))));
        result.AddRange(levels.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.RackLevel,
                value.LogicalId,
                TryGetFloor(rackFloors, value.RackLogicalId),
                value.LifecycleState,
                $"{value.RackLogicalId:D}/{value.LevelNo}",
                Json(new
                {
                    value.RackLogicalId,
                    value.LevelNo,
                    value.BottomZ,
                    value.ClearHeight,
                    value.BinCount,
                    value.DepthCount,
                    value.CellWidth,
                    value.CellDepth,
                    value.BeamHeight,
                    value.MaxLoad,
                }),
                EmptyJson(),
                Json(new
                {
                    value.RackLogicalId,
                    value.LevelNo,
                    value.BinCount,
                    value.DepthCount,
                    value.MaxLoad,
                }),
                Provenance(value, sources))));
        result.AddRange(locations.Select(value =>
        {
            adoptions.TryGetValue(value.LogicalId, out var adoption);
            var externalBindingId =
                adoption?.ExternalLocationId ??
                adoption?.WmsLogicalId.ToString("D") ??
                (value.ExternalBindingState ==
                 SpaceExternalBindingState.Bound
                    ? value.LogicalId.ToString("D")
                    : null);
            return SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Location,
                value.LogicalId,
                value.FloorLogicalId,
                value.LifecycleState,
                value.LocationCode,
                Json(new
                {
                    value.FloorLogicalId,
                    value.RackLogicalId,
                    value.LocationCode,
                    value.ColumnNo,
                    value.LevelNo,
                    value.DepthNo,
                    value.Width,
                    value.Height,
                    value.Depth,
                    value.MaxLoad,
                    value.CodeOrigin,
                    value.ExternalBindingState,
                }),
                EmptyJson(),
                Json(new
                {
                    value.FloorLogicalId,
                    value.RackLogicalId,
                    value.LocationCode,
                    value.ColumnNo,
                    value.LevelNo,
                    value.DepthNo,
                    value.Width,
                    value.Height,
                    value.Depth,
                    value.MaxLoad,
                }),
                Provenance(value, sources),
                externalBindingId);
        }));
        result.AddRange(elements.Select(value =>
            SpacePublishObjectSnapshot.Create(
                SpacePublishObjectTypes.Element,
                value.LogicalId,
                value.FloorLogicalId,
                value.LifecycleState,
                value.BusinessCode,
                Json(new
                {
                    value.FloorLogicalId,
                    value.ParentLogicalId,
                    value.ElementType,
                    value.ModelAssetId,
                    value.ModelAssetScope,
                    value.ModelAssetOwnerTenantId,
                    value.BusinessCode,
                    value.LinkedEntityType,
                    value.LinkedLogicalId,
                    Attributes =
                        attributesByElement.GetValueOrDefault(value.Id) ?? [],
                }),
                Json(new
                {
                    Geometry = Canonical(value.GeometryJson),
                    value.X,
                    value.Y,
                    value.Z,
                    value.RotationZ,
                    value.Width,
                    value.Height,
                    value.Depth,
                }),
                EmptyJson(),
                Provenance(value, sources))));
        return result;
    }

    private static string Provenance(
        SpaceRevisionEntity value,
        IReadOnlyDictionary<Guid, SpaceModelSource> sources) =>
        Json(new
        {
            Source = SourceFingerprint(value.SourceId, sources),
            value.SourceRef,
        });

    private static Guid? TryGetFloor(
        IReadOnlyDictionary<Guid, Guid> floors,
        Guid parentLogicalId) =>
        floors.TryGetValue(parentLogicalId, out var floorLogicalId)
            ? floorLogicalId
            : null;

    private static SpacePublishSourceFingerprint? SourceFingerprint(
        Guid? sourceId,
        IReadOnlyDictionary<Guid, SpaceModelSource> sources)
    {
        if (!sourceId.HasValue)
            return null;
        if (!sources.TryGetValue(sourceId.Value, out var source))
        {
            return new SpacePublishSourceFingerprint(
                MissingSourceId: sourceId,
                SourceType: null,
                Sha256: null,
                ParserVersion: null,
                MappingProfileId: null,
                MappingProfileVersion: null,
                Unit: null,
                ScaleToMillimeters: null,
                TransformJson: null,
                State: null);
        }
        return new SpacePublishSourceFingerprint(
            MissingSourceId: null,
            source.SourceType.ToString(),
            source.Sha256,
            source.ParserVersion,
            source.MappingProfileId,
            source.MappingProfileVersion,
            source.Unit,
            source.ScaleToMillimeters,
            source.TransformJson is null
                ? null
                : Canonical(source.TransformJson),
            source.State.ToString());
    }

    private static string Canonical(string value) =>
        SpaceCanonicalJson.Normalize(value);

    private static string EmptyJson() => "{}";

    private static string Json(object value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private sealed record SpacePublishSourceFingerprint(
        Guid? MissingSourceId,
        string? SourceType,
        string? Sha256,
        string? ParserVersion,
        Guid? MappingProfileId,
        long? MappingProfileVersion,
        string? Unit,
        decimal? ScaleToMillimeters,
        string? TransformJson,
        string? State);
}

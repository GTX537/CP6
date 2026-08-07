using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceValidationProfile(
    string AdapterId,
    string CapabilityHash,
    int MaxLocationCodeLength,
    string LocationCodePattern,
    int MaxObjectCount)
{
    public static SpaceValidationProfile Create(
        string adapterId,
        int maxLocationCodeLength,
        string locationCodePattern,
        int maxObjectCount)
    {
        var normalizedAdapter = RequireText(
            adapterId,
            100,
            nameof(adapterId));
        var normalizedPattern = RequireText(
            locationCodePattern,
            500,
            nameof(locationCodePattern));
        if (maxLocationCodeLength is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maxLocationCodeLength));
        if (maxObjectCount is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxObjectCount));

        _ = new Regex(
            normalizedPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var canonical = string.Join(
            "\n",
            normalizedAdapter,
            maxLocationCodeLength,
            normalizedPattern,
            maxObjectCount);
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new SpaceValidationProfile(
            normalizedAdapter,
            hash,
            maxLocationCodeLength,
            normalizedPattern,
            maxObjectCount);
    }

    public static SpaceValidationProfile FromCapabilities(
        SpaceWmsCapabilitySnapshot snapshot,
        int maxObjectCount = 100_000)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maxObjectCount is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxObjectCount));
        return new SpaceValidationProfile(
            RequireText(snapshot.AdapterId, 100, nameof(snapshot)),
            RequireHash(snapshot.CapabilityHash),
            snapshot.Capabilities.CodeMaxLength,
            RequireText(
                snapshot.Capabilities.AllowedCodePattern,
                500,
                nameof(snapshot)),
            maxObjectCount);
    }

    private static string RequireText(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }

    private static string RequireHash(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "A SHA-256 capability hash is required.",
                nameof(value));
        return value.ToLowerInvariant();
    }
}

public interface ISpaceValidationProfileProvider
{
    Task<SpaceValidationProfile> GetProfileAsync(
        Guid tenantId,
        Guid siteId,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceValidationService
{
    Task<CreateSpaceValidationResponse> RequestValidationAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<SpaceValidationRunDto> GetValidationAsync(
        Guid validationId,
        CancellationToken cancellationToken = default);
}

public static class SpaceValidationRuleSet
{
    public const string Version = "space-validation-v1";
    public const string ProcessorVersion = "space-validation-processor-v1";
}

public static class SpaceValidationCategories
{
    public const string Schema = "Schema";
    public const string Hierarchy = "Hierarchy";
    public const string Identity = "Identity";
    public const string Geometry = "Geometry";
    public const string Coding = "Coding";
    public const string Source = "Source";
    public const string Binding = "Binding";
    public const string AiProvenance = "AiProvenance";
    public const string ModelIssue = "ModelIssue";
    public const string Performance = "Performance";
}

public static class SpaceValidationIssueCodes
{
    public const string ObjectLimitExceeded = "VALIDATION_OBJECT_LIMIT_EXCEEDED";
    public const string FloorRequired = "MODEL_FLOOR_REQUIRED";
    public const string LogicalIdDuplicate = "MODEL_LOGICAL_ID_DUPLICATE";
    public const string ParentMissing = "MODEL_PARENT_MISSING";
    public const string ParentMismatch = "MODEL_PARENT_MISMATCH";
    public const string SourceMissing = "MODEL_SOURCE_MISSING";
    public const string SourceNotReady = "MODEL_SOURCE_NOT_READY";
    public const string SourceMetadataInvalid =
        "MODEL_SOURCE_METADATA_INVALID";
    public const string AiSourceIncomplete = "AI_PROPOSAL_SOURCE_INCOMPLETE";
    public const string GeometryInvalid = "MODEL_GEOMETRY_INVALID";
    public const string FloorBoundaryMissing = "FLOOR_BOUNDARY_MISSING";
    public const string GeometryOutOfBounds = "MODEL_GEOMETRY_OUT_OF_BOUNDS";
    public const string GeometryCollision = "MODEL_GEOMETRY_COLLISION";
    public const string RackLevelMissing = "RACK_LEVEL_REQUIRED";
    public const string RackLevelInvalid = "RACK_LEVEL_INVALID";
    public const string RackLocationIncomplete = "RACK_LOCATION_SET_INCOMPLETE";
    public const string LocationSlotInvalid = "LOCATION_SLOT_INVALID";
    public const string CodeRequired = "MODEL_CODE_REQUIRED";
    public const string CodeDuplicate = "MODEL_CODE_DUPLICATE";
    public const string CodeUnsupported = "MODEL_CODE_UNSUPPORTED";
    public const string LocationIdentityConflict = "LOCATION_IDENTITY_CONFLICT";
    public const string LocationCodeFrozen = "LOCATION_CODE_FROZEN";
    public const string AssetBindingInvalid = "ELEMENT_ASSET_BINDING_INVALID";
    public const string InternalBindingMissing = "ELEMENT_INTERNAL_BINDING_MISSING";
}

public sealed record SpaceValidationRevisionRef(
    Guid LogicalId,
    Guid? SourceId,
    string? SourceRef,
    SpaceLifecycleState LifecycleState);

public sealed record SpaceValidationFloor(
    SpaceValidationRevisionRef Revision,
    Guid SiteLogicalId,
    int Level,
    string FloorCode,
    int Elevation,
    int Height,
    string BoundaryJson,
    string CoordinateSystem,
    Guid? UnderlaySourceId,
    decimal? UnderlayScale);

public sealed record SpaceValidationZone(
    SpaceValidationRevisionRef Revision,
    Guid FloorLogicalId,
    string ZoneCode,
    string PolygonJson);

public sealed record SpaceValidationAisle(
    SpaceValidationRevisionRef Revision,
    Guid ZoneLogicalId,
    string AisleCode,
    string PolygonJson,
    string CenterlineJson);

public sealed record SpaceValidationRack(
    SpaceValidationRevisionRef Revision,
    Guid FloorLogicalId,
    Guid ZoneLogicalId,
    Guid? AisleLogicalId,
    string RackCode,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Depth,
    int Height);

public sealed record SpaceValidationRackLevel(
    SpaceValidationRevisionRef Revision,
    Guid RackLogicalId,
    int LevelNo,
    int BottomZ,
    int ClearHeight,
    int BinCount,
    int DepthCount,
    int CellWidth,
    int CellDepth,
    int BeamHeight);

public sealed record SpaceValidationLocation(
    SpaceValidationRevisionRef Revision,
    Guid FloorLogicalId,
    Guid? RackLogicalId,
    string? LocationCode,
    int ColumnNo,
    int LevelNo,
    int DepthNo,
    int Width,
    int Height,
    int Depth,
    SpaceLocationCodeOrigin CodeOrigin,
    SpaceExternalBindingState ExternalBindingState);

public sealed record SpaceValidationElementAttribute(
    string Namespace,
    string Key,
    string ValueType,
    string? Value,
    string? Unit);

public sealed record SpaceValidationElement(
    SpaceValidationRevisionRef Revision,
    Guid FloorLogicalId,
    Guid? ParentLogicalId,
    string ElementType,
    string GeometryJson,
    Guid? ModelAssetId,
    SpaceAssetScope? ModelAssetScope,
    Guid? ModelAssetOwnerTenantId,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Height,
    int Depth,
    string? BusinessCode,
    string? LinkedEntityType,
    Guid? LinkedLogicalId,
    IReadOnlyList<SpaceValidationElementAttribute> Attributes);

public sealed record SpaceValidationSource(
    Guid Id,
    SpaceSourceType SourceType,
    string Sha256,
    SpaceSourceState State,
    string? Unit,
    decimal? ScaleToMillimeters);

public sealed record SpaceValidationAssetVersion(
    Guid Id,
    SpaceAssetScope Scope,
    Guid OwnerTenantId,
    SpaceAssetVersionStatus Status);

public sealed record SpaceValidationPublishedLocation(
    Guid LogicalId,
    string? LocationCode,
    SpaceExternalBindingState ExternalBindingState);

public sealed record SpaceValidationExistingIssue(
    SpaceIssueSeverity Severity,
    string? Category,
    string Code,
    Guid? SourceId,
    string? SourceRef,
    Guid? TargetLogicalId,
    string? FieldPath,
    string MessageArgsJson,
    string? SuggestedActionCode,
    Guid? GenerationRunId,
    Guid? GenerationProposalId,
    string EvidenceJson);

public sealed record SpaceValidationSnapshot(
    Guid TenantId,
    Guid ModelId,
    Guid ModelVersionId,
    Guid SiteId,
    long ContentRevision,
    IReadOnlyList<SpaceValidationFloor> Floors,
    IReadOnlyList<SpaceValidationZone> Zones,
    IReadOnlyList<SpaceValidationAisle> Aisles,
    IReadOnlyList<SpaceValidationRack> Racks,
    IReadOnlyList<SpaceValidationRackLevel> RackLevels,
    IReadOnlyList<SpaceValidationLocation> Locations,
    IReadOnlyList<SpaceValidationElement> Elements,
    IReadOnlyList<SpaceValidationSource> Sources,
    IReadOnlyList<SpaceValidationAssetVersion> AssetVersions,
    IReadOnlyList<SpaceValidationPublishedLocation> PublishedLocations,
    IReadOnlyList<SpaceValidationExistingIssue> ExistingIssues);

public sealed record SpaceValidationIssueCandidate(
    SpaceIssueSeverity Severity,
    string Category,
    string Code,
    string MessageArgsJson,
    string? SourceRef = null,
    Guid? SourceId = null,
    Guid? TargetLogicalId = null,
    string? FieldPath = null,
    string? SuggestedActionCode = null,
    Guid? GenerationRunId = null,
    Guid? GenerationProposalId = null,
    string EvidenceJson = "{}");

public sealed record SpaceValidationEngineResult(
    string ContentHash,
    IReadOnlyList<SpaceValidationIssueCandidate> Issues)
{
    public int BlockingCount =>
        Issues.Count(issue => issue.Severity == SpaceIssueSeverity.Blocking);

    public int WarningCount =>
        Issues.Count(issue => issue.Severity == SpaceIssueSeverity.Warning);

    public int InfoCount =>
        Issues.Count(issue => issue.Severity == SpaceIssueSeverity.Info);
}

public sealed class SpaceValidationEngine
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ComputeContentHash(SpaceValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var canonical = new
        {
            snapshot.SiteId,
            Floors = snapshot.Floors.OrderBy(Key).ToArray(),
            Zones = snapshot.Zones.OrderBy(Key).ToArray(),
            Aisles = snapshot.Aisles.OrderBy(Key).ToArray(),
            Racks = snapshot.Racks.OrderBy(Key).ToArray(),
            RackLevels = snapshot.RackLevels
                .OrderBy(value => value.RackLogicalId)
                .ThenBy(value => value.LevelNo)
                .ThenBy(Key)
                .ToArray(),
            Locations = snapshot.Locations.OrderBy(Key).ToArray(),
            Elements = snapshot.Elements
                .OrderBy(Key)
                .Select(element => element with
                {
                    Attributes = element.Attributes
                        .OrderBy(value => value.Namespace, StringComparer.Ordinal)
                        .ThenBy(value => value.Key, StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray(),
            Sources = snapshot.Sources.OrderBy(value => value.Id).ToArray(),
            AssetVersions = snapshot.AssetVersions
                .OrderBy(value => value.Id)
                .ToArray(),
            PublishedLocations = snapshot.PublishedLocations
                .OrderBy(value => value.LogicalId)
                .ToArray(),
            ExistingIssues = snapshot.ExistingIssues
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .ThenBy(value => value.TargetLogicalId)
                .ThenBy(value => value.SourceRef, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.Severity,
                    value.Category,
                    value.Code,
                    value.SourceId,
                    value.SourceRef,
                    value.TargetLogicalId,
                    value.FieldPath,
                    value.MessageArgsJson,
                    value.SuggestedActionCode,
                    value.GenerationRunId,
                    value.GenerationProposalId,
                    value.EvidenceJson,
                })
                .ToArray(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public SpaceValidationEngineResult Validate(
        SpaceValidationSnapshot snapshot,
        SpaceValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profile);
        var issues = new List<SpaceValidationIssueCandidate>();
        var sources = snapshot.Sources.ToDictionary(source => source.Id);
        var floors = Active(snapshot.Floors).ToDictionary(Key);
        var zones = Active(snapshot.Zones).ToDictionary(Key);
        var aisles = Active(snapshot.Aisles).ToDictionary(Key);
        var racks = Active(snapshot.Racks).ToDictionary(Key);
        var rackLevels = Active(snapshot.RackLevels).ToArray();
        var locations = Active(snapshot.Locations).ToArray();
        var elements = Active(snapshot.Elements).ToArray();

        ValidateObjectLimit(snapshot, profile, issues);
        ValidateLogicalIds(snapshot, issues);
        ValidateCodes(
            floors.Values,
            zones.Values,
            aisles.Values,
            racks.Values,
            locations,
            elements,
            profile,
            issues);
        ValidateSources(snapshot, sources, issues);
        ValidateFloorGeometry(floors.Values, issues);
        ValidateHierarchy(
            snapshot.SiteId,
            floors,
            zones,
            aisles,
            racks,
            rackLevels,
            locations,
            elements,
            issues);
        ValidateRackGeometry(
            floors,
            zones,
            racks,
            rackLevels,
            locations,
            issues);
        ValidateElementGeometry(
            snapshot.TenantId,
            floors,
            snapshot.AssetVersions,
            elements,
            issues);
        ValidatePublishedLocationIdentity(
            snapshot.PublishedLocations,
            locations,
            issues);
        var unboundDisableTargets = FindUnboundDisableTargets(snapshot);
        AppendExistingIssues(
            snapshot.ExistingIssues,
            unboundDisableTargets,
            issues);

        var ordered = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Category, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.TargetLogicalId)
            .ThenBy(issue => issue.SourceRef, StringComparer.Ordinal)
            .ThenBy(issue => issue.MessageArgsJson, StringComparer.Ordinal)
            .ToArray();
        return new SpaceValidationEngineResult(
            ComputeContentHash(snapshot),
            ordered);
    }

    private static void ValidateObjectLimit(
        SpaceValidationSnapshot snapshot,
        SpaceValidationProfile profile,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var count =
            snapshot.Floors.Count +
            snapshot.Zones.Count +
            snapshot.Aisles.Count +
            snapshot.Racks.Count +
            snapshot.RackLevels.Count +
            snapshot.Locations.Count +
            snapshot.Elements.Count;
        if (count > profile.MaxObjectCount)
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Performance,
                SpaceValidationIssueCodes.ObjectLimitExceeded,
                new { count, maximum = profile.MaxObjectCount },
                suggestedActionCode: "reduce-model-size");
        }
    }

    private static void ValidateLogicalIds(
        SpaceValidationSnapshot snapshot,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var identities = new List<(string Type, SpaceValidationRevisionRef Revision)>();
        identities.AddRange(snapshot.Floors.Select(value => ("Floor", value.Revision)));
        identities.AddRange(snapshot.Zones.Select(value => ("Zone", value.Revision)));
        identities.AddRange(snapshot.Aisles.Select(value => ("Aisle", value.Revision)));
        identities.AddRange(snapshot.Racks.Select(value => ("Rack", value.Revision)));
        identities.AddRange(
            snapshot.RackLevels.Select(value => ("RackLevel", value.Revision)));
        identities.AddRange(
            snapshot.Locations.Select(value => ("Location", value.Revision)));
        identities.AddRange(
            snapshot.Elements.Select(value => ("Element", value.Revision)));

        foreach (var duplicate in identities
                     .GroupBy(value => value.Revision.LogicalId)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key))
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Identity,
                SpaceValidationIssueCodes.LogicalIdDuplicate,
                new
                {
                    logicalId = duplicate.Key,
                    objectTypes = duplicate
                        .Select(value => value.Type)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                },
                targetLogicalId: duplicate.Key,
                suggestedActionCode: "restore-stable-identity");
        }
    }

    private static void ValidateCodes(
        IEnumerable<SpaceValidationFloor> floors,
        IEnumerable<SpaceValidationZone> zones,
        IEnumerable<SpaceValidationAisle> aisles,
        IEnumerable<SpaceValidationRack> racks,
        IReadOnlyCollection<SpaceValidationLocation> locations,
        IReadOnlyCollection<SpaceValidationElement> elements,
        SpaceValidationProfile profile,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        ValidateRequiredAndDuplicateCodes(
            floors.Select(value => (
                Type: "Floor",
                Parent: Guid.Empty,
                value.Revision.LogicalId,
                Code: value.FloorCode)),
            issues);
        ValidateRequiredAndDuplicateCodes(
            zones.Select(value => (
                Type: "Zone",
                Parent: value.FloorLogicalId,
                value.Revision.LogicalId,
                Code: value.ZoneCode)),
            issues);
        ValidateRequiredAndDuplicateCodes(
            aisles.Select(value => (
                Type: "Aisle",
                Parent: value.ZoneLogicalId,
                value.Revision.LogicalId,
                Code: value.AisleCode)),
            issues);
        ValidateRequiredAndDuplicateCodes(
            racks.Select(value => (
                Type: "Rack",
                Parent: value.ZoneLogicalId,
                value.Revision.LogicalId,
                Code: value.RackCode)),
            issues);

        var pattern = new Regex(
            profile.LocationCodePattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        foreach (var location in locations.OrderBy(Key))
        {
            var code = location.LocationCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Coding,
                    SpaceValidationIssueCodes.CodeRequired,
                    new { objectType = "Location" },
                    targetLogicalId: Key(location),
                    fieldPath: "/locationCode",
                    suggestedActionCode: "generate-location-code");
                continue;
            }
            if (code.Length > profile.MaxLocationCodeLength ||
                !pattern.IsMatch(code))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Coding,
                    SpaceValidationIssueCodes.CodeUnsupported,
                    new
                    {
                        objectType = "Location",
                        code,
                        maximumLength = profile.MaxLocationCodeLength,
                        profile.LocationCodePattern,
                    },
                    targetLogicalId: Key(location),
                    fieldPath: "/locationCode",
                    suggestedActionCode: "correct-location-code");
            }
        }

        foreach (var duplicate in locations
                     .Where(value => !string.IsNullOrWhiteSpace(value.LocationCode))
                     .GroupBy(
                         value => value.LocationCode!.Trim(),
                         StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var location in duplicate.OrderBy(Key))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Coding,
                    SpaceValidationIssueCodes.CodeDuplicate,
                    new
                    {
                        objectType = "Location",
                        code = duplicate.Key,
                        logicalIds = duplicate.Select(Key).Order().ToArray(),
                    },
                    targetLogicalId: Key(location),
                    fieldPath: "/locationCode",
                    suggestedActionCode: "regenerate-location-codes");
            }
        }

        foreach (var duplicate in elements
                     .Where(value => !string.IsNullOrWhiteSpace(value.BusinessCode))
                     .GroupBy(
                         value => value.BusinessCode!.Trim(),
                         StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var element in duplicate.OrderBy(Key))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Coding,
                    SpaceValidationIssueCodes.CodeDuplicate,
                    new
                    {
                        objectType = "Element",
                        code = duplicate.Key,
                        logicalIds = duplicate.Select(Key).Order().ToArray(),
                    },
                    targetLogicalId: Key(element),
                    fieldPath: "/businessCode",
                    suggestedActionCode: "correct-business-code");
            }
        }
    }

    private static void ValidateRequiredAndDuplicateCodes(
        IEnumerable<(string Type, Guid Parent, Guid LogicalId, string Code)> values,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var materialized = values.ToArray();
        foreach (var value in materialized
                     .Where(value => string.IsNullOrWhiteSpace(value.Code))
                     .OrderBy(value => value.LogicalId))
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Coding,
                SpaceValidationIssueCodes.CodeRequired,
                new { objectType = value.Type },
                targetLogicalId: value.LogicalId,
                fieldPath: "/code",
                suggestedActionCode: "supply-code");
        }

        foreach (var duplicate in materialized
                     .Where(value => !string.IsNullOrWhiteSpace(value.Code))
                     .GroupBy(
                         value => (value.Type, value.Parent, value.Code.Trim()),
                         new CodeScopeComparer())
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key.Type, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Parent)
                     .ThenBy(group => group.Key.Item3, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var value in duplicate.OrderBy(value => value.LogicalId))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Coding,
                    SpaceValidationIssueCodes.CodeDuplicate,
                    new
                    {
                        objectType = value.Type,
                        code = duplicate.Key.Item3,
                        parentLogicalId = value.Parent == Guid.Empty
                            ? (Guid?)null
                            : value.Parent,
                    },
                    targetLogicalId: value.LogicalId,
                    fieldPath: "/code",
                    suggestedActionCode: "correct-code");
            }
        }
    }

    private static void ValidateSources(
        SpaceValidationSnapshot snapshot,
        IReadOnlyDictionary<Guid, SpaceValidationSource> sources,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var revisions = snapshot.Floors.Select(value => value.Revision)
            .Concat(snapshot.Zones.Select(value => value.Revision))
            .Concat(snapshot.Aisles.Select(value => value.Revision))
            .Concat(snapshot.Racks.Select(value => value.Revision))
            .Concat(snapshot.RackLevels.Select(value => value.Revision))
            .Concat(snapshot.Locations.Select(value => value.Revision))
            .Concat(snapshot.Elements.Select(value => value.Revision));
        foreach (var revision in revisions
                     .Where(value => value.SourceId.HasValue)
                     .OrderBy(value => value.LogicalId))
        {
            if (!sources.TryGetValue(
                    revision.SourceId!.Value,
                    out var source))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Source,
                    SpaceValidationIssueCodes.SourceMissing,
                    new { sourceId = revision.SourceId },
                    sourceId: revision.SourceId,
                    sourceRef: revision.SourceRef,
                    targetLogicalId: revision.LogicalId,
                    fieldPath: "/sourceId",
                    suggestedActionCode: "restore-source-lineage");
            }
            else
            {
                ValidateSource(
                    source,
                    revision.LogicalId,
                    revision.SourceRef,
                    issues);
            }
        }

        foreach (var floor in snapshot.Floors
                     .Where(value => value.UnderlaySourceId.HasValue)
                     .OrderBy(Key))
        {
            if (!sources.TryGetValue(
                    floor.UnderlaySourceId!.Value,
                    out var source))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Source,
                    SpaceValidationIssueCodes.SourceMissing,
                    new { sourceId = floor.UnderlaySourceId },
                    sourceId: floor.UnderlaySourceId,
                    targetLogicalId: Key(floor),
                    fieldPath: "/underlaySourceId",
                    suggestedActionCode: "restore-underlay-source");
            }
            else
            {
                ValidateSource(
                    source,
                    Key(floor),
                    null,
                    issues);
            }
        }
    }

    private static void ValidateSource(
        SpaceValidationSource source,
        Guid targetLogicalId,
        string? sourceRef,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var isReady =
            source.State is SpaceSourceState.PreviewReady or
                SpaceSourceState.Imported ||
            source.State == SpaceSourceState.Ready &&
            source.SourceType is SpaceSourceType.Editor or
                SpaceSourceType.Template;
        if (!isReady)
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Source,
                SpaceValidationIssueCodes.SourceNotReady,
                new { sourceId = source.Id, state = source.State.ToString() },
                sourceId: source.Id,
                sourceRef: sourceRef,
                targetLogicalId: targetLogicalId,
                fieldPath: "/source/state",
                suggestedActionCode: "complete-source-processing");
        }

        var invalidHash =
            source.Sha256.Length != 64 ||
            !source.Sha256.All(Uri.IsHexDigit);
        var invalidCadScale =
            source.SourceType is SpaceSourceType.Dwg or SpaceSourceType.Dxf &&
            (string.IsNullOrWhiteSpace(source.Unit) ||
             source.ScaleToMillimeters is null or <= 0);
        if (invalidHash || invalidCadScale)
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Source,
                SpaceValidationIssueCodes.SourceMetadataInvalid,
                new
                {
                    sourceId = source.Id,
                    invalidHash,
                    invalidCadScale,
                },
                sourceId: source.Id,
                sourceRef: sourceRef,
                targetLogicalId: targetLogicalId,
                fieldPath: "/source",
                suggestedActionCode: "repair-source-metadata");
        }
    }

    private static void ValidateFloorGeometry(
        IEnumerable<SpaceValidationFloor> floors,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var materialized = floors.OrderBy(Key).ToArray();
        if (materialized.Length == 0)
        {
            Add(
                issues,
                SpaceIssueSeverity.Blocking,
                SpaceValidationCategories.Schema,
                SpaceValidationIssueCodes.FloorRequired,
                new { minimum = 1 },
                suggestedActionCode: "create-floor");
            return;
        }

        foreach (var floor in materialized)
        {
            if (!TryPolygon(floor.BoundaryJson, out _, out var error))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Geometry,
                    SpaceValidationIssueCodes.FloorBoundaryMissing,
                    new { error },
                    targetLogicalId: Key(floor),
                    fieldPath: "/boundary",
                    suggestedActionCode: "define-floor-boundary");
            }
        }
    }

    private static void ValidateHierarchy(
        Guid siteId,
        IReadOnlyDictionary<Guid, SpaceValidationFloor> floors,
        IReadOnlyDictionary<Guid, SpaceValidationZone> zones,
        IReadOnlyDictionary<Guid, SpaceValidationAisle> aisles,
        IReadOnlyDictionary<Guid, SpaceValidationRack> racks,
        IReadOnlyCollection<SpaceValidationRackLevel> rackLevels,
        IReadOnlyCollection<SpaceValidationLocation> locations,
        IReadOnlyCollection<SpaceValidationElement> elements,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        foreach (var floor in floors.Values.OrderBy(Key))
        {
            if (floor.SiteLogicalId != siteId)
            {
                AddParentMismatch(
                    issues,
                    "Floor",
                    Key(floor),
                    "Site",
                    siteId,
                    floor.SiteLogicalId);
            }
        }
        foreach (var zone in zones.Values.OrderBy(Key))
        {
            if (!floors.ContainsKey(zone.FloorLogicalId))
                AddParentMissing(issues, "Zone", Key(zone), "Floor", zone.FloorLogicalId);
        }
        foreach (var aisle in aisles.Values.OrderBy(Key))
        {
            if (!zones.ContainsKey(aisle.ZoneLogicalId))
                AddParentMissing(issues, "Aisle", Key(aisle), "Zone", aisle.ZoneLogicalId);
        }
        foreach (var rack in racks.Values.OrderBy(Key))
        {
            if (!floors.ContainsKey(rack.FloorLogicalId))
                AddParentMissing(issues, "Rack", Key(rack), "Floor", rack.FloorLogicalId);
            if (!zones.TryGetValue(rack.ZoneLogicalId, out var zone))
            {
                AddParentMissing(issues, "Rack", Key(rack), "Zone", rack.ZoneLogicalId);
            }
            else if (zone.FloorLogicalId != rack.FloorLogicalId)
            {
                AddParentMismatch(
                    issues,
                    "Rack",
                    Key(rack),
                    "Zone.Floor",
                    rack.FloorLogicalId,
                    zone.FloorLogicalId);
            }
            if (rack.AisleLogicalId.HasValue)
            {
                if (!aisles.TryGetValue(rack.AisleLogicalId.Value, out var aisle))
                {
                    AddParentMissing(
                        issues,
                        "Rack",
                        Key(rack),
                        "Aisle",
                        rack.AisleLogicalId.Value);
                }
                else if (aisle.ZoneLogicalId != rack.ZoneLogicalId)
                {
                    AddParentMismatch(
                        issues,
                        "Rack",
                        Key(rack),
                        "Aisle.Zone",
                        rack.ZoneLogicalId,
                        aisle.ZoneLogicalId);
                }
            }
        }
        foreach (var level in rackLevels.OrderBy(Key))
        {
            if (!racks.ContainsKey(level.RackLogicalId))
                AddParentMissing(
                    issues,
                    "RackLevel",
                    Key(level),
                    "Rack",
                    level.RackLogicalId);
        }
        foreach (var location in locations.OrderBy(Key))
        {
            if (!floors.ContainsKey(location.FloorLogicalId))
                AddParentMissing(
                    issues,
                    "Location",
                    Key(location),
                    "Floor",
                    location.FloorLogicalId);
            if (location.RackLogicalId.HasValue)
            {
                if (!racks.TryGetValue(location.RackLogicalId.Value, out var rack))
                {
                    AddParentMissing(
                        issues,
                        "Location",
                        Key(location),
                        "Rack",
                        location.RackLogicalId.Value);
                }
                else if (rack.FloorLogicalId != location.FloorLogicalId)
                {
                    AddParentMismatch(
                        issues,
                        "Location",
                        Key(location),
                        "Rack.Floor",
                        location.FloorLogicalId,
                        rack.FloorLogicalId);
                }
            }
        }

        var allLogicalIds = floors.Keys
            .Concat(zones.Keys)
            .Concat(aisles.Keys)
            .Concat(racks.Keys)
            .Concat(rackLevels.Select(Key))
            .Concat(locations.Select(Key))
            .Concat(elements.Select(Key))
            .ToHashSet();
        foreach (var element in elements.OrderBy(Key))
        {
            if (!floors.ContainsKey(element.FloorLogicalId))
                AddParentMissing(
                    issues,
                    "Element",
                    Key(element),
                    "Floor",
                    element.FloorLogicalId);
            if (element.ParentLogicalId.HasValue &&
                !allLogicalIds.Contains(element.ParentLogicalId.Value))
            {
                AddParentMissing(
                    issues,
                    "Element",
                    Key(element),
                    "Parent",
                    element.ParentLogicalId.Value);
            }
            if (element.LinkedLogicalId.HasValue &&
                IsInternalEntityType(element.LinkedEntityType) &&
                !allLogicalIds.Contains(element.LinkedLogicalId.Value))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Binding,
                    SpaceValidationIssueCodes.InternalBindingMissing,
                    new
                    {
                        element.LinkedEntityType,
                        element.LinkedLogicalId,
                    },
                    targetLogicalId: Key(element),
                    fieldPath: "/linkedLogicalId",
                    suggestedActionCode: "repair-element-binding");
            }
        }
    }

    private static void ValidateRackGeometry(
        IReadOnlyDictionary<Guid, SpaceValidationFloor> floors,
        IReadOnlyDictionary<Guid, SpaceValidationZone> zones,
        IReadOnlyDictionary<Guid, SpaceValidationRack> racks,
        IReadOnlyCollection<SpaceValidationRackLevel> rackLevels,
        IReadOnlyCollection<SpaceValidationLocation> locations,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var floorPolygons = floors.Values
            .Select(floor => (
                Id: Key(floor),
                Parsed: TryPolygon(floor.BoundaryJson, out var points, out _),
                Points: points))
            .ToDictionary(value => value.Id);
        var zonePolygons = zones.Values
            .Select(zone => (
                Id: Key(zone),
                Parsed: TryPolygon(zone.PolygonJson, out var points, out _),
                Points: points))
            .ToDictionary(value => value.Id);

        foreach (var zone in zones.Values.OrderBy(Key))
        {
            if (!zonePolygons[Key(zone)].Parsed)
            {
                AddGeometryInvalid(
                    issues,
                    "Zone",
                    Key(zone),
                    "/polygon",
                    "Polygon JSON is invalid.");
                continue;
            }
            if (floorPolygons.TryGetValue(zone.FloorLogicalId, out var floor) &&
                floor.Parsed &&
                !zonePolygons[Key(zone)].Points.All(point =>
                    PointInPolygonOrBoundary(point, floor.Points)))
            {
                AddOutOfBounds(
                    issues,
                    "Zone",
                    Key(zone),
                    zone.FloorLogicalId);
            }
        }

        foreach (var rack in racks.Values.OrderBy(Key))
        {
            if (rack.Width <= 0 || rack.Depth <= 0 || rack.Height <= 0)
            {
                AddGeometryInvalid(
                    issues,
                    "Rack",
                    Key(rack),
                    "/dimensions",
                    "Rack dimensions must be positive.");
                continue;
            }
            var corners = RackCorners(rack);
            if (floorPolygons.TryGetValue(rack.FloorLogicalId, out var floor) &&
                floor.Parsed &&
                !corners.All(point => PointInPolygonOrBoundary(point, floor.Points)))
            {
                AddOutOfBounds(
                    issues,
                    "Rack",
                    Key(rack),
                    rack.FloorLogicalId);
            }
            if (zonePolygons.TryGetValue(rack.ZoneLogicalId, out var zone) &&
                zone.Parsed &&
                !corners.All(point => PointInPolygonOrBoundary(point, zone.Points)))
            {
                AddOutOfBounds(
                    issues,
                    "Rack",
                    Key(rack),
                    rack.ZoneLogicalId);
            }
            if (floors.TryGetValue(rack.FloorLogicalId, out var floorRevision) &&
                floorRevision.Height > 0 &&
                rack.Z + rack.Height > floorRevision.Elevation + floorRevision.Height)
            {
                AddOutOfBounds(
                    issues,
                    "Rack",
                    Key(rack),
                    rack.FloorLogicalId);
            }
        }

        ValidateRackCollisions(racks.Values, issues);
        var levelsByRack = rackLevels
            .GroupBy(level => level.RackLogicalId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.LevelNo).ToArray());
        var locationsByRack = locations
            .Where(location => location.RackLogicalId.HasValue)
            .GroupBy(location => location.RackLogicalId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var rack in racks.Values.OrderBy(Key))
        {
            if (!levelsByRack.TryGetValue(Key(rack), out var levels) ||
                levels.Length == 0)
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Geometry,
                    SpaceValidationIssueCodes.RackLevelMissing,
                    new { rackLogicalId = Key(rack) },
                    targetLogicalId: Key(rack),
                    suggestedActionCode: "define-rack-levels");
                continue;
            }

            var levelNumbers = new HashSet<int>();
            var previousTop = 0;
            foreach (var level in levels)
            {
                var levelTop =
                    level.BottomZ + level.BeamHeight + level.ClearHeight;
                var invalid =
                    level.LevelNo <= 0 ||
                    !levelNumbers.Add(level.LevelNo) ||
                    level.BottomZ < previousTop ||
                    level.ClearHeight <= 0 ||
                    level.BinCount <= 0 ||
                    level.DepthCount <= 0 ||
                    level.CellWidth <= 0 ||
                    level.CellDepth <= 0 ||
                    level.BeamHeight < 0 ||
                    level.BinCount * level.CellWidth > rack.Width ||
                    level.DepthCount * level.CellDepth > rack.Depth ||
                    levelTop > rack.Height;
                if (invalid)
                {
                    Add(
                        issues,
                        SpaceIssueSeverity.Blocking,
                        SpaceValidationCategories.Geometry,
                        SpaceValidationIssueCodes.RackLevelInvalid,
                        new
                        {
                            rackLogicalId = Key(rack),
                            level.LevelNo,
                        },
                        targetLogicalId: Key(level),
                        suggestedActionCode: "correct-rack-level");
                }
                previousTop = Math.Max(previousTop, levelTop);
            }

            var rackLocations = locationsByRack.GetValueOrDefault(Key(rack)) ?? [];
            foreach (var level in levels)
            {
                var levelLocations = rackLocations
                    .Where(location => location.LevelNo == level.LevelNo)
                    .ToArray();
                var expected = level.BinCount * level.DepthCount;
                var uniqueSlots = levelLocations
                    .Select(location => (location.ColumnNo, location.DepthNo))
                    .Distinct()
                    .Count();
                if (levelLocations.Length != expected || uniqueSlots != expected)
                {
                    Add(
                        issues,
                        SpaceIssueSeverity.Blocking,
                        SpaceValidationCategories.Hierarchy,
                        SpaceValidationIssueCodes.RackLocationIncomplete,
                        new
                        {
                            rackLogicalId = Key(rack),
                            level.LevelNo,
                            expected,
                            actual = levelLocations.Length,
                            uniqueSlots,
                        },
                        targetLogicalId: Key(level),
                        suggestedActionCode: "regenerate-rack-locations");
                }
                foreach (var location in levelLocations.OrderBy(Key))
                {
                    if (location.ColumnNo <= 0 ||
                        location.ColumnNo > level.BinCount ||
                        location.DepthNo <= 0 ||
                        location.DepthNo > level.DepthCount ||
                        location.Width != level.CellWidth ||
                        location.Height != level.ClearHeight ||
                        location.Depth != level.CellDepth)
                    {
                        Add(
                            issues,
                            SpaceIssueSeverity.Blocking,
                            SpaceValidationCategories.Geometry,
                            SpaceValidationIssueCodes.LocationSlotInvalid,
                            new
                            {
                                rackLogicalId = Key(rack),
                                level.LevelNo,
                                location.ColumnNo,
                                location.DepthNo,
                            },
                            targetLogicalId: Key(location),
                            suggestedActionCode: "regenerate-rack-location");
                    }
                }
            }
        }
    }

    private static void ValidateRackCollisions(
        IEnumerable<SpaceValidationRack> racks,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var candidates = racks
            .Select(rack =>
            {
                var corners = RackCorners(rack);
                return new RackPolygon(
                    rack,
                    corners,
                    corners.Min(point => point.X),
                    corners.Max(point => point.X),
                    corners.Min(point => point.Y),
                    corners.Max(point => point.Y));
            })
            .OrderBy(value => value.MinX)
            .ThenBy(value => Key(value.Rack))
            .ToArray();
        var active = new List<RackPolygon>();
        foreach (var current in candidates)
        {
            active.RemoveAll(value => value.MaxX <= current.MinX);
            foreach (var other in active
                         .Where(value =>
                             value.Rack.FloorLogicalId ==
                             current.Rack.FloorLogicalId &&
                             value.MaxY > current.MinY &&
                             current.MaxY > value.MinY)
                         .OrderBy(value => Key(value.Rack)))
            {
                if (!PolygonsOverlap(other.Corners, current.Corners))
                    continue;
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Geometry,
                    SpaceValidationIssueCodes.GeometryCollision,
                    new
                    {
                        firstLogicalId = Key(other.Rack),
                        secondLogicalId = Key(current.Rack),
                    },
                    targetLogicalId: Key(current.Rack),
                    suggestedActionCode: "separate-rack-geometry");
            }
            active.Add(current);
        }
    }

    private static void ValidateElementGeometry(
        Guid tenantId,
        IReadOnlyDictionary<Guid, SpaceValidationFloor> floors,
        IReadOnlyCollection<SpaceValidationAssetVersion> assetVersions,
        IReadOnlyCollection<SpaceValidationElement> elements,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var assets = assetVersions.ToDictionary(value => value.Id);
        foreach (var element in elements.OrderBy(Key))
        {
            if (!TryValidateElementGeometry(element.GeometryJson, out var assetId, out var error))
            {
                AddGeometryInvalid(
                    issues,
                    "Element",
                    Key(element),
                    "/geometryJson",
                    error);
            }
            if (element.Width < 0 ||
                element.Height < 0 ||
                element.Depth < 0 ||
                element.RotationZ is < 0 or >= 360)
            {
                AddGeometryInvalid(
                    issues,
                    "Element",
                    Key(element),
                    "/placement",
                    "Element placement dimensions or rotation are invalid.");
            }
            if (!floors.ContainsKey(element.FloorLogicalId))
                continue;

            var hasAnyAssetField =
                element.ModelAssetId.HasValue ||
                element.ModelAssetScope.HasValue ||
                element.ModelAssetOwnerTenantId.HasValue;
            var hasAllAssetFields =
                element.ModelAssetId.HasValue &&
                element.ModelAssetScope.HasValue &&
                element.ModelAssetOwnerTenantId.HasValue;
            if (hasAnyAssetField != hasAllAssetFields ||
                (assetId.HasValue &&
                 (!element.ModelAssetId.HasValue ||
                  assetId != element.ModelAssetId)))
            {
                AddAssetBindingInvalid(issues, element, "Asset fields are incomplete or disagree with geometry.");
                continue;
            }
            if (!element.ModelAssetId.HasValue)
                continue;
            if (!assets.TryGetValue(element.ModelAssetId.Value, out var asset) ||
                asset.Status != SpaceAssetVersionStatus.Ready ||
                asset.Scope != element.ModelAssetScope ||
                asset.OwnerTenantId != element.ModelAssetOwnerTenantId ||
                (asset.Scope == SpaceAssetScope.System &&
                 asset.OwnerTenantId != Guid.Empty) ||
                (asset.Scope == SpaceAssetScope.Tenant &&
                 asset.OwnerTenantId != tenantId))
            {
                AddAssetBindingInvalid(
                    issues,
                    element,
                    "The concrete asset version is missing, unavailable, or outside tenant scope.");
            }
        }
    }

    private static void ValidatePublishedLocationIdentity(
        IReadOnlyCollection<SpaceValidationPublishedLocation> published,
        IReadOnlyCollection<SpaceValidationLocation> target,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        var publishedById = published.ToDictionary(value => value.LogicalId);
        var publishedByCode = published
            .Where(value => !string.IsNullOrWhiteSpace(value.LocationCode))
            .GroupBy(
                value => value.LocationCode!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var location in target.OrderBy(Key))
        {
            if (!string.IsNullOrWhiteSpace(location.LocationCode) &&
                publishedByCode.TryGetValue(
                    location.LocationCode.Trim(),
                    out var historical) &&
                historical.LogicalId != Key(location))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Binding,
                    SpaceValidationIssueCodes.LocationIdentityConflict,
                    new
                    {
                        location.LocationCode,
                        historicalLogicalId = historical.LogicalId,
                    },
                    targetLogicalId: Key(location),
                    suggestedActionCode: "restore-published-logical-id");
            }
            if (publishedById.TryGetValue(Key(location), out var previous) &&
                previous.ExternalBindingState == SpaceExternalBindingState.Bound &&
                !string.Equals(
                    previous.LocationCode?.Trim(),
                    location.LocationCode?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.Binding,
                    SpaceValidationIssueCodes.LocationCodeFrozen,
                    new
                    {
                        before = previous.LocationCode,
                        after = location.LocationCode,
                    },
                    targetLogicalId: Key(location),
                    fieldPath: "/locationCode",
                    suggestedActionCode: "use-certified-location-rename");
            }
        }
    }

    private static IReadOnlySet<Guid> FindUnboundDisableTargets(
        SpaceValidationSnapshot snapshot)
    {
        var publishedIds = snapshot.PublishedLocations
            .Select(value => value.LogicalId)
            .ToHashSet();
        var targetById = snapshot.Locations.ToDictionary(Key);
        return snapshot.ExistingIssues
            .Where(value =>
                value.Code == SpaceErrorCodes.WmsLocationUnbound &&
                value.TargetLogicalId.HasValue &&
                publishedIds.Contains(value.TargetLogicalId.Value) &&
                (!targetById.TryGetValue(
                     value.TargetLogicalId.Value,
                     out var target) ||
                 target.Revision.LifecycleState !=
                 SpaceLifecycleState.Active))
            .Select(value => value.TargetLogicalId!.Value)
            .ToHashSet();
    }

    private static void AppendExistingIssues(
        IEnumerable<SpaceValidationExistingIssue> existing,
        IReadOnlySet<Guid> unboundDisableTargets,
        ICollection<SpaceValidationIssueCandidate> issues)
    {
        foreach (var issue in existing)
        {
            issues.Add(
                new SpaceValidationIssueCandidate(
                    issue.Code == SpaceErrorCodes.WmsLocationUnbound &&
                    issue.TargetLogicalId.HasValue &&
                    unboundDisableTargets.Contains(
                        issue.TargetLogicalId.Value)
                        ? SpaceIssueSeverity.Blocking
                        : issue.Severity,
                    issue.Category ??
                    (issue.GenerationRunId.HasValue
                        ? SpaceValidationCategories.AiProvenance
                        : SpaceValidationCategories.ModelIssue),
                    issue.Code,
                    issue.MessageArgsJson,
                    issue.SourceRef,
                    issue.SourceId,
                    issue.TargetLogicalId,
                    issue.FieldPath,
                    issue.SuggestedActionCode,
                    issue.GenerationRunId,
                    issue.GenerationProposalId,
                    issue.EvidenceJson));
            if (issue.GenerationProposalId.HasValue &&
                !issue.GenerationRunId.HasValue)
            {
                Add(
                    issues,
                    SpaceIssueSeverity.Blocking,
                    SpaceValidationCategories.AiProvenance,
                    SpaceValidationIssueCodes.AiSourceIncomplete,
                    new { issue.GenerationProposalId },
                    sourceRef: issue.SourceRef,
                    targetLogicalId: issue.TargetLogicalId,
                    fieldPath: issue.FieldPath,
                    suggestedActionCode: "restore-ai-provenance");
            }
        }
    }

    private static void AddParentMissing(
        ICollection<SpaceValidationIssueCandidate> issues,
        string objectType,
        Guid logicalId,
        string parentType,
        Guid parentLogicalId) =>
        Add(
            issues,
            SpaceIssueSeverity.Blocking,
            SpaceValidationCategories.Hierarchy,
            SpaceValidationIssueCodes.ParentMissing,
            new { objectType, parentType, parentLogicalId },
            targetLogicalId: logicalId,
            suggestedActionCode: "repair-hierarchy");

    private static void AddParentMismatch(
        ICollection<SpaceValidationIssueCandidate> issues,
        string objectType,
        Guid logicalId,
        string parentType,
        Guid expected,
        Guid actual) =>
        Add(
            issues,
            SpaceIssueSeverity.Blocking,
            SpaceValidationCategories.Hierarchy,
            SpaceValidationIssueCodes.ParentMismatch,
            new { objectType, parentType, expected, actual },
            targetLogicalId: logicalId,
            suggestedActionCode: "repair-hierarchy");

    private static void AddGeometryInvalid(
        ICollection<SpaceValidationIssueCandidate> issues,
        string objectType,
        Guid logicalId,
        string fieldPath,
        string error) =>
        Add(
            issues,
            SpaceIssueSeverity.Blocking,
            SpaceValidationCategories.Geometry,
            SpaceValidationIssueCodes.GeometryInvalid,
            new { objectType, error },
            targetLogicalId: logicalId,
            fieldPath: fieldPath,
            suggestedActionCode: "correct-geometry");

    private static void AddOutOfBounds(
        ICollection<SpaceValidationIssueCandidate> issues,
        string objectType,
        Guid logicalId,
        Guid boundaryLogicalId) =>
        Add(
            issues,
            SpaceIssueSeverity.Blocking,
            SpaceValidationCategories.Geometry,
            SpaceValidationIssueCodes.GeometryOutOfBounds,
            new { objectType, boundaryLogicalId },
            targetLogicalId: logicalId,
            suggestedActionCode: "move-inside-boundary");

    private static void AddAssetBindingInvalid(
        ICollection<SpaceValidationIssueCandidate> issues,
        SpaceValidationElement element,
        string reason) =>
        Add(
            issues,
            SpaceIssueSeverity.Blocking,
            SpaceValidationCategories.Binding,
            SpaceValidationIssueCodes.AssetBindingInvalid,
            new
            {
                assetVersionId = element.ModelAssetId,
                reason,
            },
            targetLogicalId: Key(element),
            fieldPath: "/modelAssetId",
            suggestedActionCode: "select-valid-asset-version");

    private static void Add(
        ICollection<SpaceValidationIssueCandidate> issues,
        SpaceIssueSeverity severity,
        string category,
        string code,
        object messageArgs,
        string? sourceRef = null,
        Guid? sourceId = null,
        Guid? targetLogicalId = null,
        string? fieldPath = null,
        string? suggestedActionCode = null,
        Guid? generationRunId = null,
        Guid? generationProposalId = null,
        object? evidence = null)
    {
        issues.Add(
            new SpaceValidationIssueCandidate(
                severity,
                category,
                code,
                JsonSerializer.Serialize(messageArgs, JsonOptions),
                sourceRef,
                sourceId,
                targetLogicalId,
                fieldPath,
                suggestedActionCode,
                generationRunId,
                generationProposalId,
                evidence is null
                    ? "{}"
                    : JsonSerializer.Serialize(evidence, JsonOptions)));
    }

    private static IEnumerable<T> Active<T>(IEnumerable<T> values)
        where T : notnull =>
        values.Where(value => Revision(value).LifecycleState == SpaceLifecycleState.Active);

    private static SpaceValidationRevisionRef Revision<T>(T value) =>
        value switch
        {
            SpaceValidationFloor typed => typed.Revision,
            SpaceValidationZone typed => typed.Revision,
            SpaceValidationAisle typed => typed.Revision,
            SpaceValidationRack typed => typed.Revision,
            SpaceValidationRackLevel typed => typed.Revision,
            SpaceValidationLocation typed => typed.Revision,
            SpaceValidationElement typed => typed.Revision,
            _ => throw new ArgumentException(
                $"Unsupported validation revision type {typeof(T).Name}."),
        };

    private static Guid Key<T>(T value) => Revision(value).LogicalId;

    private static bool IsInternalEntityType(string? value) =>
        value is not null &&
        InternalEntityTypes.Contains(value.Trim());

    private static bool TryValidateElementGeometry(
        string json,
        out Guid? assetVersionId,
        out string error)
    {
        assetVersionId = null;
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schema) ||
                !schema.TryGetInt32(out var version) ||
                version != 1 ||
                !root.TryGetProperty("kind", out var kindProperty) ||
                kindProperty.ValueKind != JsonValueKind.String)
            {
                error = "Geometry schemaVersion 1 and kind are required.";
                return false;
            }

            var kind = kindProperty.GetString();
            var valid = kind switch
            {
                "point" =>
                    HasInteger(root, "x") &&
                    HasInteger(root, "y") &&
                    HasInteger(root, "z"),
                "path" =>
                    HasPointArray(root, "points", 2) &&
                    HasPositiveInteger(root, "width"),
                "polygon" =>
                    HasPointArray(root, "outer", 3) &&
                    HasPositiveInteger(root, "height"),
                "box" =>
                    HasPositiveInteger(root, "width") &&
                    HasPositiveInteger(root, "height") &&
                    HasPositiveInteger(root, "depth"),
                "asset" => TryReadAssetGeometry(root, out assetVersionId, out error),
                _ => false,
            };
            if (valid)
                return true;
            if (string.IsNullOrEmpty(error))
            {
                error = kind is "point" or "path" or "polygon" or "box"
                    ? $"Geometry values for kind '{kind}' are invalid."
                    : $"Unsupported geometry kind '{kind}'.";
            }
            return false;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryReadAssetGeometry(
        JsonElement root,
        out Guid? assetVersionId,
        out string error)
    {
        assetVersionId = null;
        error = string.Empty;
        if (!root.TryGetProperty("assetVersionId", out var asset) ||
            asset.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(asset.GetString(), out var parsed) ||
            parsed == Guid.Empty ||
            !root.TryGetProperty("transform", out var transform) ||
            transform.ValueKind != JsonValueKind.Object)
        {
            error = "Asset geometry requires assetVersionId and transform.";
            return false;
        }
        assetVersionId = parsed;
        return true;
    }

    private static bool HasPointArray(
        JsonElement root,
        string name,
        int minimum)
    {
        if (!root.TryGetProperty(name, out var points) ||
            points.ValueKind != JsonValueKind.Array ||
            points.GetArrayLength() < minimum)
        {
            return false;
        }
        return points.EnumerateArray().All(point =>
            point.ValueKind == JsonValueKind.Object &&
            HasInteger(point, "x") &&
            HasInteger(point, "y"));
    }

    private static bool HasInteger(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out _);

    private static bool HasPositiveInteger(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed) &&
        parsed > 0;

    private static bool TryPolygon(
        string json,
        out IReadOnlyList<Point2> points,
        out string error)
    {
        points = [];
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            JsonElement values;
            if (root.ValueKind == JsonValueKind.Array)
            {
                values = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     (root.TryGetProperty("points", out values) ||
                      root.TryGetProperty("outer", out values)))
            {
            }
            else
            {
                error = "Polygon points are required.";
                return false;
            }
            if (values.ValueKind != JsonValueKind.Array ||
                values.GetArrayLength() < 3)
            {
                error = "Polygon requires at least three points.";
                return false;
            }

            var parsed = new List<Point2>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Array &&
                    value.GetArrayLength() >= 2)
                {
                    var items = value.EnumerateArray().Take(2).ToArray();
                    if (!items[0].TryGetDouble(out var x) ||
                        !items[1].TryGetDouble(out var y))
                    {
                        error = "Polygon coordinates must be numeric.";
                        return false;
                    }
                    parsed.Add(new Point2(x, y));
                }
                else if (value.ValueKind == JsonValueKind.Object &&
                         value.TryGetProperty("x", out var xValue) &&
                         value.TryGetProperty("y", out var yValue) &&
                         xValue.TryGetDouble(out var x) &&
                         yValue.TryGetDouble(out var y))
                {
                    parsed.Add(new Point2(x, y));
                }
                else
                {
                    error = "Polygon point is invalid.";
                    return false;
                }
            }
            points = parsed;
            return PolygonArea(parsed) > 0.001;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static IReadOnlyList<Point2> RackCorners(SpaceValidationRack rack)
    {
        var radians = (double)rack.RotationZ * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return
        [
            Transform(0, 0),
            Transform(rack.Width, 0),
            Transform(rack.Width, rack.Depth),
            Transform(0, rack.Depth),
        ];

        Point2 Transform(double x, double y) =>
            new(
                rack.X + x * cos - y * sin,
                rack.Y + x * sin + y * cos);
    }

    private static bool PointInPolygonOrBoundary(
        Point2 point,
        IReadOnlyList<Point2> polygon)
    {
        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            if (PointOnSegment(point, previous, current))
                return true;
            if ((current.Y > point.Y) != (previous.Y > point.Y) &&
                point.X <
                (previous.X - current.X) *
                (point.Y - current.Y) /
                (previous.Y - current.Y) +
                current.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static bool PointOnSegment(Point2 point, Point2 start, Point2 end)
    {
        const double epsilon = 0.001;
        var cross =
            (point.Y - start.Y) * (end.X - start.X) -
            (point.X - start.X) * (end.Y - start.Y);
        if (Math.Abs(cross) > epsilon)
            return false;
        var dot =
            (point.X - start.X) * (end.X - start.X) +
            (point.Y - start.Y) * (end.Y - start.Y);
        if (dot < -epsilon)
            return false;
        var lengthSquared =
            Math.Pow(end.X - start.X, 2) +
            Math.Pow(end.Y - start.Y, 2);
        return dot <= lengthSquared + epsilon;
    }

    private static bool PolygonsOverlap(
        IReadOnlyList<Point2> first,
        IReadOnlyList<Point2> second)
    {
        foreach (var polygon in new[] { first, second })
        {
            for (var index = 0; index < polygon.Count; index++)
            {
                var start = polygon[index];
                var end = polygon[(index + 1) % polygon.Count];
                var axis = new Point2(-(end.Y - start.Y), end.X - start.X);
                var firstProjection = Project(first, axis);
                var secondProjection = Project(second, axis);
                if (firstProjection.Max <= secondProjection.Min + 0.001 ||
                    secondProjection.Max <= firstProjection.Min + 0.001)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static (double Min, double Max) Project(
        IReadOnlyList<Point2> polygon,
        Point2 axis)
    {
        var values = polygon.Select(point => point.X * axis.X + point.Y * axis.Y);
        return (values.Min(), values.Max());
    }

    private static double PolygonArea(IReadOnlyList<Point2> polygon)
    {
        var area = 0d;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Count];
            area += current.X * next.Y - next.X * current.Y;
        }
        return Math.Abs(area) / 2;
    }

    private sealed class CodeScopeComparer :
        IEqualityComparer<(string Type, Guid Parent, string Code)>
    {
        public bool Equals(
            (string Type, Guid Parent, string Code) x,
            (string Type, Guid Parent, string Code) y) =>
            x.Parent == y.Parent &&
            string.Equals(x.Type, y.Type, StringComparison.Ordinal) &&
            string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            (string Type, Guid Parent, string Code) value) =>
            HashCode.Combine(
                value.Parent,
                StringComparer.Ordinal.GetHashCode(value.Type),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Code));
    }

    private sealed record RackPolygon(
        SpaceValidationRack Rack,
        IReadOnlyList<Point2> Corners,
        double MinX,
        double MaxX,
        double MinY,
        double MaxY);

    private readonly record struct Point2(double X, double Y);

    private static readonly HashSet<string> InternalEntityTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Floor",
            "Zone",
            "Aisle",
            "Rack",
            "RackLevel",
            "Location",
            "Element",
        };
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadSemanticParser
{
    private const decimal DegradedGeometryConfidenceCeiling = 0.69m;

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SpaceCadSemanticPreviewV1 Parse(
        SpaceCadConversionRequest request,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceCadInventoryV1 inventory,
        SpaceCadMappingProfileV1 profile,
        SpaceCadMappingPreviewV1 mappingPreview)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(mappingPreview);

        if (!preparation.ReadyForParsing
            || preparation.Issues.Any(issue => issue.Severity == SpaceCadIssueSeverity.Blocking))
        {
            throw new InvalidDataException(
                "CAD semantic parsing requires coordinate preparation that is ready for parsing.");
        }

        SpaceCadConversionContract.ValidatePackage(request, preparation.Package);
        _ = SpaceCadCoordinatePreparation.SerializeMetadata(preparation.Metadata);
        var expectedInventory = SpaceCadInventory.Build(request, preparation);
        SpaceCadInventory.Validate(inventory);
        if (!inventory.InventorySha256.Equals(
                expectedInventory.InventorySha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD semantic inventory does not match the prepared CAD package.");
        }

        SpaceCadMapping.Validate(profile);
        SpaceCadMapping.ValidatePreview(mappingPreview);
        if (request.TenantId != mappingPreview.TenantId)
        {
            throw new UnauthorizedAccessException(
                "CAD source and semantic mapping preview belong to different tenants.");
        }
        var expectedMappingPreview = SpaceCadMapping.Preview(
            mappingPreview.TenantId,
            inventory,
            profile,
            mappingPreview.LayerOverrides);
        if (!mappingPreview.PreviewSha256.Equals(
                expectedMappingPreview.PreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD semantic mapping preview does not match its inventory and profile.");
        }
        if (!mappingPreview.ReadyForSemanticParsing)
        {
            throw new InvalidDataException(
                "CAD mapping preview is not ready for semantic parsing.");
        }

        var rules = profile.Rules.ToDictionary(rule => rule.RuleId, StringComparer.Ordinal);
        var layerDecisions = mappingPreview.Decisions
            .Where(decision => decision.SourceKind == SpaceCadMappingSourceKind.Layer)
            .ToDictionary(decision => decision.SourceKey, StringComparer.Ordinal);
        var blockDecisions = mappingPreview.Decisions
            .Where(decision => decision.SourceKind == SpaceCadMappingSourceKind.Block)
            .ToDictionary(decision => decision.SourceKey, StringComparer.Ordinal);
        var items = new List<SpaceCadSemanticPreviewItemV1>();
        var issues = new List<SpaceCadSemanticIssueV1>();

        foreach (var entity in preparation.Package.Entities
                     .OrderBy(entity => entity.SourceRef, StringComparer.Ordinal))
        {
            var decision = SelectDecision(
                entity,
                layerDecisions,
                blockDecisions,
                rules);
            if (decision is null)
                continue;

            items.Add(BuildItem(entity, decision, mappingPreview, issues));
        }

        foreach (var requiredRule in profile.Rules.Where(rule => rule.IsRequired))
        {
            if (HasUsableRequiredSource(
                    requiredRule,
                    preparation.Package,
                    items))
            {
                continue;
            }

            issues.Add(new SpaceCadSemanticIssueV1(
                "SPACE_CAD_SEMANTIC_REQUIRED_SOURCE_REJECTED",
                SpaceCadIssueSeverity.Blocking,
                SourceKind: requiredRule.SourceKind,
                SourceKey: requiredRule.Pattern,
                RuleId: requiredRule.RuleId));
        }

        if (items.Count == 0)
        {
            issues.Add(new SpaceCadSemanticIssueV1(
                "SPACE_CAD_SEMANTIC_NO_PROPOSALS",
                SpaceCadIssueSeverity.Info));
        }

        var canonicalItems = items
            .OrderBy(item => item.Source.SourceRef, StringComparer.Ordinal)
            .ToArray();
        foreach (var issue in SpaceCadSemanticQualityDiagnostics.DetectOverlaps(
                     canonicalItems))
        {
            issues.Add(issue);
        }
        var canonicalIssues = CanonicalIssues(issues);
        var summary = Summary(
            preparation.Package.Entities.Count,
            canonicalItems,
            canonicalIssues);
        var withoutHash = new SpaceCadSemanticPreviewV1(
            SpaceCadSemanticVersions.SchemaVersion,
            IsReadOnlyPreview: true,
            mappingPreview.TenantId,
            inventory.FloorLogicalId,
            inventory.FloorCode,
            inventory.SourceSha256,
            inventory.CoordinateTransformSha256,
            inventory.InventorySha256,
            mappingPreview.ProfileId,
            mappingPreview.ProfileVersion,
            mappingPreview.ProfileDefinitionSha256,
            mappingPreview.PreviewSha256,
            canonicalItems,
            canonicalIssues,
            summary,
            ReadyForConfirmation: summary.BlockingCount == 0
                                  && summary.ConfirmableCount > 0,
            SemanticPreviewSha256: string.Empty);
        var preview = withoutHash with
        {
            SemanticPreviewSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        Validate(preview);
        return preview;
    }

    public static string Serialize(SpaceCadSemanticPreviewV1 preview)
    {
        Validate(preview);
        return JsonSerializer.Serialize(preview, CanonicalJsonOptions);
    }

    public static void Validate(SpaceCadSemanticPreviewV1 preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(preview.Items);
        ArgumentNullException.ThrowIfNull(preview.Issues);
        ArgumentNullException.ThrowIfNull(preview.Summary);
        if (preview.SchemaVersion != SpaceCadSemanticVersions.SchemaVersion
            || !preview.IsReadOnlyPreview
            || preview.TenantId == Guid.Empty
            || preview.FloorLogicalId == Guid.Empty
            || string.IsNullOrWhiteSpace(preview.FloorCode)
            || preview.FloorCode.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !preview.FloorCode.Equals(preview.FloorCode.Trim(), StringComparison.Ordinal)
            || !IsSha256(preview.SourceSha256)
            || !IsSha256(preview.CoordinateTransformSha256)
            || !IsSha256(preview.InventorySha256)
            || preview.ProfileId == Guid.Empty
            || preview.ProfileVersion <= 0
            || !IsSha256(preview.ProfileDefinitionSha256)
            || !IsSha256(preview.MappingPreviewSha256)
            || !IsSha256(preview.SemanticPreviewSha256))
        {
            throw new InvalidDataException("CAD semantic preview identity is incomplete.");
        }

        if (!preview.Items.SequenceEqual(
                preview.Items.OrderBy(item => item.Source.SourceRef, StringComparer.Ordinal))
            || !preview.Issues.SequenceEqual(CanonicalIssues(preview.Issues)))
        {
            throw new InvalidDataException("CAD semantic preview records are not canonical.");
        }

        var previewIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in preview.Items)
        {
            ValidateItem(item, preview.MappingPreviewSha256);
            if (!previewIds.Add(item.PreviewObjectId)
                || !sourceRefs.Add(item.Source.SourceRef))
            {
                throw new InvalidDataException(
                    "CAD semantic preview item identities must be unique.");
            }
        }

        foreach (var issue in preview.Issues)
            ValidateIssue(issue);

        var expectedSummary = Summary(
            preview.Summary.SourceEntityCount,
            preview.Items,
            preview.Issues);
        if (expectedSummary != preview.Summary
            || preview.Summary.MappedEntityCount > preview.Summary.SourceEntityCount
            || preview.ReadyForConfirmation != (preview.Summary.BlockingCount == 0
                                                 && preview.Summary.ConfirmableCount > 0))
        {
            throw new InvalidDataException("CAD semantic preview summary is inconsistent.");
        }

        var expectedHash = ComputeSha256(CanonicalJson(
            preview with { SemanticPreviewSha256 = string.Empty }));
        if (!preview.SemanticPreviewSha256.Equals(expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD semantic preview hash does not match its content.");
        }
    }

    private static SpaceCadMappingDecisionV1? SelectDecision(
        SpaceCadIrEntityV1 entity,
        IReadOnlyDictionary<string, SpaceCadMappingDecisionV1> layerDecisions,
        IReadOnlyDictionary<string, SpaceCadMappingDecisionV1> blockDecisions,
        IReadOnlyDictionary<string, SpaceCadMappingRuleV1> rules)
    {
        if (entity.Type == SpaceCadIrEntityType.BlockReference
            && entity.BlockName is { } blockName
            && blockDecisions.TryGetValue(blockName, out var blockDecision)
            && blockDecision.Status == SpaceCadMappingDecisionStatus.Mapped
            && DecisionApplies(blockDecision, entity.Attributes, rules))
        {
            return blockDecision;
        }

        return layerDecisions.TryGetValue(entity.LayerId, out var layerDecision)
               && layerDecision.Status == SpaceCadMappingDecisionStatus.Mapped
            ? layerDecision
            : null;
    }

    private static bool HasUsableRequiredSource(
        SpaceCadMappingRuleV1 rule,
        SpaceCadIrPackageV1 package,
        IReadOnlyList<SpaceCadSemanticPreviewItemV1> items)
    {
        IEnumerable<string> matchingSourceRefs;
        if (rule.SourceKind == SpaceCadMappingSourceKind.Layer)
        {
            var layerIds = package.Layers
                .Where(layer => Matches(rule.MatchKind, rule.Pattern, layer.Name))
                .Select(layer => layer.LayerId)
                .ToHashSet(StringComparer.Ordinal);
            matchingSourceRefs = package.Entities
                .Where(entity => layerIds.Contains(entity.LayerId))
                .Select(entity => entity.SourceRef);
        }
        else
        {
            matchingSourceRefs = package.Entities
                .Where(entity => entity.Type == SpaceCadIrEntityType.BlockReference
                                 && entity.BlockName is { } blockName
                                 && Matches(rule.MatchKind, rule.Pattern, blockName)
                                 && (rule.AttributeName is null
                                     || entity.Attributes.Any(attribute =>
                                         attribute.Key.Equals(
                                             rule.AttributeName,
                                             StringComparison.OrdinalIgnoreCase)
                                         && Matches(
                                             rule.AttributeMatchKind!.Value,
                                             rule.AttributePattern!,
                                             attribute.Value))))
                .Select(entity => entity.SourceRef);
        }

        var requiredSourceRefs = matchingSourceRefs.ToHashSet(StringComparer.Ordinal);
        return items.Any(item => requiredSourceRefs.Contains(item.Source.SourceRef)
                                 && item.Disposition
                                 != SpaceCadSemanticDisposition.Rejected);
    }

    private static bool DecisionApplies(
        SpaceCadMappingDecisionV1 decision,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyDictionary<string, SpaceCadMappingRuleV1> rules)
    {
        if (decision.DecisionSource != SpaceCadMappingDecisionSource.ProfileRule)
            return true;
        if (decision.RuleId is null || !rules.TryGetValue(decision.RuleId, out var rule))
            throw new InvalidDataException("CAD semantic mapping rule cannot be resolved.");
        if (rule.AttributeName is null)
            return true;
        return attributes.Any(attribute =>
            attribute.Key.Equals(rule.AttributeName, StringComparison.OrdinalIgnoreCase)
            && Matches(rule.AttributeMatchKind!.Value, rule.AttributePattern!, attribute.Value));
    }

    private static SpaceCadSemanticPreviewItemV1 BuildItem(
        SpaceCadIrEntityV1 entity,
        SpaceCadMappingDecisionV1 decision,
        SpaceCadMappingPreviewV1 mappingPreview,
        ICollection<SpaceCadSemanticIssueV1> issues)
    {
        var previewObjectId = PreviewObjectId(
            mappingPreview.PreviewSha256,
            entity.SourceRef,
            decision);
        var appliedMapping = new SpaceCadSemanticAppliedMappingV1(
            decision.SourceKind,
            decision.SourceKey,
            decision.DecisionSource,
            decision.RuleId,
            decision.GeometryRule!.Value,
            NormalizeDimension(decision.DefaultHeightMillimeters),
            NormalizeDimension(decision.DefaultThicknessMillimeters));
        var source = new SpaceCadSemanticSourceReferenceV1(
            entity.SourceRef,
            entity.RawType,
            entity.LayerId,
            entity.BlockName,
            SortedAttributes(entity.Attributes));
        var target = decision.Target!.Value;

        if (!entity.IsSupported)
        {
            issues.Add(ItemIssue(
                "SPACE_CAD_SEMANTIC_ENTITY_UNSUPPORTED",
                SpaceCadIssueSeverity.Warning,
                entity,
                decision,
                previewObjectId,
                entity.RawType));
            return new SpaceCadSemanticPreviewItemV1(
                previewObjectId,
                DraftObjectKind(target),
                target,
                decision.TargetSubtype,
                source,
                appliedMapping,
                Geometry: null,
                Confidence: 0,
                SpaceCadSemanticDisposition.Rejected,
                IsConfirmable: false,
                IsSelected: false);
        }

        var geometryResult = BuildGeometry(entity, decision.GeometryRule.Value);
        if (geometryResult.Geometry is null)
        {
            issues.Add(ItemIssue(
                GeometryRejectionCode(geometryResult.DetailToken),
                SpaceCadIssueSeverity.Warning,
                entity,
                decision,
                previewObjectId,
                geometryResult.DetailToken));
            return new SpaceCadSemanticPreviewItemV1(
                previewObjectId,
                DraftObjectKind(target),
                target,
                decision.TargetSubtype,
                source,
                appliedMapping,
                Geometry: null,
                Confidence: 0,
                SpaceCadSemanticDisposition.Rejected,
                IsConfirmable: false,
                IsSelected: false);
        }

        var confidence = decision.ConfidenceWeight!.Value;
        if (geometryResult.IsDegraded)
        {
            confidence = decimal.Min(confidence, DegradedGeometryConfidenceCeiling);
            issues.Add(ItemIssue(
                "SPACE_CAD_SEMANTIC_BLOCK_FOOTPRINT_UNAVAILABLE",
                SpaceCadIssueSeverity.Warning,
                entity,
                decision,
                previewObjectId,
                geometryResult.DetailToken));
        }

        var disposition = confidence >= SpaceCadSemanticVersions.AutoAcceptanceThreshold
            ? SpaceCadSemanticDisposition.AutoAccepted
            : SpaceCadSemanticDisposition.Candidate;
        var confirmable = confidence >= SpaceCadSemanticVersions.ReviewThreshold;
        var selected = disposition == SpaceCadSemanticDisposition.AutoAccepted;
        if (confidence < SpaceCadSemanticVersions.ReviewThreshold)
        {
            issues.Add(ItemIssue(
                "SPACE_CAD_SEMANTIC_CANDIDATE_ONLY",
                SpaceCadIssueSeverity.Info,
                entity,
                decision,
                previewObjectId));
        }
        else if (confidence < SpaceCadSemanticVersions.AutoAcceptanceThreshold)
        {
            issues.Add(ItemIssue(
                "SPACE_CAD_SEMANTIC_CONFIDENCE_REVIEW",
                SpaceCadIssueSeverity.Warning,
                entity,
                decision,
                previewObjectId));
        }

        return new SpaceCadSemanticPreviewItemV1(
            previewObjectId,
            DraftObjectKind(target),
            target,
            decision.TargetSubtype,
            source,
            appliedMapping,
            geometryResult.Geometry,
            confidence,
            disposition,
            confirmable,
            selected);
    }

    private static string GeometryRejectionCode(string? detailToken) => detailToken switch
    {
        "closed-boundary-requires-closed-polyline-or-circle" =>
            "SPACE_CAD_SEMANTIC_BOUNDARY_UNCLOSED",
        "path-requires-two-distinct-points"
            or "polygon-requires-three-distinct-points"
            or "polygon-area-is-zero"
            or "circle-radius-is-missing"
            or "arc-radius-is-missing"
            or "block-transform-is-degenerate" =>
            "SPACE_CAD_SEMANTIC_ZERO_SIZE",
        _ => "SPACE_CAD_SEMANTIC_GEOMETRY_REJECTED",
    };

    private static GeometryBuildResult BuildGeometry(
        SpaceCadIrEntityV1 entity,
        SpaceCadGeometryRule rule)
    {
        try
        {
            return rule switch
            {
                SpaceCadGeometryRule.DirectGeometry => DirectGeometry(entity),
                SpaceCadGeometryRule.Centerline => CenterlineGeometry(entity),
                SpaceCadGeometryRule.ClosedBoundary => ClosedBoundaryGeometry(entity),
                SpaceCadGeometryRule.InsertionPoint => InsertionPointGeometry(entity),
                SpaceCadGeometryRule.BlockFootprint => BlockFootprintGeometry(entity),
                _ => GeometryBuildResult.Rejected("unsupported-geometry-rule"),
            };
        }
        catch (Exception exception) when (
            exception is InvalidDataException or OverflowException)
        {
            return GeometryBuildResult.Rejected(exception.Message);
        }
    }

    private static GeometryBuildResult DirectGeometry(SpaceCadIrEntityV1 entity) =>
        entity.Type switch
        {
            SpaceCadIrEntityType.Line => GeometryBuildResult.Valid(Path(entity, false)),
            SpaceCadIrEntityType.Polyline when entity.IsClosed =>
                GeometryBuildResult.Valid(Polygon(entity)),
            SpaceCadIrEntityType.Polyline => GeometryBuildResult.Valid(Path(entity, false)),
            SpaceCadIrEntityType.ClosedPolyline =>
                GeometryBuildResult.Valid(Polygon(entity)),
            SpaceCadIrEntityType.Circle => GeometryBuildResult.Valid(Circle(entity)),
            SpaceCadIrEntityType.Arc => GeometryBuildResult.Valid(Arc(entity)),
            SpaceCadIrEntityType.BlockReference =>
                GeometryBuildResult.Valid(BlockInstance(entity)),
            SpaceCadIrEntityType.Text => GeometryBuildResult.Valid(Point(entity)),
            _ => GeometryBuildResult.Rejected("direct-geometry-not-supported-for-entity"),
        };

    private static GeometryBuildResult CenterlineGeometry(SpaceCadIrEntityV1 entity) =>
        entity.Type switch
        {
            SpaceCadIrEntityType.Line or SpaceCadIrEntityType.Polyline =>
                GeometryBuildResult.Valid(Path(entity, entity.IsClosed)),
            SpaceCadIrEntityType.ClosedPolyline =>
                GeometryBuildResult.Valid(Path(entity, true)),
            SpaceCadIrEntityType.Circle => GeometryBuildResult.Valid(Circle(entity)),
            SpaceCadIrEntityType.Arc => GeometryBuildResult.Valid(Arc(entity)),
            _ => GeometryBuildResult.Rejected("centerline-requires-linear-or-curved-path"),
        };

    private static GeometryBuildResult ClosedBoundaryGeometry(SpaceCadIrEntityV1 entity) =>
        entity.Type switch
        {
            SpaceCadIrEntityType.Polyline when entity.IsClosed =>
                GeometryBuildResult.Valid(Polygon(entity)),
            SpaceCadIrEntityType.ClosedPolyline =>
                GeometryBuildResult.Valid(Polygon(entity)),
            SpaceCadIrEntityType.Circle => GeometryBuildResult.Valid(Circle(entity)),
            _ => GeometryBuildResult.Rejected("closed-boundary-requires-closed-polyline-or-circle"),
        };

    private static GeometryBuildResult InsertionPointGeometry(SpaceCadIrEntityV1 entity) =>
        entity.Type == SpaceCadIrEntityType.BlockReference
            ? GeometryBuildResult.Valid(Point(entity))
            : GeometryBuildResult.Rejected("insertion-point-requires-block-reference");

    private static GeometryBuildResult BlockFootprintGeometry(SpaceCadIrEntityV1 entity)
    {
        if (entity.Type != SpaceCadIrEntityType.BlockReference)
            return GeometryBuildResult.Rejected("block-footprint-requires-block-reference");
        if (entity.Bounds is { } bounds
            && bounds.MaxX > bounds.MinX
            && bounds.MaxY > bounds.MinY)
        {
            try
            {
                return GeometryBuildResult.Valid(Polygon(
                [
                    new SpaceCadPointV1(bounds.MinX, bounds.MinY),
                    new SpaceCadPointV1(bounds.MaxX, bounds.MinY),
                    new SpaceCadPointV1(bounds.MaxX, bounds.MaxY),
                    new SpaceCadPointV1(bounds.MinX, bounds.MaxY),
                ]));
            }
            catch (InvalidDataException)
            {
                // Sub-millimeter source bounds may collapse after canonical rounding.
            }
        }

        return GeometryBuildResult.Degraded(
            BlockInstance(entity),
            "block-instance-retained-without-invented-footprint");
    }

    private static SpaceCadSemanticGeometryV1 Point(SpaceCadIrEntityV1 entity)
    {
        var point = FirstPoint(entity);
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Point,
            [point],
            RadiusMillimeters: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            IsClosed: false,
            Transform: null,
            Bounds([point]));
    }

    private static SpaceCadSemanticGeometryV1 Path(
        SpaceCadIrEntityV1 entity,
        bool isClosed) => Path(ToPoints(entity.Points), isClosed);

    private static SpaceCadSemanticGeometryV1 Path(
        IReadOnlyList<SpaceCadMillimeterPointV1> sourcePoints,
        bool isClosed)
    {
        var points = RemoveConsecutiveDuplicates(sourcePoints);
        if (points.Count < 2)
            throw new InvalidDataException("path-requires-two-distinct-points");
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Path,
            points,
            RadiusMillimeters: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            isClosed,
            Transform: null,
            Bounds(points));
    }

    private static SpaceCadSemanticGeometryV1 Polygon(SpaceCadIrEntityV1 entity) =>
        Polygon(entity.Points);

    private static SpaceCadSemanticGeometryV1 Polygon(
        IReadOnlyList<SpaceCadPointV1> sourcePoints) => Polygon(ToPoints(sourcePoints));

    private static SpaceCadSemanticGeometryV1 Polygon(
        IReadOnlyList<SpaceCadMillimeterPointV1> sourcePoints)
    {
        var ring = RemoveConsecutiveDuplicates(sourcePoints).ToList();
        if (ring.Count > 1 && ring[0] == ring[^1])
            ring.RemoveAt(ring.Count - 1);
        if (ring.Distinct().Count() < 3)
            throw new InvalidDataException("polygon-requires-three-distinct-points");

        var signedArea = SignedArea(ring);
        if (signedArea == 0)
            throw new InvalidDataException("polygon-area-is-zero");
        if (signedArea < 0)
            ring.Reverse();
        ring = RotateToCanonicalStart(ring);
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Polygon,
            ring,
            RadiusMillimeters: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            IsClosed: true,
            Transform: null,
            Bounds(ring));
    }

    private static SpaceCadSemanticGeometryV1 Circle(SpaceCadIrEntityV1 entity)
    {
        var center = FirstPoint(entity);
        var radius = PositiveMillimeters(entity.Radius, "circle-radius-is-missing");
        var bounds = new SpaceCadMillimeterBoundsV1(
            checked(center.X - radius),
            checked(center.Y - radius),
            checked(center.X + radius),
            checked(center.Y + radius));
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Circle,
            [center],
            radius,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            IsClosed: true,
            Transform: null,
            bounds);
    }

    private static SpaceCadSemanticGeometryV1 Arc(SpaceCadIrEntityV1 entity)
    {
        var center = FirstPoint(entity);
        var radius = PositiveMillimeters(entity.Radius, "arc-radius-is-missing");
        if (entity.StartAngleDegrees is null || entity.EndAngleDegrees is null)
            throw new InvalidDataException("arc-angles-are-missing");
        var bounds = entity.Bounds is { } sourceBounds
            ? ToBounds(sourceBounds)
            : new SpaceCadMillimeterBoundsV1(
                checked(center.X - radius),
                checked(center.Y - radius),
                checked(center.X + radius),
                checked(center.Y + radius));
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Arc,
            [center],
            radius,
            entity.StartAngleDegrees,
            entity.EndAngleDegrees,
            IsClosed: false,
            Transform: null,
            bounds);
    }

    private static SpaceCadSemanticGeometryV1 BlockInstance(SpaceCadIrEntityV1 entity)
    {
        var point = FirstPoint(entity);
        var transform = entity.Transform;
        if ((transform.M11 * transform.M22) - (transform.M12 * transform.M21) == 0)
            throw new InvalidDataException("block-transform-is-degenerate");
        return new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.BlockInstance,
            [point],
            RadiusMillimeters: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            IsClosed: false,
            new SpaceCadSemanticTransformV1(
                transform.M11,
                transform.M12,
                transform.M21,
                transform.M22,
                Millimeters(transform.OffsetX),
                Millimeters(transform.OffsetY),
                Millimeters(transform.OffsetZ)),
            entity.Bounds is { } bounds ? ToBounds(bounds) : Bounds([point]));
    }

    private static SpaceCadMillimeterPointV1 FirstPoint(SpaceCadIrEntityV1 entity)
    {
        if (entity.Points.Count == 0)
            throw new InvalidDataException("entity-point-is-missing");
        return ToPoint(entity.Points[0]);
    }

    private static IReadOnlyList<SpaceCadMillimeterPointV1> ToPoints(
        IReadOnlyList<SpaceCadPointV1> points) => points.Select(ToPoint).ToArray();

    private static SpaceCadMillimeterPointV1 ToPoint(SpaceCadPointV1 point) =>
        new(Millimeters(point.X), Millimeters(point.Y), Millimeters(point.Z));

    private static int Millimeters(decimal value) =>
        decimal.ToInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static int PositiveMillimeters(decimal? value, string error)
    {
        if (value is null or <= 0)
            throw new InvalidDataException(error);
        var result = Millimeters(value.Value);
        if (result <= 0)
            throw new InvalidDataException(error);
        return result;
    }

    private static int? NormalizeDimension(decimal? value)
    {
        if (value is null)
            return null;
        return Math.Max(1, Millimeters(value.Value));
    }

    private static IReadOnlyList<SpaceCadMillimeterPointV1> RemoveConsecutiveDuplicates(
        IReadOnlyList<SpaceCadMillimeterPointV1> points)
    {
        var result = new List<SpaceCadMillimeterPointV1>(points.Count);
        foreach (var point in points)
        {
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        }
        return result;
    }

    private static decimal SignedArea(IReadOnlyList<SpaceCadMillimeterPointV1> ring)
    {
        decimal twiceArea = 0;
        for (var index = 0; index < ring.Count; index++)
        {
            var current = ring[index];
            var next = ring[(index + 1) % ring.Count];
            twiceArea += ((decimal)current.X * next.Y) - ((decimal)next.X * current.Y);
        }
        return twiceArea;
    }

    private static List<SpaceCadMillimeterPointV1> RotateToCanonicalStart(
        IReadOnlyList<SpaceCadMillimeterPointV1> ring)
    {
        var start = 0;
        for (var index = 1; index < ring.Count; index++)
        {
            if (Compare(ring[index], ring[start]) < 0)
                start = index;
        }
        return Enumerable.Range(0, ring.Count)
            .Select(offset => ring[(start + offset) % ring.Count])
            .ToList();
    }

    private static int Compare(
        SpaceCadMillimeterPointV1 left,
        SpaceCadMillimeterPointV1 right)
    {
        var x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;
        var y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : left.Z.CompareTo(right.Z);
    }

    private static SpaceCadMillimeterBoundsV1 Bounds(
        IReadOnlyList<SpaceCadMillimeterPointV1> points) =>
        new(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));

    private static SpaceCadMillimeterBoundsV1 ToBounds(SpaceCadBoundsV1 bounds)
    {
        var result = new SpaceCadMillimeterBoundsV1(
            Millimeters(bounds.MinX),
            Millimeters(bounds.MinY),
            Millimeters(bounds.MaxX),
            Millimeters(bounds.MaxY));
        if (result.MinX > result.MaxX || result.MinY > result.MaxY)
            throw new InvalidDataException("geometry-bounds-are-inverted");
        return result;
    }

    private static SpaceCadSemanticDraftObjectKind DraftObjectKind(
        SpaceCadSemanticTarget target) => target switch
        {
            SpaceCadSemanticTarget.Zone => SpaceCadSemanticDraftObjectKind.Zone,
            SpaceCadSemanticTarget.Aisle => SpaceCadSemanticDraftObjectKind.Aisle,
            SpaceCadSemanticTarget.Rack => SpaceCadSemanticDraftObjectKind.Rack,
            _ => SpaceCadSemanticDraftObjectKind.Element,
        };

    private static SpaceCadSemanticIssueV1 ItemIssue(
        string code,
        SpaceCadIssueSeverity severity,
        SpaceCadIrEntityV1 entity,
        SpaceCadMappingDecisionV1 decision,
        string previewObjectId,
        string? detailToken = null) =>
        new(
            code,
            severity,
            entity.SourceRef,
            previewObjectId,
            decision.SourceKind,
            decision.SourceKey,
            decision.RuleId,
            BoundedDetail(detailToken));

    private static string? BoundedDetail(string? value)
    {
        if (value is null || value.Length <= SpaceCadConversionContract.MaximumIdentifierLength)
            return value;
        return $"sha256:{ComputeSha256(value)}";
    }

    private static string PreviewObjectId(
        string mappingPreviewSha256,
        string sourceRef,
        SpaceCadMappingDecisionV1 decision)
    {
        var hash = ComputeSha256(string.Join(
            '|',
            mappingPreviewSha256,
            sourceRef,
            decision.SourceKind,
            decision.SourceKey,
            decision.RuleId ?? "override"));
        return $"cad-preview-{hash[..32]}";
    }

    private static SpaceCadSemanticPreviewSummaryV1 Summary(
        long sourceEntityCount,
        IReadOnlyList<SpaceCadSemanticPreviewItemV1> items,
        IReadOnlyList<SpaceCadSemanticIssueV1> issues) =>
        new(
            sourceEntityCount,
            items.Count,
            items.LongCount(item =>
                item.Disposition == SpaceCadSemanticDisposition.AutoAccepted),
            items.LongCount(item =>
                item.Disposition == SpaceCadSemanticDisposition.Candidate),
            items.LongCount(item =>
                item.Disposition == SpaceCadSemanticDisposition.Rejected),
            items.LongCount(item => item.IsConfirmable),
            items.LongCount(item => item.IsSelected),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Info),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Warning),
            issues.LongCount(issue => issue.Severity == SpaceCadIssueSeverity.Blocking));

    private static SpaceCadSemanticIssueV1[] CanonicalIssues(
        IEnumerable<SpaceCadSemanticIssueV1> issues) => issues
        .OrderByDescending(issue => issue.Severity)
        .ThenBy(issue => issue.Code, StringComparer.Ordinal)
        .ThenBy(issue => issue.SourceRef, StringComparer.Ordinal)
        .ThenBy(issue => issue.PreviewObjectId, StringComparer.Ordinal)
        .ThenBy(issue => issue.SourceKind)
        .ThenBy(issue => issue.SourceKey, StringComparer.Ordinal)
        .ThenBy(issue => issue.RuleId, StringComparer.Ordinal)
        .ThenBy(issue => issue.DetailToken, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyDictionary<string, string> SortedAttributes(
        IReadOnlyDictionary<string, string> attributes) =>
        attributes
            .OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(
                attribute => attribute.Key,
                attribute => attribute.Value,
                StringComparer.Ordinal);

    private static void ValidateItem(
        SpaceCadSemanticPreviewItemV1 item,
        string mappingPreviewSha256)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Source);
        ArgumentNullException.ThrowIfNull(item.AppliedMapping);
        ArgumentNullException.ThrowIfNull(item.Source.Attributes);
        RequireToken(item.PreviewObjectId, nameof(item.PreviewObjectId));
        RequireSourceRef(item.Source.SourceRef);
        RequireToken(item.Source.RawType, nameof(item.Source.RawType));
        RequireToken(item.Source.LayerId, nameof(item.Source.LayerId));
        if (item.Source.BlockName is not null)
            RequireToken(item.Source.BlockName, nameof(item.Source.BlockName));
        if (!item.Source.Attributes.SequenceEqual(
                item.Source.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("CAD semantic source attributes are not canonical.");
        }
        foreach (var (key, value) in item.Source.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key.Length > SpaceCadConversionContract.MaximumAttributeKeyLength
                || value.Length > SpaceCadConversionContract.MaximumAttributeValueLength)
            {
                throw new InvalidDataException("CAD semantic source attribute is invalid.");
            }
        }

        if (!Enum.IsDefined(item.DraftObjectKind)
            || !Enum.IsDefined(item.Target)
            || !Enum.IsDefined(item.Disposition)
            || !Enum.IsDefined(item.AppliedMapping.SourceKind)
            || !Enum.IsDefined(item.AppliedMapping.DecisionSource)
            || !Enum.IsDefined(item.AppliedMapping.GeometryRule)
            || item.DraftObjectKind != DraftObjectKind(item.Target)
            || item.Confidence is < 0 or > 1)
        {
            throw new InvalidDataException("CAD semantic preview item classification is invalid.");
        }
        if (item.TargetSubtype is not null)
            RequireToken(item.TargetSubtype, nameof(item.TargetSubtype));
        RequireToken(item.AppliedMapping.SourceKey, nameof(item.AppliedMapping.SourceKey));
        if (item.AppliedMapping.RuleId is not null)
            RequireToken(item.AppliedMapping.RuleId, nameof(item.AppliedMapping.RuleId));
        if (item.AppliedMapping.DecisionSource == SpaceCadMappingDecisionSource.ProfileRule
            != (item.AppliedMapping.RuleId is not null)
            || item.AppliedMapping.DecisionSource == SpaceCadMappingDecisionSource.None
            || (item.AppliedMapping.DecisionSource
                == SpaceCadMappingDecisionSource.LayerOverride
                && item.AppliedMapping.SourceKind != SpaceCadMappingSourceKind.Layer)
            || item.AppliedMapping.DefaultHeightMillimeters is <= 0
            || item.AppliedMapping.DefaultThicknessMillimeters is <= 0)
        {
            throw new InvalidDataException("CAD semantic applied mapping is invalid.");
        }

        var expectedId = PreviewObjectId(
            mappingPreviewSha256,
            item.Source.SourceRef,
            new SpaceCadMappingDecisionV1(
                item.AppliedMapping.SourceKind,
                item.AppliedMapping.SourceKey,
                item.AppliedMapping.SourceKind == SpaceCadMappingSourceKind.Layer
                    ? item.AppliedMapping.SourceKey
                    : null,
                ObjectCount: 0,
                SpaceCadMappingDecisionStatus.Mapped,
                item.AppliedMapping.DecisionSource,
                item.AppliedMapping.RuleId,
                item.Target,
                item.TargetSubtype,
                item.AppliedMapping.GeometryRule,
                item.AppliedMapping.DefaultHeightMillimeters,
                item.AppliedMapping.DefaultThicknessMillimeters,
                item.Confidence));
        if (!item.PreviewObjectId.Equals(expectedId, StringComparison.Ordinal))
            throw new InvalidDataException("CAD semantic preview object ID is not deterministic.");

        if (item.Disposition == SpaceCadSemanticDisposition.Rejected)
        {
            if (item.Geometry is not null || item.Confidence != 0
                || item.IsConfirmable || item.IsSelected)
            {
                throw new InvalidDataException("Rejected CAD semantic item is inconsistent.");
            }
            return;
        }

        ArgumentNullException.ThrowIfNull(item.Geometry);
        ValidateGeometry(item.Geometry);
        var expectedDisposition =
            item.Confidence >= SpaceCadSemanticVersions.AutoAcceptanceThreshold
                ? SpaceCadSemanticDisposition.AutoAccepted
                : SpaceCadSemanticDisposition.Candidate;
        if (item.Disposition != expectedDisposition
            || item.IsConfirmable !=
               (item.Confidence >= SpaceCadSemanticVersions.ReviewThreshold)
            || item.IsSelected !=
               (item.Disposition == SpaceCadSemanticDisposition.AutoAccepted))
        {
            throw new InvalidDataException("CAD semantic confidence disposition is inconsistent.");
        }
    }

    private static void ValidateGeometry(SpaceCadSemanticGeometryV1 geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(geometry.Points);
        ArgumentNullException.ThrowIfNull(geometry.Bounds);
        if (!Enum.IsDefined(geometry.Kind)
            || geometry.Bounds.MinX > geometry.Bounds.MaxX
            || geometry.Bounds.MinY > geometry.Bounds.MaxY
            || geometry.Points.Any(point => point.X < geometry.Bounds.MinX
                                            || point.X > geometry.Bounds.MaxX
                                            || point.Y < geometry.Bounds.MinY
                                            || point.Y > geometry.Bounds.MaxY))
        {
            throw new InvalidDataException("CAD semantic geometry bounds are invalid.");
        }

        var valid = geometry.Kind switch
        {
            SpaceCadSemanticGeometryKind.Point =>
                geometry.Points.Count == 1 && !geometry.IsClosed
                && geometry.RadiusMillimeters is null
                && geometry.StartAngleDegrees is null
                && geometry.EndAngleDegrees is null
                && geometry.Transform is null,
            SpaceCadSemanticGeometryKind.Path =>
                geometry.Points.Count >= 2
                && geometry.Points.Distinct().Count() >= 2
                && geometry.RadiusMillimeters is null
                && geometry.StartAngleDegrees is null
                && geometry.EndAngleDegrees is null
                && geometry.Transform is null,
            SpaceCadSemanticGeometryKind.Polygon =>
                geometry.Points.Distinct().Count() >= 3 && geometry.IsClosed
                && SignedArea(geometry.Points) > 0
                && geometry.RadiusMillimeters is null
                && geometry.StartAngleDegrees is null
                && geometry.EndAngleDegrees is null
                && geometry.Transform is null,
            SpaceCadSemanticGeometryKind.Circle =>
                geometry.Points.Count == 1 && geometry.IsClosed
                && geometry.RadiusMillimeters is > 0
                && geometry.StartAngleDegrees is null
                && geometry.EndAngleDegrees is null
                && geometry.Transform is null,
            SpaceCadSemanticGeometryKind.Arc =>
                geometry.Points.Count == 1 && !geometry.IsClosed
                && geometry.RadiusMillimeters is > 0
                && geometry.StartAngleDegrees is not null
                && geometry.EndAngleDegrees is not null
                && geometry.Transform is null,
            SpaceCadSemanticGeometryKind.BlockInstance =>
                geometry.Points.Count == 1 && !geometry.IsClosed
                && geometry.RadiusMillimeters is null
                && geometry.StartAngleDegrees is null
                && geometry.EndAngleDegrees is null
                && geometry.Transform is not null
                && ((geometry.Transform.M11 * geometry.Transform.M22)
                    - (geometry.Transform.M12 * geometry.Transform.M21)) != 0,
            _ => false,
        };
        if (!valid)
            throw new InvalidDataException("CAD semantic geometry payload is invalid.");
    }

    private static void ValidateIssue(SpaceCadSemanticIssueV1 issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        RequireToken(issue.Code, nameof(issue.Code));
        if (!Enum.IsDefined(issue.Severity)
            || (issue.SourceKind is { } kind && !Enum.IsDefined(kind)))
        {
            throw new InvalidDataException("CAD semantic issue classification is invalid.");
        }
        if (issue.SourceRef is not null)
            RequireSourceRef(issue.SourceRef);
        if (issue.PreviewObjectId is not null)
            RequireToken(issue.PreviewObjectId, nameof(issue.PreviewObjectId));
        if (issue.SourceKey is not null)
            RequireToken(issue.SourceKey, nameof(issue.SourceKey));
        if (issue.RuleId is not null)
            RequireToken(issue.RuleId, nameof(issue.RuleId));
        if (issue.DetailToken is { Length: > SpaceCadConversionContract.MaximumIdentifierLength })
            throw new InvalidDataException("CAD semantic issue detail token is too long.");
    }

    private static bool Matches(
        SpaceCadMappingMatchKind matchKind,
        string pattern,
        string value) => matchKind switch
        {
            SpaceCadMappingMatchKind.Exact => value.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            SpaceCadMappingMatchKind.Glob => Regex.IsMatch(
                value,
                $"^{Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal)}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100)),
            SpaceCadMappingMatchKind.Regex => Regex.IsMatch(
                value,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100)),
            _ => false,
        };

    private static void RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"CAD semantic token '{parameterName}' is invalid.");
        }
    }

    private static void RequireSourceRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumSourceReferenceLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("CAD semantic source reference is invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record GeometryBuildResult(
        SpaceCadSemanticGeometryV1? Geometry,
        bool IsDegraded,
        string? DetailToken)
    {
        public static GeometryBuildResult Valid(SpaceCadSemanticGeometryV1 geometry) =>
            new(geometry, IsDegraded: false, DetailToken: null);

        public static GeometryBuildResult Degraded(
            SpaceCadSemanticGeometryV1 geometry,
            string detailToken) => new(geometry, IsDegraded: true, detailToken);

        public static GeometryBuildResult Rejected(string detailToken) =>
            new(Geometry: null, IsDegraded: false, detailToken);
    }
}

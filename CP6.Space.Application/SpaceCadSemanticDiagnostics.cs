using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadSemanticDiagnostics
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SpaceCadSemanticDiagnosticIndexV1 Build(
        SpaceCadConversionRequest request,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceCadInventoryV1 inventory,
        SpaceCadMappingProfileV1 profile,
        SpaceCadMappingPreviewV1 mappingPreview,
        SpaceCadSemanticPreviewV1 semanticPreview)
    {
        ArgumentNullException.ThrowIfNull(semanticPreview);
        var expectedSemanticPreview = SpaceCadSemanticParser.Parse(
            request,
            preparation,
            inventory,
            profile,
            mappingPreview);
        SpaceCadSemanticParser.Validate(semanticPreview);
        if (!semanticPreview.SemanticPreviewSha256.Equals(
                expectedSemanticPreview.SemanticPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD semantic diagnostic input does not match the prepared semantic chain.");
        }

        var entityBySourceRef = preparation.Package.Entities.ToDictionary(
            entity => entity.SourceRef,
            StringComparer.Ordinal);
        var evidence = semanticPreview.Items
            .OrderBy(item => item.Source.SourceRef, StringComparer.Ordinal)
            .Select(item => Evidence(
                semanticPreview,
                item,
                entityBySourceRef[item.Source.SourceRef]))
            .ToArray();
        var evidenceByPreviewId = evidence.ToDictionary(
            item => item.PreviewObjectId,
            StringComparer.Ordinal);
        var evidenceBySourceRef = evidence.ToDictionary(
            item => item.SourceRef,
            StringComparer.Ordinal);
        var diagnostics = mappingPreview.Issues
            .Select(issue => MappingDiagnostic(
                semanticPreview,
                preparation,
                inventory,
                profile,
                issue))
            .Concat(semanticPreview.Issues.Select(issue => SemanticDiagnostic(
                semanticPreview,
                preparation,
                inventory,
                issue,
                evidenceByPreviewId,
                evidenceBySourceRef)))
            .ToArray();
        diagnostics = CanonicalDiagnostics(diagnostics)
            .Select((diagnostic, index) => diagnostic with
            {
                DiagnosticId = DiagnosticId(
                    semanticPreview.SemanticPreviewSha256,
                    diagnostic,
                    index),
            })
            .ToArray();

        var summary = Summary(
            semanticPreview.Summary.SourceEntityCount,
            evidence,
            diagnostics);
        var withoutHash = new SpaceCadSemanticDiagnosticIndexV1(
            SpaceCadSemanticDiagnosticVersions.SchemaVersion,
            IsReadOnlyIndex: true,
            semanticPreview.TenantId,
            semanticPreview.FloorLogicalId,
            semanticPreview.FloorCode,
            semanticPreview.SourceSha256,
            semanticPreview.CoordinateTransformSha256,
            semanticPreview.InventorySha256,
            semanticPreview.ProfileId,
            semanticPreview.ProfileVersion,
            semanticPreview.ProfileDefinitionSha256,
            semanticPreview.MappingPreviewSha256,
            semanticPreview.SemanticPreviewSha256,
            evidence,
            diagnostics,
            summary,
            DiagnosticIndexSha256: string.Empty);
        var index = withoutHash with
        {
            DiagnosticIndexSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        Validate(index);
        return index;
    }

    public static string Serialize(SpaceCadSemanticDiagnosticIndexV1 index)
    {
        Validate(index);
        return JsonSerializer.Serialize(index, CanonicalJsonOptions);
    }

    public static void Validate(SpaceCadSemanticDiagnosticIndexV1 index)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(index.Evidence);
        ArgumentNullException.ThrowIfNull(index.Diagnostics);
        ArgumentNullException.ThrowIfNull(index.Summary);
        if (index.SchemaVersion != SpaceCadSemanticDiagnosticVersions.SchemaVersion
            || !index.IsReadOnlyIndex
            || index.TenantId == Guid.Empty
            || index.FloorLogicalId == Guid.Empty
            || string.IsNullOrWhiteSpace(index.FloorCode)
            || index.FloorCode.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !index.FloorCode.Equals(index.FloorCode.Trim(), StringComparison.Ordinal)
            || !IsSha256(index.SourceSha256)
            || !IsSha256(index.CoordinateTransformSha256)
            || !IsSha256(index.InventorySha256)
            || index.ProfileId == Guid.Empty
            || index.ProfileVersion <= 0
            || !IsSha256(index.ProfileDefinitionSha256)
            || !IsSha256(index.MappingPreviewSha256)
            || !IsSha256(index.SemanticPreviewSha256)
            || !IsSha256(index.DiagnosticIndexSha256))
        {
            throw new InvalidDataException("CAD semantic diagnostic index identity is incomplete.");
        }

        if (!index.Evidence.SequenceEqual(
                index.Evidence.OrderBy(item => item.SourceRef, StringComparer.Ordinal))
            || !index.Diagnostics.SequenceEqual(CanonicalDiagnostics(index.Diagnostics)))
        {
            throw new InvalidDataException("CAD semantic diagnostic records are not canonical.");
        }

        var previewIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in index.Evidence)
        {
            ValidateEvidence(
                item,
                index.FloorLogicalId,
                index.SemanticPreviewSha256);
            if (!previewIds.Add(item.PreviewObjectId) || !sourceRefs.Add(item.SourceRef))
                throw new InvalidDataException("CAD semantic evidence identities are duplicated.");
        }

        var diagnosticIds = new HashSet<string>(StringComparer.Ordinal);
        for (var position = 0; position < index.Diagnostics.Count; position++)
        {
            var diagnostic = index.Diagnostics[position];
            ValidateDiagnostic(diagnostic, index.FloorLogicalId);
            var expectedId = DiagnosticId(index.SemanticPreviewSha256, diagnostic, position);
            if (!diagnostic.DiagnosticId.Equals(expectedId, StringComparison.Ordinal)
                || !diagnosticIds.Add(diagnostic.DiagnosticId))
            {
                throw new InvalidDataException(
                    "CAD semantic diagnostic identity is not deterministic and unique.");
            }
        }

        var expectedSummary = Summary(
            index.Summary.SourceEntityCount,
            index.Evidence,
            index.Diagnostics);
        if (expectedSummary != index.Summary
            || index.Summary.ProposalCount > index.Summary.SourceEntityCount)
        {
            throw new InvalidDataException("CAD semantic diagnostic summary is inconsistent.");
        }

        var expectedHash = ComputeSha256(CanonicalJson(
            index with { DiagnosticIndexSha256 = string.Empty }));
        if (!index.DiagnosticIndexSha256.Equals(expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD semantic diagnostic index hash does not match its content.");
        }
    }

    public static SpaceCadSemanticPageV1<SpaceCadSemanticEvidenceV1> QueryEvidence(
        SpaceCadSemanticDiagnosticIndexV1 index,
        SpaceCadSemanticEvidenceQueryV1 query)
    {
        Validate(index);
        ArgumentNullException.ThrowIfNull(query);
        ValidatePage(query.Offset, query.Limit);
        ValidateOptionalToken(query.LayerId, nameof(query.LayerId));
        ValidateOptionalSourceRef(query.SourceRef);
        if (query.ConfidenceBand is { } band && !Enum.IsDefined(band)
            || query.Target is { } target && !Enum.IsDefined(target))
        {
            throw new ArgumentException("CAD semantic evidence query enum is invalid.");
        }

        var diagnosticPreviewIds = query.OnlyWithDiagnostics
            ? index.Diagnostics
                .Where(item => item.PreviewObjectId is not null)
                .Select(item => item.PreviewObjectId!)
                .ToHashSet(StringComparer.Ordinal)
            : null;
        var filtered = index.Evidence.Where(item =>
            (query.ConfidenceBand is null || item.ConfidenceBand == query.ConfidenceBand)
            && (query.Target is null || item.Target == query.Target)
            && (query.LayerId is null || item.Location.LayerId?.Equals(
                query.LayerId,
                StringComparison.OrdinalIgnoreCase) == true)
            && (query.SourceRef is null || item.SourceRef.Equals(
                query.SourceRef,
                StringComparison.Ordinal))
            && (diagnosticPreviewIds is null
                || diagnosticPreviewIds.Contains(item.PreviewObjectId)));
        return Page(filtered, query.Offset, query.Limit);
    }

    public static SpaceCadSemanticPageV1<SpaceCadSemanticDiagnosticV1> QueryDiagnostics(
        SpaceCadSemanticDiagnosticIndexV1 index,
        SpaceCadSemanticDiagnosticQueryV1 query)
    {
        Validate(index);
        ArgumentNullException.ThrowIfNull(query);
        ValidatePage(query.Offset, query.Limit);
        ValidateOptionalToken(query.Code, nameof(query.Code));
        ValidateOptionalToken(query.LayerId, nameof(query.LayerId));
        ValidateOptionalSourceRef(query.SourceRef);
        if (query.Severity is { } severity && !Enum.IsDefined(severity)
            || query.Origin is { } origin && !Enum.IsDefined(origin))
        {
            throw new ArgumentException("CAD semantic diagnostic query enum is invalid.");
        }

        var filtered = index.Diagnostics.Where(item =>
            (query.Severity is null || item.Severity == query.Severity)
            && (query.Origin is null || item.Origin == query.Origin)
            && (query.Code is null || item.Code.Equals(
                query.Code,
                StringComparison.OrdinalIgnoreCase))
            && (query.LayerId is null || item.Location.LayerId?.Equals(
                query.LayerId,
                StringComparison.OrdinalIgnoreCase) == true)
            && (query.SourceRef is null || item.SourceRef?.Equals(
                query.SourceRef,
                StringComparison.Ordinal) == true)
            && (!query.OnlyLocatable || item.Location.CanFocusCanvas));
        return Page(filtered, query.Offset, query.Limit);
    }

    private static SpaceCadSemanticEvidenceV1 Evidence(
        SpaceCadSemanticPreviewV1 semanticPreview,
        SpaceCadSemanticPreviewItemV1 item,
        SpaceCadIrEntityV1 entity)
    {
        var withoutHash = new SpaceCadSemanticEvidenceV1(
            item.PreviewObjectId,
            item.Source.SourceRef,
            semanticPreview.SemanticPreviewSha256,
            item.Target,
            item.TargetSubtype,
            item.Confidence,
            item.Disposition,
            ConfidenceBand(item),
            item.AppliedMapping.SourceKind,
            item.AppliedMapping.SourceKey,
            item.AppliedMapping.DecisionSource,
            item.AppliedMapping.RuleId,
            item.AppliedMapping.GeometryRule,
            EntityLocation(
                semanticPreview.FloorLogicalId,
                item,
                entity),
            EvidenceSha256: string.Empty);
        return withoutHash with
        {
            EvidenceSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
    }

    private static SpaceCadSemanticDiagnosticV1 MappingDiagnostic(
        SpaceCadSemanticPreviewV1 semanticPreview,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceCadInventoryV1 inventory,
        SpaceCadMappingProfileV1 profile,
        SpaceCadMappingIssueV1 issue)
    {
        var rule = issue.RuleId is null
            ? null
            : profile.Rules.SingleOrDefault(rule => rule.RuleId == issue.RuleId);
        return new SpaceCadSemanticDiagnosticV1(
            DiagnosticId: string.Empty,
            SpaceCadDiagnosticOrigin.Mapping,
            issue.Code,
            issue.Severity,
            Recovery(issue.Code),
            ConfidenceBand: null,
            issue.SourceKind,
            issue.SourceKey ?? rule?.Pattern,
            SourceRef: null,
            PreviewObjectId: null,
            issue.RuleId,
            issue.DetailToken,
            AggregateLocation(
                semanticPreview.FloorLogicalId,
                preparation,
                inventory,
                issue.SourceKind,
                issue.SourceKey));
    }

    private static SpaceCadSemanticDiagnosticV1 SemanticDiagnostic(
        SpaceCadSemanticPreviewV1 semanticPreview,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceCadInventoryV1 inventory,
        SpaceCadSemanticIssueV1 issue,
        IReadOnlyDictionary<string, SpaceCadSemanticEvidenceV1> evidenceByPreviewId,
        IReadOnlyDictionary<string, SpaceCadSemanticEvidenceV1> evidenceBySourceRef)
    {
        SpaceCadSemanticEvidenceV1? evidence = null;
        if (issue.PreviewObjectId is { } previewObjectId)
            evidenceByPreviewId.TryGetValue(previewObjectId, out evidence);
        if (evidence is null && issue.SourceRef is { } sourceRef)
            evidenceBySourceRef.TryGetValue(sourceRef, out evidence);

        var location = evidence?.Location
                       ?? SourceLocation(
                           semanticPreview.FloorLogicalId,
                           preparation,
                           issue.SourceRef)
                       ?? AggregateLocation(
                           semanticPreview.FloorLogicalId,
                           preparation,
                           inventory,
                           issue.SourceKind,
                           issue.SourceKey);
        return new SpaceCadSemanticDiagnosticV1(
            DiagnosticId: string.Empty,
            SpaceCadDiagnosticOrigin.Semantic,
            issue.Code,
            issue.Severity,
            Recovery(issue.Code),
            evidence?.ConfidenceBand,
            issue.SourceKind,
            issue.SourceKey,
            issue.SourceRef,
            issue.PreviewObjectId,
            issue.RuleId,
            issue.DetailToken,
            location);
    }

    private static SpaceCadDiagnosticLocationV1 EntityLocation(
        Guid floorLogicalId,
        SpaceCadSemanticPreviewItemV1 item,
        SpaceCadIrEntityV1 entity)
    {
        var bounds = item.Geometry?.Bounds
                     ?? (entity.Bounds is { } sourceBounds ? ToBounds(sourceBounds) : null);
        return Location(
            SpaceCadDiagnosticLocationKind.Entity,
            floorLogicalId,
            item.Source.LayerId,
            item.Source.BlockName,
            item.Source.SourceRef,
            item.PreviewObjectId,
            bounds);
    }

    private static SpaceCadDiagnosticLocationV1? SourceLocation(
        Guid floorLogicalId,
        SpaceCadCoordinatePreparationV1 preparation,
        string? sourceRef)
    {
        if (sourceRef is null)
            return null;
        var entity = preparation.Package.Entities.SingleOrDefault(
            entity => entity.SourceRef.Equals(sourceRef, StringComparison.Ordinal));
        return entity is null
            ? null
            : Location(
                SpaceCadDiagnosticLocationKind.Entity,
                floorLogicalId,
                entity.LayerId,
                entity.BlockName,
                entity.SourceRef,
                previewObjectId: null,
                entity.Bounds is { } bounds ? ToBounds(bounds) : null);
    }

    private static SpaceCadDiagnosticLocationV1 AggregateLocation(
        Guid floorLogicalId,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceCadInventoryV1 inventory,
        SpaceCadMappingSourceKind? sourceKind,
        string? sourceKey)
    {
        if (sourceKind == SpaceCadMappingSourceKind.Layer && sourceKey is not null)
        {
            var layer = inventory.Layers.SingleOrDefault(layer => layer.LayerId.Equals(
                sourceKey,
                StringComparison.Ordinal));
            if (layer is not null)
            {
                return Location(
                    SpaceCadDiagnosticLocationKind.Layer,
                    floorLogicalId,
                    layer.LayerId,
                    blockName: null,
                    sourceRef: null,
                    previewObjectId: null,
                    layer.Bounds is { } bounds ? ToBounds(bounds) : null);
            }
        }
        if (sourceKind == SpaceCadMappingSourceKind.Block && sourceKey is not null)
        {
            var block = inventory.Blocks.SingleOrDefault(block => block.Name.Equals(
                sourceKey,
                StringComparison.Ordinal));
            if (block is not null)
            {
                return Location(
                    SpaceCadDiagnosticLocationKind.Block,
                    floorLogicalId,
                    layerId: null,
                    block.Name,
                    sourceRef: null,
                    previewObjectId: null,
                    block.ReferenceBounds is { } bounds ? ToBounds(bounds) : null);
            }
        }

        return Location(
            SpaceCadDiagnosticLocationKind.Document,
            floorLogicalId,
            layerId: null,
            blockName: null,
            sourceRef: null,
            previewObjectId: null,
            preparation.Package.Document.Bounds is { } documentBounds
                ? ToBounds(documentBounds)
                : null);
    }

    private static SpaceCadDiagnosticLocationV1 Location(
        SpaceCadDiagnosticLocationKind kind,
        Guid floorLogicalId,
        string? layerId,
        string? blockName,
        string? sourceRef,
        string? previewObjectId,
        SpaceCadMillimeterBoundsV1? bounds)
    {
        var anchor = bounds is null
            ? null
            : new SpaceCadMillimeterPointV1(
                Midpoint(bounds.MinX, bounds.MaxX),
                Midpoint(bounds.MinY, bounds.MaxY));
        var padding = bounds is null
            ? 0
            : SuggestedPadding(bounds);
        return new SpaceCadDiagnosticLocationV1(
            kind,
            floorLogicalId,
            layerId,
            blockName,
            sourceRef,
            previewObjectId,
            bounds,
            anchor,
            padding,
            CanFocusCanvas: bounds is not null);
    }

    private static int Midpoint(int minimum, int maximum) =>
        checked((int)(((long)minimum + maximum) / 2));

    private static int SuggestedPadding(SpaceCadMillimeterBoundsV1 bounds)
    {
        var span = Math.Max(
            (long)bounds.MaxX - bounds.MinX,
            (long)bounds.MaxY - bounds.MinY);
        return checked((int)Math.Clamp(span / 10, 250L, 10_000L));
    }

    private static SpaceCadMillimeterBoundsV1 ToBounds(SpaceCadBoundsV1 bounds)
    {
        var result = new SpaceCadMillimeterBoundsV1(
            Millimeters(bounds.MinX),
            Millimeters(bounds.MinY),
            Millimeters(bounds.MaxX),
            Millimeters(bounds.MaxY));
        if (result.MinX > result.MaxX || result.MinY > result.MaxY)
            throw new InvalidDataException("CAD diagnostic bounds are inverted.");
        return result;
    }

    private static int Millimeters(decimal value) =>
        decimal.ToInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static SpaceCadConfidenceBand ConfidenceBand(
        SpaceCadSemanticPreviewItemV1 item) =>
        item.Disposition == SpaceCadSemanticDisposition.Rejected
            ? SpaceCadConfidenceBand.Rejected
            : item.Confidence >= SpaceCadSemanticVersions.AutoAcceptanceThreshold
                ? SpaceCadConfidenceBand.High
                : item.Confidence >= SpaceCadSemanticVersions.ReviewThreshold
                    ? SpaceCadConfidenceBand.Review
                    : SpaceCadConfidenceBand.Low;

    private static SpaceCadDiagnosticRecovery Recovery(string code)
    {
        if (code.Contains("REQUIRED", StringComparison.Ordinal))
            return SpaceCadDiagnosticRecovery.ConfirmRequiredSource;
        if (code.Contains("CONFLICT", StringComparison.Ordinal))
            return SpaceCadDiagnosticRecovery.FixMappingConflict;
        if (code.Contains("UNMAPPED", StringComparison.Ordinal))
            return SpaceCadDiagnosticRecovery.MapSource;
        if (code.Contains("CONFIDENCE", StringComparison.Ordinal)
            || code.Contains("CANDIDATE", StringComparison.Ordinal))
        {
            return SpaceCadDiagnosticRecovery.ReviewCandidate;
        }
        if (code.Contains("GEOMETRY", StringComparison.Ordinal)
            || code.Contains("BOUNDARY", StringComparison.Ordinal)
            || code.Contains("ZERO_SIZE", StringComparison.Ordinal)
            || code.Contains("UNSUPPORTED", StringComparison.Ordinal)
            || code.Contains("FOOTPRINT", StringComparison.Ordinal))
        {
            return SpaceCadDiagnosticRecovery.InspectGeometry;
        }
        return SpaceCadDiagnosticRecovery.None;
    }

    private static SpaceCadSemanticDiagnosticSummaryV1 Summary(
        long sourceEntityCount,
        IReadOnlyList<SpaceCadSemanticEvidenceV1> evidence,
        IReadOnlyList<SpaceCadSemanticDiagnosticV1> diagnostics) =>
        new(
            sourceEntityCount,
            evidence.Count,
            evidence.LongCount(item => item.ConfidenceBand == SpaceCadConfidenceBand.High),
            evidence.LongCount(item => item.ConfidenceBand == SpaceCadConfidenceBand.Review),
            evidence.LongCount(item => item.ConfidenceBand == SpaceCadConfidenceBand.Low),
            evidence.LongCount(item => item.ConfidenceBand == SpaceCadConfidenceBand.Rejected),
            diagnostics.LongCount(item => item.Origin == SpaceCadDiagnosticOrigin.Mapping),
            diagnostics.LongCount(item => item.Origin == SpaceCadDiagnosticOrigin.Semantic),
            diagnostics.LongCount(item => item.Location.CanFocusCanvas),
            diagnostics.LongCount(item => !item.Location.CanFocusCanvas),
            diagnostics.LongCount(item => item.Severity == SpaceCadIssueSeverity.Info),
            diagnostics.LongCount(item => item.Severity == SpaceCadIssueSeverity.Warning),
            diagnostics.LongCount(item => item.Severity == SpaceCadIssueSeverity.Blocking));

    private static SpaceCadSemanticDiagnosticV1[] CanonicalDiagnostics(
        IEnumerable<SpaceCadSemanticDiagnosticV1> diagnostics) => diagnostics
        .OrderBy(item => item.Origin)
        .ThenByDescending(item => item.Severity)
        .ThenBy(item => item.Code, StringComparer.Ordinal)
        .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
        .ThenBy(item => item.PreviewObjectId, StringComparer.Ordinal)
        .ThenBy(item => item.SourceKind)
        .ThenBy(item => item.SourceKey, StringComparer.Ordinal)
        .ThenBy(item => item.RuleId, StringComparer.Ordinal)
        .ThenBy(item => item.DetailToken, StringComparer.Ordinal)
        .ToArray();

    private static string DiagnosticId(
        string semanticPreviewSha256,
        SpaceCadSemanticDiagnosticV1 diagnostic,
        int position) =>
        $"cad-diagnostic-{ComputeSha256(string.Join(
            '|',
            semanticPreviewSha256,
            position,
            CanonicalJson(diagnostic with { DiagnosticId = string.Empty })))[..32]}";

    private static void ValidateEvidence(
        SpaceCadSemanticEvidenceV1 evidence,
        Guid floorLogicalId,
        string semanticPreviewSha256)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RequireToken(evidence.PreviewObjectId, nameof(evidence.PreviewObjectId));
        RequireSourceRef(evidence.SourceRef);
        if (!Enum.IsDefined(evidence.Target)
            || !Enum.IsDefined(evidence.ConfidenceBand)
            || !Enum.IsDefined(evidence.Disposition)
            || !Enum.IsDefined(evidence.SourceKind)
            || !Enum.IsDefined(evidence.DecisionSource)
            || !Enum.IsDefined(evidence.GeometryRule)
            || !evidence.SemanticPreviewSha256.Equals(
                semanticPreviewSha256,
                StringComparison.Ordinal)
            || !IsSha256(evidence.SemanticPreviewSha256)
            || evidence.DecisionSource == SpaceCadMappingDecisionSource.None
            || evidence.Confidence is < 0 or > 1
            || evidence.ConfidenceBand != ConfidenceBand(
                evidence.Disposition,
                evidence.Confidence))
        {
            throw new InvalidDataException("CAD semantic evidence classification is invalid.");
        }
        if (evidence.TargetSubtype is not null)
            RequireToken(evidence.TargetSubtype, nameof(evidence.TargetSubtype));
        var expectedDisposition = evidence.ConfidenceBand switch
        {
            SpaceCadConfidenceBand.High => SpaceCadSemanticDisposition.AutoAccepted,
            SpaceCadConfidenceBand.Review or SpaceCadConfidenceBand.Low =>
                SpaceCadSemanticDisposition.Candidate,
            SpaceCadConfidenceBand.Rejected => SpaceCadSemanticDisposition.Rejected,
            _ => throw new InvalidDataException(
                "CAD semantic evidence confidence band is invalid."),
        };
        if (evidence.Disposition != expectedDisposition
            || evidence.Disposition == SpaceCadSemanticDisposition.Rejected
            && evidence.Confidence != 0)
        {
            throw new InvalidDataException(
                "CAD semantic evidence disposition is inconsistent.");
        }
        RequireToken(evidence.SourceKey, nameof(evidence.SourceKey));
        if (evidence.RuleId is not null)
            RequireToken(evidence.RuleId, nameof(evidence.RuleId));
        if (evidence.DecisionSource == SpaceCadMappingDecisionSource.ProfileRule
            != (evidence.RuleId is not null))
        {
            throw new InvalidDataException("CAD semantic evidence rule provenance is invalid.");
        }
        ValidateLocation(evidence.Location, floorLogicalId);
        if (evidence.Location.Kind != SpaceCadDiagnosticLocationKind.Entity
            || !evidence.PreviewObjectId.Equals(
                evidence.Location.PreviewObjectId,
                StringComparison.Ordinal)
            || !evidence.SourceRef.Equals(evidence.Location.SourceRef, StringComparison.Ordinal)
            || evidence.SourceKind == SpaceCadMappingSourceKind.Layer
            && !evidence.SourceKey.Equals(
                evidence.Location.LayerId,
                StringComparison.Ordinal)
            || evidence.SourceKind == SpaceCadMappingSourceKind.Block
            && !evidence.SourceKey.Equals(
                evidence.Location.BlockName,
                StringComparison.Ordinal)
            || !IsSha256(evidence.EvidenceSha256))
        {
            throw new InvalidDataException("CAD semantic evidence location is inconsistent.");
        }
        var expectedHash = ComputeSha256(CanonicalJson(
            evidence with { EvidenceSha256 = string.Empty }));
        if (!evidence.EvidenceSha256.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("CAD semantic evidence hash does not match its content.");
    }

    private static SpaceCadConfidenceBand ConfidenceBand(
        SpaceCadSemanticDisposition disposition,
        decimal confidence) =>
        disposition == SpaceCadSemanticDisposition.Rejected
            ? SpaceCadConfidenceBand.Rejected
            : confidence >= SpaceCadSemanticVersions.AutoAcceptanceThreshold
                ? SpaceCadConfidenceBand.High
                : confidence >= SpaceCadSemanticVersions.ReviewThreshold
                    ? SpaceCadConfidenceBand.Review
                    : SpaceCadConfidenceBand.Low;

    private static void ValidateDiagnostic(
        SpaceCadSemanticDiagnosticV1 diagnostic,
        Guid floorLogicalId)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        RequireToken(diagnostic.DiagnosticId, nameof(diagnostic.DiagnosticId));
        RequireToken(diagnostic.Code, nameof(diagnostic.Code));
        if (!Enum.IsDefined(diagnostic.Origin)
            || !Enum.IsDefined(diagnostic.Severity)
            || !Enum.IsDefined(diagnostic.Recovery)
            || (diagnostic.ConfidenceBand is { } band && !Enum.IsDefined(band))
            || (diagnostic.SourceKind is { } sourceKind && !Enum.IsDefined(sourceKind)))
        {
            throw new InvalidDataException("CAD semantic diagnostic classification is invalid.");
        }
        if (diagnostic.SourceKey is not null)
            RequireToken(diagnostic.SourceKey, nameof(diagnostic.SourceKey));
        if (diagnostic.SourceRef is not null)
            RequireSourceRef(diagnostic.SourceRef);
        if (diagnostic.PreviewObjectId is not null)
            RequireToken(diagnostic.PreviewObjectId, nameof(diagnostic.PreviewObjectId));
        if (diagnostic.RuleId is not null)
            RequireToken(diagnostic.RuleId, nameof(diagnostic.RuleId));
        if (diagnostic.DetailToken is { Length: > SpaceCadConversionContract.MaximumIdentifierLength })
            throw new InvalidDataException("CAD semantic diagnostic detail token is too long.");
        ValidateLocation(diagnostic.Location, floorLogicalId);
        if (diagnostic.SourceRef is not null
            && !diagnostic.SourceRef.Equals(
                diagnostic.Location.SourceRef,
                StringComparison.Ordinal)
            || diagnostic.PreviewObjectId is not null
            && !diagnostic.PreviewObjectId.Equals(
                diagnostic.Location.PreviewObjectId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("CAD semantic diagnostic source location is inconsistent.");
        }
    }

    private static void ValidateLocation(
        SpaceCadDiagnosticLocationV1 location,
        Guid floorLogicalId)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!Enum.IsDefined(location.Kind)
            || location.FloorLogicalId != floorLogicalId
            || location.SuggestedPaddingMillimeters < 0)
        {
            throw new InvalidDataException("CAD diagnostic location classification is invalid.");
        }
        if (location.LayerId is not null)
            RequireToken(location.LayerId, nameof(location.LayerId));
        if (location.BlockName is not null)
            RequireToken(location.BlockName, nameof(location.BlockName));
        if (location.SourceRef is not null)
            RequireSourceRef(location.SourceRef);
        if (location.PreviewObjectId is not null)
            RequireToken(location.PreviewObjectId, nameof(location.PreviewObjectId));
        if (location.Kind == SpaceCadDiagnosticLocationKind.Layer && location.LayerId is null
            || location.Kind == SpaceCadDiagnosticLocationKind.Block && location.BlockName is null
            || location.Kind == SpaceCadDiagnosticLocationKind.Entity && location.SourceRef is null)
        {
            throw new InvalidDataException("CAD diagnostic location key is missing.");
        }

        if (!location.CanFocusCanvas)
        {
            if (location.Bounds is not null || location.Anchor is not null
                || location.SuggestedPaddingMillimeters != 0)
            {
                throw new InvalidDataException("Unlocatable CAD diagnostic carries viewport data.");
            }
            return;
        }
        ArgumentNullException.ThrowIfNull(location.Bounds);
        ArgumentNullException.ThrowIfNull(location.Anchor);
        if (location.Bounds.MinX > location.Bounds.MaxX
            || location.Bounds.MinY > location.Bounds.MaxY
            || location.Anchor.X != Midpoint(location.Bounds.MinX, location.Bounds.MaxX)
            || location.Anchor.Y != Midpoint(location.Bounds.MinY, location.Bounds.MaxY)
            || location.SuggestedPaddingMillimeters != SuggestedPadding(location.Bounds))
        {
            throw new InvalidDataException("CAD diagnostic viewport is invalid.");
        }
    }

    private static SpaceCadSemanticPageV1<T> Page<T>(
        IEnumerable<T> source,
        int offset,
        int limit)
    {
        var values = source.ToArray();
        return new SpaceCadSemanticPageV1<T>(
            offset,
            limit,
            values.LongLength,
            values.Skip(offset).Take(limit).ToArray());
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0 || limit <= 0
            || limit > SpaceCadSemanticDiagnosticVersions.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "CAD semantic diagnostic page is outside the supported range.");
        }
    }

    private static void ValidateOptionalToken(string? value, string parameterName)
    {
        if (value is not null)
            RequireToken(value, parameterName);
    }

    private static void ValidateOptionalSourceRef(string? value)
    {
        if (value is not null)
            RequireSourceRef(value);
    }

    private static void RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("CAD semantic diagnostic token is invalid.", parameterName);
        }
    }

    private static void RequireSourceRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumSourceReferenceLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("CAD semantic diagnostic source reference is invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

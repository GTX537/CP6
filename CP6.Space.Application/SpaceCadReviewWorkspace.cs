using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadReviewWorkspace
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static SpaceCadReviewWorkspaceV1 Build(
        SpaceCadSemanticDiagnosticIndexV1 diagnosticIndex,
        SpaceExcelEditorSnapshotV1 editorSnapshot,
        SpaceExcelCadMatchPreviewV1? matchPreview = null,
        SpaceCadReviewWorkspaceV1? previousWorkspace = null,
        Guid? sourceId = null,
        Guid? cadParseJobId = null,
        string? semanticPreviewSha256 = null,
        IReadOnlyList<SpaceCadChangeV1>? changes = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticIndex);
        ArgumentNullException.ThrowIfNull(editorSnapshot);
        SpaceCadSemanticDiagnostics.Validate(diagnosticIndex);
        SpaceExcelCadMatching.ValidateEditorSnapshot(editorSnapshot);
        if (diagnosticIndex.TenantId != editorSnapshot.TenantId
            || diagnosticIndex.FloorLogicalId != editorSnapshot.FloorLogicalId
            || !diagnosticIndex.FloorCode.Equals(
                editorSnapshot.FloorCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "CAD review inputs do not belong to one tenant and floor chain.");
        }

        if (matchPreview is not null)
        {
            SpaceExcelCadMatching.Validate(matchPreview);
            if (matchPreview.TenantId != diagnosticIndex.TenantId
                || matchPreview.ModelVersionId != editorSnapshot.ModelVersionId
                || matchPreview.FloorLogicalId != diagnosticIndex.FloorLogicalId
                || !matchPreview.FloorCode.Equals(
                    diagnosticIndex.FloorCode,
                    StringComparison.OrdinalIgnoreCase)
                || !matchPreview.DiagnosticIndexSha256.Equals(
                    diagnosticIndex.DiagnosticIndexSha256,
                    StringComparison.Ordinal)
                || !matchPreview.EditorSnapshotSha256.Equals(
                    editorSnapshot.SnapshotSha256,
                    StringComparison.Ordinal)
                || matchPreview.EditorContentRevision != editorSnapshot.ContentRevision
                || !string.Equals(
                    matchPreview.EditorContentHash,
                    editorSnapshot.ContentHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "CAD review match preview is outside the current source or editor chain.");
            }
        }

        if (previousWorkspace is not null)
        {
            Validate(previousWorkspace);
            if (previousWorkspace.TenantId != diagnosticIndex.TenantId
                || previousWorkspace.ModelVersionId != editorSnapshot.ModelVersionId
                || previousWorkspace.FloorLogicalId != diagnosticIndex.FloorLogicalId
                || !previousWorkspace.FloorCode.Equals(
                    diagnosticIndex.FloorCode,
                    StringComparison.OrdinalIgnoreCase)
                || previousWorkspace.EditorContentRevision > editorSnapshot.ContentRevision
                || previousWorkspace.EditorContentRevision == editorSnapshot.ContentRevision
                    && !string.Equals(
                        previousWorkspace.EditorContentHash,
                        editorSnapshot.ContentHash,
                        StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Previous CAD review workspace is outside the current identity or revision chain.");
            }
        }

        var openItems = diagnosticIndex.Diagnostics
            .Select(DiagnosticItem)
            .Concat(diagnosticIndex.Evidence
                .Where(item => item.ConfidenceBand is SpaceCadConfidenceBand.Low
                    or SpaceCadConfidenceBand.Rejected)
                .Select(ProposalItem))
            .Concat(matchPreview?.Rows
                .Where(item => item.Disposition is SpaceExcelCadMatchDisposition.Unmatched
                    or SpaceExcelCadMatchDisposition.Conflict
                    or SpaceExcelCadMatchDisposition.Error)
                .Select(item => ExcelItem(item, diagnosticIndex.FloorLogicalId))
                ?? [])
            .ToDictionary(item => item.TrackingKey, StringComparer.Ordinal);

        var resolvedItems = previousWorkspace?.Items
            .Where(item => !openItems.ContainsKey(item.TrackingKey))
            .Select(item => item with
            {
                Status = SpaceCadReviewItemStatus.Resolved,
                ResolvedFromWorkspaceSha256 =
                    item.ResolvedFromWorkspaceSha256
                    ?? previousWorkspace.WorkspaceSha256,
            })
            .ToArray()
            ?? [];
        var items = CanonicalItems(openItems.Values.Concat(resolvedItems));
        if (items.Length > SpaceCadReviewWorkspaceVersions.MaximumItems)
        {
            throw new InvalidDataException(
                "CAD review workspace exceeds the bounded item count.");
        }

        var canonicalChanges = CanonicalChanges(changes ?? []);
        var changesetSha256 = sourceId.HasValue
            ? ComputeSha256(CanonicalJson(new
            {
                sourceId,
                cadParseJobId,
                semanticPreviewSha256,
                editorSnapshot.ContentRevision,
                editorSnapshot.ContentHash,
                changes = canonicalChanges,
            }))
            : null;
        var withoutHash = new SpaceCadReviewWorkspaceV1(
            SpaceCadReviewWorkspaceVersions.SchemaVersion,
            IsReadOnlyWorkspace: true,
            diagnosticIndex.TenantId,
            editorSnapshot.ModelVersionId,
            diagnosticIndex.FloorLogicalId,
            diagnosticIndex.FloorCode,
            diagnosticIndex.DiagnosticIndexSha256,
            matchPreview?.MatchPreviewSha256,
            editorSnapshot.ContentRevision,
            editorSnapshot.ContentHash,
            editorSnapshot.SnapshotSha256,
            previousWorkspace?.WorkspaceSha256,
            items,
            Summary(items),
            WorkspaceSha256: string.Empty,
            sourceId,
            cadParseJobId,
            semanticPreviewSha256,
            canonicalChanges,
            ChangeSummary(canonicalChanges),
            changesetSha256);
        var workspace = withoutHash with
        {
            WorkspaceSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        Validate(workspace);
        return workspace;
    }

    public static string Serialize(SpaceCadReviewWorkspaceV1 workspace)
    {
        Validate(workspace);
        return JsonSerializer.Serialize(workspace, CanonicalJsonOptions);
    }

    public static void Validate(SpaceCadReviewWorkspaceV1 workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(workspace.Items);
        ArgumentNullException.ThrowIfNull(workspace.Summary);
        if (workspace.SchemaVersion != SpaceCadReviewWorkspaceVersions.SchemaVersion
            || !workspace.IsReadOnlyWorkspace
            || workspace.TenantId == Guid.Empty
            || workspace.ModelVersionId == Guid.Empty
            || workspace.FloorLogicalId == Guid.Empty
            || !IsToken(workspace.FloorCode)
            || !IsSha256(workspace.DiagnosticIndexSha256)
            || workspace.MatchPreviewSha256 is not null
                && !IsSha256(workspace.MatchPreviewSha256)
            || workspace.EditorContentRevision < 0
            || workspace.EditorContentHash is not null
                && !IsSha256(workspace.EditorContentHash)
            || !IsSha256(workspace.EditorSnapshotSha256)
            || workspace.PreviousWorkspaceSha256 is not null
                && !IsSha256(workspace.PreviousWorkspaceSha256)
            || !IsSha256(workspace.WorkspaceSha256)
            || workspace.Items.Count > SpaceCadReviewWorkspaceVersions.MaximumItems
            || !workspace.Items.SequenceEqual(CanonicalItems(workspace.Items))
            || workspace.SourceId == Guid.Empty
            || workspace.CadParseJobId == Guid.Empty
            || workspace.SourceId.HasValue != workspace.CadParseJobId.HasValue
            || workspace.SourceId.HasValue !=
                (workspace.SemanticPreviewSha256 is not null)
            || workspace.SemanticPreviewSha256 is not null &&
                !IsSha256(workspace.SemanticPreviewSha256)
            || workspace.SourceId.HasValue !=
                (workspace.ChangesetSha256 is not null)
            || workspace.ChangesetSha256 is not null &&
                !IsSha256(workspace.ChangesetSha256))
        {
            throw new InvalidDataException("CAD review workspace identity is invalid.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var trackingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in workspace.Items)
        {
            ValidateItem(item, workspace.FloorLogicalId);
            if (!ids.Add(item.ReviewItemId) || !trackingKeys.Add(item.TrackingKey))
            {
                throw new InvalidDataException(
                    "CAD review workspace item identity is duplicated.");
            }
        }
        if (Summary(workspace.Items) != workspace.Summary)
        {
            throw new InvalidDataException(
                "CAD review workspace summary is inconsistent.");
        }
        var changes = workspace.Changes ?? [];
        if (changes.Count > SpaceCadReviewWorkspaceVersions.MaximumItems ||
            !changes.SequenceEqual(CanonicalChanges(changes)) ||
            changes.Select(item => item.ChangeId).Distinct(StringComparer.Ordinal)
                .Count() != changes.Count ||
            changes.Select(item => item.LogicalId).Distinct().Count() != changes.Count)
        {
            throw new InvalidDataException("CAD review changeset is invalid.");
        }
        foreach (var change in changes)
            ValidateChange(change);
        if (workspace.ChangeSummary != ChangeSummary(changes))
        {
            throw new InvalidDataException(
                "CAD review changeset summary is inconsistent.");
        }
        if (workspace.SourceId.HasValue)
        {
            var expectedChangesetSha256 = ComputeSha256(CanonicalJson(new
            {
                sourceId = workspace.SourceId,
                cadParseJobId = workspace.CadParseJobId,
                semanticPreviewSha256 = workspace.SemanticPreviewSha256,
                ContentRevision = workspace.EditorContentRevision,
                ContentHash = workspace.EditorContentHash,
                changes = CanonicalChanges(changes),
            }));
            if (!string.Equals(
                    workspace.ChangesetSha256,
                    expectedChangesetSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "CAD review changeset hash is invalid.");
            }
        }
        var expectedHash = ComputeSha256(CanonicalJson(
            workspace with { WorkspaceSha256 = string.Empty }));
        if (!workspace.WorkspaceSha256.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("CAD review workspace hash is invalid.");
    }

    public static SpaceCadReviewWorkspacePageV1 Query(
        SpaceCadReviewWorkspaceV1 workspace,
        SpaceCadReviewWorkspaceQueryV1 query)
    {
        Validate(workspace);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0
            || query.Limit <= 0
            || query.Limit > SpaceCadReviewWorkspaceVersions.MaximumPageSize
            || query.Status is { } status && !Enum.IsDefined(status)
            || query.Severity is { } severity && !Enum.IsDefined(severity)
            || query.Kind is { } kind && !Enum.IsDefined(kind)
            || query.SourceRef is not null && !IsSourceRef(query.SourceRef)
            || query.Search is not null && !IsSearch(query.Search))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "CAD review workspace query is invalid.");
        }

        var filtered = workspace.Items.Where(item =>
            (query.Status is null || item.Status == query.Status)
            && (query.Severity is null || item.Severity == query.Severity)
            && (query.Kind is null || item.Kind == query.Kind)
            && (query.SourceRef is null || item.SourceRef?.Equals(
                query.SourceRef,
                StringComparison.Ordinal) == true)
            && (!query.OnlyLocatable || item.Location.CanFocusCanvas)
            && (query.Search is null || MatchesSearch(item, query.Search)))
            .ToArray();
        return new SpaceCadReviewWorkspacePageV1(
            query.Offset,
            query.Limit,
            filtered.LongLength,
            filtered.Skip(query.Offset).Take(query.Limit).ToArray());
    }

    private static SpaceCadReviewItemV1 DiagnosticItem(
        SpaceCadSemanticDiagnosticV1 diagnostic)
    {
        var trackingKey = $"cad-diagnostic:{diagnostic.DiagnosticId}";
        return Item(
            trackingKey,
            diagnostic.Origin == SpaceCadDiagnosticOrigin.Mapping
                ? SpaceCadReviewItemKind.MappingDiagnostic
                : SpaceCadReviewItemKind.SemanticDiagnostic,
            diagnostic.Severity,
            diagnostic.Code,
            [],
            diagnostic.DetailToken,
            RecoveryCode(diagnostic.Recovery),
            diagnostic.SourceRef,
            diagnostic.PreviewObjectId,
            null,
            null,
            diagnostic.ConfidenceBand,
            diagnostic.Location,
            ComputeSha256(CanonicalJson(diagnostic)));
    }

    private static SpaceCadReviewItemV1 ProposalItem(
        SpaceCadSemanticEvidenceV1 evidence)
    {
        var rejected = evidence.ConfidenceBand == SpaceCadConfidenceBand.Rejected;
        var trackingKey = string.Join(
            ':',
            "cad-proposal",
            evidence.SourceRef,
            evidence.Target,
            evidence.TargetSubtype ?? "-");
        return Item(
            trackingKey,
            rejected
                ? SpaceCadReviewItemKind.RejectedProposal
                : SpaceCadReviewItemKind.LowConfidenceProposal,
            rejected ? SpaceCadIssueSeverity.Blocking : SpaceCadIssueSeverity.Warning,
            rejected
                ? "SPACE_CAD_REJECTED_PROPOSAL"
                : "SPACE_CAD_LOW_CONFIDENCE",
            [],
            evidence.TargetSubtype ?? evidence.SourceKey,
            rejected ? "inspect-geometry-or-mapping" : "review-candidate",
            evidence.SourceRef,
            evidence.PreviewObjectId,
            null,
            null,
            evidence.ConfidenceBand,
            evidence.Location,
            evidence.EvidenceSha256);
    }

    private static SpaceCadReviewItemV1 ExcelItem(
        SpaceExcelCadRackMatchV1 row,
        Guid floorLogicalId)
    {
        var (kind, severity, code, action) = row.Disposition switch
        {
            SpaceExcelCadMatchDisposition.Unmatched => (
                SpaceCadReviewItemKind.ExcelUnmatched,
                SpaceCadIssueSeverity.Warning,
                "SPACE_EXCEL_CAD_UNMATCHED",
                "map-source-or-rack-code"),
            SpaceExcelCadMatchDisposition.Conflict => (
                SpaceCadReviewItemKind.ExcelConflict,
                SpaceCadIssueSeverity.Blocking,
                "SPACE_EXCEL_CAD_CONFLICT",
                "resolve-match-conflict"),
            SpaceExcelCadMatchDisposition.Error => (
                SpaceCadReviewItemKind.ExcelError,
                SpaceCadIssueSeverity.Blocking,
                "SPACE_EXCEL_ROW_INVALID",
                "fix-excel-row"),
            _ => throw new InvalidDataException(
                "Only exceptional Excel match rows belong in CAD review."),
        };
        return Item(
            $"excel-match:{row.ExcelRowId}:{row.Disposition}",
            kind,
            severity,
            code,
            row.ErrorCodes,
            row.DifferenceFields.Count > 0
                ? string.Join(',', row.DifferenceFields)
                : null,
            action,
            row.MatchedSourceRef,
            row.CadPreviewObjectId,
            row.EditorLogicalId,
            row.Values.RackCode,
            row.CadConfidenceBand,
            row.Location ?? DocumentLocation(floorLogicalId),
            row.MatchEvidenceSha256);
    }

    private static SpaceCadReviewItemV1 Item(
        string trackingKey,
        SpaceCadReviewItemKind kind,
        SpaceCadIssueSeverity severity,
        string code,
        IReadOnlyList<string> relatedCodes,
        string? detailToken,
        string suggestedActionCode,
        string? sourceRef,
        string? previewObjectId,
        Guid? targetLogicalId,
        string? rackCode,
        SpaceCadConfidenceBand? confidenceBand,
        SpaceCadDiagnosticLocationV1 location,
        string upstreamEvidenceSha256) => new(
            $"cad-review-{ComputeSha256(trackingKey)[..32]}",
            trackingKey,
            kind,
            severity,
            SpaceCadReviewItemStatus.Open,
            code,
            relatedCodes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            detailToken,
            suggestedActionCode,
            sourceRef,
            previewObjectId,
            targetLogicalId,
            rackCode,
            confidenceBand,
            location,
            upstreamEvidenceSha256,
            ResolvedFromWorkspaceSha256: null);

    private static SpaceCadDiagnosticLocationV1 DocumentLocation(
        Guid floorLogicalId) => new(
            SpaceCadDiagnosticLocationKind.Document,
            floorLogicalId,
            LayerId: null,
            BlockName: null,
            SourceRef: null,
            PreviewObjectId: null,
            Bounds: null,
            Anchor: null,
            SuggestedPaddingMillimeters: 0,
            CanFocusCanvas: false);

    private static string RecoveryCode(SpaceCadDiagnosticRecovery recovery) =>
        recovery switch
        {
            SpaceCadDiagnosticRecovery.None => "inspect-diagnostic",
            SpaceCadDiagnosticRecovery.MapSource => "map-source",
            SpaceCadDiagnosticRecovery.FixMappingConflict => "fix-mapping-conflict",
            SpaceCadDiagnosticRecovery.ReviewCandidate => "review-candidate",
            SpaceCadDiagnosticRecovery.InspectGeometry => "inspect-geometry",
            SpaceCadDiagnosticRecovery.ConfirmRequiredSource => "confirm-required-source",
            _ => throw new InvalidDataException("CAD diagnostic recovery is invalid."),
        };

    private static void ValidateItem(
        SpaceCadReviewItemV1 item,
        Guid floorLogicalId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.RelatedCodes);
        ArgumentNullException.ThrowIfNull(item.Location);
        var resolved = item.Status == SpaceCadReviewItemStatus.Resolved;
        if (!IsToken(item.ReviewItemId)
            || !IsText(item.TrackingKey, 512)
            || !item.ReviewItemId.Equals(
                $"cad-review-{ComputeSha256(item.TrackingKey)[..32]}",
                StringComparison.Ordinal)
            || !Enum.IsDefined(item.Kind)
            || !Enum.IsDefined(item.Severity)
            || !Enum.IsDefined(item.Status)
            || !IsToken(item.Code)
            || !IsToken(item.SuggestedActionCode)
            || item.DetailToken is not null && !IsText(item.DetailToken, 512)
            || item.SourceRef is not null && !IsSourceRef(item.SourceRef)
            || item.PreviewObjectId is not null && !IsToken(item.PreviewObjectId)
            || item.TargetLogicalId == Guid.Empty
            || item.RackCode is not null && !IsToken(item.RackCode)
            || item.ConfidenceBand is { } band && !Enum.IsDefined(band)
            || !IsSha256(item.UpstreamEvidenceSha256)
            || resolved != (item.ResolvedFromWorkspaceSha256 is not null)
            || item.ResolvedFromWorkspaceSha256 is not null
                && !IsSha256(item.ResolvedFromWorkspaceSha256)
            || item.RelatedCodes.Any(code => !IsToken(code))
            || !item.RelatedCodes.SequenceEqual(item.RelatedCodes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("CAD review item is invalid.");
        }
        ValidateLocation(item.Location, floorLogicalId);
    }

    private static void ValidateLocation(
        SpaceCadDiagnosticLocationV1 location,
        Guid floorLogicalId)
    {
        if (!Enum.IsDefined(location.Kind)
            || location.FloorLogicalId != floorLogicalId
            || location.LayerId is not null && !IsToken(location.LayerId)
            || location.BlockName is not null && !IsToken(location.BlockName)
            || location.SourceRef is not null && !IsSourceRef(location.SourceRef)
            || location.PreviewObjectId is not null
                && !IsToken(location.PreviewObjectId)
            || location.SuggestedPaddingMillimeters < 0
            || location.Bounds is { } bounds
                && (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY)
            || location.CanFocusCanvas
                && location.Bounds is null
                && location.Anchor is null)
        {
            throw new InvalidDataException("CAD review item location is invalid.");
        }
    }

    private static SpaceCadReviewItemV1[] CanonicalItems(
        IEnumerable<SpaceCadReviewItemV1> items) => items
        .OrderBy(item => item.Status)
        .ThenByDescending(item => item.Severity)
        .ThenBy(item => item.Kind)
        .ThenBy(item => item.TrackingKey, StringComparer.Ordinal)
        .ToArray();

    private static SpaceCadChangeV1[] CanonicalChanges(
        IEnumerable<SpaceCadChangeV1> changes) => changes
        .OrderBy(item => item.Kind)
        .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
        .ThenBy(item => item.LogicalId)
        .ToArray();

    private static SpaceCadChangeSummaryV1 ChangeSummary(
        IEnumerable<SpaceCadChangeV1> source)
    {
        var changes = source as IReadOnlyList<SpaceCadChangeV1>
            ?? source.ToArray();
        return new SpaceCadChangeSummaryV1(
            changes.Count,
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.Add),
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.Modify),
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.Delete),
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.Conflict),
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.LowConfidence),
            changes.LongCount(item => item.Kind == SpaceCadChangeKind.Unrecognized),
            changes.LongCount(item => item.IsSelected),
            changes.LongCount(item => item.CanApply));
    }

    private static void ValidateChange(SpaceCadChangeV1 change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var expectedId =
            $"cad-change-{ComputeSha256($"{change.SourceRef}\n{change.LogicalId:D}")[..32]}";
        if (!change.ChangeId.Equals(expectedId, StringComparison.Ordinal) ||
            !Enum.IsDefined(change.Kind) ||
            change.LogicalId == Guid.Empty ||
            !IsSourceRef(change.SourceRef) ||
            change.PreviewObjectId is not null &&
                !IsToken(change.PreviewObjectId) ||
            !IsToken(change.ObjectType) ||
            change.Confidence is < 0 or > 1 ||
            change.IsSelected && !change.CanApply ||
            change.CanApply && change.Kind is not (
                SpaceCadChangeKind.Add or
                SpaceCadChangeKind.Modify or
                SpaceCadChangeKind.Delete) ||
            change.BlockingReasonCode is not null &&
                !IsToken(change.BlockingReasonCode))
        {
            throw new InvalidDataException("CAD review change is invalid.");
        }
    }

    private static SpaceCadReviewWorkspaceSummaryV1 Summary(
        IReadOnlyList<SpaceCadReviewItemV1> items) => new(
            items.Count,
            items.LongCount(item => item.Status == SpaceCadReviewItemStatus.Open),
            items.LongCount(item => item.Status == SpaceCadReviewItemStatus.Resolved),
            items.LongCount(item => item.Status == SpaceCadReviewItemStatus.Open
                && item.Severity == SpaceCadIssueSeverity.Info),
            items.LongCount(item => item.Status == SpaceCadReviewItemStatus.Open
                && item.Severity == SpaceCadIssueSeverity.Warning),
            items.LongCount(item => item.Status == SpaceCadReviewItemStatus.Open
                && item.Severity == SpaceCadIssueSeverity.Blocking),
            items.LongCount(item => item.Location.CanFocusCanvas),
            items.LongCount(item => !item.Location.CanFocusCanvas),
            items.LongCount(item => item.Kind is SpaceCadReviewItemKind.MappingDiagnostic
                or SpaceCadReviewItemKind.SemanticDiagnostic),
            items.LongCount(item => item.Kind is SpaceCadReviewItemKind.LowConfidenceProposal
                or SpaceCadReviewItemKind.RejectedProposal),
            items.LongCount(item => item.Kind is SpaceCadReviewItemKind.ExcelUnmatched
                or SpaceCadReviewItemKind.ExcelConflict
                or SpaceCadReviewItemKind.ExcelError));

    private static bool MatchesSearch(
        SpaceCadReviewItemV1 item,
        string search) =>
        item.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.SourceRef?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.PreviewObjectId?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.RackCode?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.DetailToken?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || item.RelatedCodes.Any(code => code.Contains(
            search,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsToken(string? value) =>
        IsText(value, SpaceCadConversionContract.MaximumIdentifierLength);

    private static bool IsSourceRef(string? value) =>
        IsText(value, SpaceCadConversionContract.MaximumSourceReferenceLength);

    private static bool IsSearch(string? value) => IsText(value, 128);

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.Equals(value.Trim(), StringComparison.Ordinal);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

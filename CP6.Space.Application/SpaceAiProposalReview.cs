using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class WarehouseProposalReviewWorkbench
{
    private const string CursorResourcePrefix = "warehouse-proposal-review-v1:";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static WarehouseProposalReviewBaselineSnapshotV1 SealBaseline(
        Guid tenantId,
        Guid modelVersionId,
        Guid floorLogicalId,
        long contentRevision,
        string? contentHash,
        IReadOnlyList<WarehouseProposalReviewBaselineObjectV1> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var canonicalObjects = objects
            .Select(CanonicalBaselineObject)
            .OrderBy(item => item.LogicalId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var withoutHash = new WarehouseProposalReviewBaselineSnapshotV1(
            WarehouseProposalReviewVersions.SchemaVersion,
            IsReadOnlySnapshot: true,
            IsCompleteFloorProjection: true,
            tenantId,
            modelVersionId,
            floorLogicalId,
            contentRevision,
            contentHash,
            canonicalObjects,
            SnapshotSha256: string.Empty);
        var snapshot = withoutHash with
        {
            SnapshotSha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        ValidateBaseline(snapshot);
        return snapshot;
    }

    public static WarehouseProposalReviewWorkspaceV1 Build(
        WarehouseDraftProposalSetV1 proposalSet,
        WarehouseProposalReviewBaselineSnapshotV1 baseline)
    {
        ArgumentNullException.ThrowIfNull(proposalSet);
        ArgumentNullException.ThrowIfNull(baseline);
        _ = WarehouseDraftSynthesizer.Serialize(proposalSet);
        ValidateBaseline(baseline);
        if (proposalSet.TenantId != baseline.TenantId
            || proposalSet.ModelVersionId != baseline.ModelVersionId
            || proposalSet.FloorLogicalId != baseline.FloorLogicalId)
        {
            throw new InvalidDataException(
                "Proposal and Draft baseline identities do not match.");
        }

        var baselineById = baseline.Objects.ToDictionary(item => item.LogicalId);
        var issuesByProposal = proposalSet.Proposals.ToDictionary(
            proposal => proposal.LogicalId,
            proposal => proposalSet.Issues
                .Where(issue => BelongsTo(issue, proposal))
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.FieldPath, StringComparer.Ordinal)
                .ToArray());
        var assignedIssues = issuesByProposal.Values
            .SelectMany(item => item)
            .ToHashSet();
        var runIssues = proposalSet.Issues
            .Where(issue => !assignedIssues.Contains(issue))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.SourceRef, StringComparer.Ordinal)
            .ThenBy(issue => issue.FieldPath, StringComparer.Ordinal)
            .ToArray();
        var items = proposalSet.Proposals
            .Select(proposal => ReviewItem(
                proposalSet.ProposalSetSha256,
                proposalSet.FloorLogicalId,
                proposal,
                issuesByProposal[proposal.LogicalId],
                baselineById.GetValueOrDefault(proposal.LogicalId)))
            .OrderBy(item => item.ConfidenceBand)
            .ThenBy(item => item.ObjectType.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.LogicalId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (items.Length > WarehouseProposalReviewVersions.MaximumItems)
        {
            throw new InvalidDataException(
                "AI proposal review workspace exceeds its bounded item count.");
        }

        var core = new WarehouseProposalReviewWorkspaceV1(
            WarehouseProposalReviewVersions.SchemaVersion,
            IsReadOnlyWorkspace: true,
            DecisionWritten: false,
            DraftWritten: false,
            proposalSet.TenantId,
            proposalSet.ModelVersionId,
            proposalSet.FloorLogicalId,
            proposalSet.ProposalSetSha256,
            baseline.SnapshotSha256,
            baseline.ContentRevision,
            baseline.ContentHash,
            runIssues,
            items,
            Summary(items, runIssues),
            ReviewEtag: string.Empty,
            WorkspaceSha256: string.Empty);
        var withEtag = core with
        {
            ReviewEtag = ComputeSha256(CanonicalJson(core)),
        };
        var workspace = withEtag with
        {
            WorkspaceSha256 = ComputeSha256(CanonicalJson(withEtag)),
        };
        Validate(workspace);
        return workspace;
    }

    public static string SerializeBaseline(
        WarehouseProposalReviewBaselineSnapshotV1 baseline)
    {
        ValidateBaseline(baseline);
        return CanonicalJson(baseline);
    }

    public static string Serialize(WarehouseProposalReviewWorkspaceV1 workspace)
    {
        Validate(workspace);
        return CanonicalJson(workspace);
    }

    public static WarehouseProposalReviewPageV1 Query(
        WarehouseProposalReviewWorkspaceV1 workspace,
        WarehouseProposalReviewQueryV1 query,
        ISpaceCursorCodec cursorCodec)
    {
        Validate(workspace);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(cursorCodec);
        var filter = query.Filter ?? new WarehouseProposalReviewFilterV1();
        ValidateFilter(filter);
        if (query.Limit is < 1 or > WarehouseProposalReviewVersions.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        var filterHash = ComputeSha256(CanonicalJson(filter));
        var resource = CursorResourcePrefix + workspace.WorkspaceSha256;
        var offset = query.Cursor is null
            ? 0
            : cursorCodec.Decode(query.Cursor, resource, filterHash).Offset;
        var matches = workspace.Items.Where(item => Matches(item, filter)).ToArray();
        if (offset < 0 || offset > matches.Length)
            throw new InvalidDataException("AI proposal review cursor offset is invalid.");
        var pageItems = matches.Skip(offset).Take(query.Limit).ToArray();
        var nextOffset = offset + pageItems.Length;
        var nextCursor = nextOffset < matches.Length
            ? cursorCodec.Encode(new SpaceCursorState(resource, filterHash, nextOffset))
            : null;
        return new WarehouseProposalReviewPageV1(
            workspace.ReviewEtag,
            filterHash,
            query.Limit,
            matches.LongLength,
            pageItems,
            nextCursor);
    }

    public static WarehouseProposalBatchSelectionPreviewV1 PreviewBatchSelection(
        WarehouseProposalReviewWorkspaceV1 workspace,
        WarehouseProposalBatchSelectionRequestV1 request)
    {
        Validate(workspace);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Action)
            || !request.ReviewEtag.Equals(workspace.ReviewEtag, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Batch selection is stale or its action is invalid.");
        }
        var hasIds = request.ReviewItemIds is not null;
        var hasFilter = request.Filter is not null;
        if (hasIds == hasFilter)
        {
            throw new InvalidDataException(
                "Batch selection must use either explicit IDs or one filter.");
        }

        WarehouseProposalReviewItemV1[] selected;
        if (request.ReviewItemIds is { } ids)
        {
            if (ids.Count == 0
                || ids.Count > WarehouseProposalReviewVersions.MaximumBatchSelection
                || ids.Any(id => !IsText(id, 128))
                || ids.Count != ids.Distinct(StringComparer.Ordinal).Count())
            {
                throw new InvalidDataException("Explicit batch selection is invalid.");
            }
            var requested = ids.ToHashSet(StringComparer.Ordinal);
            selected = workspace.Items
                .Where(item => requested.Contains(item.ReviewItemId))
                .ToArray();
            if (selected.Length != ids.Count)
                throw new InvalidDataException("Batch selection contains an unknown item.");
        }
        else
        {
            ValidateFilter(request.Filter!);
            selected = workspace.Items
                .Where(item => Matches(item, request.Filter!))
                .ToArray();
            if (selected.Length == 0
                || selected.Length > WarehouseProposalReviewVersions.MaximumBatchSelection)
            {
                throw new InvalidDataException(
                    "Filtered batch selection must contain between 1 and 1,000 proposals.");
            }
        }

        var eligible = new List<string>(selected.Length);
        var ineligible = new List<WarehouseProposalBatchIneligibleItemV1>();
        foreach (var item in selected)
        {
            var reason = request.Action == WarehouseProposalBatchAction.Reject
                ? null
                : BatchAcceptIneligibility(item);
            if (reason is null)
                eligible.Add(item.ReviewItemId);
            else
                ineligible.Add(new(item.ReviewItemId, reason));
        }
        return new WarehouseProposalBatchSelectionPreviewV1(
            request.Action,
            workspace.ReviewEtag,
            selected.LongLength,
            eligible,
            ineligible,
            RequiresServerRevalidation: true,
            DecisionWritten: false,
            DraftWritten: false);
    }

    public static void ValidateBaseline(
        WarehouseProposalReviewBaselineSnapshotV1 baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(baseline.Objects);
        if (baseline.SchemaVersion != WarehouseProposalReviewVersions.SchemaVersion
            || !baseline.IsReadOnlySnapshot
            || !baseline.IsCompleteFloorProjection
            || baseline.TenantId == Guid.Empty
            || baseline.ModelVersionId == Guid.Empty
            || baseline.FloorLogicalId == Guid.Empty
            || baseline.ContentRevision < 0
            || baseline.ContentHash is not null && !IsSha256(baseline.ContentHash)
            || !IsSha256(baseline.SnapshotSha256)
            || baseline.Objects.Count > WarehouseProposalReviewVersions.MaximumItems)
        {
            throw new InvalidDataException("AI proposal review baseline is invalid.");
        }
        var canonical = baseline.Objects
            .Select(CanonicalBaselineObject)
            .OrderBy(item => item.LogicalId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (!CanonicalJson(baseline.Objects).Equals(
                CanonicalJson(canonical),
                StringComparison.Ordinal)
            || canonical.Select(item => item.LogicalId).Distinct().Count() != canonical.Length)
        {
            throw new InvalidDataException(
                "AI proposal review baseline objects are not canonical and unique.");
        }
        var expected = ComputeSha256(CanonicalJson(
            baseline with { SnapshotSha256 = string.Empty }));
        if (!baseline.SnapshotSha256.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("AI proposal review baseline hash is invalid.");
    }

    public static void Validate(WarehouseProposalReviewWorkspaceV1 workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(workspace.RunIssues);
        ArgumentNullException.ThrowIfNull(workspace.Items);
        ArgumentNullException.ThrowIfNull(workspace.Summary);
        if (workspace.SchemaVersion != WarehouseProposalReviewVersions.SchemaVersion
            || !workspace.IsReadOnlyWorkspace
            || workspace.DecisionWritten
            || workspace.DraftWritten
            || workspace.TenantId == Guid.Empty
            || workspace.ModelVersionId == Guid.Empty
            || workspace.FloorLogicalId == Guid.Empty
            || !IsSha256(workspace.ProposalSetSha256)
            || !IsSha256(workspace.BaselineSnapshotSha256)
            || workspace.BaselineContentRevision < 0
            || workspace.BaselineContentHash is not null
                && !IsSha256(workspace.BaselineContentHash)
            || !IsSha256(workspace.ReviewEtag)
            || !IsSha256(workspace.WorkspaceSha256)
            || workspace.Items.Count > WarehouseProposalReviewVersions.MaximumItems)
        {
            throw new InvalidDataException("AI proposal review workspace identity is invalid.");
        }
        var canonicalItems = workspace.Items
            .OrderBy(item => item.ConfidenceBand)
            .ThenBy(item => item.ObjectType.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.LogicalId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (!workspace.Items.SequenceEqual(canonicalItems)
            || workspace.Items.Select(item => item.ReviewItemId).Distinct().Count()
                != workspace.Items.Count
            || workspace.Items.Select(item => item.LogicalId).Distinct().Count()
                != workspace.Items.Count)
        {
            throw new InvalidDataException(
                "AI proposal review items are not canonical and unique.");
        }
        foreach (var item in workspace.Items)
            ValidateItem(item, workspace);
        ValidateIssues(workspace.RunIssues);
        var expectedSummary = Summary(workspace.Items, workspace.RunIssues);
        if (workspace.Summary != expectedSummary)
            throw new InvalidDataException("AI proposal review summary is invalid.");
        var expectedEtag = ComputeSha256(CanonicalJson(
            workspace with
            {
                ReviewEtag = string.Empty,
                WorkspaceSha256 = string.Empty,
            }));
        if (!workspace.ReviewEtag.Equals(expectedEtag, StringComparison.Ordinal))
            throw new InvalidDataException("AI proposal review etag is invalid.");
        var expectedHash = ComputeSha256(CanonicalJson(
            workspace with { WorkspaceSha256 = string.Empty }));
        if (!workspace.WorkspaceSha256.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("AI proposal review workspace hash is invalid.");
    }

    private static WarehouseProposalReviewBaselineObjectV1 CanonicalBaselineObject(
        WarehouseProposalReviewBaselineObjectV1 item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.GeometryBounds);
        ArgumentNullException.ThrowIfNull(item.Fields);
        if (item.LogicalId == Guid.Empty
            || !Enum.IsDefined(item.ObjectType)
            || !IsSha256(item.GeometrySha256)
            || item.GeometryBounds.MinX > item.GeometryBounds.MaxX
            || item.GeometryBounds.MinY > item.GeometryBounds.MaxY
            || item.RackLevelCount < 0
            || item.LocationCount < 0
            || item.ObjectType != WarehouseSpaceType.Rack
                && (item.RackLevelCount != 0 || item.LocationCount != 0))
        {
            throw new InvalidDataException("AI proposal baseline object is invalid.");
        }
        var fields = item.Fields.ToDictionary(
            field => field.FieldPath,
            field => field.ValueToken,
            StringComparer.Ordinal);
        if (fields.Count != item.Fields.Count
            || fields.Any(pair => !IsText(pair.Key, 128) || !IsText(pair.Value, 512)))
        {
            throw new InvalidDataException("AI proposal baseline fields are invalid.");
        }
        if (fields.TryGetValue("type", out var type)
            && !type.Equals(item.ObjectType.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "AI proposal baseline type field does not match its object type.");
        }
        fields["type"] = item.ObjectType.ToString();
        return item with
        {
            Fields = fields
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new WarehouseProposalReviewBaselineFieldV1(
                    pair.Key,
                    pair.Value))
                .ToArray(),
        };
    }

    private static WarehouseProposalReviewItemV1 ReviewItem(
        string proposalSetSha256,
        Guid floorLogicalId,
        WarehouseDraftProposalV1 proposal,
        IReadOnlyList<WarehouseProposalIssueV1> issues,
        WarehouseProposalReviewBaselineObjectV1? baseline)
    {
        var hasBlocking = issues.Any(
            issue => issue.Severity == WarehouseProposalIssueSeverity.Blocking);
        var readiness = hasBlocking
            ? WarehouseProposalReviewReadiness.Blocked
            : proposal.RequiresHumanReview
                ? WarehouseProposalReviewReadiness.NeedsReview
                : WarehouseProposalReviewReadiness.Ready;
        var canBatchAccept = readiness == WarehouseProposalReviewReadiness.Ready
            && proposal.CanBatchAccept;
        var bounds = proposal.Geometry.Bounds;
        var anchor = new SpaceCadMillimeterPointV1(
            checked(bounds.MinX + (bounds.MaxX - bounds.MinX) / 2),
            checked(bounds.MinY + (bounds.MaxY - bounds.MinY) / 2));
        return new WarehouseProposalReviewItemV1(
            $"ai-review-{ComputeSha256($"{proposalSetSha256}\n{proposal.LogicalId:D}")[..32]}",
            proposal.LogicalId,
            proposal.SourceKey,
            proposal.SourceRef,
            proposal.ObjectType,
            proposal.Confidence,
            proposal.ConfidenceBand,
            readiness,
            hasBlocking,
            canBatchAccept,
            new WarehouseProposalReviewLocationV1(
                floorLogicalId,
                proposal.SourceRef,
                bounds,
                anchor,
                SuggestedPaddingMillimeters: 2_000,
                CanFocusCanvas: true),
            proposal.Fields,
            proposal.Relations,
            proposal.RackDerivation,
            issues,
            Difference(proposal, baseline));
    }

    private static WarehouseProposalDifferenceV1 Difference(
        WarehouseDraftProposalV1 proposal,
        WarehouseProposalReviewBaselineObjectV1? baseline)
    {
        var currentFields = proposal.Fields.ToDictionary(
            field => field.FieldPath,
            StringComparer.Ordinal);
        var beforeFields = baseline?.Fields.ToDictionary(
            field => field.FieldPath,
            field => field.ValueToken,
            StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var fieldPaths = beforeFields.Keys
            .Concat(currentFields.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal);
        var differences = new List<WarehouseProposalFieldDifferenceV1>();
        foreach (var path in fieldPaths)
        {
            beforeFields.TryGetValue(path, out var before);
            currentFields.TryGetValue(path, out var current);
            if (before == current?.ValueToken)
                continue;
            var fieldKind = before is null
                ? WarehouseProposalFieldDifferenceKind.Added
                : current is null
                    ? WarehouseProposalFieldDifferenceKind.Removed
                    : WarehouseProposalFieldDifferenceKind.Changed;
            differences.Add(new WarehouseProposalFieldDifferenceV1(
                path,
                fieldKind,
                before,
                current?.ValueToken,
                current?.WinningSource,
                current?.Confidence,
                current?.Evidence ?? []));
        }

        var afterGeometrySha = ComputeSha256(CanonicalJson(proposal.Geometry));
        var geometryChanged = baseline is null
            || !baseline.GeometrySha256.Equals(afterGeometrySha, StringComparison.Ordinal);
        var afterRackLevelCount = proposal.RackDerivation?.Levels.Count ?? 0;
        var afterLocationCount = proposal.RackDerivation?.LocationCount ?? 0;
        var kind = baseline is null
            ? WarehouseProposalDifferenceKind.Added
            : geometryChanged
                || differences.Count > 0
                || baseline.RackLevelCount != afterRackLevelCount
                || baseline.LocationCount != afterLocationCount
                ? WarehouseProposalDifferenceKind.Modified
                : WarehouseProposalDifferenceKind.Unchanged;
        return new WarehouseProposalDifferenceV1(
            kind,
            geometryChanged,
            baseline?.GeometrySha256,
            afterGeometrySha,
            baseline?.GeometryBounds,
            proposal.Geometry.Bounds,
            differences,
            baseline?.RackLevelCount ?? 0,
            afterRackLevelCount,
            baseline?.LocationCount ?? 0,
            afterLocationCount);
    }

    private static WarehouseProposalReviewSummaryV1 Summary(
        IReadOnlyList<WarehouseProposalReviewItemV1> items,
        IReadOnlyList<WarehouseProposalIssueV1> runIssues)
    {
        var issues = items.SelectMany(item => item.Issues).Concat(runIssues).ToArray();
        return new WarehouseProposalReviewSummaryV1(
            items.Count,
            items.LongCount(item => item.ConfidenceBand == WarehouseFusionConfidenceBand.High),
            items.LongCount(item => item.ConfidenceBand == WarehouseFusionConfidenceBand.Medium),
            items.LongCount(item => item.ConfidenceBand == WarehouseFusionConfidenceBand.Low),
            items.LongCount(item => item.Readiness == WarehouseProposalReviewReadiness.Ready),
            items.LongCount(item => item.Readiness == WarehouseProposalReviewReadiness.NeedsReview),
            items.LongCount(item => item.Readiness == WarehouseProposalReviewReadiness.Blocked),
            items.LongCount(item => item.CanBatchAccept),
            items.LongCount(item => item.Difference.Kind == WarehouseProposalDifferenceKind.Added),
            items.LongCount(item => item.Difference.Kind == WarehouseProposalDifferenceKind.Modified),
            items.LongCount(item => item.Difference.Kind == WarehouseProposalDifferenceKind.Unchanged),
            items.LongCount(item => item.Location.CanFocusCanvas),
            issues.LongCount(issue => issue.Severity == WarehouseProposalIssueSeverity.Info),
            issues.LongCount(issue => issue.Severity == WarehouseProposalIssueSeverity.Warning),
            issues.LongCount(issue => issue.Severity == WarehouseProposalIssueSeverity.Blocking),
            runIssues.Count,
            runIssues.LongCount(issue =>
                issue.Severity == WarehouseProposalIssueSeverity.Blocking));
    }

    private static void ValidateItem(
        WarehouseProposalReviewItemV1 item,
        WarehouseProposalReviewWorkspaceV1 workspace)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Location);
        ArgumentNullException.ThrowIfNull(item.Fields);
        ArgumentNullException.ThrowIfNull(item.Relations);
        ArgumentNullException.ThrowIfNull(item.Issues);
        ArgumentNullException.ThrowIfNull(item.Difference);
        var expectedId =
            $"ai-review-{ComputeSha256($"{workspace.ProposalSetSha256}\n{item.LogicalId:D}")[..32]}";
        var blocking = item.Issues.Any(
            issue => issue.Severity == WarehouseProposalIssueSeverity.Blocking);
        if (!item.ReviewItemId.Equals(expectedId, StringComparison.Ordinal)
            || item.LogicalId == Guid.Empty
            || !IsText(item.SourceKey, 256)
            || !IsText(item.SourceRef, SpaceCadConversionContract.MaximumSourceReferenceLength)
            || !Enum.IsDefined(item.ObjectType)
            || item.Confidence is < 0 or > 1
            || !Enum.IsDefined(item.ConfidenceBand)
            || !Enum.IsDefined(item.Readiness)
            || item.HasBlockingIssue != blocking
            || item.Readiness == WarehouseProposalReviewReadiness.Blocked != blocking
            || item.CanBatchAccept
                && (item.Readiness != WarehouseProposalReviewReadiness.Ready
                    || item.ConfidenceBand != WarehouseFusionConfidenceBand.High)
            || item.Location.FloorLogicalId != workspace.FloorLogicalId
            || !item.Location.SourceRef.Equals(item.SourceRef, StringComparison.Ordinal)
            || item.Location.Bounds.MinX > item.Location.Bounds.MaxX
            || item.Location.Bounds.MinY > item.Location.Bounds.MaxY
            || item.Location.SuggestedPaddingMillimeters < 0
            || !item.Location.CanFocusCanvas
            || item.Fields.Count == 0
            || item.Fields.Select(field => field.FieldPath).Distinct(StringComparer.Ordinal).Count()
                != item.Fields.Count
            || !item.Fields.SequenceEqual(item.Fields.OrderBy(
                field => field.FieldPath,
                StringComparer.Ordinal)))
        {
            throw new InvalidDataException("AI proposal review item is invalid.");
        }
        ValidateIssues(item.Issues);
        ValidateDifference(item.Difference, item);
    }

    private static void ValidateDifference(
        WarehouseProposalDifferenceV1 difference,
        WarehouseProposalReviewItemV1 item)
    {
        if (!Enum.IsDefined(difference.Kind)
            || !IsSha256(difference.AfterGeometrySha256)
            || difference.BeforeGeometrySha256 is not null
                && !IsSha256(difference.BeforeGeometrySha256)
            || difference.AfterGeometryBounds != item.Location.Bounds
            || difference.AfterRackLevelCount < 0
            || difference.AfterLocationCount < 0
            || difference.BeforeRackLevelCount < 0
            || difference.BeforeLocationCount < 0
            || difference.Fields.Select(field => field.FieldPath)
                .Distinct(StringComparer.Ordinal).Count() != difference.Fields.Count
            || !difference.Fields.SequenceEqual(difference.Fields.OrderBy(
                field => field.FieldPath,
                StringComparer.Ordinal)))
        {
            throw new InvalidDataException("AI proposal difference is invalid.");
        }
        foreach (var field in difference.Fields)
        {
            if (!IsText(field.FieldPath, 128)
                || !Enum.IsDefined(field.Kind)
                || field.BeforeValueToken is not null
                    && !IsText(field.BeforeValueToken, 512)
                || field.AfterValueToken is not null
                    && !IsText(field.AfterValueToken, 512)
                || field.Kind == WarehouseProposalFieldDifferenceKind.Added
                    && (field.BeforeValueToken is not null || field.AfterValueToken is null)
                || field.Kind == WarehouseProposalFieldDifferenceKind.Removed
                    && (field.BeforeValueToken is null || field.AfterValueToken is not null)
                || field.Kind == WarehouseProposalFieldDifferenceKind.Changed
                    && (field.BeforeValueToken is null || field.AfterValueToken is null
                        || field.BeforeValueToken == field.AfterValueToken))
            {
                throw new InvalidDataException("AI proposal field difference is invalid.");
            }
        }
    }

    private static void ValidateIssues(IReadOnlyList<WarehouseProposalIssueV1> issues)
    {
        foreach (var issue in issues)
        {
            ArgumentNullException.ThrowIfNull(issue);
            if (!IsText(issue.Code, 128)
                || !Enum.IsDefined(issue.Severity)
                || issue.SourceRef is not null
                    && !IsText(issue.SourceRef,
                        SpaceCadConversionContract.MaximumSourceReferenceLength)
                || issue.SourceKey is not null && !IsText(issue.SourceKey, 256)
                || issue.FieldPath is not null && !IsText(issue.FieldPath, 128)
                || issue.DetailToken is not null && !IsText(issue.DetailToken, 512))
            {
                throw new InvalidDataException("AI proposal review issue is invalid.");
            }
        }
    }

    private static bool BelongsTo(
        WarehouseProposalIssueV1 issue,
        WarehouseDraftProposalV1 proposal) =>
        issue.SourceRef is not null
            && issue.SourceRef.Equals(proposal.SourceRef, StringComparison.Ordinal)
        || issue.SourceKey is not null
            && issue.SourceKey.Equals(proposal.SourceKey, StringComparison.Ordinal);

    private static void ValidateFilter(WarehouseProposalReviewFilterV1 filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.ConfidenceBand is { } band && !Enum.IsDefined(band)
            || filter.ObjectType is { } type && !Enum.IsDefined(type)
            || filter.Readiness is { } readiness && !Enum.IsDefined(readiness)
            || filter.DifferenceKind is { } difference && !Enum.IsDefined(difference)
            || filter.IssueSeverity is { } severity && !Enum.IsDefined(severity)
            || filter.WinningSource is { } source && !Enum.IsDefined(source)
            || filter.IssueCode is not null && !IsText(filter.IssueCode, 128)
            || filter.EvidenceCode is not null && !IsText(filter.EvidenceCode, 128)
            || filter.SourceRef is not null
                && !IsText(filter.SourceRef,
                    SpaceCadConversionContract.MaximumSourceReferenceLength)
            || filter.Search is not null && !IsText(filter.Search, 128))
        {
            throw new InvalidDataException("AI proposal review filter is invalid.");
        }
    }

    private static bool Matches(
        WarehouseProposalReviewItemV1 item,
        WarehouseProposalReviewFilterV1 filter) =>
        (filter.ConfidenceBand is null || item.ConfidenceBand == filter.ConfidenceBand)
        && (filter.ObjectType is null || item.ObjectType == filter.ObjectType)
        && (filter.Readiness is null || item.Readiness == filter.Readiness)
        && (filter.DifferenceKind is null || item.Difference.Kind == filter.DifferenceKind)
        && (filter.IssueSeverity is null
            || item.Issues.Any(issue => issue.Severity == filter.IssueSeverity))
        && (filter.IssueCode is null
            || item.Issues.Any(issue => issue.Code.Equals(
                filter.IssueCode,
                StringComparison.Ordinal)))
        && (filter.WinningSource is null
            || item.Fields.Any(field => field.WinningSource == filter.WinningSource))
        && (filter.EvidenceCode is null || HasEvidenceCode(item, filter.EvidenceCode))
        && (filter.SourceRef is null || item.SourceRef.Equals(
            filter.SourceRef,
            StringComparison.OrdinalIgnoreCase))
        && (!filter.OnlyLocatable || item.Location.CanFocusCanvas)
        && (filter.Search is null || MatchesSearch(item, filter.Search));

    private static bool HasEvidenceCode(
        WarehouseProposalReviewItemV1 item,
        string code) =>
        item.Fields.SelectMany(field => field.Evidence)
            .SelectMany(evidence => evidence.EvidenceCodes)
            .Any(candidate => candidate.Equals(code, StringComparison.Ordinal))
        || item.Relations.SelectMany(relation => relation.EvidenceCodes)
            .Any(candidate => candidate.Equals(code, StringComparison.Ordinal))
        || item.Issues.Any(issue => issue.Code.Equals(code, StringComparison.Ordinal));

    private static bool MatchesSearch(
        WarehouseProposalReviewItemV1 item,
        string search) =>
        item.ReviewItemId.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.LogicalId.ToString("D").Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.SourceKey.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.SourceRef.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.ObjectType.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.Fields.Any(field =>
            field.FieldPath.Contains(search, StringComparison.OrdinalIgnoreCase)
            || field.ValueToken.Contains(search, StringComparison.OrdinalIgnoreCase))
        || item.Issues.Any(issue =>
            issue.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
            || issue.DetailToken?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    private static string? BatchAcceptIneligibility(
        WarehouseProposalReviewItemV1 item)
    {
        if (item.HasBlockingIssue)
            return "SPACE_AI_PROPOSAL_BLOCKING";
        if (item.Readiness != WarehouseProposalReviewReadiness.Ready)
            return "SPACE_AI_INDIVIDUAL_REVIEW_REQUIRED";
        if (item.ConfidenceBand != WarehouseFusionConfidenceBand.High)
            return "SPACE_AI_HIGH_CONFIDENCE_REQUIRED";
        if (!item.CanBatchAccept)
            return "SPACE_AI_BATCH_ACCEPT_DISABLED";
        return null;
    }

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

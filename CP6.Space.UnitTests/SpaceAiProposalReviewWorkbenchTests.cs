using CP6.Space.Application;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiProposalReviewWorkbenchTests
{
    [Fact]
    public async Task Workspace_builds_deterministic_field_geometry_and_capacity_diffs()
    {
        var proposalSet = await ProposalSet();
        var empty = Baseline(proposalSet, []);
        var added = WarehouseProposalReviewWorkbench.Build(proposalSet, empty);
        var target = added.Items[0];
        var proposal = proposalSet.Proposals.Single(
            item => item.LogicalId == target.LogicalId);
        var unchangedObject = BaselineObject(target, proposal);
        var modifiedObject = unchangedObject with
        {
            Fields = unchangedObject.Fields
                .Append(new("attributes.legacy", "retire-me"))
                .ToArray(),
        };

        var unchangedWorkspace = WarehouseProposalReviewWorkbench.Build(
            proposalSet,
            Baseline(proposalSet, [unchangedObject]));
        var modifiedWorkspace = WarehouseProposalReviewWorkbench.Build(
            proposalSet,
            Baseline(proposalSet, [modifiedObject]));
        var unchanged = unchangedWorkspace.Items.Single(
            item => item.LogicalId == target.LogicalId);
        var modified = modifiedWorkspace.Items.Single(
            item => item.LogicalId == target.LogicalId);

        Assert.Equal(WarehouseProposalDifferenceKind.Unchanged, unchanged.Difference.Kind);
        Assert.False(unchanged.Difference.GeometryChanged);
        Assert.Empty(unchanged.Difference.Fields);
        Assert.Equal(WarehouseProposalDifferenceKind.Modified, modified.Difference.Kind);
        var removed = Assert.Single(modified.Difference.Fields);
        Assert.Equal("attributes.legacy", removed.FieldPath);
        Assert.Equal(WarehouseProposalFieldDifferenceKind.Removed, removed.Kind);
        Assert.Equal("retire-me", removed.BeforeValueToken);
        Assert.Null(removed.AfterValueToken);
        Assert.Equal(
            proposal.RackDerivation?.LocationCount ?? 0,
            modified.Difference.AfterLocationCount);
        Assert.True(modifiedWorkspace.IsReadOnlyWorkspace);
        Assert.False(modifiedWorkspace.DecisionWritten);
        Assert.False(modifiedWorkspace.DraftWritten);
        Assert.Equal(
            WarehouseProposalReviewWorkbench.Serialize(modifiedWorkspace),
            WarehouseProposalReviewWorkbench.Serialize(
                WarehouseProposalReviewWorkbench.Build(
                    proposalSet,
                    Baseline(proposalSet, [modifiedObject]))));
    }

    [Fact]
    public async Task Query_uses_fixed_order_bounded_pages_and_filter_scoped_cursor()
    {
        var proposalSet = await ProposalSet();
        var workspace = WarehouseProposalReviewWorkbench.Build(
            proposalSet,
            Baseline(proposalSet, []));
        var codec = new TestCursorCodec();

        var first = WarehouseProposalReviewWorkbench.Query(
            workspace,
            new WarehouseProposalReviewQueryV1(Limit: 2),
            codec);
        var second = WarehouseProposalReviewWorkbench.Query(
            workspace,
            new WarehouseProposalReviewQueryV1(Cursor: first.NextCursor, Limit: 2),
            codec);

        Assert.Equal(workspace.ReviewEtag, first.ReviewEtag);
        Assert.Equal(2, first.Items.Count);
        Assert.DoesNotContain(
            second.Items,
            item => first.Items.Any(firstItem => firstItem.ReviewItemId == item.ReviewItemId));
        Assert.Equal(
            workspace.Items.Select(item => item.ReviewItemId).Take(4),
            first.Items.Concat(second.Items).Select(item => item.ReviewItemId));
        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.Query(
                workspace,
                new WarehouseProposalReviewQueryV1(
                    new WarehouseProposalReviewFilterV1(
                        ConfidenceBand: WarehouseFusionConfidenceBand.High),
                    first.NextCursor,
                    2),
                codec));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WarehouseProposalReviewWorkbench.Query(
                workspace,
                new WarehouseProposalReviewQueryV1(Limit: 201),
                codec));

        var blocking = WarehouseProposalReviewWorkbench.Query(
            workspace,
            new WarehouseProposalReviewQueryV1(
                new WarehouseProposalReviewFilterV1(
                    Readiness: WarehouseProposalReviewReadiness.Blocked)),
            codec);
        Assert.All(blocking.Items, item => Assert.True(item.HasBlockingIssue));
    }

    [Fact]
    public async Task Batch_preview_never_writes_and_accept_excludes_ineligible_items()
    {
        var proposalSet = await ProposalSet(includeRackProfiles: false);
        var workspace = WarehouseProposalReviewWorkbench.Build(
            proposalSet,
            Baseline(proposalSet, []));
        var all = new WarehouseProposalReviewFilterV1();

        var accept = WarehouseProposalReviewWorkbench.PreviewBatchSelection(
            workspace,
            new WarehouseProposalBatchSelectionRequestV1(
                WarehouseProposalBatchAction.Accept,
                workspace.ReviewEtag,
                Filter: all));
        var reject = WarehouseProposalReviewWorkbench.PreviewBatchSelection(
            workspace,
            new WarehouseProposalBatchSelectionRequestV1(
                WarehouseProposalBatchAction.Reject,
                workspace.ReviewEtag,
                Filter: all));

        Assert.Equal(workspace.Items.Count, accept.SelectedCount);
        Assert.NotEmpty(accept.IneligibleItems);
        Assert.DoesNotContain(
            accept.EligibleReviewItemIds,
            id => workspace.Items.Single(item => item.ReviewItemId == id).HasBlockingIssue);
        Assert.Equal(reject.SelectedCount, reject.EligibleReviewItemIds.Count);
        Assert.Empty(reject.IneligibleItems);
        Assert.True(accept.RequiresServerRevalidation);
        Assert.False(accept.DecisionWritten);
        Assert.False(accept.DraftWritten);
        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.PreviewBatchSelection(
                workspace,
                new WarehouseProposalBatchSelectionRequestV1(
                    WarehouseProposalBatchAction.Accept,
                    new string('0', 64),
                    ReviewItemIds: [workspace.Items[0].ReviewItemId])));
        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.PreviewBatchSelection(
                workspace,
                new WarehouseProposalBatchSelectionRequestV1(
                    WarehouseProposalBatchAction.Accept,
                    workspace.ReviewEtag,
                    ReviewItemIds: ["unknown-review-item"])));
    }

    [Fact]
    public async Task Baseline_and_workspace_tampering_fail_closed()
    {
        var proposalSet = await ProposalSet();
        var baseline = Baseline(proposalSet, []);
        var workspace = WarehouseProposalReviewWorkbench.Build(proposalSet, baseline);

        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.ValidateBaseline(
                baseline with { ContentRevision = baseline.ContentRevision + 1 }));
        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.Validate(
                workspace with { ReviewEtag = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() =>
            WarehouseProposalReviewWorkbench.Validate(
                workspace with
                {
                    Items = workspace.Items.Select((item, index) => index == 0
                        ? item with { CanBatchAccept = !item.CanBatchAccept }
                        : item).ToArray(),
                }));
    }

    private static async Task<WarehouseDraftProposalSetV1> ProposalSet(
        bool includeRackProfiles = true)
    {
        var fixture = SpaceAiWarehouseSynthesisTests.Fixture();
        IReadOnlyList<WarehouseRackProfileBindingV1> profiles = includeRackProfiles
            ?
            [
                new("H:160", WarehouseRackProfileSource.ExplicitSelected,
                    SpaceAiWarehouseSynthesisTests.Profile(
                        "10000000-0000-0000-0000-000000000001")),
                new("H:161", WarehouseRackProfileSource.ExplicitSelected,
                    SpaceAiWarehouseSynthesisTests.Profile(
                        "20000000-0000-0000-0000-000000000002")),
            ]
            : [];
        return await new WarehouseDraftSynthesizer().SynthesizeAsync(
            SpaceAiWarehouseSynthesisTests.Request(fixture, [], profiles: profiles));
    }

    private static WarehouseProposalReviewBaselineSnapshotV1 Baseline(
        WarehouseDraftProposalSetV1 proposalSet,
        IReadOnlyList<WarehouseProposalReviewBaselineObjectV1> objects) =>
        WarehouseProposalReviewWorkbench.SealBaseline(
            proposalSet.TenantId,
            proposalSet.ModelVersionId,
            proposalSet.FloorLogicalId,
            contentRevision: 12,
            contentHash: new string('a', 64),
            objects);

    private static WarehouseProposalReviewBaselineObjectV1 BaselineObject(
        WarehouseProposalReviewItemV1 item,
        WarehouseDraftProposalV1 proposal) => new(
        item.LogicalId,
        item.ObjectType,
        item.Difference.AfterGeometrySha256,
        item.Location.Bounds,
        item.Fields.Select(field => new WarehouseProposalReviewBaselineFieldV1(
            field.FieldPath,
            field.ValueToken)).ToArray(),
        proposal.RackDerivation?.Levels.Count ?? 0,
        proposal.RackDerivation?.LocationCount ?? 0);

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        private readonly Dictionary<string, SpaceCursorState> _states = new();

        public string Encode(SpaceCursorState state)
        {
            var token = Guid.NewGuid().ToString("N");
            _states[token] = state;
            return token;
        }

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            if (!_states.TryGetValue(cursor, out var state)
                || !state.Resource.Equals(expectedResource, StringComparison.Ordinal)
                || !state.FilterHash.Equals(expectedFilterHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("cursor-scope-mismatch");
            }
            return state;
        }
    }
}

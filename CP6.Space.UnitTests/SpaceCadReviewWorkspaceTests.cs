using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadReviewWorkspaceTests
{
    [Fact]
    public void Workspace_combines_diagnostics_and_low_confidence_proposals()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-001")]);

        var workspace = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor);

        var proposalCount = context.Diagnostics.Evidence.LongCount(
            item => item.ConfidenceBand is SpaceCadConfidenceBand.Low
                or SpaceCadConfidenceBand.Rejected);
        Assert.Equal(
            context.Diagnostics.Diagnostics.Count + proposalCount,
            workspace.Summary.TotalCount);
        Assert.Equal(
            context.Diagnostics.Diagnostics.Count,
            workspace.Summary.CadDiagnosticCount);
        Assert.Equal(proposalCount, workspace.Summary.ProposalReviewCount);
        Assert.Equal(0, workspace.Summary.ResolvedCount);
        Assert.All(workspace.Items, item =>
            Assert.Equal(SpaceCadReviewItemStatus.Open, item.Status));
        Assert.Contains(workspace.Items, item =>
            item.Kind == SpaceCadReviewItemKind.LowConfidenceProposal
            && item.SourceRef == "H:160"
            && item.Location.CanFocusCanvas);
    }

    [Fact]
    public void Exceptional_excel_matches_are_listed_and_queryable()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-NOT-IN-CAD")]);
        var match = SpaceExcelCadMatchingTests.Build(context);
        var workspace = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor,
            match);

        var page = SpaceCadReviewWorkspace.Query(
            workspace,
            new SpaceCadReviewWorkspaceQueryV1(
                Kind: SpaceCadReviewItemKind.ExcelUnmatched,
                Search: "R-NOT-IN-CAD"));

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(SpaceCadIssueSeverity.Warning, item.Severity);
        Assert.Equal("SPACE_EXCEL_CAD_UNMATCHED", item.Code);
        Assert.Equal("R-NOT-IN-CAD", item.RackCode);
        Assert.False(item.Location.CanFocusCanvas);
        Assert.Equal(1, workspace.Summary.ExcelReviewCount);
    }

    [Fact]
    public void Missing_item_in_successor_is_resolved_and_can_reopen()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-NOT-IN-CAD")]);
        var match = SpaceExcelCadMatchingTests.Build(context);
        var first = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor,
            match);

        var successor = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor,
            matchPreview: null,
            previousWorkspace: first);
        var resolved = Assert.Single(
            successor.Items,
            item => item.Kind == SpaceCadReviewItemKind.ExcelUnmatched);

        Assert.Equal(SpaceCadReviewItemStatus.Resolved, resolved.Status);
        Assert.Equal(first.WorkspaceSha256, resolved.ResolvedFromWorkspaceSha256);
        Assert.Equal(first.WorkspaceSha256, successor.PreviousWorkspaceSha256);
        Assert.Equal(1, successor.Summary.ResolvedCount);

        var reopened = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor,
            match,
            successor);
        var open = Assert.Single(
            reopened.Items,
            item => item.Kind == SpaceCadReviewItemKind.ExcelUnmatched);
        Assert.Equal(SpaceCadReviewItemStatus.Open, open.Status);
        Assert.Null(open.ResolvedFromWorkspaceSha256);
    }

    [Fact]
    public void Workspace_rejects_cross_tenant_or_stale_editor_chain()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-001")]);
        var foreignEditor = SpaceExcelCadMatching.SealEditorSnapshot(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            context.Editor.ModelVersionId,
            context.Editor.FloorLogicalId,
            context.Editor.FloorCode,
            context.Editor.ContentRevision,
            context.Editor.ContentHash,
            context.Editor.Racks);
        Assert.Throws<InvalidDataException>(() => SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            foreignEditor));

        var newerEditor = SpaceExcelCadMatching.SealEditorSnapshot(
            context.Editor.TenantId,
            context.Editor.ModelVersionId,
            context.Editor.FloorLogicalId,
            context.Editor.FloorCode,
            context.Editor.ContentRevision + 1,
            context.Editor.ContentHash,
            context.Editor.Racks);
        var newer = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            newerEditor);
        Assert.Throws<InvalidDataException>(() => SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor,
            previousWorkspace: newer));

        var sameRevisionDifferentHash = SpaceExcelCadMatching.SealEditorSnapshot(
            context.Editor.TenantId,
            context.Editor.ModelVersionId,
            context.Editor.FloorLogicalId,
            context.Editor.FloorCode,
            context.Editor.ContentRevision,
            new string('1', 64),
            context.Editor.Racks);
        Assert.Throws<InvalidDataException>(() => SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            sameRevisionDifferentHash,
            previousWorkspace: SpaceCadReviewWorkspace.Build(
                context.Diagnostics,
                context.Editor)));
    }

    [Fact]
    public void Workspace_is_deterministic_and_rejects_tampering_or_large_pages()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-001")]);

        var first = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor);
        var second = SpaceCadReviewWorkspace.Build(
            context.Diagnostics,
            context.Editor);
        var serialized = SpaceCadReviewWorkspace.Serialize(first);

        Assert.Equal(first.WorkspaceSha256, second.WorkspaceSha256);
        Assert.Equal(serialized, SpaceCadReviewWorkspace.Serialize(second));
        Assert.DoesNotContain("\"matchPreviewSha256\":", serialized);
        Assert.DoesNotContain("\"previousWorkspaceSha256\":", serialized);
        Assert.Throws<InvalidDataException>(() => SpaceCadReviewWorkspace.Validate(
            first with { WorkspaceSha256 = new string('0', 64) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpaceCadReviewWorkspace.Query(
                first,
                new SpaceCadReviewWorkspaceQueryV1(
                    Limit: SpaceCadReviewWorkspaceVersions.MaximumPageSize + 1)));
    }
}

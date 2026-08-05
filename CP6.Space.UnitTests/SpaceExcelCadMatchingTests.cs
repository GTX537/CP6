using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceExcelCadMatchingTests
{
    private static readonly Guid ModelVersionId =
        Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid ExcelSourceId =
        Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid PreflightJobId =
        Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid EditorLogicalId =
        Guid.Parse("dddddddd-1111-2222-3333-444444444444");
    private static readonly Guid EditorRevisionId =
        Guid.Parse("eeeeeeee-1111-2222-3333-444444444444");

    [Fact]
    public void Rack_code_match_creates_new_row_with_cad_provenance_and_location()
    {
        var context = Context([Rack("R-001")]);

        var preview = Build(context);

        var row = Assert.Single(preview.Rows);
        Assert.Equal(SpaceExcelCadMatchDisposition.New, row.Disposition);
        Assert.Equal("H:160", row.MatchedSourceRef);
        Assert.Equal(0.69m, row.CadConfidence);
        Assert.Equal(SpaceCadConfidenceBand.Low, row.CadConfidenceBand);
        Assert.Contains(
            row.KeyEvidence,
            item => item.Kind == SpaceExcelCadMatchKeyKind.CadRackCode);
        Assert.True(row.Location!.CanFocusCanvas);
        Assert.Equal(1, preview.Summary.NewCount);
        Assert.False(preview.CanConfirm);
        Assert.Matches("^[0-9a-f]{64}$", row.MatchEvidenceSha256);
        Assert.Matches("^[0-9a-f]{64}$", preview.MatchPreviewSha256);
    }

    [Fact]
    public void Graphical_source_key_matches_without_requiring_a_business_attribute()
    {
        var context = Context([Rack("H:160")]);

        var row = Assert.Single(Build(context).Rows);

        Assert.Equal(SpaceExcelCadMatchDisposition.New, row.Disposition);
        Assert.Contains(
            row.KeyEvidence,
            item => item.Kind == SpaceExcelCadMatchKeyKind.CadSourceRef);
        Assert.Equal("H:160", row.MatchedSourceRef);
    }

    [Fact]
    public void Existing_editor_rack_is_unchanged_or_update_from_canonical_values()
    {
        var unchanged = Context(
            [Rack("R-001")],
            [EditorRack("R-001", "H:160")]);
        var updated = Context(
            [Rack("R-001", x: "3500")],
            [EditorRack("R-001", "H:160")]);

        var same = Assert.Single(Build(unchanged).Rows);
        var changed = Assert.Single(Build(updated).Rows);

        Assert.Equal(SpaceExcelCadMatchDisposition.Unchanged, same.Disposition);
        Assert.Equal(EditorLogicalId, same.EditorLogicalId);
        Assert.Empty(same.DifferenceFields);
        Assert.Equal(SpaceExcelCadMatchDisposition.Update, changed.Disposition);
        Assert.Equal(["XMm"], changed.DifferenceFields);
    }

    [Fact]
    public void Unmatched_rows_are_explicit_and_queryable_as_a_separate_list()
    {
        var context = Context([Rack("R-NOT-IN-CAD")]);
        var preview = Build(context);

        var unmatched = SpaceExcelCadMatching.Query(
            preview,
            new SpaceExcelCadMatchQueryV1(
                Disposition: SpaceExcelCadMatchDisposition.Unmatched));

        var row = Assert.Single(unmatched.Items);
        Assert.Equal(1, unmatched.TotalCount);
        Assert.Equal("R-NOT-IN-CAD", row.Values.RackCode);
        Assert.Null(row.CadPreviewObjectId);
        Assert.Null(row.EditorLogicalId);
        Assert.Empty(row.KeyEvidence);
        Assert.Equal(1, preview.Summary.UnmatchedCount);
        Assert.False(preview.CanConfirm);
    }

    [Fact]
    public void Cad_and_editor_source_disagreement_is_a_conflict()
    {
        var context = Context(
            [Rack("R-001")],
            [EditorRack("R-001", "H:161")]);

        var row = Assert.Single(Build(context).Rows);

        Assert.Equal(SpaceExcelCadMatchDisposition.Conflict, row.Disposition);
        Assert.Contains("SourceRef", row.DifferenceFields);
        Assert.Equal(1, Build(context).Summary.ConflictCount);
    }

    [Fact]
    public void Preflight_blocking_row_is_error_without_fabricated_values()
    {
        var context = Context([Rack("R-001", width: "not-a-number")]);

        var preview = Build(context);
        var row = Assert.Single(preview.Rows);

        Assert.Equal(SpaceExcelCadMatchDisposition.Error, row.Disposition);
        Assert.Contains("SPACE_EXCEL_TYPE_INVALID", row.ErrorCodes);
        Assert.Null(row.Values.WidthMillimeters);
        Assert.Null(row.CadPreviewObjectId);
        Assert.Equal(1, preview.Summary.ErrorCount);
        Assert.False(preview.CanConfirm);
        Assert.DoesNotContain(
            "\"widthMillimeters\":",
            SpaceExcelCadMatching.Serialize(preview));
    }

    [Fact]
    public void Two_excel_rows_cannot_claim_the_same_cad_target()
    {
        var context = Context([
            Rack("R-001", row: 2),
            Rack("H:160", row: 3),
        ]);

        var preview = Build(context);

        Assert.Equal(2, preview.Summary.ConflictCount);
        Assert.All(
            preview.Rows,
            row => Assert.Contains(
                "TargetClaimedByMultipleExcelRows",
                row.DifferenceFields));
    }

    [Fact]
    public void Matching_is_deterministic_and_rejects_chain_or_hash_tampering()
    {
        var context = Context([Rack("R-001")]);

        var first = Build(context);
        var second = Build(context);

        Assert.Equal(first.MatchPreviewSha256, second.MatchPreviewSha256);
        var serialized = SpaceExcelCadMatching.Serialize(first);
        Assert.Equal(serialized, SpaceExcelCadMatching.Serialize(second));
        Assert.DoesNotContain("\"editorContentHash\":", serialized);
        Assert.DoesNotContain("\"editorLogicalId\":", serialized);
        Assert.Throws<InvalidDataException>(() => SpaceExcelCadMatching.Validate(
            first with { MatchPreviewSha256 = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() => SpaceExcelCadMatching.Build(
            context.Scenario.Request.TenantId,
            ModelVersionId,
            ExcelSourceId,
            PreflightJobId,
            context.Profile,
            context.Workbook,
            context.Semantic with { FloorCode = "OTHER" },
            context.Diagnostics,
            context.Editor));
        var foreignEditor = SpaceExcelCadMatching.SealEditorSnapshot(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            ModelVersionId,
            context.Semantic.FloorLogicalId,
            context.Semantic.FloorCode,
            context.Editor.ContentRevision,
            context.Editor.ContentHash,
            context.Editor.Racks);
        Assert.Throws<InvalidDataException>(() => Build(
            context with { Editor = foreignEditor }));
        Assert.Throws<InvalidDataException>(() =>
            SpaceExcelCadMatching.ValidateEditorSnapshot(
                context.Editor with { ContentRevision = 99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpaceExcelCadMatching.Query(
            first,
            new SpaceExcelCadMatchQueryV1(
                Limit: SpaceExcelCadMatchVersions.MaximumPageSize + 1)));
    }

    internal static SpaceExcelCadMatchPreviewV1 Build(MatchContext context) =>
        SpaceExcelCadMatching.Build(
            context.Scenario.Request.TenantId,
            ModelVersionId,
            ExcelSourceId,
            PreflightJobId,
            context.Profile,
            context.Workbook,
            context.Semantic,
            context.Diagnostics,
            context.Editor);

    internal static MatchContext Context(
        IReadOnlyList<RowValues> racks,
        IReadOnlyList<SpaceExcelEditorRackSnapshotV1>? editorRacks = null)
    {
        var scenario = SpaceCadSemanticParserTests.Scenario();
        var semantic = SpaceCadSemanticParser.Parse(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview);
        var diagnostics = SpaceCadSemanticDiagnostics.Build(
            scenario.Request,
            scenario.Preparation,
            scenario.Inventory,
            scenario.Profile,
            scenario.MappingPreview,
            semantic);
        var definition = Definition();
        var profile = new SpaceExcelMappingProfileDto(
            Guid.Parse("ffffffff-1111-2222-3333-444444444444"),
            "E03-S04 test profile",
            "Tenant",
            1,
            false,
            new string('a', 64),
            definition,
            null,
            null,
            null,
            null,
            null);
        var editor = SpaceExcelCadMatching.SealEditorSnapshot(
            scenario.Request.TenantId,
            ModelVersionId,
            semantic.FloorLogicalId,
            semantic.FloorCode,
            7,
            null,
            editorRacks ?? []);
        return new MatchContext(
            scenario,
            semantic,
            diagnostics,
            profile,
            Workbook(racks),
            editor);
    }

    private static SpaceExcelMappingDefinitionDto Definition() => new(
        SpaceExcelTargetCatalog.MappingSchemaVersion,
        "Ignore",
        "Reject",
        "Reject",
        [
            new SpaceExcelSheetMappingDto(
                "Racks",
                "Racks",
                "Exact",
                1,
                2,
                SpaceExcelTargetCatalog.ForSheet("Racks")
                    .Select(field => new SpaceExcelColumnMappingDto(
                        field.Field,
                        field.Field,
                        null,
                        field.DataType,
                        null,
                        null,
                        field.IsBusinessKey,
                        field.ReferenceTarget,
                        [],
                        null))
                    .ToArray()),
        ]);

    private static SpaceExcelWorkbookData Workbook(IReadOnlyList<RowValues> rows)
    {
        var fields = SpaceExcelTargetCatalog.ForSheet("Racks");
        var header = new SpaceExcelWorkbookRow(
            1,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ColumnName(index + 1),
                    field.Field,
                    false))
                .ToDictionary(cell => cell.ColumnIndex));
        var data = rows.Select(row => new SpaceExcelWorkbookRow(
                row.Row,
                fields.Select((field, index) => new SpaceExcelWorkbookCell(
                        index + 1,
                        ColumnName(index + 1),
                        row.Values.GetValueOrDefault(field.Field),
                        false))
                    .ToDictionary(cell => cell.ColumnIndex)))
            .ToArray();
        return new SpaceExcelWorkbookData(
            [new SpaceExcelWorkbookSheet("Racks", [header, .. data])]);
    }

    internal static RowValues Rack(
        string rackCode,
        int row = 2,
        string x = "3000",
        string width = "1000") => new(
        row,
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FloorCode"] = "F01",
            ["ZoneCode"] = "Z1",
            ["RackCode"] = rackCode,
            ["XMm"] = x,
            ["YMm"] = "3000",
            ["ZMm"] = "0",
            ["WidthMm"] = width,
            ["DepthMm"] = "1200",
            ["HeightMm"] = "5000",
            ["RotationZDeg"] = "0",
            ["RackTemplateCode"] = null,
            ["LifecycleStatus"] = "Active",
        });

    private static SpaceExcelEditorRackSnapshotV1 EditorRack(
        string rackCode,
        string? sourceRef) => new(
        EditorLogicalId,
        EditorRevisionId,
        rackCode,
        sourceRef,
        "F01",
        "Z1",
        3_000,
        3_000,
        0,
        1_000,
        1_200,
        5_000,
        0,
        "Active");

    private static string ColumnName(int index)
    {
        var value = index;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    internal sealed record RowValues(
        int Row,
        IReadOnlyDictionary<string, string?> Values);

    internal sealed record MatchContext(
        SpaceCadSemanticParserTests.SemanticScenario Scenario,
        SpaceCadSemanticPreviewV1 Semantic,
        SpaceCadSemanticDiagnosticIndexV1 Diagnostics,
        SpaceExcelMappingProfileDto Profile,
        SpaceExcelWorkbookData Workbook,
        SpaceExcelEditorSnapshotV1 Editor);
}

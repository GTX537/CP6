using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExcelPreflightValidatorTests
{
    private readonly SpaceExcelPreflightValidator _validator = new();

    [Fact]
    public void Standard_workbook_passes_all_preflight_rules()
    {
        var result = _validator.Validate(StandardDefinition(), ValidWorkbook());

        Assert.Equal(5, result.SheetCount);
        Assert.Equal(5, result.DataRowCount);
        Assert.Equal(5, result.ValidRowCount);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Required_type_range_enum_duplicate_and_reference_errors_are_located()
    {
        var workbook = ValidWorkbook();
        var racks = workbook.Sheets.Single(sheet => sheet.Name == "Racks");
        var rackRows = racks.Rows.ToList();
        rackRows[1] = DataRow("Racks", 2, new()
        {
            ["FloorCode"] = null,
            ["ZoneCode"] = "Z1",
            ["RackCode"] = "R1",
            ["XMm"] = "0",
            ["YMm"] = "0",
            ["WidthMm"] = "-1",
            ["DepthMm"] = "100",
            ["HeightMm"] = "200",
            ["LifecycleStatus"] = "Unknown",
        });
        var locations = workbook.Sheets.Single(sheet => sheet.Name == "Locations");
        var locationRows = locations.Rows.ToList();
        locationRows[1] = DataRow("Locations", 2, new()
        {
            ["LocationCode"] = "L1",
            ["RackCode"] = "MISSING",
            ["ColumnNo"] = "abc",
            ["LevelNo"] = "1",
            ["DepthNo"] = "1",
            ["LifecycleStatus"] = "Active",
        });
        locationRows.Add(DataRow("Locations", 3, new()
        {
            ["LocationCode"] = "L1",
            ["RackCode"] = "R1",
            ["ColumnNo"] = "1",
            ["LevelNo"] = "9",
            ["DepthNo"] = "1",
            ["LifecycleStatus"] = "Active",
        }));
        workbook = workbook with
        {
            Sheets = workbook.Sheets.Select(sheet => sheet.Name switch
            {
                "Racks" => sheet with { Rows = rackRows },
                "Locations" => sheet with { Rows = locationRows },
                _ => sheet,
            }).ToArray(),
        };

        var result = _validator.Validate(StandardDefinition(), workbook);

        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_REQUIRED_VALUE_MISSING" &&
            item.Sheet == "Racks" && item.Row == 2 && item.Column == "A");
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_TYPE_INVALID" &&
            item.Sheet == "Locations" && item.Row == 2 && item.Column == "C");
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_VALUE_OUT_OF_RANGE" &&
            item.TargetField == "WidthMm");
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_ENUM_VALUE_INVALID" &&
            item.TargetField == "LifecycleStatus");
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_DUPLICATE_KEY" && item.Row == 3);
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_REFERENCE_NOT_FOUND" &&
            item.TargetField == "RackCode" && item.Row == 2);
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_REFERENCE_NOT_FOUND" &&
            item.TargetField == "LevelNo" && item.Row == 3);
        Assert.All(result.Findings, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.SuggestedActionCode)));
        Assert.True(result.ValidRowCount < result.DataRowCount);
    }

    [Fact]
    public void Duplicate_policy_can_warn_without_blocking_and_unknown_columns_can_block()
    {
        var definition = StandardDefinition() with
        {
            DuplicateRowPolicy = "KeepFirst",
            UnknownColumnPolicy = "Reject",
        };
        var workbook = ValidWorkbook();
        var racks = workbook.Sheets.Single(sheet => sheet.Name == "Racks");
        var header = racks.Rows[0];
        var extraIndex = header.Cells.Keys.Max() + 1;
        var headerCells = header.Cells.ToDictionary(item => item.Key, item => item.Value);
        headerCells[extraIndex] = new(
            extraIndex,
            ColumnName(extraIndex),
            "VendorNote",
            false);
        var duplicate = new SpaceExcelWorkbookRow(
            3,
            racks.Rows[1].Cells.ToDictionary(
                item => item.Key,
                item => item.Value with { }));
        workbook = workbook with
        {
            Sheets = workbook.Sheets.Select(sheet =>
                sheet.Name == "Racks"
                    ? sheet with
                    {
                        Rows =
                        [
                            header with { Cells = headerCells },
                            racks.Rows[1],
                            duplicate,
                        ],
                    }
                    : sheet).ToArray(),
        };

        var result = _validator.Validate(definition, workbook);

        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_UNKNOWN_COLUMN" &&
            item.Severity == SpaceIssueSeverity.Blocking &&
            item.Row == 1 && item.Column == ColumnName(extraIndex));
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_DUPLICATE_KEY" &&
            item.Severity == SpaceIssueSeverity.Warning &&
            item.Row == 3);
    }

    [Fact]
    public void Enum_and_unit_conversions_apply_before_validation()
    {
        var standard = StandardDefinition();
        var racks = standard.Sheets.Single(sheet => sheet.TargetSheet == "Racks");
        var converted = racks with
        {
            Columns = racks.Columns.Select(column => column.TargetField switch
            {
                "WidthMm" => column with { UnitConversionMultiplier = 1000m },
                "LifecycleStatus" => column with
                {
                    EnumConversions = [new("启用", "Active")],
                },
                _ => column,
            }).ToArray(),
        };
        var workbook = ValidWorkbook();
        var rackSheet = workbook.Sheets.Single(sheet => sheet.Name == "Racks");
        var values = ValuesFromRow("Racks", rackSheet.Rows[1]);
        values["WidthMm"] = "1.2";
        values["LifecycleStatus"] = "启用";
        workbook = workbook with
        {
            Sheets = workbook.Sheets.Select(sheet =>
                sheet.Name == "Racks"
                    ? sheet with { Rows = [sheet.Rows[0], DataRow("Racks", 2, values)] }
                    : sheet).ToArray(),
        };

        var result = _validator.Validate(
            standard with
            {
                Sheets = standard.Sheets.Select(sheet =>
                    sheet.TargetSheet == "Racks" ? converted : sheet).ToArray(),
            },
            workbook);

        Assert.DoesNotContain(result.Findings, item =>
            item.TargetField is "WidthMm" or "LifecycleStatus");
    }

    [Fact]
    public void Formula_in_a_mapped_cell_is_rejected_without_evaluating_it()
    {
        var workbook = ValidWorkbook();
        var racks = workbook.Sheets.Single(sheet => sheet.Name == "Racks");
        var cells = racks.Rows[1].Cells.ToDictionary(item => item.Key, item => item.Value);
        var width = SpaceExcelTargetCatalog.ForSheet("Racks")
            .Select((field, index) => (field, index: index + 1))
            .Single(item => item.field.Field == "WidthMm");
        cells[width.index] = cells[width.index] with { HasFormula = true };
        workbook = workbook with
        {
            Sheets = workbook.Sheets.Select(sheet =>
                sheet.Name == "Racks"
                    ? sheet with
                    {
                        Rows = [sheet.Rows[0], sheet.Rows[1] with { Cells = cells }],
                    }
                    : sheet).ToArray(),
        };

        var result = _validator.Validate(StandardDefinition(), workbook);

        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_FORMULA_UNSUPPORTED" &&
            item.Row == 2 && item.Column == ColumnName(width.index));
    }

    [Fact]
    public void Header_only_workbook_cannot_be_confirmed_as_an_empty_import()
    {
        var workbook = new SpaceExcelWorkbookData(
            SpaceExcelTargetCatalog.Sheets.Select(sheet =>
                new SpaceExcelWorkbookSheet(sheet, [HeaderRow(sheet)]))
                .ToArray());

        var result = _validator.Validate(StandardDefinition(), workbook);

        Assert.Equal(0, result.DataRowCount);
        Assert.Contains(result.Findings, item =>
            item.Code == "SPACE_EXCEL_NO_DATA_ROWS" &&
            item.Severity == SpaceIssueSeverity.Blocking &&
            item.Row is null);
    }

    private static SpaceExcelMappingDefinitionDto StandardDefinition() =>
        new(
            1,
            "Warning",
            "Reject",
            "Reject",
            SpaceExcelTargetCatalog.Sheets.Select(sheet =>
                new SpaceExcelSheetMappingDto(
                    sheet,
                    sheet,
                    "Exact",
                    1,
                    2,
                    SpaceExcelTargetCatalog.ForSheet(sheet).Select(field =>
                        new SpaceExcelColumnMappingDto(
                            field.Field,
                            field.Field,
                            null,
                            field.DataType,
                            null,
                            null,
                            field.IsBusinessKey,
                            field.ReferenceTarget,
                            [],
                            null)).ToArray())).ToArray());

    private static SpaceExcelWorkbookData ValidWorkbook() =>
        new(
        [
            Sheet("Racks", new()
            {
                ["FloorCode"] = "F1", ["ZoneCode"] = "Z1",
                ["RackCode"] = "R1", ["XMm"] = "0", ["YMm"] = "0",
                ["ZMm"] = "0", ["WidthMm"] = "100", ["DepthMm"] = "100",
                ["HeightMm"] = "200", ["RotationZDeg"] = "0",
                ["LifecycleStatus"] = "Active",
            }),
            Sheet("RackLevels", new()
            {
                ["RackCode"] = "R1", ["LevelNo"] = "1",
                ["BottomZMm"] = "0", ["ClearHeightMm"] = "100",
                ["BinCount"] = "2", ["DepthCount"] = "2",
                ["LoadCapacityKg"] = "100", ["LifecycleStatus"] = "Active",
            }),
            Sheet("Locations", new()
            {
                ["LocationCode"] = "L1", ["RackCode"] = "R1",
                ["ColumnNo"] = "1", ["LevelNo"] = "1", ["DepthNo"] = "1",
                ["LifecycleStatus"] = "Active", ["LocationType"] = "Storage",
            }),
            Sheet("Bindings", new()
            {
                ["WmsWarehouseCode"] = "W1", ["ExternalLocationId"] = "E1",
                ["LocationCode"] = "L1", ["BindingMode"] = "WmsPrimary",
            }),
            Sheet("Attributes", new()
            {
                ["ObjectType"] = "Rack", ["BusinessKey"] = "R1",
                ["Namespace"] = "Owner", ["Key"] = "Name", ["Value"] = "Acme",
            }),
        ]);

    private static SpaceExcelWorkbookSheet Sheet(
        string name,
        Dictionary<string, string?> values) =>
        new(name, [HeaderRow(name), DataRow(name, 2, values)]);

    private static SpaceExcelWorkbookRow HeaderRow(string sheet)
    {
        var cells = SpaceExcelTargetCatalog.ForSheet(sheet)
            .Select((field, index) => new SpaceExcelWorkbookCell(
                index + 1,
                ColumnName(index + 1),
                field.Field,
                false))
            .ToDictionary(cell => cell.ColumnIndex);
        return new SpaceExcelWorkbookRow(1, cells);
    }

    private static SpaceExcelWorkbookRow DataRow(
        string sheet,
        int row,
        Dictionary<string, string?> values)
    {
        var cells = SpaceExcelTargetCatalog.ForSheet(sheet)
            .Select((field, index) => new SpaceExcelWorkbookCell(
                index + 1,
                ColumnName(index + 1),
                values.GetValueOrDefault(field.Field),
                false))
            .ToDictionary(cell => cell.ColumnIndex);
        return new SpaceExcelWorkbookRow(row, cells);
    }

    private static Dictionary<string, string?> ValuesFromRow(
        string sheet,
        SpaceExcelWorkbookRow row) =>
        SpaceExcelTargetCatalog.ForSheet(sheet)
            .Select((field, index) => new
            {
                field.Field,
                Value = row.Cells.GetValueOrDefault(index + 1)?.Value,
            })
            .ToDictionary(item => item.Field, item => item.Value);

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
}

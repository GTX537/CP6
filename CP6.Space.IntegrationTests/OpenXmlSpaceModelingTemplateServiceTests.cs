using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using CP6.Space.Application;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.Infrastructure.Tests;

public sealed class OpenXmlSpaceModelingTemplateServiceTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedHeaders =
        new Dictionary<string, string[]>
        {
            ["Racks"] =
            [
                "FloorCode", "ZoneCode", "RackCode", "XMm", "YMm", "ZMm",
                "WidthMm", "DepthMm", "HeightMm", "RotationZDeg",
                "RackTemplateCode", "LifecycleStatus",
            ],
            ["RackLevels"] =
            [
                "RackCode", "LevelNo", "BottomZMm", "ClearHeightMm",
                "BinCount", "DepthCount", "LoadCapacityKg", "LifecycleStatus",
            ],
            ["Locations"] =
            [
                "LocationCode", "RackCode", "ColumnNo", "LevelNo", "DepthNo",
                "LifecycleStatus", "LocationType",
            ],
            ["Bindings"] =
            [
                "WmsWarehouseCode", "ExternalLocationId", "LocationCode",
                "BindingMode",
            ],
            ["Attributes"] =
            [
                "ObjectType", "BusinessKey", "Namespace", "Key", "Value", "Unit",
            ],
        };

    [Fact]
    public void Infrastructure_registration_exposes_the_standard_template_service()
    {
        var services = new ServiceCollection();
        services.AddSpaceDesignV1Persistence(
            "Server=(localdb)\\mssqllocaldb;Database=cp6-space-template-test;");
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OpenXmlSpaceModelingTemplateService>(
            provider.GetRequiredService<ISpaceModelingTemplateService>());
    }

    [Fact]
    public void Standard_template_is_a_valid_versioned_xlsx_contract()
    {
        var result = new OpenXmlSpaceModelingTemplateService()
            .CreateStandardExcelTemplate();

        Assert.Equal(OpenXmlSpaceModelingTemplateService.FileName, result.FileName);
        Assert.Equal(OpenXmlSpaceModelingTemplateService.ContentType, result.ContentType);
        Assert.Equal(OpenXmlSpaceModelingTemplateService.SchemaVersion, result.SchemaVersion);
        Assert.True(result.Content.Length > 8_000);
        Assert.Equal((byte)'P', result.Content[0]);
        Assert.Equal((byte)'K', result.Content[1]);

        using var stream = new MemoryStream(result.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator()
            .Validate(document)
            .Select(error =>
                $"{error.Path?.XPath}: {error.Description}")
            .ToArray();

        Assert.True(
            errors.Length == 0,
            "Open XML validation failed:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Standard_template_freezes_the_canonical_sheet_and_header_contract()
    {
        var result = new OpenXmlSpaceModelingTemplateService()
            .CreateStandardExcelTemplate();
        using var stream = new MemoryStream(result.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheets = workbookPart.Workbook.Sheets!.Elements<Sheet>().ToArray();

        Assert.Equal(
            [
                "Instructions", "Racks", "RackLevels", "Locations", "Bindings",
                "Attributes", "_Lists",
            ],
            sheets.Select(sheet => sheet.Name!.Value));
        Assert.Equal(SheetStateValues.VeryHidden, sheets[^1].State!.Value);

        foreach (var expected in ExpectedHeaders)
        {
            var worksheet = GetWorksheet(workbookPart, sheets, expected.Key);
            var firstRow = worksheet.GetFirstChild<SheetData>()!
                .Elements<Row>()
                .Single(row => row.RowIndex!.Value == 1U);
            Assert.Equal(expected.Value, firstRow.Elements<Cell>().Select(ReadText));

            var pane = worksheet.Descendants<Pane>().Single();
            Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
            Assert.Equal(1D, pane.VerticalSplit!.Value);
            Assert.NotEmpty(worksheet.Descendants<AutoFilter>());
            Assert.NotEmpty(worksheet.Descendants<DataValidations>());
        }

        var names = workbookPart.Workbook.DefinedNames!
            .Elements<DefinedName>()
            .ToDictionary(
                name => name.Name!.Value!,
                name => name.Text ?? string.Empty);
        Assert.Equal("Instructions!$B$3", names["TemplateSchemaVersion"]);
        Assert.Equal("_Lists!$C$2:$C$6", names["AttributeNamespaces"]);
    }

    [Fact]
    public void Instructions_explain_mapping_without_importing_runtime_inventory()
    {
        var result = new OpenXmlSpaceModelingTemplateService()
            .CreateStandardExcelTemplate();
        using var stream = new MemoryStream(result.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheets = workbookPart.Workbook.Sheets!.Elements<Sheet>().ToArray();
        var instructions = GetWorksheet(workbookPart, sheets, "Instructions");
        var instructionText = string.Join(
            " ",
            instructions.Descendants<Cell>().Select(ReadText));

        Assert.Contains("Owner", instructionText);
        Assert.Contains("Batch", instructionText);
        Assert.Contains("Container", instructionText);
        Assert.Contains("Manufacturing", instructionText);
        Assert.Contains("Draft", instructionText);
        Assert.Contains("运行时库存", instructionText);

        var forbiddenRuntimeHeaders = new HashSet<string>(
            [
                "Quantity", "MaterialNumber", "LotNumber", "ContainerNumber",
                "BatchQuantity", "TaskId",
            ],
            StringComparer.OrdinalIgnoreCase);
        var businessHeaders = ExpectedHeaders.Values.SelectMany(value => value);
        Assert.DoesNotContain(businessHeaders, forbiddenRuntimeHeaders.Contains);
    }

    private static Worksheet GetWorksheet(
        WorkbookPart workbookPart,
        IReadOnlyCollection<Sheet> sheets,
        string name)
    {
        var sheet = sheets.Single(candidate => candidate.Name!.Value == name);
        return ((WorksheetPart)workbookPart.GetPartById(sheet.Id!)).Worksheet;
    }

    private static string ReadText(Cell cell) =>
        cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;
}

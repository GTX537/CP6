using CP6.Space.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CP6.Space.IntegrationTests;

public sealed class OpenXmlSpaceExcelWorkbookReaderTests
{
    [Fact]
    public async Task Standard_template_is_read_as_headers_without_materializing_empty_rows()
    {
        var template = new OpenXmlSpaceModelingTemplateService()
            .CreateStandardExcelTemplate();
        await using var stream = new MemoryStream(template.Content);

        var workbook = await new OpenXmlSpaceExcelWorkbookReader()
            .ReadAsync(stream);

        Assert.Equal(7, workbook.Sheets.Count);
        var racks = Assert.Single(
            workbook.Sheets,
            sheet => sheet.Name == "Racks");
        Assert.Equal(2, racks.Rows.Count);
        var header = racks.Rows[0];
        Assert.Equal(1, header.RowNumber);
        Assert.Equal("FloorCode", header.Cells[1].Value);
        Assert.Equal("LifecycleStatus", header.Cells[12].Value);
    }

    [Fact]
    public async Task Formula_is_exposed_as_metadata_and_is_never_evaluated()
    {
        await using var stream = WorkbookWithFormula();

        var workbook = await new OpenXmlSpaceExcelWorkbookReader()
            .ReadAsync(stream);

        var row = workbook.Sheets[0].Rows.Single(item => item.RowNumber == 2);
        Assert.True(row.Cells[1].HasFormula);
        Assert.Equal("2", row.Cells[1].Value);
    }

    private static MemoryStream WorkbookWithFormula()
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   autoSave: true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(
                new SheetData(
                    new Row(
                        new Cell
                        {
                            CellReference = "A1",
                            DataType = CellValues.InlineString,
                            InlineString = new InlineString(new Text("Value")),
                        })
                    { RowIndex = 1 },
                    new Row(
                        new Cell
                        {
                            CellReference = "A2",
                            CellFormula = new CellFormula("1+1"),
                            CellValue = new CellValue("2"),
                        })
                    { RowIndex = 2 }));
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Data",
            });
            workbookPart.Workbook.Save();
        }
        stream.Position = 0;
        return stream;
    }
}

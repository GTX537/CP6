using CP6.Space.Application;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CP6.Space.Infrastructure;

public sealed class OpenXmlSpaceExcelWorkbookReader
    : ISpaceExcelWorkbookReader
{
    public const int MaximumSheets = 20;
    public const int MaximumRowsPerSheet = 50_000;
    public const int MaximumCells = 1_000_000;
    public const int MaximumColumns = 16_384;
    public const int MaximumSharedStrings = 200_000;
    public const int MaximumSharedStringCharacters = 10_000_000;
    public const int MaximumCellCharacters = 32_767;

    public Task<SpaceExcelWorkbookData> ReadAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || !content.CanSeek)
        {
            throw Invalid(
                "The Excel workbook stream must be readable and seekable.");
        }

        try
        {
            content.Position = 0;
            using var document = SpreadsheetDocument.Open(
                content,
                false,
                new OpenSettings
                {
                    AutoSave = false,
                    MaxCharactersInPart = 20_000_000,
                });
            if (document.DocumentType != SpreadsheetDocumentType.Workbook)
            {
                throw Invalid(
                    "Only a non-macro .xlsx workbook is supported.");
            }

            var workbookPart = document.WorkbookPart ??
                throw Invalid("The Excel workbook part is missing.");
            if (workbookPart.ExternalRelationships.Any())
            {
                throw Invalid(
                    "External workbook relationships are not supported.");
            }

            var sharedStrings = ReadSharedStrings(workbookPart);
            var sheetDefinitions = workbookPart.Workbook.Sheets?
                .Elements<Sheet>()
                .ToArray() ?? [];
            if (sheetDefinitions.Length is < 1 or > MaximumSheets)
            {
                throw Invalid(
                    $"A workbook must contain 1 to {MaximumSheets} sheets.");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sheets = new List<SpaceExcelWorkbookSheet>(
                sheetDefinitions.Length);
            var totalCells = 0;
            foreach (var sheet in sheetDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = sheet.Name?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Length > 100 ||
                    !names.Add(name))
                {
                    throw Invalid(
                        "Workbook sheet names must be unique and valid.");
                }
                var relationshipId = sheet.Id?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId) ||
                    workbookPart.GetPartById(relationshipId) is not WorksheetPart part)
                {
                    throw Invalid(
                        $"Worksheet '{name}' is unavailable.");
                }

                var rows = ReadRows(
                    part,
                    sharedStrings,
                    ref totalCells,
                    cancellationToken);
                sheets.Add(new SpaceExcelWorkbookSheet(name, rows));
            }
            return Task.FromResult(new SpaceExcelWorkbookData(sheets));
        }
        catch (SpaceExcelWorkbookException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is OpenXmlPackageException or
                InvalidDataException or
                FormatException or
                OverflowException or
                KeyNotFoundException)
        {
            throw Invalid("The Excel workbook package is invalid.");
        }
    }

    private static string[] ReadSharedStrings(WorkbookPart workbookPart)
    {
        var table = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (table is null)
            return [];
        var values = new List<string>();
        var characters = 0;
        foreach (var item in table.Elements<SharedStringItem>())
        {
            if (values.Count >= MaximumSharedStrings)
                throw Invalid("The shared-string table is too large.");
            var value = item.InnerText ?? string.Empty;
            characters = checked(characters + value.Length);
            if (characters > MaximumSharedStringCharacters)
                throw Invalid("The shared-string content is too large.");
            values.Add(value);
        }
        return values.ToArray();
    }

    private static IReadOnlyList<SpaceExcelWorkbookRow> ReadRows(
        WorksheetPart worksheetPart,
        IReadOnlyList<string> sharedStrings,
        ref int totalCells,
        CancellationToken cancellationToken)
    {
        var rows = new List<SpaceExcelWorkbookRow>();
        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
                continue;
            if (rows.Count >= MaximumRowsPerSheet + 1000)
            {
                throw Invalid(
                    $"A worksheet cannot exceed {MaximumRowsPerSheet} data rows.");
            }

            var row = reader.LoadCurrentElement() as Row ??
                throw Invalid("A worksheet row is invalid.");
            var rowNumber = checked((int)(row.RowIndex?.Value ?? 0));
            if (rowNumber is < 1 or > 1_048_576)
                throw Invalid("A worksheet contains an invalid row index.");
            var cells = new Dictionary<int, SpaceExcelWorkbookCell>();
            foreach (var cell in row.Elements<Cell>())
            {
                totalCells = checked(totalCells + 1);
                if (totalCells > MaximumCells)
                    throw Invalid("The workbook contains too many populated cells.");
                var reference = cell.CellReference?.Value;
                var columnIndex = ReadColumnIndex(reference);
                if (columnIndex > MaximumColumns)
                {
                    throw Invalid(
                        $"Mapped workbook columns cannot extend beyond column {ColumnName(MaximumColumns)}.");
                }
                var value = ReadCellValue(cell, sharedStrings);
                if (value?.Length > MaximumCellCharacters)
                    throw Invalid("A workbook cell exceeds the Excel text limit.");
                if (!cells.TryAdd(
                        columnIndex,
                        new SpaceExcelWorkbookCell(
                            columnIndex,
                            ColumnName(columnIndex),
                            value,
                            cell.CellFormula is not null)))
                {
                    throw Invalid("A worksheet row contains duplicate cell references.");
                }
            }
            rows.Add(new SpaceExcelWorkbookRow(rowNumber, cells));
        }
        return rows;
    }

    private static string? ReadCellValue(
        Cell cell,
        IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText;
        var raw = cell.CellValue?.Text;
        if (cell.DataType?.Value != CellValues.SharedString)
            return raw;
        if (!int.TryParse(raw, out var index) ||
            index < 0 ||
            index >= sharedStrings.Count)
        {
            throw Invalid("A shared-string cell index is invalid.");
        }
        return sharedStrings[index];
    }

    private static int ReadColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw Invalid("A workbook cell reference is missing.");
        var index = 0;
        var letters = 0;
        foreach (var character in reference)
        {
            if (character is >= 'A' and <= 'Z')
            {
                index = checked(index * 26 + character - 'A' + 1);
                letters++;
                continue;
            }
            if (character is >= 'a' and <= 'z')
            {
                index = checked(index * 26 + character - 'a' + 1);
                letters++;
                continue;
            }
            break;
        }
        if (letters == 0 || index is < 1 or > 16_384)
            throw Invalid("A workbook cell reference is invalid.");
        return index;
    }

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

    private static SpaceExcelWorkbookException Invalid(string message) =>
        new("SPACE_EXCEL_WORKBOOK_INVALID", message);
}

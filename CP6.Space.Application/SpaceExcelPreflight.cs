using System.Globalization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceExcelWorkbookCell(
    int ColumnIndex,
    string ColumnName,
    string? Value,
    bool HasFormula);

public sealed record SpaceExcelWorkbookRow(
    int RowNumber,
    IReadOnlyDictionary<int, SpaceExcelWorkbookCell> Cells);

public sealed record SpaceExcelWorkbookSheet(
    string Name,
    IReadOnlyList<SpaceExcelWorkbookRow> Rows);

public sealed record SpaceExcelWorkbookData(
    IReadOnlyList<SpaceExcelWorkbookSheet> Sheets);

public interface ISpaceExcelWorkbookReader
{
    Task<SpaceExcelWorkbookData> ReadAsync(
        Stream content,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceExcelWorkbookException : InvalidOperationException
{
    public SpaceExcelWorkbookException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record SpaceExcelPreflightFinding(
    SpaceIssueSeverity Severity,
    string Code,
    string Sheet,
    int? Row,
    string? Column,
    string? TargetField,
    string SuggestedActionCode);

public sealed record SpaceExcelPreflightValidationResult(
    int SheetCount,
    int DataRowCount,
    int ValidRowCount,
    IReadOnlyList<SpaceExcelPreflightFinding> Findings);

public sealed record SpaceExcelPreflightJobPayload(
    int SchemaVersion,
    Guid ModelVersionId,
    Guid SourceId,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionHash);

public sealed record SpaceExcelPreflightReport(
    Stream Content,
    string ContentType,
    string FileName);

public interface ISpaceExcelPreflightService
{
    Task<UploadSpaceExcelSourceResponse> UploadAsync(
        Guid versionId,
        string originalName,
        string? declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<StartSpaceExcelPreflightResponse> StartAsync(
        Guid versionId,
        Guid sourceId,
        StartSpaceExcelPreflightRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceExcelPreflightDto> GetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        int issueLimit,
        CancellationToken cancellationToken = default);

    Task<SpaceExcelPreflightReport> OpenErrorReportAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceExcelPreflightValidator
{
    public const int MaximumFindings = 10_000;
    public const int MaximumDataRowsPerSheet = 50_000;

    private static readonly IReadOnlyDictionary<string, NumericRule> NumericRules =
        new Dictionary<string, NumericRule>(StringComparer.Ordinal)
        {
            [Key("Racks", "XMm")] = new(-1_000_000_000m, 1_000_000_000m),
            [Key("Racks", "YMm")] = new(-1_000_000_000m, 1_000_000_000m),
            [Key("Racks", "ZMm")] = new(-1_000_000_000m, 1_000_000_000m),
            [Key("Racks", "WidthMm")] = new(0m, null, MinimumExclusive: true),
            [Key("Racks", "DepthMm")] = new(0m, null, MinimumExclusive: true),
            [Key("Racks", "HeightMm")] = new(0m, null, MinimumExclusive: true),
            [Key("Racks", "RotationZDeg")] = new(-360m, 360m),
            [Key("RackLevels", "LevelNo")] = new(1m, int.MaxValue),
            [Key("RackLevels", "BottomZMm")] = new(0m, null),
            [Key("RackLevels", "ClearHeightMm")] = new(0m, null, MinimumExclusive: true),
            [Key("RackLevels", "BinCount")] = new(1m, int.MaxValue),
            [Key("RackLevels", "DepthCount")] = new(1m, int.MaxValue),
            [Key("RackLevels", "LoadCapacityKg")] = new(0m, null),
            [Key("Locations", "ColumnNo")] = new(1m, int.MaxValue),
            [Key("Locations", "LevelNo")] = new(1m, int.MaxValue),
            [Key("Locations", "DepthNo")] = new(1m, int.MaxValue),
        };

    private static readonly IReadOnlyDictionary<string, string[]> EnumRules =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Key("Racks", "LifecycleStatus")] = ["Active", "Disabled"],
            [Key("RackLevels", "LifecycleStatus")] = ["Active", "Disabled"],
            [Key("Locations", "LifecycleStatus")] = ["Active", "Disabled"],
            [Key("Locations", "LocationType")] = ["Storage", "Staging", "Picking", "Buffer"],
            [Key("Bindings", "BindingMode")] = ["WmsPrimary", "WmsAlias"],
            [Key("Attributes", "ObjectType")] = ["Rack", "RackLevel", "Location"],
            [Key("Attributes", "Namespace")] = ["Owner", "Batch", "Container", "Manufacturing", "Custom"],
        };

    public SpaceExcelPreflightValidationResult Validate(
        SpaceExcelMappingDefinitionDto definition,
        SpaceExcelWorkbookData workbook)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(workbook);
        var findings = new FindingCollector(MaximumFindings);
        var rows = new List<CanonicalRow>();
        var matchedSheetCount = 0;
        var dataRowCount = 0;

        foreach (var mapping in definition.Sheets)
        {
            var matches = workbook.Sheets
                .Where(sheet => SheetMatches(
                    sheet.Name,
                    mapping.SourceSheet,
                    mapping.SheetMatchMode))
                .ToArray();
            if (matches.Length != 1)
            {
                findings.Add(new SpaceExcelPreflightFinding(
                    SpaceIssueSeverity.Blocking,
                    matches.Length == 0
                        ? "SPACE_EXCEL_SOURCE_SHEET_MISSING"
                        : "SPACE_EXCEL_SOURCE_SHEET_AMBIGUOUS",
                    mapping.SourceSheet,
                    null,
                    null,
                    null,
                    matches.Length == 0
                        ? "provide-mapped-sheet"
                        : "narrow-sheet-pattern"));
                continue;
            }

            matchedSheetCount++;
            var sheet = matches[0];
            var headerRow = sheet.Rows.SingleOrDefault(row =>
                row.RowNumber == mapping.HeaderRow);
            if (headerRow is null)
            {
                findings.Add(new SpaceExcelPreflightFinding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_HEADER_ROW_MISSING",
                    sheet.Name,
                    mapping.HeaderRow,
                    null,
                    null,
                    "set-correct-header-row"));
                continue;
            }

            var headers = headerRow.Cells.Values
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                .GroupBy(cell => cell.Value!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var resolved = ResolveColumns(mapping, sheet.Name, headers, findings);
            AddUnknownColumnFindings(
                definition,
                mapping,
                sheet.Name,
                headerRow,
                resolved,
                findings);

            var dataRows = sheet.Rows.Where(row =>
                    row.RowNumber >= mapping.DataStartRow &&
                    HasMappedInput(row, resolved))
                .ToArray();
            dataRowCount = checked(dataRowCount + dataRows.Length);
            if (dataRows.Length > MaximumDataRowsPerSheet)
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_ROW_LIMIT_EXCEEDED",
                    sheet.Name,
                    dataRows[MaximumDataRowsPerSheet].RowNumber,
                    null,
                    null,
                    "split-workbook-below-row-limit"));
            }
            foreach (var sourceRow in dataRows.Take(MaximumDataRowsPerSheet))
            {
                rows.Add(ValidateRow(
                    definition,
                    mapping,
                    sheet.Name,
                    sourceRow,
                    resolved,
                    findings));
            }
        }

        ValidateDuplicates(definition, rows, findings);
        ValidateReferences(rows, findings);
        if (dataRowCount == 0)
        {
            findings.Add(Finding(
                SpaceIssueSeverity.Blocking,
                "SPACE_EXCEL_NO_DATA_ROWS",
                "Workbook",
                null,
                null,
                null,
                "add-at-least-one-data-row"));
        }
        var blockingRows = findings.Items
            .Where(item => item.Severity == SpaceIssueSeverity.Blocking &&
                           item.Row.HasValue)
            .Select(item => (item.Sheet, item.Row!.Value))
            .ToHashSet();
        var validRows = rows.Count(row =>
            !blockingRows.Contains((row.SourceSheet, row.RowNumber)));
        return new SpaceExcelPreflightValidationResult(
            matchedSheetCount,
            dataRowCount,
            validRows,
            findings.Complete());
    }

    private static IReadOnlyDictionary<string, ResolvedColumn> ResolveColumns(
        SpaceExcelSheetMappingDto mapping,
        string sheetName,
        IReadOnlyDictionary<string, SpaceExcelWorkbookCell[]> headers,
        FindingCollector findings)
    {
        var resolved = new Dictionary<string, ResolvedColumn>(StringComparer.Ordinal);
        foreach (var target in SpaceExcelTargetCatalog.ForSheet(mapping.TargetSheet))
        {
            var column = mapping.Columns.SingleOrDefault(item =>
                string.Equals(item.TargetField, target.Field, StringComparison.Ordinal));
            if (column is null)
                continue;

            int? byHeader = null;
            if (column.SourceHeader is not null)
            {
                if (!headers.TryGetValue(column.SourceHeader, out var matches))
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_SOURCE_HEADER_MISSING",
                        sheetName,
                        mapping.HeaderRow,
                        null,
                        target.Field,
                        "correct-source-header"));
                    continue;
                }
                if (matches.Length != 1)
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_SOURCE_HEADER_DUPLICATE",
                        sheetName,
                        mapping.HeaderRow,
                        matches[0].ColumnName,
                        target.Field,
                        "make-header-unique"));
                    continue;
                }
                byHeader = matches[0].ColumnIndex;
            }

            int? byColumn = null;
            if (column.SourceColumn is not null)
                byColumn = ColumnIndex(column.SourceColumn);
            if (byHeader.HasValue && byColumn.HasValue && byHeader != byColumn)
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_SOURCE_COLUMN_MISMATCH",
                    sheetName,
                    mapping.HeaderRow,
                    column.SourceColumn,
                    target.Field,
                    "align-header-and-column"));
                continue;
            }

            var index = byHeader ?? byColumn;
            resolved[target.Field] = new ResolvedColumn(
                target,
                column,
                index,
                index.HasValue ? ColumnName(index.Value) : null);
        }
        return resolved;
    }

    private static void AddUnknownColumnFindings(
        SpaceExcelMappingDefinitionDto definition,
        SpaceExcelSheetMappingDto mapping,
        string sheetName,
        SpaceExcelWorkbookRow headerRow,
        IReadOnlyDictionary<string, ResolvedColumn> resolved,
        FindingCollector findings)
    {
        if (definition.UnknownColumnPolicy == "Ignore")
            return;
        var consumed = resolved.Values
            .Where(item => item.ColumnIndex.HasValue)
            .Select(item => item.ColumnIndex!.Value)
            .ToHashSet();
        foreach (var header in headerRow.Cells.Values.Where(cell =>
                     !string.IsNullOrWhiteSpace(cell.Value) &&
                     !consumed.Contains(cell.ColumnIndex)))
        {
            findings.Add(Finding(
                definition.UnknownColumnPolicy == "Reject"
                    ? SpaceIssueSeverity.Blocking
                    : SpaceIssueSeverity.Warning,
                "SPACE_EXCEL_UNKNOWN_COLUMN",
                sheetName,
                mapping.HeaderRow,
                header.ColumnName,
                null,
                definition.UnknownColumnPolicy == "Reject"
                    ? "map-or-remove-column"
                    : "review-unmapped-column"));
        }
    }

    private static CanonicalRow ValidateRow(
        SpaceExcelMappingDefinitionDto definition,
        SpaceExcelSheetMappingDto mapping,
        string sheetName,
        SpaceExcelWorkbookRow sourceRow,
        IReadOnlyDictionary<string, ResolvedColumn> resolved,
        FindingCollector findings)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var columns = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var target in SpaceExcelTargetCatalog.ForSheet(mapping.TargetSheet))
        {
            if (!resolved.TryGetValue(target.Field, out var selector))
            {
                if (target.Required)
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_REQUIRED_FIELD_UNMAPPED",
                        sheetName,
                        sourceRow.RowNumber,
                        null,
                        target.Field,
                        "map-required-field"));
                }
                continue;
            }

            columns[target.Field] = selector.ColumnName;
            SpaceExcelWorkbookCell? cell = null;
            if (selector.ColumnIndex.HasValue)
                sourceRow.Cells.TryGetValue(selector.ColumnIndex.Value, out cell);
            if (cell?.HasFormula == true)
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_FORMULA_UNSUPPORTED",
                    sheetName,
                    sourceRow.RowNumber,
                    selector.ColumnName,
                    target.Field,
                    "replace-formula-with-value"));
                continue;
            }

            var raw = string.IsNullOrWhiteSpace(cell?.Value)
                ? selector.Mapping.DefaultValue
                : cell!.Value!.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                values[target.Field] = null;
                if (target.Required)
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_REQUIRED_VALUE_MISSING",
                        sheetName,
                        sourceRow.RowNumber,
                        selector.ColumnName,
                        target.Field,
                        definition.EmptyValuePolicy == "UseDefault"
                            ? "supply-default-or-value"
                            : "fill-required-value"));
                }
                continue;
            }

            raw = ApplyEnumConversion(raw, selector.Mapping.EnumConversions);
            if (!TryCanonicalValue(target.DataType, raw, out var canonical, out var number))
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_TYPE_INVALID",
                    sheetName,
                    sourceRow.RowNumber,
                    selector.ColumnName,
                    target.Field,
                    target.DataType == "Integer"
                        ? "use-whole-number"
                        : "use-number"));
                continue;
            }

            if (number.HasValue && selector.Mapping.UnitConversionMultiplier.HasValue)
            {
                try
                {
                    number = checked(number.Value *
                        selector.Mapping.UnitConversionMultiplier.Value);
                    canonical = number.Value.ToString(
                        "G29",
                        CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_VALUE_OUT_OF_RANGE",
                        sheetName,
                        sourceRow.RowNumber,
                        selector.ColumnName,
                        target.Field,
                        "reduce-value-or-unit-multiplier"));
                    continue;
                }
            }

            if (target.DataType == "Integer" && number.HasValue &&
                (decimal.Truncate(number.Value) != number.Value ||
                 number.Value is < int.MinValue or > int.MaxValue))
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_TYPE_INVALID",
                    sheetName,
                    sourceRow.RowNumber,
                    selector.ColumnName,
                    target.Field,
                    "use-whole-number-unit-conversion"));
                continue;
            }

            if (number.HasValue && NumericRules.TryGetValue(
                    Key(mapping.TargetSheet, target.Field),
                    out var rule) && !rule.Contains(number.Value))
            {
                findings.Add(Finding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_VALUE_OUT_OF_RANGE",
                    sheetName,
                    sourceRow.RowNumber,
                    selector.ColumnName,
                    target.Field,
                    "use-allowed-range"));
                continue;
            }

            if (EnumRules.TryGetValue(
                    Key(mapping.TargetSheet, target.Field),
                    out var allowed))
            {
                var match = allowed.SingleOrDefault(item =>
                    string.Equals(item, canonical, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    findings.Add(Finding(
                        SpaceIssueSeverity.Blocking,
                        "SPACE_EXCEL_ENUM_VALUE_INVALID",
                        sheetName,
                        sourceRow.RowNumber,
                        selector.ColumnName,
                        target.Field,
                        "use-allowed-enum-value"));
                    continue;
                }
                canonical = match;
            }
            values[target.Field] = canonical;
        }

        return new CanonicalRow(
            mapping.TargetSheet,
            sheetName,
            sourceRow.RowNumber,
            values,
            columns);
    }

    private static void ValidateDuplicates(
        SpaceExcelMappingDefinitionDto definition,
        IReadOnlyList<CanonicalRow> rows,
        FindingCollector findings)
    {
        foreach (var sheetRows in rows.GroupBy(row => row.TargetSheet))
        {
            var keys = SpaceExcelTargetCatalog.ForSheet(sheetRows.Key)
                .Where(field => field.IsBusinessKey)
                .Select(field => field.Field)
                .ToArray();
            if (keys.Length == 0)
                continue;

            var groups = sheetRows
                .Select(row => new
                {
                    Row = row,
                    Key = TryBusinessKey(row, keys),
                })
                .Where(item => item.Key is not null)
                .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);
            foreach (var group in groups)
            {
                var ordered = group.OrderBy(item => item.Row.RowNumber).ToArray();
                foreach (var duplicate in ordered.Skip(1))
                {
                    var keyField = keys[0];
                    findings.Add(Finding(
                        definition.DuplicateRowPolicy == "Reject"
                            ? SpaceIssueSeverity.Blocking
                            : SpaceIssueSeverity.Warning,
                        "SPACE_EXCEL_DUPLICATE_KEY",
                        duplicate.Row.SourceSheet,
                        duplicate.Row.RowNumber,
                        duplicate.Row.Columns.GetValueOrDefault(keyField),
                        keyField,
                        definition.DuplicateRowPolicy switch
                        {
                            "KeepFirst" => "remove-duplicate-or-keep-first",
                            "KeepLast" => "remove-duplicate-or-keep-last",
                            _ => "make-business-key-unique",
                        }));
                }
            }
        }
    }

    private static void ValidateReferences(
        IReadOnlyList<CanonicalRow> rows,
        FindingCollector findings)
    {
        var racks = Values(rows, "Racks", "RackCode");
        var locations = Values(rows, "Locations", "LocationCode");
        var levels = rows.Where(row => row.TargetSheet == "RackLevels")
            .Where(row => Value(row, "RackCode") is not null &&
                          Value(row, "LevelNo") is not null)
            .GroupBy(row => Composite(
                    Value(row, "RackCode")!,
                    Value(row, "LevelNo")!),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.TargetSheet == "RackLevels")
            {
                RequireReference(row, "RackCode", racks, findings);
            }
            else if (row.TargetSheet == "Locations")
            {
                RequireReference(row, "RackCode", racks, findings);
                var rack = Value(row, "RackCode");
                var level = Value(row, "LevelNo");
                if (rack is not null && level is not null)
                {
                    if (!levels.TryGetValue(Composite(rack, level), out var levelRow))
                    {
                        AddReferenceFinding(row, "LevelNo", findings);
                    }
                    else
                    {
                        ValidateLocationCapacity(
                            row,
                            "ColumnNo",
                            levelRow,
                            "BinCount",
                            findings);
                        ValidateLocationCapacity(
                            row,
                            "DepthNo",
                            levelRow,
                            "DepthCount",
                            findings);
                    }
                }
            }
            else if (row.TargetSheet == "Bindings")
            {
                RequireReference(row, "LocationCode", locations, findings);
            }
            else if (row.TargetSheet == "Attributes")
            {
                var objectType = Value(row, "ObjectType");
                var businessKey = Value(row, "BusinessKey");
                if (objectType == "Rack" && businessKey is not null &&
                    !racks.Contains(businessKey))
                {
                    AddReferenceFinding(row, "BusinessKey", findings);
                }
                else if (objectType == "Location" && businessKey is not null &&
                         !locations.Contains(businessKey))
                {
                    AddReferenceFinding(row, "BusinessKey", findings);
                }
            }
        }
    }

    private static void ValidateLocationCapacity(
        CanonicalRow location,
        string locationField,
        CanonicalRow level,
        string capacityField,
        FindingCollector findings)
    {
        if (!decimal.TryParse(
                Value(location, locationField),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var index) ||
            !decimal.TryParse(
                Value(level, capacityField),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var capacity) ||
            index <= capacity)
        {
            return;
        }
        findings.Add(Finding(
            SpaceIssueSeverity.Blocking,
            "SPACE_EXCEL_VALUE_OUT_OF_RANGE",
            location.SourceSheet,
            location.RowNumber,
            location.Columns.GetValueOrDefault(locationField),
            locationField,
            "fit-rack-level-capacity"));
    }

    private static void RequireReference(
        CanonicalRow row,
        string field,
        IReadOnlySet<string> targets,
        FindingCollector findings)
    {
        var value = Value(row, field);
        if (value is not null && !targets.Contains(value))
            AddReferenceFinding(row, field, findings);
    }

    private static void AddReferenceFinding(
        CanonicalRow row,
        string field,
        FindingCollector findings) =>
        findings.Add(Finding(
            SpaceIssueSeverity.Blocking,
            "SPACE_EXCEL_REFERENCE_NOT_FOUND",
            row.SourceSheet,
            row.RowNumber,
            row.Columns.GetValueOrDefault(field),
            field,
            "add-or-correct-referenced-row"));

    private static HashSet<string> Values(
        IReadOnlyList<CanonicalRow> rows,
        string sheet,
        string field) =>
        rows.Where(row => row.TargetSheet == sheet)
            .Select(row => Value(row, field))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? TryBusinessKey(
        CanonicalRow row,
        IReadOnlyList<string> fields)
    {
        var values = fields.Select(field => Value(row, field)).ToArray();
        return values.Any(string.IsNullOrEmpty)
            ? null
            : string.Join('\u001f', values);
    }

    private static string? Value(CanonicalRow row, string field) =>
        row.Values.GetValueOrDefault(field);

    private static string Composite(string left, string right) =>
        $"{left}|{right}";

    private static bool HasMappedInput(
        SpaceExcelWorkbookRow row,
        IReadOnlyDictionary<string, ResolvedColumn> resolved) =>
        resolved.Values.Any(selector =>
            selector.ColumnIndex.HasValue &&
            row.Cells.TryGetValue(selector.ColumnIndex.Value, out var cell) &&
            (!string.IsNullOrWhiteSpace(cell.Value) || cell.HasFormula));

    private static string ApplyEnumConversion(
        string value,
        IReadOnlyList<SpaceExcelEnumConversionDto>? conversions)
    {
        var conversion = conversions?.SingleOrDefault(item =>
            string.Equals(item.SourceValue, value, StringComparison.OrdinalIgnoreCase));
        return conversion?.TargetValue ?? value;
    }

    private static bool TryCanonicalValue(
        string dataType,
        string value,
        out string canonical,
        out decimal? number)
    {
        canonical = value.Trim();
        number = null;
        if (dataType == "Text")
            return true;
        if (dataType == "Integer")
        {
            if (!long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer) ||
                integer is < int.MinValue or > int.MaxValue)
            {
                return false;
            }
            number = integer;
            canonical = integer.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        if (dataType == "Decimal" && decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var decimalValue))
        {
            number = decimalValue;
            canonical = decimalValue.ToString("G29", CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }

    private static SpaceExcelPreflightFinding Finding(
        SpaceIssueSeverity severity,
        string code,
        string sheet,
        int? row,
        string? column,
        string? targetField,
        string suggestedActionCode) =>
        new(
            severity,
            code,
            sheet,
            row,
            column,
            targetField,
            suggestedActionCode);

    private static bool SheetMatches(
        string value,
        string pattern,
        string mode) =>
        mode == "Exact"
            ? string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase)
            : WildcardMatches(value, pattern);

    private static bool WildcardMatches(string value, string pattern)
    {
        var text = 0;
        var token = 0;
        var star = -1;
        var retry = -1;
        while (text < value.Length)
        {
            if (token < pattern.Length &&
                (pattern[token] == '?' ||
                 char.ToUpperInvariant(pattern[token]) ==
                 char.ToUpperInvariant(value[text])))
            {
                token++;
                text++;
            }
            else if (token < pattern.Length && pattern[token] == '*')
            {
                star = token++;
                retry = text;
            }
            else if (star >= 0)
            {
                token = star + 1;
                text = ++retry;
            }
            else
            {
                return false;
            }
        }
        while (token < pattern.Length && pattern[token] == '*')
            token++;
        return token == pattern.Length;
    }

    private static int ColumnIndex(string name)
    {
        var index = 0;
        foreach (var character in name)
            index = checked(index * 26 + character - 'A' + 1);
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

    private static string Key(string sheet, string field) =>
        $"{sheet}:{field}";

    private sealed record ResolvedColumn(
        SpaceExcelTargetFieldDefinition Target,
        SpaceExcelColumnMappingDto Mapping,
        int? ColumnIndex,
        string? ColumnName);

    private sealed record CanonicalRow(
        string TargetSheet,
        string SourceSheet,
        int RowNumber,
        IReadOnlyDictionary<string, string?> Values,
        IReadOnlyDictionary<string, string?> Columns);

    private sealed record NumericRule(
        decimal? Minimum,
        decimal? Maximum,
        bool MinimumExclusive = false)
    {
        public bool Contains(decimal value) =>
            (!Minimum.HasValue ||
             (MinimumExclusive ? value > Minimum : value >= Minimum)) &&
            (!Maximum.HasValue || value <= Maximum);
    }

    private sealed class FindingCollector(int maximum)
    {
        private readonly List<SpaceExcelPreflightFinding> _items = [];
        private bool _truncated;

        public IReadOnlyList<SpaceExcelPreflightFinding> Items => _items;

        public void Add(SpaceExcelPreflightFinding finding)
        {
            if (_items.Count < maximum - 1)
                _items.Add(finding);
            else
                _truncated = true;
        }

        public IReadOnlyList<SpaceExcelPreflightFinding> Complete()
        {
            if (_truncated && _items.Count < maximum)
            {
                _items.Add(new SpaceExcelPreflightFinding(
                    SpaceIssueSeverity.Blocking,
                    "SPACE_EXCEL_ISSUE_LIMIT_EXCEEDED",
                    "Workbook",
                    null,
                    null,
                    null,
                    "fix-reported-errors-and-run-again"));
            }
            return _items;
        }
    }
}

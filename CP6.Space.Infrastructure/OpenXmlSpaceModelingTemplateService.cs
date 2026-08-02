using CP6.Space.Application;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CP6.Space.Infrastructure;

public sealed class OpenXmlSpaceModelingTemplateService
    : ISpaceModelingTemplateService
{
    public const string SchemaVersion = "1.0";
    public const string FileName = "cp6-space-standard-model-v1.xlsx";
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint FirstDataRow = 2;
    private const uint LastDataRow = 50_001;

    private static readonly SheetDefinition[] DataSheets =
    [
        new(
            "Racks",
            "货架主数据：一个 RackCode 对应一个可布置的货架。",
            [
                Field.RequiredText("FloorCode", "楼层业务编码"),
                Field.RequiredText("ZoneCode", "库区业务编码"),
                Field.RequiredText("RackCode", "货架唯一业务编码"),
                Field.RequiredDecimal("XMm", "货架原点 X 坐标", "mm", -1_000_000_000, 1_000_000_000),
                Field.RequiredDecimal("YMm", "货架原点 Y 坐标", "mm", -1_000_000_000, 1_000_000_000),
                Field.OptionalDecimal("ZMm", "货架原点 Z 坐标；空值按 0 处理", "mm", -1_000_000_000, 1_000_000_000),
                Field.RequiredPositiveDecimal("WidthMm", "货架宽度", "mm"),
                Field.RequiredPositiveDecimal("DepthMm", "货架深度", "mm"),
                Field.RequiredPositiveDecimal("HeightMm", "货架高度", "mm"),
                Field.OptionalDecimal("RotationZDeg", "绕 Z 轴旋转角；空值按 0 处理", "deg", -360, 360),
                Field.OptionalText("RackTemplateCode", "可选的标准货架模板编码"),
                Field.RequiredList("LifecycleStatus", "生命周期状态", "LifecycleStatuses"),
            ]),
        new(
            "RackLevels",
            "货架层规格：RackCode + LevelNo 在模板内必须唯一。",
            [
                Field.RequiredText("RackCode", "关联 Racks.RackCode"),
                Field.RequiredWhole("LevelNo", "自下而上的层号", 1),
                Field.RequiredDecimal("BottomZMm", "层底相对货架底部高度", "mm", 0),
                Field.RequiredPositiveDecimal("ClearHeightMm", "层净高", "mm"),
                Field.RequiredWhole("BinCount", "横向货位数量", 1),
                Field.RequiredWhole("DepthCount", "纵深货位数量", 1),
                Field.OptionalDecimal("LoadCapacityKg", "该层额定承载；空值表示未提供", "kg", 0),
                Field.RequiredList("LifecycleStatus", "生命周期状态", "LifecycleStatuses"),
            ]),
        new(
            "Locations",
            "货位主数据：位置索引必须落在对应货架层的 BinCount / DepthCount 范围内。",
            [
                Field.RequiredText("LocationCode", "货位唯一业务编码"),
                Field.RequiredText("RackCode", "关联 Racks.RackCode"),
                Field.RequiredWhole("ColumnNo", "横向列号", 1),
                Field.RequiredWhole("LevelNo", "关联 RackLevels.LevelNo", 1),
                Field.RequiredWhole("DepthNo", "纵深序号", 1),
                Field.RequiredList("LifecycleStatus", "生命周期状态", "LifecycleStatuses"),
                Field.OptionalList("LocationType", "货位用途", "LocationTypes"),
            ]),
        new(
            "Bindings",
            "WMS 业务映射：外部仓库与外部货位标识映射到标准 LocationCode。",
            [
                Field.RequiredText("WmsWarehouseCode", "WMS 仓库编码"),
                Field.RequiredText("ExternalLocationId", "WMS 外部货位标识"),
                Field.RequiredText("LocationCode", "关联 Locations.LocationCode"),
                Field.OptionalList("BindingMode", "主映射或别名映射", "BindingModes"),
            ]),
        new(
            "Attributes",
            "可扩展业务属性：对象类型 + 业务键 + 命名空间 + Key 必须唯一。",
            [
                Field.RequiredList("ObjectType", "目标对象类型", "ObjectTypes"),
                Field.RequiredText("BusinessKey", "目标对象业务编码"),
                Field.RequiredList("Namespace", "业务属性命名空间", "AttributeNamespaces"),
                Field.RequiredText("Key", "属性键"),
                Field.RequiredText("Value", "属性值；按字符串导入"),
                Field.OptionalText("Unit", "可选单位"),
            ]),
    ];

    public SpaceModelingTemplateFile CreateStandardExcelTemplate()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   autoSave: true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook(
                new WorkbookProperties { Date1904 = false },
                new BookViews(new WorkbookView()),
                new Sheets());
            AddStyles(workbookPart);

            var sheets = workbookPart.Workbook.GetFirstChild<Sheets>()!;
            uint sheetId = 1;
            AddInstructionsSheet(workbookPart, sheets, sheetId++);
            foreach (var definition in DataSheets)
            {
                AddDataSheet(workbookPart, sheets, sheetId++, definition);
            }

            AddListsSheet(workbookPart, sheets, sheetId);
            AddDefinedNames(workbookPart.Workbook);
            workbookPart.Workbook.CalculationProperties =
                new CalculationProperties
                {
                    CalculationId = 191029U,
                    ForceFullCalculation = false,
                    FullCalculationOnLoad = false,
                };
            workbookPart.Workbook.Save();
        }

        return new SpaceModelingTemplateFile(
            stream.ToArray(),
            FileName,
            ContentType,
            SchemaVersion);
    }

    private static void AddInstructionsSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var worksheet = new Worksheet(
            CreateFrozenView("A7", 6),
            new Columns(
                Column(1, 1, 22),
                Column(2, 2, 26),
                Column(3, 3, 15),
                Column(4, 4, 13),
                Column(5, 5, 14),
                Column(6, 6, 68)),
            sheetData);
        worksheetPart.Worksheet = worksheet;

        AddRow(sheetData, 1, 32,
            Text("A1", "CP6 Space 标准建模 Excel 模板", 1));
        AddRow(sheetData, 2, 22,
            Text("A2", "先阅读说明，再填写六张业务数据表；不要修改字段名。", 2));
        AddRow(sheetData, 3, 20,
            Text("A3", "TemplateSchemaVersion", 3),
            Text("B3", SchemaVersion, 4),
            Text("D3", "默认单位", 3),
            Text("E3", "长度 mm；载荷 kg；角度 deg", 4));
        AddRow(sheetData, 4, 20,
            Text("A4", "最大文件", 3),
            Text("B4", "50 MB", 4),
            Text("D4", "数据起始行", 3),
            Text("E4", "第 2 行", 4));
        AddRow(sheetData, 5, 30,
            Text("A5", "重要边界", 3),
            Text(
                "B5",
                "本模板只描述设计主数据。库存数量、物料、批次数量、容器库存和运行任务不得写入业务数据表。",
                4));

        AddRow(sheetData, 7, 24,
            Text("A7", "推荐流程", 2));
        var workflow = new[]
        {
            "1. 使用标准字段名填写 Racks、RackLevels、Locations、Bindings、Attributes。",
            "2. 如果来源文件使用自定义表头，在平台中建立租户映射档案并先预览。",
            "3. 运行导入预检，处理必填、类型、重复键和引用错误。",
            "4. 确认预检结果后才创建导入任务；导入只能写入 Draft 版本。",
            "5. 保留原始文件与预检报告，便于审计、重放和问题定位。",
        };
        for (var index = 0; index < workflow.Length; index++)
        {
            AddRow(sheetData, (uint)(8 + index), 22,
                Text($"A{8 + index}", workflow[index], 4));
        }

        AddRow(sheetData, 14, 24,
            Text("A14", "业务映射规则", 2));
        AddRow(sheetData, 15, 42,
            Text(
                "A15",
                "Owner、Batch、Container、Manufacturing 等扩展字段统一写入 Attributes：ObjectType 指向 Rack / RackLevel / Location，BusinessKey 填目标业务编码，Namespace 选择业务域，Key/Value 保存属性。运行时库存仍由 WMS 读取。",
                4));

        AddRow(sheetData, 17, 24,
            Text("A17", "数据表", 2));
        AddRow(sheetData, 18, 22,
            Text("A18", "Sheet", 5),
            Text("B18", "用途", 5));
        var catalogRow = 19U;
        foreach (var definition in DataSheets)
        {
            AddRow(sheetData, catalogRow, 28,
                Text($"A{catalogRow}", definition.Name, 3),
                Text($"B{catalogRow}", definition.Description, 4));
            catalogRow++;
        }

        var dictionaryHeaderRow = catalogRow + 1;
        AddRow(sheetData, dictionaryHeaderRow, 24,
            Text($"A{dictionaryHeaderRow}", "标准字段字典", 2));
        dictionaryHeaderRow++;
        AddRow(sheetData, dictionaryHeaderRow, 22,
            Text($"A{dictionaryHeaderRow}", "Sheet", 5),
            Text($"B{dictionaryHeaderRow}", "Field", 5),
            Text($"C{dictionaryHeaderRow}", "必填", 5),
            Text($"D{dictionaryHeaderRow}", "类型", 5),
            Text($"E{dictionaryHeaderRow}", "单位", 5),
            Text($"F{dictionaryHeaderRow}", "说明", 5));
        var fieldRow = dictionaryHeaderRow + 1;
        foreach (var definition in DataSheets)
        {
            foreach (var field in definition.Fields)
            {
                AddRow(sheetData, fieldRow, 28,
                    Text($"A{fieldRow}", definition.Name, 3),
                    Text($"B{fieldRow}", field.Name, 3),
                    Text($"C{fieldRow}", field.Required ? "是" : "否", 4),
                    Text($"D{fieldRow}", field.TypeLabel, 4),
                    Text($"E{fieldRow}", field.Unit ?? string.Empty, 4),
                    Text($"F{fieldRow}", field.Description, 4));
                fieldRow++;
            }
        }

        worksheet.Append(new AutoFilter
        {
            Reference = $"A{dictionaryHeaderRow}:F{fieldRow - 1}",
        });
        worksheet.Append(new MergeCells(
            new MergeCell { Reference = "A1:F1" },
            new MergeCell { Reference = "A2:F2" },
            new MergeCell { Reference = "B5:F5" },
            new MergeCell { Reference = "A7:F7" },
            new MergeCell { Reference = "A8:F8" },
            new MergeCell { Reference = "A9:F9" },
            new MergeCell { Reference = "A10:F10" },
            new MergeCell { Reference = "A11:F11" },
            new MergeCell { Reference = "A12:F12" },
            new MergeCell { Reference = "A14:F14" },
            new MergeCell { Reference = "A15:F15" },
            new MergeCell { Reference = "A17:F17" },
            new MergeCell
            {
                Reference = $"A{dictionaryHeaderRow - 1}:F{dictionaryHeaderRow - 1}",
            }));
        worksheet.Append(StandardPageMargins());
        worksheet.Save();

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = "Instructions",
        });
    }

    private static void AddDataSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId,
        SheetDefinition definition)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var columns = new Columns();
        for (var index = 0; index < definition.Fields.Length; index++)
        {
            var width = Math.Clamp(
                Math.Max(definition.Fields[index].Name.Length + 4, 14),
                14,
                24);
            columns.Append(Column(index + 1, index + 1, width));
        }

        var worksheet = new Worksheet(
            CreateFrozenView("A2", 1),
            columns,
            sheetData);
        worksheetPart.Worksheet = worksheet;

        var headerCells = definition.Fields
            .Select((field, index) => Text(
                $"{ColumnName(index + 1)}1",
                field.Name,
                field.Required ? 5U : 6U))
            .ToArray();
        AddRow(sheetData, 1, 30, headerCells);
        sheetData.Append(new Row { RowIndex = 2U, Height = 20D });

        var finalColumn = ColumnName(definition.Fields.Length);
        var tableReference = $"A1:{finalColumn}2";
        worksheet.Append(new AutoFilter { Reference = tableReference });

        var validations = new DataValidations();
        for (var index = 0; index < definition.Fields.Length; index++)
        {
            var validation = CreateValidation(
                definition.Fields[index],
                ColumnName(index + 1));
            if (validation is not null)
            {
                validations.Append(validation);
            }
        }
        if (validations.ChildElements.Count > 0)
        {
            validations.Count = (uint)validations.ChildElements.Count;
            worksheet.Append(validations);
        }

        worksheet.Append(StandardPageMargins());
        worksheet.Save();

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = definition.Name,
        });
    }

    private static void AddListsSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var worksheet = new Worksheet(
            new Columns(
                Column(1, 5, 24)),
            sheetData);
        worksheetPart.Worksheet = worksheet;

        var lists = new[]
        {
            new[] { "LifecycleStatuses", "Active", "Disabled" },
            new[] { "ObjectTypes", "Rack", "RackLevel", "Location" },
            new[] { "AttributeNamespaces", "Owner", "Batch", "Container", "Manufacturing", "Custom" },
            new[] { "BindingModes", "WmsPrimary", "WmsAlias" },
            new[] { "LocationTypes", "Storage", "Staging", "Picking", "Buffer" },
        };
        var maxRows = lists.Max(list => list.Length);
        for (var row = 0; row < maxRows; row++)
        {
            var cells = new List<Cell>();
            for (var column = 0; column < lists.Length; column++)
            {
                if (row < lists[column].Length)
                {
                    cells.Add(Text(
                        $"{ColumnName(column + 1)}{row + 1}",
                        lists[column][row],
                        row == 0 ? 5U : 4U));
                }
            }
            AddRow(sheetData, (uint)(row + 1), 20, cells.ToArray());
        }
        worksheet.Append(StandardPageMargins());
        worksheet.Save();

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = "_Lists",
            State = SheetStateValues.VeryHidden,
        });
    }

    private static void AddDefinedNames(Workbook workbook)
    {
        workbook.Append(new DefinedNames(
            DefinedName("LifecycleStatuses", "_Lists!$A$2:$A$3"),
            DefinedName("ObjectTypes", "_Lists!$B$2:$B$4"),
            DefinedName("AttributeNamespaces", "_Lists!$C$2:$C$6"),
            DefinedName("BindingModes", "_Lists!$D$2:$D$3"),
            DefinedName("LocationTypes", "_Lists!$E$2:$E$5"),
            DefinedName("TemplateSchemaVersion", "Instructions!$B$3")));
    }

    private static DefinedName DefinedName(string name, string reference) =>
        new(reference) { Name = name };

    private static DataValidation? CreateValidation(
        Field field,
        string column)
    {
        if (field.ValidationKind == ValidationKind.None)
        {
            return null;
        }

        var validation = new DataValidation
        {
            AllowBlank = !field.Required,
            ShowErrorMessage = true,
            ShowInputMessage = true,
            ErrorStyle = DataValidationErrorStyleValues.Stop,
            ErrorTitle = "数据格式不正确",
            Error = field.ValidationKind switch
            {
                ValidationKind.List => "请选择下拉列表中的标准值。",
                ValidationKind.Whole => "请输入允许范围内的整数。",
                ValidationKind.Decimal => "请输入允许范围内的数值。",
                _ => "请输入有效值。",
            },
            PromptTitle = field.Name,
            Prompt = field.Description,
            SequenceOfReferences = new ListValue<StringValue>
            {
                InnerText = $"{column}{FirstDataRow}:{column}{LastDataRow}",
            },
        };

        switch (field.ValidationKind)
        {
            case ValidationKind.List:
                validation.Type = DataValidationValues.List;
                validation.Formula1 = new Formula1(field.ListName!);
                break;
            case ValidationKind.Whole:
                validation.Type = DataValidationValues.Whole;
                AddRangeFormula(validation, field);
                break;
            case ValidationKind.Decimal:
                validation.Type = DataValidationValues.Decimal;
                AddRangeFormula(validation, field);
                break;
        }
        return validation;
    }

    private static void AddRangeFormula(DataValidation validation, Field field)
    {
        if (field.Minimum is not null && field.Maximum is not null)
        {
            validation.Operator = DataValidationOperatorValues.Between;
            validation.Formula1 = new Formula1(field.Minimum.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            validation.Formula2 = new Formula2(field.Maximum.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (field.Minimum is not null)
        {
            validation.Operator = field.MinimumExclusive
                ? DataValidationOperatorValues.GreaterThan
                : DataValidationOperatorValues.GreaterThanOrEqual;
            validation.Formula1 = new Formula1(field.Minimum.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void AddStyles(WorkbookPart workbookPart)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new Fonts(
                new Font(
                    new FontSize { Val = 11D },
                    new FontName { Val = "Aptos" },
                    new FontFamilyNumbering { Val = 2 }),
                new Font(
                    new Bold(),
                    new FontSize { Val = 18D },
                    new Color { Rgb = "FFFFFFFF" },
                    new FontName { Val = "Aptos Display" }),
                new Font(
                    new Bold(),
                    new FontSize { Val = 11D },
                    new Color { Rgb = "FFFFFFFF" },
                    new FontName { Val = "Aptos" }),
                new Font(
                    new Bold(),
                    new FontSize { Val = 11D },
                    new Color { Rgb = "FF17365D" },
                    new FontName { Val = "Aptos" }))
            { Count = 4U },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                Fill("FF17365D"),
                Fill("FFD9EAF7"),
                Fill("FFEAF2F8"),
                Fill("FFFFF2CC"),
                Fill("FFF2F2F2"))
            { Count = 7U },
            new Borders(
                new Border(),
                new Border(
                    new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2F3" } },
                    new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2F3" } },
                    new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2F3" } },
                    new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFD9E2F3" } },
                    new DiagonalBorder()))
            { Count = 2U },
            new CellStyleFormats(new CellFormat()) { Count = 1U },
            new CellFormats(
                new CellFormat(),
                Format(1, 2, 0, horizontal: HorizontalAlignmentValues.Left),
                Format(2, 2, 0, horizontal: HorizontalAlignmentValues.Left),
                Format(3, 3, 1),
                Format(0, 0, 1, wrap: true, vertical: VerticalAlignmentValues.Top),
                Format(2, 2, 1, wrap: true),
                Format(3, 5, 1, wrap: true))
            { Count = 7U },
            new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U },
            new DifferentialFormats { Count = 0U },
            new TableStyles
            {
                Count = 0U,
                DefaultTableStyle = "TableStyleMedium2",
                DefaultPivotStyle = "PivotStyleLight16",
            });
        stylesPart.Stylesheet.Save();
    }

    private static Fill Fill(string rgb) =>
        new(new PatternFill(
            new ForegroundColor { Rgb = rgb },
            new BackgroundColor { Indexed = 64U })
        {
            PatternType = PatternValues.Solid,
        });

    private static CellFormat Format(
        uint fontId,
        uint fillId,
        uint borderId,
        bool wrap = false,
        HorizontalAlignmentValues? horizontal = null,
        VerticalAlignmentValues? vertical = null) =>
        new()
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyFont = true,
            ApplyFill = true,
            ApplyBorder = borderId > 0,
            ApplyAlignment = true,
            Alignment = new Alignment
            {
                Horizontal = horizontal,
                Vertical = vertical ?? VerticalAlignmentValues.Center,
                WrapText = wrap,
            },
        };

    private static SheetViews CreateFrozenView(
        string topLeftCell,
        uint frozenRows) =>
        new(new SheetView(
            new Pane
            {
                VerticalSplit = frozenRows,
                TopLeftCell = topLeftCell,
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            },
            new Selection
            {
                Pane = PaneValues.BottomLeft,
                ActiveCell = topLeftCell,
                SequenceOfReferences = new ListValue<StringValue>
                {
                    InnerText = topLeftCell,
                },
            })
        {
            WorkbookViewId = 0U,
            ShowGridLines = true,
        });

    private static Column Column(int min, int max, double width) =>
        new()
        {
            Min = (uint)min,
            Max = (uint)max,
            Width = width,
            CustomWidth = true,
        };

    private static PageMargins StandardPageMargins() =>
        new()
        {
            Left = 0.35D,
            Right = 0.35D,
            Top = 0.5D,
            Bottom = 0.5D,
            Header = 0.2D,
            Footer = 0.2D,
        };

    private static void AddRow(
        SheetData sheetData,
        uint rowIndex,
        double height,
        params Cell[] cells) =>
        sheetData.Append(new Row(cells)
        {
            RowIndex = rowIndex,
            Height = height,
            CustomHeight = true,
        });

    private static Cell Text(
        string reference,
        string value,
        uint styleIndex) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex,
            InlineString = new InlineString(new Text(value)
            {
                Space = SpaceProcessingModeValues.Preserve,
            }),
        };

    private static string ColumnName(int number)
    {
        var result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private sealed record SheetDefinition(
        string Name,
        string Description,
        Field[] Fields);

    private sealed record Field(
        string Name,
        bool Required,
        string TypeLabel,
        string Description,
        string? Unit,
        ValidationKind ValidationKind,
        decimal? Minimum,
        decimal? Maximum,
        bool MinimumExclusive,
        string? ListName)
    {
        public static Field RequiredText(string name, string description) =>
            Create(name, true, "文本", description);

        public static Field OptionalText(string name, string description) =>
            Create(name, false, "文本", description);

        public static Field RequiredDecimal(
            string name,
            string description,
            string unit,
            decimal? minimum = null,
            decimal? maximum = null) =>
            Create(name, true, "数值", description, unit,
                ValidationKind.Decimal, minimum, maximum);

        public static Field OptionalDecimal(
            string name,
            string description,
            string unit,
            decimal? minimum = null,
            decimal? maximum = null) =>
            Create(name, false, "数值", description, unit,
                ValidationKind.Decimal, minimum, maximum);

        public static Field RequiredPositiveDecimal(
            string name,
            string description,
            string unit) =>
            Create(name, true, "正数", description, unit,
                ValidationKind.Decimal, 0, null, true);

        public static Field RequiredWhole(
            string name,
            string description,
            decimal minimum) =>
            Create(name, true, "整数", description, null,
                ValidationKind.Whole, minimum);

        public static Field RequiredList(
            string name,
            string description,
            string listName) =>
            Create(name, true, "枚举", description, null,
                ValidationKind.List, listName: listName);

        public static Field OptionalList(
            string name,
            string description,
            string listName) =>
            Create(name, false, "枚举", description, null,
                ValidationKind.List, listName: listName);

        private static Field Create(
            string name,
            bool required,
            string typeLabel,
            string description,
            string? unit = null,
            ValidationKind validationKind = ValidationKind.None,
            decimal? minimum = null,
            decimal? maximum = null,
            bool minimumExclusive = false,
            string? listName = null) =>
            new(
                name,
                required,
                typeLabel,
                description,
                unit,
                validationKind,
                minimum,
                maximum,
                minimumExclusive,
                listName);
    }

    private enum ValidationKind
    {
        None,
        List,
        Whole,
        Decimal,
    }
}

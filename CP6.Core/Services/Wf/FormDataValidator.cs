using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Core.Services.Wf;

/// <summary>
/// SFS schema 与数据的服务端可信边界。动态表单前端只负责体验，这里负责发布、草稿形态和正式提交复核。
/// </summary>
internal static class FormDataValidator
{
    internal const int DefaultMaxTableRows = 100;
    internal const int HardMaxTableRows = 200;
    internal const int HardMaxTableColumns = 50;

    private static readonly HashSet<string> TableColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "textarea", "number", "select", "date", "datetime"
    };

    internal static IReadOnlyList<string> ValidateSchema(FormSchema schema)
    {
        var errors = new List<string>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name))
            {
                errors.Add("字段标识为空或重复");
                continue;
            }

            var fieldLabel = LabelOf(field.Label, field.Name);
            if (string.IsNullOrWhiteSpace(field.Type))
            {
                errors.Add($"{fieldLabel} 的字段类型不能为空");
                continue;
            }
            if (field.MaxLength is <= 0 or > 10_000)
                errors.Add($"{fieldLabel} 的最大长度必须为 1-10000");
            if (!IsValidPattern(field.Pattern))
                errors.Add($"{fieldLabel} 的校验正则无效");

            if (!string.Equals(field.Type, "table", StringComparison.OrdinalIgnoreCase)) continue;
            var label = fieldLabel;
            var columns = field.Columns ?? new();
            if (columns.Count is 0 or > HardMaxTableColumns)
                errors.Add($"{label} 的子表列数必须为 1-{HardMaxTableColumns}");

            var columnNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                if (string.IsNullOrWhiteSpace(column.Name) || !columnNames.Add(column.Name))
                    errors.Add($"{label} 存在空或重复的列标识");
                if (!TableColumnTypes.Contains(column.Type))
                    errors.Add($"{label}.{LabelOf(column.Label, column.Name)} 的列类型不受支持");
                if (column.MaxLength is <= 0 or > 10_000)
                    errors.Add($"{label}.{LabelOf(column.Label, column.Name)} 的最大长度必须为 1-10000");
                if (!IsValidPattern(column.Pattern))
                    errors.Add($"{label}.{LabelOf(column.Label, column.Name)} 的校验正则无效");
            }

            var minRows = field.MinRows ?? 0;
            var maxRows = field.MaxRows ?? DefaultMaxTableRows;
            if (minRows < 0 || maxRows < 1 || maxRows > HardMaxTableRows || minRows > maxRows)
                errors.Add($"{label} 的行数范围无效");
        }
        return errors;
    }

    internal static List<string> ValidateFields(
        FormSchema schema,
        JsonElement data,
        IReadOnlyDictionary<string, bool>? requiredOverride,
        IReadOnlySet<string>? hidden)
    {
        var errors = new List<string>();
        foreach (var field in schema.Fields)
        {
            if (hidden is not null && hidden.Contains(field.Name)) continue;

            JsonElement value = default;
            var has = data.ValueKind == JsonValueKind.Object && data.TryGetProperty(field.Name, out value);
            var empty = !has || IsEmpty(value, field.Type);
            var required = requiredOverride is not null &&
                           requiredOverride.TryGetValue(field.Name, out var overridden)
                ? overridden
                : field.Required;
            var label = LabelOf(field.Label, field.Name);

            if (required && empty)
            {
                errors.Add($"{label} 必填");
                continue;
            }
            if (empty) continue;

            ValidateValue(field, value, label, enforceTableRequired: true, errors);
        }
        return errors;
    }

    /// <summary>
    /// 草稿允许缺少必填字段和必填单元格，但不允许未知列、错误类型、超限行数或嵌套对象。
    /// </summary>
    internal static bool IsValidDraftValue(FormField field, JsonElement value)
    {
        if (!string.Equals(field.Type, "table", StringComparison.OrdinalIgnoreCase))
        {
            return field.Type switch
            {
                "number" => value.ValueKind == JsonValueKind.Number,
                "checkbox" => value.ValueKind is JsonValueKind.Array or JsonValueKind.True or JsonValueKind.False,
                _ => value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False
            };
        }

        var errors = new List<string>();
        ValidateTable(field, value, LabelOf(field.Label, field.Name), enforceRequired: false, enforceMinRows: false, errors);
        return errors.Count == 0;
    }

    private static void ValidateValue(
        FormField field,
        JsonElement value,
        string label,
        bool enforceTableRequired,
        List<string> errors)
    {
        switch (field.Type?.ToLowerInvariant())
        {
            case "number":
                if (value.ValueKind != JsonValueKind.Number) errors.Add($"{label} 必须是数字");
                break;
            case "checkbox":
                break; // 兼容现有 bool / 数组数据；选项值合法性后续单独治理。
            case "table":
                ValidateTable(field, value, label, enforceTableRequired, enforceMinRows: true, errors);
                break;
            default:
                ValidateText(value, label, field.MaxLength, field.Pattern, strictString: false, errors);
                break;
        }
    }

    private static void ValidateTable(
        FormField field,
        JsonElement value,
        string label,
        bool enforceRequired,
        bool enforceMinRows,
        List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{label} 必须是子表数组");
            return;
        }

        var rows = value.GetArrayLength();
        var minRows = Math.Max(field.MinRows ?? 0, 0);
        var maxRows = Math.Clamp(field.MaxRows ?? DefaultMaxTableRows, 1, HardMaxTableRows);
        if (enforceMinRows && rows < minRows)
            errors.Add($"{label} 至少需要 {minRows} 行");
        if (rows > maxRows)
        {
            errors.Add($"{label} 最多允许 {maxRows} 行");
            return;
        }

        var definitions = field.Columns ?? new();
        var columns = definitions.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var rowIndex = 0;
        foreach (var row in value.EnumerateArray())
        {
            rowIndex++;
            if (row.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{label} 第 {rowIndex} 行必须是对象");
                continue;
            }

            foreach (var property in row.EnumerateObject())
            {
                if (!columns.Contains(property.Name))
                    errors.Add($"{label} 第 {rowIndex} 行包含未知列 {property.Name}");
            }

            foreach (var column in definitions)
            {
                var cellLabel = $"{label} 第 {rowIndex} 行 {LabelOf(column.Label, column.Name)}";
                var has = row.TryGetProperty(column.Name, out var cell);
                var empty = !has || IsEmpty(cell, column.Type);
                if (enforceRequired && column.Required && empty)
                {
                    errors.Add($"{cellLabel} 必填");
                    continue;
                }
                if (empty) continue;

                switch (column.Type?.ToLowerInvariant())
                {
                    case "number":
                        if (cell.ValueKind != JsonValueKind.Number) errors.Add($"{cellLabel} 必须是数字");
                        break;
                    case "select":
                        if (cell.ValueKind is not JsonValueKind.String and not JsonValueKind.Number)
                            errors.Add($"{cellLabel} 必须是文本或数字");
                        break;
                    default:
                        ValidateText(cell, cellLabel, column.MaxLength, column.Pattern, strictString: true, errors);
                        break;
                }
            }
        }
    }

    private static void ValidateText(
        JsonElement value,
        string label,
        int? maxLength,
        string? pattern,
        bool strictString,
        List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            if (strictString) errors.Add($"{label} 必须是文本");
            return; // 顶层历史字段保持原有兼容行为。
        }

        var text = value.GetString() ?? string.Empty;
        if (maxLength is int max && text.Length > max) errors.Add($"{label} 超出最大长度 {max}");
        if (!string.IsNullOrEmpty(pattern))
        {
            try
            {
                if (!Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                    errors.Add($"{label} 格式不符");
            }
            catch (ArgumentException)
            {
                errors.Add($"{label} 校验规则无效");
            }
            catch (RegexMatchTimeoutException)
            {
                errors.Add($"{label} 校验超时");
            }
        }
    }

    private static bool IsEmpty(JsonElement value, string? type) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
        value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString()) ||
        string.Equals(type, "table", StringComparison.OrdinalIgnoreCase) &&
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;

    private static string LabelOf(string? label, string name) =>
        string.IsNullOrWhiteSpace(label) ? name : label;

    private static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

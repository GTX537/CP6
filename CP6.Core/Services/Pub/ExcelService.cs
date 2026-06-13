using ClosedXML.Excel;

namespace CP6.Core.Services.Pub;

/// <summary>通用 Excel 导入导出实现 —— PUB 章07（ClosedXML，免费 MIT，商用安全）。</summary>
public class ExcelService : IExcelService
{
    private readonly Sys.IDictService _dict;
    public ExcelService(Sys.IDictService dict) => _dict = dict;

    public async Task<byte[]> ExportAsync<T>(IEnumerable<T> data, List<ExcelColumn> cols)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");

        for (int c = 0; c < cols.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cols[c].Title;
            cell.Style.Font.Bold = true;
            ws.Column(c + 1).Width = cols[c].Width;
        }

        int r = 2;
        foreach (var item in data)
        {
            if (item == null) continue;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                var raw = GetValue(item, col.Field);
                string text;
                if (!string.IsNullOrEmpty(col.DictType))
                    text = await _dict.TranslateAsync(col.DictType, raw?.ToString()) ?? "";
                else if (!string.IsNullOrEmpty(col.Format) && raw is IFormattable f)
                    text = f.ToString(col.Format, null);
                else
                    text = raw?.ToString() ?? "";
                ws.Cell(r, c + 1).Value = text;
            }
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] Template(List<ExcelColumn> cols)
    {
        var imp = cols.Where(c => c.Import).ToList();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("导入模板");
        for (int c = 0; c < imp.Count; c++)
        {
            var col = imp[c];
            var cell = ws.Cell(1, c + 1);
            cell.Value = col.Required ? col.Title + " *" : col.Title;
            cell.Style.Font.Bold = true;
            if (col.Required) cell.Style.Font.FontColor = XLColor.Red;
            ws.Column(c + 1).Width = col.Width;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ImportResult<T>> ImportAsync<T>(Stream excel, List<ExcelColumn> cols,
        Func<Dictionary<string, string?>, T> mapper) where T : class
    {
        var imp = cols.Where(c => c.Import).ToList();

        using var buffer = new MemoryStream();
        await excel.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var result = new ImportResult<T>();
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);

        // 表头 标题→列号（去掉必填 * 标记）
        var lastCol = ws.LastColumnUsed()?.LastCellUsed()?.Address.ColumnNumber ?? 0;
        var header = new Dictionary<string, int>();
        for (int c = 1; c <= lastCol; c++)
        {
            var t = ws.Cell(1, c).GetString().Trim().TrimEnd('*').Trim();
            if (!string.IsNullOrEmpty(t) && !header.ContainsKey(t)) header[t] = c;
        }

        var colIdx = new Dictionary<ExcelColumn, int>();
        foreach (var col in imp)
        {
            if (header.TryGetValue(col.Title, out var idx)) colIdx[col] = idx;
            else if (col.Required) throw new InvalidOperationException("E-PUB-071");   // 必填列对不上
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var rowReasons = new Dictionary<int, string>();
        for (int r = 2; r <= lastRow; r++)
        {
            // 整行空 → 跳过（忽略尾部空行）
            if (colIdx.Values.All(ci => string.IsNullOrWhiteSpace(ws.Cell(r, ci).GetString()))) continue;

            var dict = new Dictionary<string, string?>();
            var errs = new List<string>();
            foreach (var col in imp)
            {
                var cell = colIdx.TryGetValue(col, out var ci) ? ws.Cell(r, ci).GetString().Trim() : "";
                if (col.Required && string.IsNullOrEmpty(cell)) { errs.Add($"{col.Title}必填"); continue; }
                if (!string.IsNullOrEmpty(col.DictType) && !string.IsNullOrEmpty(cell))
                {
                    var items = await _dict.GetItemsAsync(col.DictType);
                    var hit = items.FirstOrDefault(i => i.Label == cell || i.Value == cell);
                    if (hit == null) { errs.Add($"{col.Title}字典值无效:{cell}"); continue; }
                    dict[col.Field] = hit.Value;   // 反查为 Value 入库
                }
                else
                {
                    dict[col.Field] = string.IsNullOrEmpty(cell) ? null : cell;
                }
            }

            if (errs.Count == 0)
            {
                try { result.ValidRows.Add(mapper(dict)); }
                catch (Exception ex) { errs.Add(ex.Message); }   // 业务校验错
            }
            if (errs.Count > 0)
            {
                var msg = string.Join("; ", errs);
                result.Errors.Add(new ImportError { Row = r, Message = msg });
                rowReasons[r] = msg;
            }
        }

        if (result.Errors.Count > 0)
            result.ErrorFile = BuildErrorFile(bytes, rowReasons);

        return result;
    }

    /// <summary>原表追加"错误原因"列 + 错误行标红。</summary>
    private static byte[] BuildErrorFile(byte[] original, Dictionary<int, string> rowReasons)
    {
        using var wb = new XLWorkbook(new MemoryStream(original));
        var ws = wb.Worksheet(1);
        var reasonCol = (ws.LastColumnUsed()?.LastCellUsed()?.Address.ColumnNumber ?? 1) + 1;
        ws.Cell(1, reasonCol).Value = "错误原因";
        ws.Cell(1, reasonCol).Style.Font.Bold = true;
        foreach (var kv in rowReasons)
        {
            ws.Cell(kv.Key, reasonCol).Value = kv.Value;
            ws.Row(kv.Key).Style.Fill.BackgroundColor = XLColor.LightPink;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static object? GetValue(object item, string field) =>
        item.GetType().GetProperty(field)?.GetValue(item);
}

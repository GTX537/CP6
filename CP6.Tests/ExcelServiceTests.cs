using ClosedXML.Excel;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Tests;

public class ExcelServiceTests
{
    /// <summary>桩字典：order_status 1→已确认。</summary>
    private sealed class StubDict : IDictService
    {
        public Task<List<Sys_DictData>> GetItemsAsync(string typeCode) => Task.FromResult(new List<Sys_DictData>());
        public Task<string?> TranslateAsync(string typeCode, string? value) =>
            Task.FromResult<string?>(typeCode == "order_status" && value == "1" ? "已确认" : value);
        public void InvalidateType(string typeCode) { }
    }

    [Fact]
    public async Task Export_WritesHeader_AndTranslatesDict()
    {
        var cols = new List<ExcelColumn>
        {
            new() { Field = "Status", Title = "状态", DictType = "order_status" },
            new() { Field = "No", Title = "单号" }
        };
        var bytes = await new ExcelService(new StubDict()).ExportAsync(new[] { new { Status = "1", No = "A001" } }, cols);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        Assert.Equal("状态", ws.Cell(1, 1).GetString());
        Assert.Equal("单号", ws.Cell(1, 2).GetString());
        Assert.Equal("已确认", ws.Cell(2, 1).GetString());   // 字典翻译
        Assert.Equal("A001", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public void Template_OnlyImportCols_MarksRequired()
    {
        var cols = new List<ExcelColumn>
        {
            new() { Field = "A", Title = "甲", Required = true, Import = true },
            new() { Field = "B", Title = "乙", Import = false }
        };
        var bytes = new ExcelService(new StubDict()).Template(cols);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        Assert.Equal("甲 *", ws.Cell(1, 1).GetString());   // 必填标 *
        Assert.True(ws.Cell(1, 2).IsEmpty());               // 非导入列不在模板
    }

    // ───── C-2 导入 ─────

    /// <summary>构造一个 [订单号,状态] 表头 + 若干数据行的 xlsx。</summary>
    private static byte[] BuildExcel(string[] headers, params string[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Import_InvalidRequiredRow_ProducesErrorFile_KeepsValidRows()
    {
        var cols = new List<ExcelColumn>
        {
            new() { Field = "OrderNo", Title = "订单号", Required = true },
            new() { Field = "Memo", Title = "备注" }
        };
        // 行2：订单号空(必填错)但备注有值(非整行空)，行3：有效
        var bytes = BuildExcel(new[] { "订单号", "备注" }, new[] { "", "hello" }, new[] { "A001", "ok" });

        var r = await new ExcelService(new StubDict()).ImportAsync(new MemoryStream(bytes), cols, d => d);

        Assert.Single(r.Errors);
        Assert.Equal(2, r.Errors[0].Row);            // 空必填行
        Assert.Single(r.ValidRows);
        Assert.Equal("A001", r.ValidRows[0]["OrderNo"]);
        Assert.NotNull(r.ErrorFile);                  // 回写错误文件
    }

    [Fact]
    public async Task Import_DictReverseTranslate_LabelToValue()
    {
        var cols = new List<ExcelColumn>
        {
            new() { Field = "OrderNo", Title = "订单号", Required = true },
            new() { Field = "Status", Title = "状态", DictType = "order_status" }
        };
        // 状态填标签"已确认" → 反查为 Value "1"
        var bytes = BuildExcel(new[] { "订单号", "状态" }, new[] { "A001", "已确认" });

        var svc = new ExcelService(new StubDictWithItems());
        var r = await svc.ImportAsync(new MemoryStream(bytes), cols, d => d);

        Assert.Empty(r.Errors);
        Assert.Equal("1", r.ValidRows[0]["Status"]);   // 标签→值
    }

    [Fact]
    public async Task Import_MissingRequiredColumn_Throws_E071()
    {
        var cols = new List<ExcelColumn> { new() { Field = "OrderNo", Title = "订单号", Required = true } };
        var bytes = BuildExcel(new[] { "别的列" }, new[] { "x" });   // 表头无"订单号"

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ExcelService(new StubDict()).ImportAsync(new MemoryStream(bytes), cols, d => d));
        Assert.Equal("E-PUB-071", ex.Message);
    }

    /// <summary>带字典项的桩：order_status 含 1=已确认。</summary>
    private sealed class StubDictWithItems : IDictService
    {
        public Task<List<Sys_DictData>> GetItemsAsync(string typeCode) => Task.FromResult(new List<Sys_DictData>
        {
            new() { TypeCode = "order_status", Value = "1", Label = "已确认", Enable = true }
        });
        public Task<string?> TranslateAsync(string typeCode, string? value) => Task.FromResult<string?>(value);
        public void InvalidateType(string typeCode) { }
    }
}

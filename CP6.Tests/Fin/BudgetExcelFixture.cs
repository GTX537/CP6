using ClosedXML.Excel;

namespace CP6.Tests.Fin;

internal static class BudgetExcelFixture
{
    // Columns: 科目编码 | 成本中心编码 | 成本对象类型 | 成本对象号 | M1..M12 ; row 1 = header
    public static Stream Build(IEnumerable<(string acct, string cc, string coType, decimal[] months)> rows)
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Budget");
        ws.Cell(1, 1).Value = "科目编码"; ws.Cell(1, 2).Value = "成本中心"; ws.Cell(1, 3).Value = "成本对象类型"; ws.Cell(1, 4).Value = "成本对象号";
        for (int m = 0; m < 12; m++) ws.Cell(1, 5 + m).Value = $"M{m + 1}";
        int r = 2;
        foreach (var (acct, cc, coType, months) in rows)
        {
            ws.Cell(r, 1).Value = acct; ws.Cell(r, 2).Value = cc; ws.Cell(r, 3).Value = coType; ws.Cell(r, 4).Value = "";
            for (int m = 0; m < 12; m++) ws.Cell(r, 5 + m).Value = m < months.Length ? months[m] : 0m;
            r++;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    // Build a one-row sheet where month column M1 (col 5) contains text garbage instead of a number.
    public static Stream BuildWithTextMonthCell(string acctCode)
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Budget");
        ws.Cell(1, 1).Value = "科目编码"; ws.Cell(1, 2).Value = "成本中心"; ws.Cell(1, 3).Value = "成本对象类型"; ws.Cell(1, 4).Value = "成本对象号";
        for (int m = 0; m < 12; m++) ws.Cell(1, 5 + m).Value = $"M{m + 1}";
        ws.Cell(2, 1).Value = acctCode; ws.Cell(2, 2).Value = ""; ws.Cell(2, 3).Value = ""; ws.Cell(2, 4).Value = "";
        ws.Cell(2, 5).Value = "abc";   // text in a number column
        for (int m = 1; m < 12; m++) ws.Cell(2, 5 + m).Value = 0m;
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }
}

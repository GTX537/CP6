using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public class BankStatementImporter : IBankStatementImporter
{
    public BankImportParseResult Parse(BankImportProfile profile, Stream file, string fileName)
    {
        return profile.FileFormat == BankFileFormat.Excel
            ? ParseExcel(profile, file)
            : ParseCsv(profile, file);
    }

    private static BankImportParseResult ParseCsv(BankImportProfile p, Stream file)
    {
        var result = new BankImportParseResult();
        var enc = SafeEncoding(p.Encoding);
        using var reader = new StreamReader(file, enc);
        var all = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
        var delim = string.IsNullOrEmpty(p.Delimiter) ? "," : p.Delimiter;
        int lineNo = 0;
        foreach (var raw in all)
        {
            lineNo++;
            if (lineNo <= p.SkipHeaderRows) continue;
            if (string.IsNullOrWhiteSpace(raw)) continue;       // 空行跳过（§3.5）
            var cols = SplitCsv(raw, delim[0]);
            try { result.Rows.Add(MapRow(p, cols, lineNo, raw)); }
            catch (Exception ex)
            {
                result.Errors.Add(new BankImportRowError { SourceLineNo = lineNo, Code = "E-A4-IMPORT-001",
                    RawText = raw, Reason = ex.Message });
            }
        }
        return result;
    }

    private static BankImportParseResult ParseExcel(BankImportProfile p, Stream file)
    {
        var result = new BankImportParseResult();
        using var wb = new ClosedXML.Excel.XLWorkbook(file);
        var ws = wb.Worksheet(1);
        foreach (var row in ws.RowsUsed())
        {
            var lineNo = row.RowNumber();                             // 1-based 物理行号，与 CSV 路径对齐
            if (lineNo <= p.SkipHeaderRows) continue;
            var cols = row.Cells(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1)
                .Select(c => c.GetString()).ToArray();
            if (cols.All(string.IsNullOrWhiteSpace)) continue;       // 空行跳过
            var raw = string.Join("", cols);
            try { result.Rows.Add(MapRow(p, cols, lineNo, raw)); }
            catch (Exception ex)
            { result.Errors.Add(new BankImportRowError { SourceLineNo = lineNo, Code = "E-A4-IMPORT-001", RawText = raw, Reason = ex.Message }); }
        }
        return result;
    }

    /// <summary>按 Profile 映射一行；方向解析显式（§3.6）。失败抛异常由上层收集。</summary>
    private static BankImportRow MapRow(BankImportProfile p, string[] cols, int lineNo, string raw)
    {
        string Col(string? field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            return int.TryParse(field, out var idx) && idx >= 0 && idx < cols.Length ? cols[idx].Trim() : "";
        }

        var dateStr = Col(p.DateField);
        if (!DateTime.TryParseExact(dateStr, p.DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var txnDate))
            throw new FormatException($"日期解析失败：'{dateStr}' 不符 {p.DateFormat}");

        int direction; decimal amount;
        if (p.AmountMode == BankAmountMode.DepositWithdrawalColumns)
        {
            var dep = ParseAmount(Col(p.DepositAmountField), p);
            var wd = ParseAmount(Col(p.WithdrawalAmountField), p);
            if (dep > 0m) { direction = 1; amount = dep; }
            else if (wd > 0m) { direction = 2; amount = wd; }
            else throw new FormatException("入款/出款列均为空或非正数");
        }
        else
        {
            var signed = ParseAmount(Col(p.AmountField), p, allowNegative: true);
            if (signed == 0m) throw new FormatException("金额为 0 或解析失败");
            var positiveIsDeposit = p.SignRule == BankSignRule.PositiveIsDeposit;
            direction = (signed > 0m) == positiveIsDeposit ? 1 : 2;
            amount = Math.Abs(signed);
        }

        var row = new BankImportRow
        {
            SourceLineNo = lineNo, TxnDate = txnDate, Direction = direction, Amount = amount,
            CurrencyCd = null,
            Description = Col(p.DescriptionField), CounterpartyName = Col(p.CounterpartyField),
            RefNo = Col(p.RefNoField),
            BalanceAfter = string.IsNullOrEmpty(Col(p.BalanceField)) ? null : ParseAmount(Col(p.BalanceField), p, true),
            RawRowJson = JsonSerializer.Serialize(cols),
            RawRowHash = Sha256(raw),
        };
        row.Fingerprint = Sha256($"{txnDate:yyyyMMdd}|{direction}|{amount}|{row.RefNo}|{row.CounterpartyName}|{row.Description}|{row.BalanceAfter}");
        return row;
    }

    private static decimal ParseAmount(string s, BankImportProfile p, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var t = s.Replace(p.ThousandsSeparator, "");
        if (p.DecimalSeparator != ".") t = t.Replace(p.DecimalSeparator, ".");
        if (!decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"金额解析失败：'{s}'");
        if (!allowNegative && v < 0m) throw new FormatException($"金额不可为负：'{s}'");
        return v;
    }

    private static string[] SplitCsv(string line, char delim)
    {
        var list = new List<string>(); var sb = new StringBuilder();
        bool inQ = false, pendingQuote = false;
        foreach (var ch in line)
        {
            if (pendingQuote)
            {
                pendingQuote = false;
                if (ch == '"') { sb.Append('"'); continue; }   // "" => literal "
                inQ = false;                                     // it was a closing quote
            }
            if (ch == '"') { if (inQ) pendingQuote = true; else inQ = true; continue; }
            if (ch == delim && !inQ) { list.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list.ToArray();
    }

    private static Encoding SafeEncoding(string name)
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(name); }
        catch { return Encoding.UTF8; }
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes);
    }
}

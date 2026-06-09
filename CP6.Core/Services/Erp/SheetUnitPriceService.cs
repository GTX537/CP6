using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA130 — Web シート単価マスタ業務サービス
/// </summary>
/// <remarks>
/// Excel xlsx 取込：System.IO.Compression（zip）+ XmlReader（純 .NET）で読込。
/// 取込区分で書込み先テーブルを切替（T_SheetUnitPrice / T_SheetUnitPriceEstimate）。
/// </remarks>
public class SheetUnitPriceService : ISheetUnitPriceService
{
    private readonly CP6Context _db;

    /// <summary>仕様書 §4 — Excel 13 列固定（順序）</summary>
    private static readonly string[] RequiredColumns =
    {
        "営業担当", "得意先", "段",
        "原紙(表)", "印刷(表)", "エンボス(表)",
        "原紙(中)", "印刷(中)", "エンボス(中)",
        "原紙(裏)", "印刷(裏)", "エンボス(裏)",
        "単価",
    };

    public SheetUnitPriceService(CP6Context db) { _db = db; }

    // ═══════════════════════════════════════════════════════════
    //  参照検索（取込区分で表切替；改定日 ≤ 基準日 の最新）
    // ═══════════════════════════════════════════════════════════

    public async Task<List<SheetUnitPriceDto>> SearchAsync(SheetPriceQueryDto query)
    {
        if (string.IsNullOrWhiteSpace(query.BaseCd))
            throw new InvalidOperationException("W10001: 必須項目に値が指定されていません（基準日、拠点）");

        return query.ImportDiv == SheetPriceImportDiv.Estimate
            ? await SearchEstimateAsync(query)
            : await SearchStandardAsync(query);
    }

    private async Task<List<SheetUnitPriceDto>> SearchStandardAsync(SheetPriceQueryDto query)
    {
        var q = _db.SheetUnitPrices.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BaseCd == query.BaseCd && x.RevisionDate <= query.BaseDate);
        if (!string.IsNullOrWhiteSpace(query.CustomerCd)) q = q.Where(x => x.CustomerCd == query.CustomerCd);
        if (!string.IsNullOrWhiteSpace(query.SalesStaffCd)) q = q.Where(x => x.SalesStaffCd == query.SalesStaffCd);

        // EF Core では GroupBy → First() を SQL 翻訳できないため、メモリ上で GROUP BY する
        // （データ量はマスタ規模で穏当 — ページング無し前提）
        var all = await q.OrderByDescending(x => x.RevisionDate).ToListAsync();
        var rows = all
            .GroupBy(x => new {
                x.BaseCd, x.CustomerCd, x.SheetFlute,
                x.PaperCdF, x.PrintCdF, x.EmbossCdF,
                x.PaperCdC, x.PrintCdC, x.EmbossCdC,
                x.PaperCdB, x.PrintCdB, x.EmbossCdB
            })
            .Select(g => g.First())  // 既に降順なので First = 最大改定日
            .OrderBy(x => x.CustomerCd).ThenBy(x => x.SheetFlute)
            .ToList();
        return rows.Select((r, i) => MapToDto(r, i + 1)).ToList();
    }

    private async Task<List<SheetUnitPriceDto>> SearchEstimateAsync(SheetPriceQueryDto query)
    {
        var q = _db.SheetUnitPriceEstimates.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BaseCd == query.BaseCd && x.RevisionDate <= query.BaseDate);
        if (!string.IsNullOrWhiteSpace(query.CustomerCd)) q = q.Where(x => x.CustomerCd == query.CustomerCd);
        if (!string.IsNullOrWhiteSpace(query.SalesStaffCd)) q = q.Where(x => x.SalesStaffCd == query.SalesStaffCd);

        var all = await q.OrderByDescending(x => x.RevisionDate).ToListAsync();
        var rows = all
            .GroupBy(x => new {
                x.BaseCd, x.CustomerCd, x.SheetFlute,
                x.PaperCdF, x.PrintCdF, x.EmbossCdF,
                x.PaperCdC, x.PrintCdC, x.EmbossCdC,
                x.PaperCdB, x.PrintCdB, x.EmbossCdB
            })
            .Select(g => g.First())
            .OrderBy(x => x.CustomerCd).ThenBy(x => x.SheetFlute)
            .ToList();
        return rows.Select((r, i) => MapEstimateToDto(r, i + 1)).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  Excel 取込（7-step）
    // ═══════════════════════════════════════════════════════════

    public Task<SheetPriceImportResultDto> ImportExcelAsync(
        Stream excelStream, DateTime baseDate, string baseCd, SheetPriceImportDiv importDiv)
    {
        var result = new SheetPriceImportResultDto();
        try
        {
            // Step 1: Excel 読込
            var rawRows = ReadXlsxRows(excelStream);
            if (rawRows.Count == 0)
            {
                result.ErrorStep = 1;
                result.Errors.Add("E10097: ファイルの読み込みに失敗しました。ファイルのフォーマット(EXCEL)を確認してください。");
                return Task.FromResult(result);
            }

            // Step 2: 列数チェック
            var firstRow = rawRows[0];
            if (firstRow.Count != RequiredColumns.Length)
            {
                result.ErrorStep = 2;
                result.Errors.Add($"E10098: 読み込んだファイルのカラム数 {firstRow.Count} が正しくありません。(正:{RequiredColumns.Length})");
                return Task.FromResult(result);
            }

            // Header 検出（ヘッダー行を含む可能性）
            int dataStart = 0;
            if (firstRow.Any(c => RequiredColumns.Contains(c?.Trim() ?? "")))
                dataStart = 1;

            var dtos = new List<SheetUnitPriceDto>();
            for (int rowIdx = dataStart; rowIdx < rawRows.Count; rowIdx++)
            {
                var srcLine = rowIdx + 1;
                var cells = rawRows[rowIdx];
                if (cells.All(c => string.IsNullOrWhiteSpace(c))) continue; // 空行スキップ

                // Step 3: 必須項目チェック
                for (int c = 0; c < RequiredColumns.Length; c++)
                {
                    var v = c < cells.Count ? cells[c] : null;
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        result.Errors.Add($"E10021: 必須項目に値が指定されていません。行：{srcLine} 列名：{RequiredColumns[c]}");
                        result.ErrorStep ??= 3;
                    }
                }
                if (result.ErrorStep == 3) continue;

                // Step 4: 型チェック（単価=数値）
                if (!decimal.TryParse(cells[12], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    result.Errors.Add($"E10099: 数字 形式ではありません。行：{srcLine} 列名：単価");
                    result.ErrorStep ??= 4;
                    continue;
                }

                dtos.Add(new SheetUnitPriceDto
                {
                    Selected = true,
                    SourceLine = srcLine,
                    RevisionDate = baseDate,
                    BaseCd = baseCd,
                    SalesStaffCd = cells[0]?.Trim(),
                    CustomerCd = cells[1]?.Trim() ?? "",
                    SheetFlute = cells[2]?.Trim() ?? "",
                    PaperCdF = cells[3]?.Trim() ?? "",
                    PrintCdF = cells[4]?.Trim() ?? "",
                    EmbossCdF = cells[5]?.Trim() ?? "",
                    PaperCdC = cells[6]?.Trim() ?? "",
                    PrintCdC = cells[7]?.Trim() ?? "",
                    EmbossCdC = cells[8]?.Trim() ?? "",
                    PaperCdB = cells[9]?.Trim() ?? "",
                    PrintCdB = cells[10]?.Trim() ?? "",
                    EmbossCdB = cells[11]?.Trim() ?? "",
                    UnitPrice = price,
                });
            }

            if (result.ErrorStep.HasValue)
            {
                result.Rows = dtos;
                return Task.FromResult(result);
            }

            // Step 5: マスタ存在チェック — 取引先（得意先 FLG=1）のみ簡易チェック
            // 拠点・原紙/印刷/エンボス/段マスタは別途整備済み前提（取引先のみ実装）
            var customerCds = dtos.Select(d => d.CustomerCd).Distinct().ToList();
            var existingCustomers = _db.BusinessPartners.AsNoTracking()
                .Where(b => !b.IsDeleted && b.CustomerFlg && customerCds.Contains(b.BpCd))
                .Select(b => b.BpCd).ToHashSet();
            for (int i = 0; i < dtos.Count; i++)
            {
                var d = dtos[i];
                if (!existingCustomers.Contains(d.CustomerCd))
                {
                    result.Errors.Add($"E10100: 指定された値はマスタに存在しません。行：{d.SourceLine} 列名：得意先 値：{d.CustomerCd}");
                    result.ErrorStep ??= 5;
                }
            }
            if (result.ErrorStep.HasValue)
            {
                result.Rows = dtos;
                return Task.FromResult(result);
            }

            // Step 6: 取込データ内重複チェック（同一 PK あれば HasDuplicates=true）
            var dupGroups = dtos
                .GroupBy(d => new {
                    d.RevisionDate, d.BaseCd, d.CustomerCd, d.SheetFlute,
                    d.PaperCdF, d.PrintCdF, d.EmbossCdF,
                    d.PaperCdC, d.PrintCdC, d.EmbossCdC,
                    d.PaperCdB, d.PrintCdB, d.EmbossCdB
                })
                .Where(g => g.Count() > 1)
                .ToList();
            if (dupGroups.Any())
                result.HasDuplicates = true;

            // 既存 DB 重複も確認（フロント上書き確認用）
            var pkSet = dtos.Select(d => $"{d.RevisionDate:yyyyMMdd}|{d.CustomerCd}|{d.SheetFlute}").ToHashSet();
            var existQuery = importDiv == SheetPriceImportDiv.Estimate
                ? _db.SheetUnitPriceEstimates.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.BaseCd == baseCd && x.RevisionDate == baseDate)
                    .Select(x => x.RevisionDate.ToString("yyyyMMdd") + "|" + x.CustomerCd + "|" + x.SheetFlute)
                : _db.SheetUnitPrices.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.BaseCd == baseCd && x.RevisionDate == baseDate)
                    .Select(x => x.RevisionDate.ToString("yyyyMMdd") + "|" + x.CustomerCd + "|" + x.SheetFlute);
            var existing = existQuery.ToList();
            if (existing.Any(k => pkSet.Contains(k)))
                result.HasDuplicates = true;

            // Step 7: 一覧返却（行番号付与）
            for (int i = 0; i < dtos.Count; i++) dtos[i].RowNo = i + 1;
            result.Ok = true;
            result.Rows = dtos;
            return Task.FromResult(result);
        }
        catch (InvalidDataException)
        {
            result.ErrorStep = 1;
            result.Errors.Add("E10097: ファイルの読み込みに失敗しました。ファイルのフォーマット(EXCEL)を確認してください。");
            return Task.FromResult(result);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  バッチ更新（UPSERT）
    // ═══════════════════════════════════════════════════════════

    public async Task<int> BatchUpdateAsync(SheetPriceBatchUpdateDto request, string? userName)
    {
        var now = DateTime.Now;
        int updated = 0;

        foreach (var r in request.Rows.Where(x => x.Selected))
        {
            if (request.ImportDiv == SheetPriceImportDiv.Estimate)
            {
                var ent = await _db.SheetUnitPriceEstimates.FirstOrDefaultAsync(x =>
                    !x.IsDeleted && x.RevisionDate == r.RevisionDate && x.BaseCd == r.BaseCd
                    && x.CustomerCd == r.CustomerCd && x.SheetFlute == r.SheetFlute
                    && x.PaperCdF == r.PaperCdF && x.PrintCdF == r.PrintCdF && x.EmbossCdF == r.EmbossCdF
                    && x.PaperCdC == r.PaperCdC && x.PrintCdC == r.PrintCdC && x.EmbossCdC == r.EmbossCdC
                    && x.PaperCdB == r.PaperCdB && x.PrintCdB == r.PrintCdB && x.EmbossCdB == r.EmbossCdB);
                if (ent == null)
                {
                    ent = new SheetUnitPriceEstimate
                    {
                        RevisionDate = r.RevisionDate, BaseCd = r.BaseCd, CustomerCd = r.CustomerCd,
                        SheetFlute = r.SheetFlute,
                        PaperCdF = r.PaperCdF, PrintCdF = r.PrintCdF, EmbossCdF = r.EmbossCdF,
                        PaperCdC = r.PaperCdC, PrintCdC = r.PrintCdC, EmbossCdC = r.EmbossCdC,
                        PaperCdB = r.PaperCdB, PrintCdB = r.PrintCdB, EmbossCdB = r.EmbossCdB,
                        SalesStaffCd = r.SalesStaffCd,
                        Creator = userName, CreateDate = now,
                    };
                    _db.SheetUnitPriceEstimates.Add(ent);
                }
                ent.UnitPrice = r.UnitPrice;
                ent.SalesStaffCd = r.SalesStaffCd;
                ent.Modifier = userName;
                ent.ModifyDate = now;
            }
            else
            {
                var ent = await _db.SheetUnitPrices.FirstOrDefaultAsync(x =>
                    !x.IsDeleted && x.RevisionDate == r.RevisionDate && x.BaseCd == r.BaseCd
                    && x.CustomerCd == r.CustomerCd && x.SheetFlute == r.SheetFlute
                    && x.PaperCdF == r.PaperCdF && x.PrintCdF == r.PrintCdF && x.EmbossCdF == r.EmbossCdF
                    && x.PaperCdC == r.PaperCdC && x.PrintCdC == r.PrintCdC && x.EmbossCdC == r.EmbossCdC
                    && x.PaperCdB == r.PaperCdB && x.PrintCdB == r.PrintCdB && x.EmbossCdB == r.EmbossCdB);
                if (ent == null)
                {
                    ent = new SheetUnitPrice
                    {
                        RevisionDate = r.RevisionDate, BaseCd = r.BaseCd, CustomerCd = r.CustomerCd,
                        SheetFlute = r.SheetFlute,
                        PaperCdF = r.PaperCdF, PrintCdF = r.PrintCdF, EmbossCdF = r.EmbossCdF,
                        PaperCdC = r.PaperCdC, PrintCdC = r.PrintCdC, EmbossCdC = r.EmbossCdC,
                        PaperCdB = r.PaperCdB, PrintCdB = r.PrintCdB, EmbossCdB = r.EmbossCdB,
                        SalesStaffCd = r.SalesStaffCd,
                        Creator = userName, CreateDate = now,
                    };
                    _db.SheetUnitPrices.Add(ent);
                }
                ent.UnitPrice = r.UnitPrice;
                ent.SalesStaffCd = r.SalesStaffCd;
                ent.Modifier = userName;
                ent.ModifyDate = now;
            }
            updated++;
        }
        await _db.SaveChangesAsync();
        return updated;
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper：xlsx を行リストとして読込（純 .NET 実装）
    // ═══════════════════════════════════════════════════════════

    /// <summary>xlsx → 全行 List&lt;List&lt;string&gt;&gt;（最初のシート）</summary>
    private static List<List<string?>> ReadXlsxRows(Stream xlsxStream)
    {
        var rows = new List<List<string?>>();
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);

        // sharedStrings.xml（任意）— 正規表現で <si>...</si> ブロック内の <t> を全抽出
        var sst = new List<string>();
        var sstEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sstEntry != null)
        {
            using var s = sstEntry.Open();
            using var sr = new StreamReader(s);
            var xml = sr.ReadToEnd();
            var siRe = new Regex(@"<si\b[^>]*>([\s\S]*?)</si>");
            var tRe = new Regex(@"<t(?:\s[^>]*)?>([\s\S]*?)</t>");
            foreach (Match m in siRe.Matches(xml))
            {
                var sb = new StringBuilder();
                foreach (Match tm in tRe.Matches(m.Groups[1].Value))
                    sb.Append(DecodeEntities(tm.Groups[1].Value));
                sst.Add(sb.ToString());
            }
        }

        // 最初のシート
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml"));
        if (sheetEntry == null) return rows;

        using var ss = sheetEntry.Open();
        using var ssr = new StreamReader(ss);
        var sheetXml = ssr.ReadToEnd();

        // 行を抽出
        var rowRe = new Regex(@"<row\b[^>]*>([\s\S]*?)</row>");
        // セル：自己終端 <c .../> と open/close <c ...>...</c> の両対応
        var cellRe = new Regex(@"<c\b([^>\/]*)(?:\/>|>([\s\S]*?)</c>)");
        var refAttrRe = new Regex(@"r=""([A-Z]+)\d+""");
        var typeAttrRe = new Regex(@"\bt=""([^""]+)""");
        var vRe = new Regex(@"<v>([\s\S]*?)</v>");
        var isRe = new Regex(@"<is>([\s\S]*?)</is>");
        var tRe2 = new Regex(@"<t(?:\s[^>]*)?>([\s\S]*?)</t>");

        foreach (Match rm in rowRe.Matches(sheetXml))
        {
            var inner = rm.Groups[1].Value;
            var row = new List<string?>();
            foreach (Match cm in cellRe.Matches(inner))
            {
                var attrs = cm.Groups[1].Value;
                var body = cm.Groups[2].Success ? cm.Groups[2].Value : string.Empty;
                var refMatch = refAttrRe.Match(attrs);
                if (!refMatch.Success) continue;
                var col = ColLetterToIndex(refMatch.Groups[1].Value);
                var typeMatch = typeAttrRe.Match(attrs);
                var type = typeMatch.Success ? typeMatch.Groups[1].Value : "n";

                while (row.Count <= col) row.Add(null);

                string? val = null;
                var vm = vRe.Match(body);
                var ism = isRe.Match(body);
                if (type == "s" && vm.Success)
                {
                    if (int.TryParse(vm.Groups[1].Value, out var idx) && idx < sst.Count)
                        val = sst[idx];
                }
                else if (type == "inlineStr" && ism.Success)
                {
                    var sb = new StringBuilder();
                    foreach (Match tm in tRe2.Matches(ism.Groups[1].Value))
                        sb.Append(DecodeEntities(tm.Groups[1].Value));
                    val = sb.ToString();
                }
                else if (vm.Success)
                {
                    val = DecodeEntities(vm.Groups[1].Value);
                }
                else
                {
                    // inline <t> without <is> wrapper
                    var tm = tRe2.Match(body);
                    if (tm.Success) val = DecodeEntities(tm.Groups[1].Value);
                }
                row[col] = val;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static string DecodeEntities(string s) => s
        .Replace("&lt;", "<").Replace("&gt;", ">")
        .Replace("&quot;", "\"").Replace("&apos;", "'")
        .Replace("&amp;", "&");

    private static int ColLetterToIndex(string letters)
    {
        int n = 0;
        foreach (var c in letters) n = n * 26 + (c - 'A' + 1);
        return n - 1;
    }

    // ═══════════════════════════════════════════════════════════
    //  Mapping helpers
    // ═══════════════════════════════════════════════════════════

    private static SheetUnitPriceDto MapToDto(SheetUnitPrice e, int rowNo) => new()
    {
        RowNo = rowNo,
        RevisionDate = e.RevisionDate,
        BaseCd = e.BaseCd,
        SalesStaffCd = e.SalesStaffCd,
        CustomerCd = e.CustomerCd,
        SheetFlute = e.SheetFlute,
        PaperCdF = e.PaperCdF, PrintCdF = e.PrintCdF, EmbossCdF = e.EmbossCdF,
        PaperCdC = e.PaperCdC, PrintCdC = e.PrintCdC, EmbossCdC = e.EmbossCdC,
        PaperCdB = e.PaperCdB, PrintCdB = e.PrintCdB, EmbossCdB = e.EmbossCdB,
        UnitPrice = e.UnitPrice,
    };

    private static SheetUnitPriceDto MapEstimateToDto(SheetUnitPriceEstimate e, int rowNo) => new()
    {
        RowNo = rowNo,
        RevisionDate = e.RevisionDate,
        BaseCd = e.BaseCd,
        SalesStaffCd = e.SalesStaffCd,
        CustomerCd = e.CustomerCd,
        SheetFlute = e.SheetFlute,
        PaperCdF = e.PaperCdF, PrintCdF = e.PrintCdF, EmbossCdF = e.EmbossCdF,
        PaperCdC = e.PaperCdC, PrintCdC = e.PrintCdC, EmbossCdC = e.EmbossCdC,
        PaperCdB = e.PaperCdB, PrintCdB = e.PrintCdB, EmbossCdB = e.EmbossCdB,
        UnitPrice = e.UnitPrice,
    };
}

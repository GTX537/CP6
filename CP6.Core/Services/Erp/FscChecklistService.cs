using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA100 FSC チェックシート発行サービス（Excel 出力）
/// </summary>
/// <remarks>
/// 仕様書 §5 — FSC 管理 NO 採番後 Excel 帳票生成。
/// 環境にテンプレート Excel が存在しない場合は plain-text プレースホルダで返却。
/// </remarks>
public class FscChecklistService : IFscChecklistService
{
    private readonly CP6Context _db;
    private readonly IConfiguration _cfg;

    public FscChecklistService(CP6Context db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    // ═══════════════════════════════════════════════════════════
    //  検索（仕様書 §4 — 7 テーブル JOIN）
    // ═══════════════════════════════════════════════════════════

    public async Task<(List<FscChecklistItemDto>, int)> SearchAsync(FscChecklistQueryDto q)
    {
        if (string.IsNullOrWhiteSpace(q.BaseCd))
            throw new InvalidOperationException("E10022: 拠点を指定してください");

        // M01 御見積書 ⇄ M05 御見積書見積計算書 ⇄ M07 見積計算書 ⇄ M06 御見積書明細(明細NO=1)
        // 環境簡略化のため Quotation/QuotationCalc/QuotationDetail/EstimateCalc を JOIN
        var qry =
            from q1 in _db.Quotations.AsNoTracking()
            where !q1.IsDeleted
            join qc in _db.QuotationCalcs.AsNoTracking().Where(x => !x.IsDeleted)
                on q1.QtnNo equals qc.QtnNo
            join ec in _db.EstimateCalcs.AsNoTracking().Where(x => !x.IsDeleted)
                on qc.QtnCalcNo equals ec.QtnCalcNo
            join det in _db.QuotationDetails.AsNoTracking().Where(x => !x.IsDeleted && x.DetailNo == 1)
                on new { q1.QtnNo, qc.QtnCalcNo } equals new { det.QtnNo, det.QtnCalcNo } into detL
            from det in detL.DefaultIfEmpty()
            where ec.FscProductDiv != null && ec.FscProductDiv != "0"
            select new { q1, qc, ec, det };

        // 拠点
        qry = qry.Where(x => x.q1.BaseCd == q.BaseCd);
        if (!string.IsNullOrWhiteSpace(q.StaffCd)) qry = qry.Where(x => x.q1.StaffCd == q.StaffCd);
        if (q.IssueDateFrom.HasValue) qry = qry.Where(x => x.q1.QtnIssueDate >= q.IssueDateFrom.Value);
        if (q.IssueDateTo.HasValue) qry = qry.Where(x => x.q1.QtnIssueDate <= q.IssueDateTo.Value);
        if (!string.IsNullOrWhiteSpace(q.QtnNoFrom)) qry = qry.Where(x => string.Compare(x.q1.QtnNo, q.QtnNoFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(q.QtnNoTo)) qry = qry.Where(x => string.Compare(x.q1.QtnNo, q.QtnNoTo) <= 0);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) qry = qry.Where(x => x.q1.CustomerCd == q.CustomerCd);

        // 未発行/発行済（FSC 管理 NO の有無で判別）
        var includeUnissued = q.IncludeUnissued ?? true;
        var includeIssued = q.IncludeIssued ?? false;
        if (includeUnissued && !includeIssued)
            qry = qry.Where(x => string.IsNullOrEmpty(x.ec.FscManagementNo));
        else if (!includeUnissued && includeIssued)
            qry = qry.Where(x => !string.IsNullOrEmpty(x.ec.FscManagementNo));
        else if (!includeUnissued && !includeIssued)
            return (new(), 0);

        var total = await qry.CountAsync();
        if (q.MaxRows.HasValue && total > q.MaxRows.Value)
            throw new InvalidOperationException($"E10013: 検索件数が上限 {q.MaxRows.Value} 件を超えました（{total} 件）");

        var rows = await qry
            .OrderBy(x => x.q1.StaffCd).ThenBy(x => x.q1.CustomerCd).ThenBy(x => x.qc.QtnCalcNo)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(x => new FscChecklistItemDto
            {
                StaffCd = x.q1.StaffCd,
                CustomerCd = x.q1.CustomerCd,
                ProjectNo = x.q1.ProjectNoParent,
                QtnNo = x.q1.QtnNo,
                QtnIssueDate = x.q1.QtnIssueDate,
                CustomerItemName1 = x.det != null ? x.det.ItemName1 : null,
                CustomerItemName2 = x.det != null ? x.det.ItemName2 : null,
                Quantity = x.det != null ? x.det.Quantity : null,
                UnitPrice = x.det != null ? x.det.UnitPrice : null,
                Amount = x.det != null ? x.det.Amount : null,
                TotalAmount = x.ec.TotalAmount,
                Status = x.ec.Status,
                FscManagementNo = x.ec.FscManagementNo,
                IssuedLabel = string.IsNullOrEmpty(x.ec.FscManagementNo) ? null : "発行済",
                QtnCalcNo = x.qc.QtnCalcNo,
                FscProductDiv = x.ec.FscProductDiv,
                Issue = false,
            }).ToListAsync();

        for (int i = 0; i < rows.Count; i++)
            rows[i].RowNo = (q.Page - 1) * q.PageSize + i + 1;
        return (rows, total);
    }

    // ═══════════════════════════════════════════════════════════
    //  発行
    // ═══════════════════════════════════════════════════════════

    public async Task<FscIssueResultDto> IssueAsync(FscIssueRequestDto req, string? userName)
    {
        if (string.IsNullOrWhiteSpace(req.FormatName))
            throw new InvalidOperationException("E10022: 出力フォーマットを選択してください");
        if (req.Targets == null || req.Targets.Count == 0)
            throw new InvalidOperationException("発行対象を選択してください");

        var formats = GetFormats();
        var fmt = formats.FirstOrDefault(f => f.Name == req.FormatName);
        if (fmt.Name == null)
            throw new InvalidOperationException($"E10022: 出力フォーマット '{req.FormatName}' が定義されていません");

        var prefix = _cfg["MSBBPA100_FSCNO_INITIAL"] ?? "FSC";
        var result = new FscIssueResultDto();
        var now = DateTime.Now;

        foreach (var t in req.Targets)
        {
            // 既存 FSC 管理 NO（発行済の場合は再発行とみなす — 仕様書記載なし、運用判断）
            var existing = await _db.EstimateCalcs
                .FirstOrDefaultAsync(x => x.QtnCalcNo == t.QtnCalcNo && !x.IsDeleted);
            if (existing == null) continue;

            string fscNo;
            if (!string.IsNullOrWhiteSpace(existing.FscManagementNo))
            {
                fscNo = existing.FscManagementNo;
            }
            else
            {
                fscNo = await NextFscNoAsync(prefix);
                existing.FscManagementNo = fscNo;
                existing.Modifier = userName;
                existing.ModifyDate = now;
            }

            // 御見積書 (M01).FSCチェックシート発行日（モデル無し → スキップ；JSON ログのみ）
            // 御見積書見積計算書（M05 確定 FLG=1）の FSC 管理 NO 更新
            // 確定 FLG=1 の行に FSC 管理 NO を更新
            await _db.QuotationCalcs
                .Where(x => x.QtnNo == t.QtnNo && x.QtnCalcNo == t.QtnCalcNo && !x.IsDeleted && x.MasterConfirmFlg == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.FscManagementNo, fscNo)
                                          .SetProperty(p => p.Modifier, userName)
                                          .SetProperty(p => p.ModifyDate, now));

            // 発行履歴に追加
            var fileName = $"{_cfg["ExcelFileName_MSBBPA100"] ?? "FscChecklist"}_{fscNo}.xlsx";
            _db.FscChecklists.Add(new FscChecklist
            {
                FscManagementNo = fscNo,
                QtnNo = t.QtnNo,
                QtnCalcNo = t.QtnCalcNo,
                CustomerCd = t.CustomerCd,
                StaffCd = t.StaffCd,
                BaseCd = null,
                IssueDate = now.Date,
                FormatName = fmt.Name,
                TemplatePath = fmt.TemplatePath,
                ExcelFileName = fileName,
                FscProductDiv = existing.FscProductDiv,
                Creator = userName,
                CreateDate = now,
                Modifier = userName,
                ModifyDate = now,
            });

            // Excel 生成 — テンプレートが存在すれば読み込んで返す、無ければ簡易テキストで代用
            var content = await BuildExcelOrFallbackAsync(fmt.TemplatePath, fscNo, t, existing);

            result.Items.Add(new FscIssueResultItem
            {
                FscManagementNo = fscNo,
                ExcelFileName = fileName,
                QtnNo = t.QtnNo,
                QtnCalcNo = t.QtnCalcNo,
                Content = content,
            });
            result.IssuedCount++;
        }

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<(byte[] Content, string FileName)?> DownloadAsync(string fscManagementNo)
    {
        var rec = await _db.FscChecklists.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FscManagementNo == fscManagementNo && !x.IsDeleted);
        if (rec == null) return null;

        // 発行時に保存していないため、テンプレートから再生成
        var content = await BuildExcelOrFallbackAsync(rec.TemplatePath, fscManagementNo, null, null);
        return (content, rec.ExcelFileName ?? $"FscChecklist_{fscManagementNo}.xlsx");
    }

    public IList<(string Name, string TemplatePath)> GetFormats()
    {
        var list = new List<(string, string)>();
        for (int i = 1; i <= 10; i++)
        {
            var key = $"MSBBPA100_ExcelFormat{i:D2}";
            var raw = _cfg[key];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // "Name,Path" 形式
            var idx = raw.IndexOf(',');
            if (idx < 0) continue;
            var name = raw[..idx].Trim();
            var path = raw[(idx + 1)..].Trim();
            list.Add((name, path));
        }
        // 未定義時のフォールバック
        if (list.Count == 0)
            list.Add(("製品用チェックシート", string.Empty));
        return list;
    }

    private async Task<string> NextFscNoAsync(string prefix)
    {
        // FSC 管理 NO = 機能コード(3)+年(4)+月(2)+自増(4)=13桁（永不重置）
        var code = (string.IsNullOrWhiteSpace(prefix) ? "FSC" : prefix).ToUpperInvariant();
        if (code.Length > 3) code = code[..3];
        var (no, _) = await DocNumber.NextAsync(_db, code);
        return no;
    }

    private async Task<byte[]> BuildExcelOrFallbackAsync(
        string? templatePath, string fscNo, FscIssueTarget? target, EstimateCalc? ec)
    {
        // テンプレート読み込み（存在時）
        if (!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))
        {
            // テンプレートをそのままバイト列として返す（プレースホルダ置換は将来 OpenXML SDK 化）
            return await File.ReadAllBytesAsync(templatePath);
        }

        // フォールバック：plain-text 簡易帳票
        var sb = new StringBuilder();
        sb.AppendLine("FSC製品化チェックシート");
        sb.AppendLine($"FSC管理NO : {fscNo}");
        if (target != null)
        {
            sb.AppendLine($"御見積書NO : {target.QtnNo}");
            sb.AppendLine($"見積計算書NO : {target.QtnCalcNo}");
            sb.AppendLine($"得意先CD : {target.CustomerCd}");
            sb.AppendLine($"担当者CD : {target.StaffCd}");
        }
        if (ec != null)
        {
            sb.AppendLine($"FSC製品区分 : {ec.FscProductDiv}");
            sb.AppendLine($"合計金額 : {ec.TotalAmount}");
        }
        sb.AppendLine($"発行日時 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}

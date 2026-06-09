using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Common;

/// <summary>
/// 全社統一採番ヘルパ。
/// 採番形式：機能コード(3) + 年(4) + 月(2) + 自増(4) = 13 桁（例：EMC2026050001）。
/// 自増は機能コードごとに永不重置（グローバル累計）。
/// </summary>
/// <remarks>
/// カウンタの加算は呼び出し側の SaveChanges と同一トランザクションで確定するため、
/// 採番→実体登録が原子的になる（従来の MAX(...)+1 と同等以上の整合性）。
/// 呼び出し側は本メソッド後に必ず SaveChangesAsync を行うこと。
/// </remarks>
public static class DocNumber
{
    /// <summary>機能コード別の次番号を採番する。戻り値：(13桁番号, 自増値)。</summary>
    public static async Task<(string No, int Seq)> NextAsync(CP6Context db, string funcCode, DateTime? date = null)
    {
        var code = funcCode.ToUpperInvariant();
        // 同一 DbContext 内で複数回採番する場合（例：FSC 一括発行のループ）、
        // 未保存の追加済みカウンタ行を DB クエリは拾えないため、まず Local を確認する。
        var row = db.DocSequences.Local.FirstOrDefault(x => x.FuncCode == code)
                  ?? await db.DocSequences.FirstOrDefaultAsync(x => x.FuncCode == code);
        if (row == null)
        {
            row = new DocSequence { FuncCode = code, LastSeq = 0, CreateDate = DateTime.Now };
            db.DocSequences.Add(row);
        }
        row.LastSeq += 1;
        var d = date ?? DateTime.Today;
        return ($"{code}{d:yyyyMM}{row.LastSeq:D4}", row.LastSeq);
    }
}

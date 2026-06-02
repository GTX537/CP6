using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MES 採番管理サービス実装
/// </summary>
/// <remarks>
/// 採番形式（全社統一）：{機能コード}{yyyyMM}{NNNN}（永不重置・グローバル累計）
/// 例：WO2026050001 / PR2026050002 / QC2026050001 / DF2026050001
/// SeqDate は "*"（全期間スコープ）固定。年月で 0 戻ししない。
/// </remarks>
public class MesSequenceService : IMesSequenceService
{
    private readonly CP6Context _db;

    public MesSequenceService(CP6Context db) => _db = db;

    /// <summary>全期間スコープを表す番兵（永不重置）</summary>
    private const string GlobalScope = "*";

    public async Task<string> NextAsync(string seqKey, DateTime? date = null)
    {
        var d = (date ?? DateTime.Today).Date;
        var key = seqKey.ToUpperInvariant();

        // 採番行を取得 or 新規作成（年月で 0 戻ししないため日付ではなく全期間スコープで管理）
        var seq = await _db.MesSequences
            .FirstOrDefaultAsync(x => x.SeqKey == key && x.SeqDate == GlobalScope);

        int next;
        if (seq == null)
        {
            seq = new MesSequence
            {
                SeqKey = key,
                SeqDate = GlobalScope,
                CurrentValue = 1,
            };
            _db.MesSequences.Add(seq);
            next = 1;
        }
        else
        {
            seq.CurrentValue += 1;
            next = seq.CurrentValue;
        }

        await _db.SaveChangesAsync();
        return $"{key}{d:yyyyMM}{next:D4}";
    }
}

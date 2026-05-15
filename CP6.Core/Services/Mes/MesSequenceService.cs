using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MES 採番管理サービス実装
/// </summary>
/// <remarks>
/// 採番形式：{Prefix}{yyyyMMdd}-{NNNN}
/// 例：WO20260515-0001 / PR20260515-0002 / QC20260515-0001 / DF20260515-0001
/// 並行安全：UPDATE 行ロック（SQL Server）/ ExecuteUpdateAsync を使用
/// </remarks>
public class MesSequenceService : IMesSequenceService
{
    private readonly CP6Context _db;

    public MesSequenceService(CP6Context db) => _db = db;

    public async Task<string> NextAsync(string seqKey, DateTime? date = null)
    {
        var d = (date ?? DateTime.Today).Date;
        var key = seqKey.ToUpperInvariant();
        var dateStr = d.ToString("yyyy-MM-dd");

        // 採番行を取得 or 新規作成
        var seq = await _db.MesSequences
            .FirstOrDefaultAsync(x => x.SeqKey == key && x.SeqDate == dateStr);

        int next;
        if (seq == null)
        {
            seq = new MesSequence
            {
                SeqKey = key,
                SeqDate = dateStr,
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
        return $"{key}{d:yyyyMMdd}-{next:D4}";
    }
}

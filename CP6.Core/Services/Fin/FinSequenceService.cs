using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务采番服务实现（章01 §4）。凭证号"{key}-{yyyy-MM}-{NNNNN}"，按月作用域归零（gapless 累计）。
/// </summary>
public class FinSequenceService : IFinSequenceService
{
    private readonly CP6Context _db;
    public FinSequenceService(CP6Context db) => _db = db;

    public async Task<string> NextAsync(string seqKey, DateTime? date = null)
    {
        var d = (date ?? DateTime.Today).Date;
        var key = seqKey.ToUpperInvariant();
        var scope = d.ToString("yyyy-MM");

        var seq = await _db.FinSequences
            .FirstOrDefaultAsync(x => x.SeqKey == key && x.SeqDate == scope);

        int next;
        if (seq == null)
        {
            seq = new FinSequence { SeqKey = key, SeqDate = scope, CurrentValue = 1 };
            _db.FinSequences.Add(seq);
            next = 1;
        }
        else
        {
            seq.CurrentValue += 1;
            next = seq.CurrentValue;
        }

        await _db.SaveChangesAsync();
        return $"{key}-{scope}-{next:D5}";
    }
}

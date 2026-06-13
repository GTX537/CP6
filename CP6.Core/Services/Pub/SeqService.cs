using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>富采番服务实现 —— PUB 章05。自包含（自身 SaveChanges 即时提交号码；号码允许跳号）。</summary>
public class SeqService : ISeqService
{
    private readonly CP6Context _db;
    public SeqService(CP6Context db) => _db = db;

    public Task<string> NextAsync(string bizKey) => NextAsync(bizKey, DateTime.Now);

    /// <summary>可注入时间的重载（供测试验证跨周期重置）。</summary>
    public async Task<string> NextAsync(string bizKey, DateTime now)
    {
        var seq = _db.Pub_DocSequences.Local.FirstOrDefault(x => x.BizKey == bizKey)
                  ?? await _db.Pub_DocSequences.FirstOrDefaultAsync(x => x.BizKey == bizKey)
                  ?? throw new InvalidOperationException("E-PUB-051");   // 未配置采番规则

        var periodKey = PeriodKey(seq.ResetCycle, now);
        if (seq.CurrentPeriod != periodKey)
        {
            seq.CurrentValue = 0;
            seq.CurrentPeriod = periodKey;   // 跨周期重置流水
        }
        seq.CurrentValue++;
        seq.ModifyDate = now;
        await _db.SaveChangesAsync();

        var datePart = string.IsNullOrEmpty(seq.DateFormat) ? "" : now.ToString(seq.DateFormat);
        return seq.Prefix + datePart + seq.CurrentValue.ToString().PadLeft(seq.SeqLength, '0');
    }

    private static string PeriodKey(int resetCycle, DateTime now) => resetCycle switch
    {
        1 => now.ToString("yyyyMMdd"),   // 每日
        2 => now.ToString("yyyyMM"),     // 每月
        3 => now.ToString("yyyy"),       // 每年
        _ => ""                           // 0 不重置
    };
}

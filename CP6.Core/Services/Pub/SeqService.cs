using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>富采番服务实现。PostgreSQL 在规则行锁内推进；有调用方事务时由调用方最终提交。</summary>
public class SeqService : ISeqService
{
    private readonly CP6Context _db;
    public SeqService(CP6Context db) => _db = db;

    public Task<string> NextAsync(string bizKey) => NextAsync(bizKey, DateTime.Now);

    /// <summary>可注入时间的重载（供测试验证跨周期重置）。</summary>
    public async Task<string> NextAsync(string bizKey, DateTime now)
    {
        if (_db.Database.IsNpgsql()) return await NextPostgreSqlAsync(bizKey, now);

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

    private async Task<string> NextPostgreSqlAsync(string bizKey, DateTime now)
    {
        var tenant = _db.CurrentTenantId;
        await using var owned = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted) : null;
        var transaction = owned ?? _db.Database.CurrentTransaction!;
        var savepoint = owned is null ? "pub_seq_" + Guid.NewGuid().ToString("N") : null;
        if (savepoint is not null) await transaction.CreateSavepointAsync(savepoint);
        var completed = false;
        try
        {
            // Native equality preserves the existing tenant/BizKey unique domain, including its
            // collation and padding semantics. Never allocate from a stale tracked instance.
            var seq = await _db.Pub_DocSequences.FromSqlInterpolated($"""
                SELECT * FROM public."Pub_DocSequence"
                WHERE "TenantId" = {tenant} AND "BizKey" = CAST({bizKey} AS bpchar) FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync()
                ?? throw new InvalidOperationException("E-PUB-051");
            var tracked = _db.ChangeTracker.Entries<Pub_DocSequence>().SingleOrDefault(e => e.Entity.Id == seq.Id);
            if (tracked is not null && tracked.State != EntityState.Unchanged)
                throw new InvalidOperationException("SEQUENCE_STATE_DIRTY");

            var period = PeriodKey(seq.ResetCycle, now);
            var current = seq.CurrentPeriod == period ? seq.CurrentValue : 0;
            if (current < 0) throw new InvalidOperationException("RULE_INVALID");
            if (current == int.MaxValue) throw new InvalidOperationException("SEQUENCE_EXHAUSTED");
            var next = current + 1;
            string number;
            try
            {
                var date = string.IsNullOrEmpty(seq.DateFormat) ? "" : now.ToString(seq.DateFormat);
                number = seq.Prefix + date + next.ToString().PadLeft(seq.SeqLength, '0');
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException("RULE_INVALID", ex);
            }
            if (_db.CurrentTenantId != tenant) throw new InvalidOperationException("SEQUENCE_CONTEXT_CHANGED");

            // No SaveChanges: allocating a number must not flush unrelated pending business.
            var updated = await _db.Pub_DocSequences.Where(s => s.TenantId == tenant && s.Id == seq.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriod, period)
                    .SetProperty(x => x.CurrentValue, next).SetProperty(x => x.ModifyDate, now));
            if (updated != 1) throw new DbUpdateConcurrencyException("SEQUENCE_STATE_CHANGED");
            if (owned is not null) await owned.CommitAsync();
            else await transaction.ReleaseSavepointAsync(savepoint!);
            completed = true;

            if (tracked is not null)
            {
                seq.CurrentPeriod = period; seq.CurrentValue = next; seq.ModifyDate = now;
                tracked.CurrentValues.SetValues(seq); tracked.OriginalValues.SetValues(seq);
                tracked.State = EntityState.Unchanged;
            }
            // Inside a caller transaction this remains provisional until that caller commits.
            // This legacy interface has no operation identity or durable allocation receipt.
            return number;
        }
        catch
        {
            if (!completed)
            {
                if (owned is not null) await owned.RollbackAsync();
                else
                {
                    await transaction.RollbackToSavepointAsync(savepoint!);
                    await transaction.ReleaseSavepointAsync(savepoint!);
                }
            }
            throw;
        }
    }

    private static string PeriodKey(int resetCycle, DateTime now) => resetCycle switch
    {
        1 => now.ToString("yyyyMMdd"),   // 每日
        2 => now.ToString("yyyyMM"),     // 每月
        3 => now.ToString("yyyy"),       // 每年
        _ => ""                           // 0 不重置
    };
}

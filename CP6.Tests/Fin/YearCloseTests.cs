using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务波D D.1：年结（损益结转 3103 → 3104 + 期初延续 + 锁年 YearClosed）。
/// 场景：收入1000/费用600 → 净利400 → 3103 贷400 → 3104 贷400；损益科目清零；
/// 12 期未全 Closed 拒；锁年后自动/手工过账拒 E-FIN-404；重复年结幂等拒；反年结红冲后可再过账；净亏向。
/// </summary>
public class YearCloseTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static FiscalPeriodService Periods(CP6Context db) => new(db, 1);   // 财年=日历年
    private static JournalEntryService Jes(CP6Context db) => new(db, Periods(db), new FinSequenceService(db));
    private static PeriodCloseService Close(CP6Context db) =>
        new(db, Periods(db), new TrialBalanceService(db), journal: Jes(db));

    private static async Task<GlAccountService> SeedCoa(CP6Context db)
    {
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        return gl;
    }

    /// <summary>过账一张手工平衡凭证到指定日期所属期间。</summary>
    private static async Task PostManual(CP6Context db, DateTime date, Guid drAcct, Guid crAcct, decimal amount)
    {
        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = date,
            Source = VoucherSource.Manual,
            Lines =
            {
                new JournalLine { AccountId = drAcct, Debit = amount },
                new JournalLine { AccountId = crAcct, Credit = amount },
            },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        var r = await svc.PostAsync(id, "u2");
        Assert.True(r.Ok, r.Code);
    }

    /// <summary>建满 12 期并全部结账。</summary>
    private static async Task CloseAll12(CP6Context db, int year)
    {
        for (var m = 1; m <= 12; m++)
            await Periods(db).EnsureOpenAsync(new DateTime(year, m, 1));
        for (var m = 1; m <= 12; m++)
        {
            var p = (await Periods(db).ResolveAsync(new DateTime(year, m, 1)))!;
            var r = await Close(db).CloseAsync(p.Id, "boss");
            Assert.True(r.Ok, $"close {year}-{m}: {r.Code}");
        }
    }

    /// <summary>账户在全部已过账凭证下的净额（借-贷）。</summary>
    private static async Task<decimal> AcctNet(CP6Context db, Guid accId)
    {
        var vals = await (from l in db.JournalLines
                          join e in db.JournalEntries on l.EntryId equals e.Id
                          where e.Status == JournalStatus.Posted && l.AccountId == accId
                          select l.Debit - l.Credit).ToListAsync();
        return vals.Sum();
    }

    // ───────────────────────── 主场景：净利 400 ─────────────────────────

    [Fact]
    public async Task YearClose_NetProfit_ClearsPlAndRollsToRetainedEarnings()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;    // 银行存款
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;     // 主营业务收入 REVENUE
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;    // 主营业务成本 COGS
        var p3103 = (await gl.GetByCodeAsync("3103"))!.Id;   // 本年利润（无 Role）
        var p3104 = (await gl.GetByCodeAsync("3104"))!.Id;   // 未分配利润 RETAINED_EARNINGS

        // 收入 1000（1 月）/ 费用 600（2 月）→ 两期
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2026, 2, 15), cogs, bank, 600m);
        await CloseAll12(db, 2026);

        var r = await Close(db).YearCloseAsync(2026, "boss");
        Assert.True(r.Ok, r.Code);

        // 凭证一：损益结转（YC-2026）
        var v1 = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026");
        Assert.Equal(JournalStatus.Posted, v1.Status);
        Assert.Equal(1000m, v1.Lines.Single(l => l.AccountId == rev).Debit);    // 收入借记冲平
        Assert.Equal(600m, v1.Lines.Single(l => l.AccountId == cogs).Credit);   // 费用贷记冲平
        Assert.Equal(400m, v1.Lines.Single(l => l.AccountId == p3103).Credit);  // 净利入 3103 贷

        // 凭证二：3103 → 3104（YC-2026-RE）
        var v2 = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026-RE");
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3103).Debit);
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3104).Credit);

        // 损益科目清零；3103 结平；3104 贷 400
        Assert.Equal(0m, await AcctNet(db, rev));
        Assert.Equal(0m, await AcctNet(db, cogs));
        Assert.Equal(0m, await AcctNet(db, p3103));
        Assert.Equal(-400m, await AcctNet(db, p3104));   // 贷方净额

        // 全年 12 期锁年
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYear == 2026).ToListAsync();
        Assert.Equal(12, periods.Count);
        Assert.All(periods, p => Assert.Equal(PeriodStatus.YearClosed, p.Status));
    }

    // ───────────────────────── 期初延续（资产负债跨年）─────────────────────────

    [Fact]
    public async Task YearClose_AssetLiabilityBalancesCarryOverToNextYear()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cap = (await gl.GetByCodeAsync("3001"))!.Id;     // 实收资本（权益，跨年延续）

        // 期初投入：银行 借1000 / 实收资本 贷1000（资产+权益，跨年不清零）
        await PostManual(db, new DateTime(2026, 1, 10), bank, cap, 1000m);
        // 收入 500：银行 借500 / 收入 贷500
        await PostManual(db, new DateTime(2026, 3, 10), bank, rev, 500m);
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        // 下年 1 月试算：银行期初=上年期末=1500；实收资本期初=1000；收入期初=0（已清零）
        var jan27 = await Periods(db).EnsureOpenAsync(new DateTime(2027, 1, 1));
        var tb = await new TrialBalanceService(db).BuildAsync(jan27.Id);
        Assert.Equal(1500m, tb.Rows.Single(r => r.Code == "1002").OpenBal);
        Assert.Equal(1000m, tb.Rows.Single(r => r.Code == "3001").OpenBal);
        Assert.DoesNotContain(tb.Rows, r => r.Code == "4001" && r.OpenBal != 0m);
    }

    // ───────────────────────── 12 期未全 Closed → 拒 ─────────────────────────

    [Fact]
    public async Task YearClose_NotAll12Closed_Rejected()
    {
        using var db = NewDb();
        await SeedCoa(db);
        for (var m = 1; m <= 12; m++)
            await Periods(db).EnsureOpenAsync(new DateTime(2026, m, 1));   // 全 Open，一个都没结

        var r = await Close(db).YearCloseAsync(2026, "boss");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-405", r.Code);
    }

    // ───────────────────────── 锁年后自动过账 → E-FIN-404 ─────────────────────────

    [Fact]
    public async Task YearClosed_AutoPost_BlockedWithYearLockCode()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var cash = (await gl.GetByCodeAsync("1001"))!.Id;
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 10),   // 落在已锁财年
            Source = VoucherSource.Inventory,
            Lines = { new JournalLine { AccountId = cash, Debit = 50m }, new JournalLine { AccountId = bank, Credit = 50m } },
        };
        var r = await Jes(db).AutoPostAsync(e);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-404", r.Code);
    }

    // ───────────────────────── 锁年后手工过账 → E-FIN-404 ─────────────────────────

    [Fact]
    public async Task YearClosed_ManualPost_BlockedWithYearLockCode()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var cash = (await gl.GetByCodeAsync("1001"))!.Id;
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        var svc = Jes(db);
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 20),
            Source = VoucherSource.Manual,
            Lines = { new JournalLine { AccountId = cash, Debit = 10m }, new JournalLine { AccountId = bank, Credit = 10m } },
        };
        var id = await svc.CreateDraftAsync(e, "u1");
        await svc.SubmitForReviewAsync(id);
        var r = await svc.PostAsync(id, "u2");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-404", r.Code);
    }

    // ───────────────────────── 重复年结 → 幂等拒（不重记）─────────────────────────

    [Fact]
    public async Task YearClose_Twice_IdempotentRejectNoDuplicateVouchers()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 1000m);
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        var before = await db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Carryover);

        var r2 = await Close(db).YearCloseAsync(2026, "boss");
        Assert.False(r2.Ok);
        Assert.Equal("E-FIN-406", r2.Code);

        var after = await db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Carryover);
        Assert.Equal(before, after);   // 不重记
    }

    // ───────────────── 反年结：反向冲销（原凭证保持 Posted）+ 余额恢复 + 回 Closed + 可再过账 ─────────────────

    [Fact]
    public async Task ReopenYear_ReversesCarryoverAndUnlocks()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;
        var cash = (await gl.GetByCodeAsync("1001"))!.Id;
        var p3103 = (await gl.GetByCodeAsync("3103"))!.Id;
        var p3104 = (await gl.GetByCodeAsync("3104"))!.Id;
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2026, 3, 15), cogs, bank, 600m);
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        var r = await Close(db).ReopenYearAsync(2026, "boss");
        Assert.True(r.Ok, r.Code);

        // ★ 原两张年结凭证保持 Posted（不走 ReverseAsync），另投一张反向 Carryover 凭证 YC-2026-REOPEN
        Assert.Equal(JournalStatus.Posted,
            (await db.JournalEntries.SingleAsync(x => x.SourceDocNo == "YC-2026")).Status);
        Assert.Equal(JournalStatus.Posted,
            (await db.JournalEntries.SingleAsync(x => x.SourceDocNo == "YC-2026-RE")).Status);
        var reopen = await db.JournalEntries.SingleAsync(
            x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026-REOPEN");
        Assert.Equal(JournalStatus.Posted, reopen.Status);
        Assert.Equal(0, await db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Reversal));

        // ★ 余额恢复原值（多冲缺陷回归锁）：原+反向同计 → 损益回年结前，3103/3104 归零
        Assert.Equal(-1000m, await AcctNet(db, rev));    // 收入贷方净额恢复（非 -2000 翻倍）
        Assert.Equal(600m, await AcctNet(db, cogs));     // 费用借方净额恢复（非 +1200 翻倍）
        Assert.Equal(0m, await AcctNet(db, p3103));
        Assert.Equal(0m, await AcctNet(db, p3104));      // 无 +400 残值

        // 12 期回 Closed
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYear == 2026).ToListAsync();
        Assert.All(periods, p => Assert.Equal(PeriodStatus.Closed, p.Status));

        // 可再过账：某月反结账 → Open → 自动过账成功（不再 E-FIN-404）
        var feb = (await Periods(db).ResolveAsync(new DateTime(2026, 2, 1)))!;
        Assert.True((await Close(db).ReopenAsync(feb.Id, "boss")).Ok);
        var e = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 2, 10),
            Source = VoucherSource.Inventory,
            Lines = { new JournalLine { AccountId = cash, Debit = 20m }, new JournalLine { AccountId = bank, Credit = 20m } },
        };
        var pr = await Jes(db).AutoPostAsync(e);
        Assert.True(pr.Ok, pr.Code);
    }

    // ─────────────── 反年结 → 再年结：读到正确损益，利润不翻倍（多冲缺陷回归锁）───────────────

    [Fact]
    public async Task ReopenYear_ThenYearCloseAgain_ProfitNotDoubled()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;
        var p3103 = (await gl.GetByCodeAsync("3103"))!.Id;
        var p3104 = (await gl.GetByCodeAsync("3104"))!.Id;
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2026, 2, 15), cogs, bank, 600m);
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);
        Assert.True((await Close(db).ReopenYearAsync(2026, "boss")).Ok);

        var r = await Close(db).YearCloseAsync(2026, "boss");
        Assert.True(r.Ok, r.Code);

        // 再年结后：损益仍清零、3103 结平、3104 = 贷 400（非 800 翻倍）
        Assert.Equal(0m, await AcctNet(db, rev));
        Assert.Equal(0m, await AcctNet(db, cogs));
        Assert.Equal(0m, await AcctNet(db, p3103));
        Assert.Equal(-400m, await AcctNet(db, p3104));

        // 全年重新锁 YearClosed
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYear == 2026).ToListAsync();
        Assert.All(periods, p => Assert.Equal(PeriodStatus.YearClosed, p.Status));
    }

    [Fact]
    public async Task ReopenYear_NotYearClosed_Rejected()
    {
        using var db = NewDb();
        await SeedCoa(db);
        await CloseAll12(db, 2026);   // 仅月结，未年结
        var r = await Close(db).ReopenYearAsync(2026, "boss");
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-407", r.Code);
    }

    // ───────────────────────── 净亏向 ─────────────────────────

    [Fact]
    public async Task YearClose_NetLoss_DebitsProfitAndReducesRetained()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;
        var p3103 = (await gl.GetByCodeAsync("3103"))!.Id;
        var p3104 = (await gl.GetByCodeAsync("3104"))!.Id;

        // 收入 600 / 费用 1000 → 净亏 400
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 600m);
        await PostManual(db, new DateTime(2026, 2, 15), cogs, bank, 1000m);
        await CloseAll12(db, 2026);
        Assert.True((await Close(db).YearCloseAsync(2026, "boss")).Ok);

        var v1 = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026");
        Assert.Equal(400m, v1.Lines.Single(l => l.AccountId == p3103).Debit);   // 净亏 → 3103 借

        var v2 = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026-RE");
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3103).Credit);
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3104).Debit);   // 未分配利润被冲减

        Assert.Equal(0m, await AcctNet(db, rev));
        Assert.Equal(0m, await AcctNet(db, cogs));
        Assert.Equal(0m, await AcctNet(db, p3103));
        Assert.Equal(400m, await AcctNet(db, p3104));   // 借方净额（减少留存）
    }

    // ──────── 重试：v1 已投而 v2 未投（3103 残额）→ 再次年结补投 v2 并锁年（终审 Important#2）────────

    [Fact]
    public async Task YearClose_Retry_AfterV1PostedV2Missing_CompletesV2AndLocks()
    {
        using var db = NewDb();
        var gl = await SeedCoa(db);
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var cogs = (await gl.GetByCodeAsync("5001"))!.Id;
        var p3103 = (await gl.GetByCodeAsync("3103"))!.Id;
        var p3104 = (await gl.GetByCodeAsync("3104"))!.Id;

        // 正常损益：收入 1000 / 费用 600 → 净利 400
        await PostManual(db, new DateTime(2026, 1, 15), bank, rev, 1000m);
        await PostManual(db, new DateTime(2026, 2, 15), cogs, bank, 600m);

        // 模拟「v1 已投而 v2 失败未投」的残态：手工 AutoPost 一张 YC-2026 结转凭证
        //（损益逐科目清零 → 净利 400 入 3103 贷），但不投 3103→3104 的 v2。
        var v1 = new JournalEntry
        {
            VoucherDate = new DateTime(2026, 12, 31),
            Source = VoucherSource.Carryover,
            SourceDocNo = "YC-2026",
            Description = "模拟 v1 已投（v2 缺投）",
            Lines =
            {
                new JournalLine { AccountId = rev, Debit = 1000m },
                new JournalLine { AccountId = cogs, Credit = 600m },
                new JournalLine { AccountId = p3103, Credit = 400m },
            },
        };
        Assert.True((await Jes(db).AutoPostAsync(v1)).Ok);

        await CloseAll12(db, 2026);

        // 前提确认：损益已清零（balances 空）、3103 残贷 400、v2 未投、年未锁
        Assert.Equal(0m, await AcctNet(db, rev));
        Assert.Equal(0m, await AcctNet(db, cogs));
        Assert.Equal(-400m, await AcctNet(db, p3103));   // 3103 残额（全年利润残死）
        Assert.Equal(0m, await AcctNet(db, p3104));

        // 重试年结：不再走「空财年仅锁年」死锁分支，而是补投 v2（3103→3104）再锁年
        var r = await Close(db).YearCloseAsync(2026, "boss");
        Assert.True(r.Ok, r.Code);

        // 补投的 v2（YC-2026-RE）：3103 借 400 / 3104 贷 400
        var v2 = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.Source == VoucherSource.Carryover && x.SourceDocNo == "YC-2026-RE");
        Assert.Equal(JournalStatus.Posted, v2.Status);
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3103).Debit);
        Assert.Equal(400m, v2.Lines.Single(l => l.AccountId == p3104).Credit);

        // 3103 结平、3104 = 贷 400（净利落定未分配利润）
        Assert.Equal(0m, await AcctNet(db, p3103));
        Assert.Equal(-400m, await AcctNet(db, p3104));

        // 全年 12 期锁 YearClosed
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYear == 2026).ToListAsync();
        Assert.All(periods, p => Assert.Equal(PeriodStatus.YearClosed, p.Status));
    }

    // ───────────────────────── 空财年：无损益 → 不产生凭证仍锁年 ─────────────────────────

    [Fact]
    public async Task YearClose_EmptyYear_LocksWithoutVouchers()
    {
        using var db = NewDb();
        await SeedCoa(db);
        await CloseAll12(db, 2026);   // 无任何损益凭证

        var r = await Close(db).YearCloseAsync(2026, "boss");
        Assert.True(r.Ok, r.Code);

        Assert.Equal(0, await db.JournalEntries.CountAsync(x => x.Source == VoucherSource.Carryover));
        var periods = await db.FiscalPeriods.Where(p => p.FiscalYear == 2026).ToListAsync();
        Assert.All(periods, p => Assert.Equal(PeriodStatus.YearClosed, p.Status));
    }
}

using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// F1 財務油路 波C.3 — 成本差异结转 COGS（用户拍板②）。
///
/// 拍板②：完工结转时，实际成本与标准成本的差异并入当期损益，转 COGS（不设留存差异科目、不分摊、月末科目自然清零）。
/// SettleAsync 三腿：①料工费→WIP（借WIP 实际全额）②WIP→FG（借FG 按 <b>标准成本</b>）③差异→COGS。
/// 差异 = TotalActual − StandardCost：&gt;0 超支 借COGS/贷WIP；&lt;0 有利 借WIP/贷COGS(|差异|)。
/// 三腿合计 WIP 借贷净零：借WIP(actual) − 贷WIP(standard→FG) ∓ 差异腿 = 0。
///
/// 守卫：StandardCost&lt;=0（未定义标准成本）或差异=0 → 维持现状（FG 按实际全额、无差异凭证）。
/// 幂等：差异凭证与 #WIP/#FG 同一 Settle 事务，靠 CostSheet.Status（Settled→E-FIN-402）防重放重记。
/// </summary>
public class CostVarianceSettleTests
{
    private static async Task<(CP6.Core.EFDbContext.CP6Context db, CostSettleService svc)> SetupAsync(
        Action<CostSheet> configure)
    {
        var db = TestHelper.CreateInMemoryContext();
        // 直接 seed 科目角色（GlAccount 默认 IsLeaf=true；RoleIdAsync 只查 Role+IsActive）
        foreach (var role in new[] { "WIP", "FG", "INVENTORY", "DIRECT_LABOR", "MFG_OVERHEAD", "COGS" })
            db.GlAccounts.Add(new GlAccount { Code = role, Name = role, Role = role, IsActive = true });

        var sheet = new CostSheet
        {
            Id = Guid.NewGuid(), No = "CS-VAR", WorkOrderNo = "WOV", CompletedQty = 10,
            Status = CostSheetStatus.Collected,
        };
        configure(sheet);
        db.CostSheets.Add(sheet);
        await db.SaveChangesAsync();

        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        return (db, new CostSettleService(db, journal));
    }

    private static async Task<Guid> AccIdAsync(CP6.Core.EFDbContext.CP6Context db, string role)
        => (await db.GlAccounts.FirstAsync(a => a.Role == role)).Id;

    // ── 场景1：实际 > 标准（超支）→ 差异凭证 借 COGS / 贷 WIP；FG 按标准；WIP 净零 ──
    [Fact]
    public async Task Settle_ActualOverStandard_PostsVarianceDebitCogs_FgAtStandard_WipNetsZero()
    {
        var (db, svc) = await SetupAsync(s =>
        {
            s.MaterialActual = 600; s.MaterialStandard = 500;   // 料超支 100
            s.LaborActual = 200; s.LaborStandard = 200;
            s.OverheadActual = 100; s.OverheadStandard = 100;
        });

        var r = await svc.SettleAsync("WOV", "u");
        Assert.True(r.Ok, r.Code);

        var wip = await AccIdAsync(db, "WIP");
        var fg = await AccIdAsync(db, "FG");
        var cogs = await AccIdAsync(db, "COGS");

        // ① 料工费 → WIP：借 WIP 实际全额 900
        var collect = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#WIP"));
        Assert.Equal(900m, collect.Lines.Single(l => l.AccountId == wip).Debit);

        // ② WIP → FG：借 FG 按标准 800 / 贷 WIP 800
        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(800m, settle.Lines.Single(l => l.AccountId == fg).Debit);
        Assert.Equal(800m, settle.Lines.Single(l => l.AccountId == wip).Credit);

        // ③ 差异 → COGS：超支 借 COGS 100 / 贷 WIP 100
        var varv = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#VAR"));
        Assert.Equal(VoucherSource.Cost, varv.Source);
        Assert.Equal(100m, varv.Lines.Single(l => l.AccountId == cogs).Debit);
        Assert.Equal(100m, varv.Lines.Single(l => l.AccountId == wip).Credit);

        // WIP 借贷净零（三腿合计）
        var wipLines = await db.JournalLines.Where(l => l.AccountId == wip).ToListAsync();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));
    }

    // ── 场景2：实际 < 标准（有利）→ 差异凭证反向 借 WIP / 贷 COGS；FG 按标准；WIP 净零 ──
    [Fact]
    public async Task Settle_ActualUnderStandard_PostsVarianceCreditCogs_FgAtStandard_WipNetsZero()
    {
        var (db, svc) = await SetupAsync(s =>
        {
            s.MaterialActual = 400; s.MaterialStandard = 500;   // 料节约 100
            s.LaborActual = 200; s.LaborStandard = 200;
            s.OverheadActual = 100; s.OverheadStandard = 100;
        });

        var r = await svc.SettleAsync("WOV", "u");
        Assert.True(r.Ok, r.Code);

        var wip = await AccIdAsync(db, "WIP");
        var fg = await AccIdAsync(db, "FG");
        var cogs = await AccIdAsync(db, "COGS");

        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(800m, settle.Lines.Single(l => l.AccountId == fg).Debit);   // FG 按标准 800

        // 有利差异 100：借 WIP 100 / 贷 COGS 100（COGS 冲减）
        var varv = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#VAR"));
        Assert.Equal(100m, varv.Lines.Single(l => l.AccountId == wip).Debit);
        Assert.Equal(100m, varv.Lines.Single(l => l.AccountId == cogs).Credit);

        var wipLines = await db.JournalLines.Where(l => l.AccountId == wip).ToListAsync();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));   // 净零
    }

    // ── 场景3：实际 == 标准 → 无差异凭证；FG 按标准(=实际) ──
    [Fact]
    public async Task Settle_ActualEqualsStandard_NoVarianceVoucher()
    {
        var (db, svc) = await SetupAsync(s =>
        {
            s.MaterialActual = 500; s.MaterialStandard = 500;
            s.LaborActual = 200; s.LaborStandard = 200;
            s.OverheadActual = 100; s.OverheadStandard = 100;
        });

        var r = await svc.SettleAsync("WOV", "u");
        Assert.True(r.Ok, r.Code);

        Assert.False(await db.JournalEntries.AnyAsync(e => e.SourceDocNo!.EndsWith("#VAR")));

        var fg = await AccIdAsync(db, "FG");
        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(800m, settle.Lines.Single(l => l.AccountId == fg).Debit);
    }

    // ── 场景4：无标准成本（StandardCost<=0）→ 维持现状：FG 按实际全额、无差异凭证 ──
    [Fact]
    public async Task Settle_NoStandardCost_KeepsActualFullAmount_NoVarianceVoucher()
    {
        var (db, svc) = await SetupAsync(s =>
        {
            s.MaterialActual = 500; s.MaterialStandard = 0;   // 未定义标准
            s.LaborActual = 0; s.LaborStandard = 0;
            s.OverheadActual = 0; s.OverheadStandard = 0;
        });

        var r = await svc.SettleAsync("WOV", "u");
        Assert.True(r.Ok, r.Code);

        Assert.False(await db.JournalEntries.AnyAsync(e => e.SourceDocNo!.EndsWith("#VAR")));

        var fg = await AccIdAsync(db, "FG");
        var settle = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceDocNo!.EndsWith("#FG"));
        Assert.Equal(500m, settle.Lines.Single(l => l.AccountId == fg).Debit);   // 实际全额
    }

    // ── 场景5：重放不重记（Status=Settled → E-FIN-402，差异凭证仍只 1 张）──
    [Fact]
    public async Task Settle_Replay_DoesNotDuplicateVarianceVoucher()
    {
        var (db, svc) = await SetupAsync(s =>
        {
            s.MaterialActual = 600; s.MaterialStandard = 500;
            s.LaborActual = 200; s.LaborStandard = 200;
            s.OverheadActual = 100; s.OverheadStandard = 100;
        });

        var r1 = await svc.SettleAsync("WOV", "u");
        Assert.True(r1.Ok, r1.Code);
        var r2 = await svc.SettleAsync("WOV", "u");   // 重放
        Assert.False(r2.Ok);
        Assert.Equal("E-FIN-402", r2.Code);

        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.SourceDocNo!.EndsWith("#VAR")));
        Assert.Equal(3, await db.JournalEntries.CountAsync(j => j.Source == VoucherSource.Cost));   // #WIP + #FG + #VAR
    }
}

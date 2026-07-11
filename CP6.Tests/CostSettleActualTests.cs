using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests;

/// <summary>
/// A2 §4.5（Task D-3）→ 波C.3 拍板②更新：料工费归集贷【实际额】不变，但 WIP→FG 改按【标准成本】资本化，
/// 实际与标准的差异结转 COGS（拍板②：差异并入当期损益转 COGS，科目月末自然清零）。
/// ①归集：贷 DIRECT_LABOR = LaborActual、贷 MFG_OVERHEAD = OverheadActual（实际额，不变）；
/// ②WIP→FG = StandardCost（标准料+标准工+标准费）；③差异 = TotalActual−StandardCost 转 COGS。
/// 场景：实际 1560（料1000/工320/费240）vs 标准 1700（料1000/工400/费300）→ 有利差异 −140 → 借WIP140/贷COGS140。
/// </summary>
public class CostSettleActualTests
{
    [Fact]
    public async Task Settle_CreditsActualLaborOverhead_FgAtStandard_VarianceToCogs()
    {
        var db = TestHelper.CreateInMemoryContext();
        // 科目角色（直接 seed：GlAccount 默认 IsLeaf=true，RoleIdAsync 只查 Role+IsActive，过账校验要求末级/启用）
        foreach (var role in new[] { "WIP", "FG", "INVENTORY", "DIRECT_LABOR", "MFG_OVERHEAD", "COGS" })
            db.GlAccounts.Add(new GlAccount { Code = role, Name = role, Role = role, IsActive = true });
        db.CostSheets.Add(new CostSheet
        {
            Id = Guid.NewGuid(), No = "CS-1", WorkOrderNo = "WO1", CompletedQty = 100,
            MaterialActual = 1000, MaterialStandard = 1000, LaborActual = 320, LaborStandard = 400,
            OverheadActual = 240, OverheadStandard = 300, Status = CostSheetStatus.Collected,
        });
        await db.SaveChangesAsync();

        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var svc = new CostSettleService(db, journal);
        var r = await svc.SettleAsync("WO1", "admin");
        Assert.True(r.Ok, r.Code);

        var lines = await db.JournalLines.AsNoTracking().ToListAsync();
        Assert.Contains(lines, l => l.Credit == 320m);   // 归集贷 DIRECT_LABOR 实际额（非标准 400）
        Assert.Contains(lines, l => l.Credit == 240m);   // 归集贷 MFG_OVERHEAD 实际额（非标准 300）

        // WIP→FG 按标准总成本 StandardCost = 1000 + 400 + 300 = 1700（非实际 1560）
        var fgId = (await db.GlAccounts.FirstAsync(a => a.Role == "FG")).Id;
        Assert.Contains(lines, l => l.AccountId == fgId && l.Debit == 1700m);

        // 差异 = 1560 − 1700 = −140（有利）→ 借 WIP 140 / 贷 COGS 140
        var cogsId = (await db.GlAccounts.FirstAsync(a => a.Role == "COGS")).Id;
        var wipId = (await db.GlAccounts.FirstAsync(a => a.Role == "WIP")).Id;
        Assert.Contains(lines, l => l.AccountId == cogsId && l.Credit == 140m);
        Assert.Contains(lines, l => l.AccountId == wipId && l.Debit == 140m);

        // WIP 借贷净零（三腿合计）
        var wipLines = lines.Where(l => l.AccountId == wipId).ToList();
        Assert.Equal(wipLines.Sum(l => l.Debit), wipLines.Sum(l => l.Credit));
    }
}

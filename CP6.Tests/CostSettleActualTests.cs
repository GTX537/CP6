using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests;

/// <summary>
/// A2 §4.5（Task D-3）：完工结转贷"实际额"。
/// SettleAsync 必须按真实成本入账：贷 DIRECT_LABOR = LaborActual、贷 MFG_OVERHEAD = OverheadActual，
/// WIP→FG 按 TotalActual（料+工+费实际）。差异为展示用，绝不生成差异凭证。
/// </summary>
public class CostSettleActualTests
{
    [Fact]
    public async Task Settle_CreditsActualLaborOverhead()
    {
        var db = TestHelper.CreateInMemoryContext();
        // 科目角色（直接 seed：GlAccount 默认 IsLeaf=true，RoleIdAsync 只查 Role+IsActive，过账校验要求末级/启用）
        foreach (var role in new[] { "WIP", "FG", "INVENTORY", "DIRECT_LABOR", "MFG_OVERHEAD" })
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
        Assert.Contains(lines, l => l.Credit == 320m);   // DIRECT_LABOR 实际额（非标准 400）
        Assert.Contains(lines, l => l.Credit == 240m);   // MFG_OVERHEAD 实际额（非标准 300）

        // WIP→FG 按实际总成本 TotalActual = 1000 + 320 + 240 = 1560
        var fgId = (await db.GlAccounts.FirstAsync(a => a.Role == "FG")).Id;
        Assert.Contains(lines, l => l.AccountId == fgId && l.Debit == 1560m);

        // 差异展示用：不得有任何标准额（400/300）落入凭证行
        Assert.DoesNotContain(lines, l => l.Credit == 400m || l.Credit == 300m);
    }
}

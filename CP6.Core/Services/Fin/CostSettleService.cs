using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本完工结转实现（章06 §5）。reversing 无关——结转是真实成本资本化：
/// ①料工费→WIP（借 WIP / 贷 原材料+直接人工+制造费用，applied-cost 吸收法，标准工费吸收入 WIP）；
/// ②WIP→FG（借 库存商品 / 贷 在制品）。出货时既有 AR.Cogs（借 COGS/贷 FG）再把 FG 转 COGS。
/// 全按 Role 取科目（模板可移植）。错误码：402 已结转 / 403 成本单不存在或未归集 / 141 角色缺失。
/// </summary>
public class CostSettleService : ICostSettleService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;

    public CostSettleService(CP6Context db, IJournalEntryService journal)
    {
        _db = db;
        _journal = journal;
    }

    public async Task<FinResult> SettleAsync(string workOrderNo, string user)
    {
        var sheet = await _db.CostSheets.FirstOrDefaultAsync(s => s.WorkOrderNo == workOrderNo);
        if (sheet == null || sheet.Status == CostSheetStatus.Draft) return FinResult.Fail("E-FIN-403");
        if (sheet.Status == CostSheetStatus.Settled) return FinResult.Fail("E-FIN-402");

        var total = sheet.TotalActual;
        if (total <= 0m)
        {
            sheet.Status = CostSheetStatus.Settled;          // 无成本可结转，仅置状态
            sheet.Modifier = user;
            sheet.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
            return FinResult.Pass();
        }

        var wip = await RoleIdAsync("WIP");
        var fg = await RoleIdAsync("FG");
        if (wip == null) return FinResult.Fail("E-FIN-141", "WIP");
        if (fg == null) return FinResult.Fail("E-FIN-141", "FG");

        var now = DateTime.Now;

        // ① 料工费 → WIP（借 WIP / 贷 各成本来源）
        var collect = new JournalEntry
        {
            VoucherDate = now,
            Source = VoucherSource.Cost,
            SourceDocNo = $"{sheet.No}#WIP",
            Description = $"成本归集→在制品 {workOrderNo}",
            Lines = { new JournalLine { AccountId = wip.Value, Debit = total } },
        };
        if (sheet.MaterialActual > 0m)
        {
            var inv = await RoleIdAsync("INVENTORY");
            if (inv == null) return FinResult.Fail("E-FIN-141", "INVENTORY");
            collect.Lines.Add(new JournalLine { AccountId = inv.Value, Credit = sheet.MaterialActual });
        }
        if (sheet.LaborActual > 0m)
        {
            var lab = await RoleIdAsync("DIRECT_LABOR");
            if (lab == null) return FinResult.Fail("E-FIN-141", "DIRECT_LABOR");
            collect.Lines.Add(new JournalLine { AccountId = lab.Value, Credit = sheet.LaborActual });
        }
        if (sheet.OverheadActual > 0m)
        {
            var oh = await RoleIdAsync("MFG_OVERHEAD");
            if (oh == null) return FinResult.Fail("E-FIN-141", "MFG_OVERHEAD");
            collect.Lines.Add(new JournalLine { AccountId = oh.Value, Credit = sheet.OverheadActual });
        }
        var r1 = await _journal.AutoPostAsync(collect);
        if (!r1.Ok) return r1;

        // ② WIP → FG（借 库存商品 / 贷 在制品）
        var settle = new JournalEntry
        {
            VoucherDate = now,
            Source = VoucherSource.Cost,
            SourceDocNo = $"{sheet.No}#FG",
            Description = $"在制品→库存商品 {workOrderNo}",
            Lines =
            {
                new JournalLine { AccountId = fg.Value, Debit = total },
                new JournalLine { AccountId = wip.Value, Credit = total },
            },
        };
        var r2 = await _journal.AutoPostAsync(settle);
        if (!r2.Ok) return r2;

        sheet.Status = CostSheetStatus.Settled;
        sheet.JournalEntryId = settle.Id;
        sheet.Modifier = user;
        sheet.ModifyDate = now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<decimal> FgUnitCostAsync(string workOrderNo)
        => (await _db.CostSheets.AsNoTracking().FirstOrDefaultAsync(s => s.WorkOrderNo == workOrderNo))?.FgUnitCost ?? 0m;

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;
}

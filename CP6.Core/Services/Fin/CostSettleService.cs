using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本完工结转实现（章06 §5）。reversing 无关——结转是真实成本资本化：
/// ①料工费→WIP（借 WIP / 贷 原材料+直接人工+制造费用，applied-cost 吸收法，标准工费吸收入 WIP）；
/// ②WIP→FG（借 库存商品 / 贷 在制品；有标准成本时 FG 按标准资本化）；
/// ③差异→COGS（拍板②：实际−标准 并入当期损益转 COGS，超支借COGS/贷WIP、有利反向；科目月末自然清零，不设留存差异科目/不分摊）。
/// 出货时既有 AR.Cogs（借 COGS/贷 FG）再把 FG 转 COGS。
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

        // 拍板②：有标准成本时 FG 按【标准成本】资本化，实际与标准的差异结转 COGS（科目月末自然清零）。
        // 无标准成本（StandardCost<=0，未定义标准的品目）或差异=0 → 维持现状：FG 按实际全额、无差异凭证。
        var standardCost = sheet.StandardCost;          // 标准料+标准工+标准费
        var hasStandard = standardCost > 0m;
        var variance = total - standardCost;            // >0 超支 / <0 有利
        var fgAmount = hasStandard ? standardCost : total;

        // 差异腿要用的 COGS 前置解析：缺配置整体 fail 在任何过账前（避免 ①② 已提交后才 141，重试重复凭证）
        Guid? cogs = null;
        if (hasStandard && variance != 0m)
        {
            cogs = await RoleIdAsync("COGS");
            if (cogs == null) return FinResult.Fail("E-FIN-141", "COGS");
        }

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

        // ② WIP → FG（借 库存商品 / 贷 在制品；有标准成本时按标准，差异走 ③）
        var settle = new JournalEntry
        {
            VoucherDate = now,
            Source = VoucherSource.Cost,
            SourceDocNo = $"{sheet.No}#FG",
            Description = $"在制品→库存商品 {workOrderNo}",
            Lines =
            {
                new JournalLine { AccountId = fg.Value, Debit = fgAmount },
                new JournalLine { AccountId = wip.Value, Credit = fgAmount },
            },
        };
        var r2 = await _journal.AutoPostAsync(settle);
        if (!r2.Ok) return r2;

        // ③ 成本差异结转 COGS（拍板②）：仅当有标准成本且差异非零（cogs 已前置解析）。
        //    超支(variance>0) 借 COGS / 贷 WIP；有利(variance<0) 反向 借 WIP / 贷 COGS（|差异|）。
        //    三腿合计 WIP 借贷净零：借WIP(actual) − 贷WIP(standard→FG) ∓ 差异腿 = 0。
        if (cogs is Guid cogsId)
        {
            var varEntry = new JournalEntry
            {
                VoucherDate = now,
                Source = VoucherSource.Cost,
                SourceDocNo = $"{sheet.No}#VAR",
                Description = $"成本差异结转 COGS {workOrderNo}",
            };
            if (variance > 0m)
            {
                varEntry.Lines.Add(new JournalLine { AccountId = cogsId, Debit = variance });
                varEntry.Lines.Add(new JournalLine { AccountId = wip.Value, Credit = variance });
            }
            else
            {
                var abs = -variance;
                varEntry.Lines.Add(new JournalLine { AccountId = wip.Value, Debit = abs });
                varEntry.Lines.Add(new JournalLine { AccountId = cogsId, Credit = abs });
            }
            var r3 = await _journal.AutoPostAsync(varEntry);
            if (!r3.Ok) return r3;
        }

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

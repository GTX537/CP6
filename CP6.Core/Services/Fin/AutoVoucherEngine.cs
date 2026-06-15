using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 自动凭证引擎实现（章05 §3/§4）。
/// 错误码：140 规则未找到 / 141 科目角色解析失败（无 Role 科目且无兜底）。
/// 借贷恒等 / 锁期 / 采番 / maker-checker 全由 <see cref="IJournalEntryService.AutoPostAsync"/> 兜底（双保险）。
/// </summary>
public class AutoVoucherEngine : IAutoVoucherEngine
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;

    public AutoVoucherEngine(CP6Context db, IJournalEntryService journal)
    {
        _db = db;
        _journal = journal;
    }

    public async Task<FinResult> GenerateAsync(FinBizEvent evt)
    {
        // —— Step 1 幂等：同来源单据已过账则跳过（自动凭证才判，手工不参与）——
        if (evt.Source != VoucherSource.Manual && !string.IsNullOrEmpty(evt.SourceDocNo))
        {
            var exists = await _db.JournalEntries.AnyAsync(j =>
                j.Source == evt.Source && j.SourceDocNo == evt.SourceDocNo && j.Status == JournalStatus.Posted);
            if (exists) return FinResult.Pass();   // 已生成，幂等跳过
        }

        // —— Step 2 找规则（按事件类型 + 启用）——
        var rule = await _db.PostingRules
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.EventType == evt.EventType && r.IsActive);
        if (rule == null) return FinResult.Fail("E-FIN-140", evt.EventType);

        // —— Step 3 拼凭证 ——
        var entry = new JournalEntry
        {
            VoucherDate = evt.BizDate,
            Source = evt.Source,
            SourceDocNo = evt.SourceDocNo,
            Description = evt.Description,
        };

        var rate = evt.EffectiveRate;          // 本位币=1，外币取记账汇率（F2-D1）
        var isFx = !evt.IsBaseCurrency;

        foreach (var rl in rule.Lines.OrderBy(l => l.LineNo))
        {
            if (rl.Source == RuleLineSource.FixedRole)
            {
                var accId = await ResolveRoleAsync(rl.AccountRole) ?? rl.FallbackAccountId;
                if (accId is not Guid aid) return FinResult.Fail("E-FIN-141", rl.AccountRole ?? "");

                var orig = evt.GetAmount(rl.AmountField);
                if (orig == 0m) continue;       // 0 额跳过（如本位币无独立进项税行）
                entry.Lines.Add(BuildLine(rl, aid, Round(orig * rate), orig, evt, isFx, evt.CostCenterId));
            }
            else // DocumentLines：按 科目 + 成本中心 分组炸开单据行
            {
                var groups = evt.DocLines
                    .Select(dl => new
                    {
                        Acc = dl.GetGuid(rl.LineAccountField),
                        Cc = rl.CarryCostCenter ? dl.CostCenterId : (Guid?)null,
                        Amt = dl.GetAmount(rl.LineAmountField),
                    })
                    .Where(x => x.Acc is Guid)
                    .GroupBy(x => new { x.Acc, x.Cc });

                foreach (var g in groups)
                {
                    var orig = g.Sum(x => x.Amt);
                    if (orig == 0m) continue;
                    entry.Lines.Add(BuildLine(rl, g.Key.Acc!.Value, Round(orig * rate), orig, evt, isFx, g.Key.Cc));
                }
            }
        }

        // —— Step 4 直过（AutoPostAsync 内含借贷恒等 + 锁期，配错的规则在此被拦）——
        return await _journal.AutoPostAsync(entry);
    }

    /// <summary>按角色锚点取启用科目 Id（自动凭证换模板包零改动）。</summary>
    private async Task<Guid?> ResolveRoleAsync(string? role)
    {
        if (string.IsNullOrEmpty(role)) return null;
        var acc = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive);
        return acc?.Id;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>拼一条分录：本位币入 Debit/Credit；外币把原币/汇率/原币额带上（JournalLine 现成列）。</summary>
    private static JournalLine BuildLine(
        PostingRuleLine rl, Guid accountId, decimal baseAmt, decimal origAmt,
        FinBizEvent evt, bool isFx, Guid? costCenterId)
    {
        var line = new JournalLine
        {
            AccountId = accountId,
            Debit = rl.Side == PostingSide.Debit ? baseAmt : 0m,
            Credit = rl.Side == PostingSide.Credit ? baseAmt : 0m,
            PartnerId = rl.CarryPartner ? evt.PartnerId : null,
            CostCenterId = rl.CarryCostCenter ? costCenterId : null,
        };
        if (isFx)
        {
            line.CurrencyCd = evt.CurrencyCd;
            line.FxRate = evt.EffectiveRate;
            line.OrigAmount = origAmt;
        }
        return line;
    }
}

using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 期末未实现汇兑重估实现（章07 §4）。reversing 法：本期末按期末汇率重估未结外币 AP/AR，
/// 生成重估凭证；下期初借贷对调冲回。两张凭证全期累计净额为零，故子账↔GL 勾稽（按全量汇总）不受影响，
/// 仅把未实现汇兑损益落进本期损益。错误码：140 期间不存在 / 141 科目角色缺失。
/// </summary>
public class FxRevaluationService : IFxRevaluationService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;
    private readonly IFxRateService _fx;
    private readonly ILogger<FxRevaluationService>? _logger;
    private const decimal Eps = 0.0001m;

    public FxRevaluationService(CP6Context db, IJournalEntryService journal, IFxRateService fx,
        ILogger<FxRevaluationService>? logger = null)
    {
        _db = db;
        _journal = journal;
        _fx = fx;
        _logger = logger;
    }

    public async Task<FinResult> RevalueAsync(Guid periodId, string user)
    {
        var p = await _db.FiscalPeriods.FindAsync(periodId);
        if (p == null) return FinResult.Fail("E-FIN-140");

        var docNo = $"FXREVAL-{p.Year:D4}{p.Month:D2}";
        // 幂等：本期已重估过则跳过（反结账后再结账不重复重估）
        if (await _db.JournalEntries.AnyAsync(e => e.SourceDocNo == docNo)) return FinResult.Pass();

        var asOf = p.PeriodEnd.Date;
        var rateCache = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);

        var apAcc = await RoleIdAsync("AP_CONTROL");
        var arAcc = await RoleIdAsync("AR_CONTROL");
        var gainAcc = await RoleIdAsync("FX_GAIN");
        var lossAcc = await RoleIdAsync("FX_LOSS");

        var lines = new List<JournalLine>();
        decimal totalGain = 0m, totalLoss = 0m;

        // ── AP：未结外币应付，账面负债 book→market 的变化量 deltaAp ──
        var apOpen = await _db.ApInvoices
            .Where(i => i.CurrencyCd != null
                && (i.Status == ApInvoiceStatus.Posted || i.Status == ApInvoiceStatus.PartiallySettled))
            .ToListAsync();
        foreach (var inv in apOpen)
        {
            var delta = await DeltaAsync(inv.CurrencyCd, inv.FxRate, inv.GrossAmount - inv.SettledAmount, inv.IsCreditMemo, rateCache, asOf);
            if (delta is not decimal deltaAp || deltaAp == 0m) continue;
            if (apAcc == null) return FinResult.Fail("E-FIN-141", "AP_CONTROL");
            if (deltaAp > 0m)   // 负债增加 → 贷应付 / 借汇兑损失（未实现损失）
            {
                lines.Add(Line(apAcc.Value, credit: deltaAp, partner: inv.SupplierId));
                totalLoss += deltaAp;
            }
            else                // 负债减少 → 借应付 / 贷汇兑收益（未实现收益）
            {
                lines.Add(Line(apAcc.Value, debit: -deltaAp, partner: inv.SupplierId));
                totalGain += -deltaAp;
            }
        }

        // ── AR：未结外币应收，账面资产 book→market 的变化量 deltaAr ──
        var arOpen = await _db.ArInvoices
            .Where(i => i.CurrencyCd != null
                && (i.Status == ArInvoiceStatus.Posted || i.Status == ArInvoiceStatus.PartiallySettled))
            .ToListAsync();
        foreach (var inv in arOpen)
        {
            var delta = await DeltaAsync(inv.CurrencyCd, inv.FxRate, inv.GrossAmount - inv.SettledAmount, inv.IsCreditMemo, rateCache, asOf);
            if (delta is not decimal deltaAr || deltaAr == 0m) continue;
            if (arAcc == null) return FinResult.Fail("E-FIN-141", "AR_CONTROL");
            if (deltaAr > 0m)   // 资产增加 → 借应收 / 贷汇兑收益（未实现收益）
            {
                lines.Add(Line(arAcc.Value, debit: deltaAr, partner: inv.CustomerId));
                totalGain += deltaAr;
            }
            else                // 资产减少 → 贷应收 / 借汇兑损失（未实现损失）
            {
                lines.Add(Line(arAcc.Value, credit: -deltaAr, partner: inv.CustomerId));
                totalLoss += -deltaAr;
            }
        }

        if (lines.Count == 0) return FinResult.Pass();   // 无外币敞口，不生成凭证

        // 汇总损益行，保持借贷恒等
        if (totalGain > 0m)
        {
            if (gainAcc == null) return FinResult.Fail("E-FIN-141", "FX_GAIN");
            lines.Add(Line(gainAcc.Value, credit: totalGain));
        }
        if (totalLoss > 0m)
        {
            if (lossAcc == null) return FinResult.Fail("E-FIN-141", "FX_LOSS");
            lines.Add(Line(lossAcc.Value, debit: totalLoss));
        }

        // ① 重估凭证（本期末）
        var reval = new JournalEntry
        {
            VoucherDate = asOf,
            Source = VoucherSource.FxReval,
            SourceDocNo = docNo,
            Description = $"期末未实现汇兑重估 {p.Year}-{p.Month:D2}",
            Lines = lines,
        };
        var r1 = await _journal.AutoPostAsync(reval);
        if (!r1.Ok) return r1;

        // ② 冲回凭证（下期初，借贷对调）—— 避免与结算已实现汇差重复计
        var reversal = new JournalEntry
        {
            VoucherDate = asOf.AddDays(1),
            Source = VoucherSource.FxReval,
            SourceDocNo = $"{docNo}-REV",
            Description = $"汇兑重估冲回 {p.Year}-{p.Month:D2}",
            Lines = lines.Select(l => new JournalLine
            {
                AccountId = l.AccountId,
                Debit = l.Credit,
                Credit = l.Debit,
                PartnerId = l.PartnerId,
            }).ToList(),
        };
        var r2 = await _journal.AutoPostAsync(reversal);
        if (!r2.Ok) return r2;

        return FinResult.Pass();
    }

    /// <summary>
    /// 账面本位币 book→market 的变化量（signed，红字反向）。
    /// 返回 null 表示跳过（本位币 / 原币开口≤0 / 期末汇率未登记）。
    /// </summary>
    private async Task<decimal?> DeltaAsync(string? ccy, decimal frozenRate, decimal origOpen, bool isCreditMemo,
        Dictionary<string, decimal?> cache, DateTime asOf)
    {
        if (FxConstants.IsBase(ccy)) return null;          // 本位币(JPY/空)不重估
        if (origOpen <= 0m) return null;                    // 已结清或无开口

        if (!cache.TryGetValue(ccy!, out var endRate))
        {
            endRate = await _fx.ResolveRateAsync(ccy!, asOf);
            cache[ccy!] = endRate;
        }
        if (endRate is not decimal er || er <= 0m)
        {
            _logger?.LogWarning("期末汇兑重估跳过：币种 {Ccy} 在 {AsOf:yyyy-MM-dd} 无登记汇率", ccy, asOf);
            return null;
        }

        var sign = isCreditMemo ? -1m : 1m;
        var book = Math.Round(sign * origOpen * frozenRate, 2, MidpointRounding.AwayFromZero);
        var market = Math.Round(sign * origOpen * er, 2, MidpointRounding.AwayFromZero);
        var delta = market - book;
        return Math.Abs(delta) < Eps ? 0m : delta;
    }

    private static JournalLine Line(Guid accountId, decimal debit = 0m, decimal credit = 0m, string? partner = null)
        => new() { AccountId = accountId, Debit = debit, Credit = credit, PartnerId = partner };

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;
}

using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付核销服务实现（章03 §3③/§4）。
/// 错误码：202 发票不存在 / 211 付款不存在 / 212 仅已过账付款可核销 / 220 超付款余额 /
/// 221 供应商不一致 / 222 发票超额核销 / 223 有折扣须给折扣科目 / 224 发票须已过账/部分核销 / 141 科目角色缺失。
/// </summary>
public class ApSettlementService : IApSettlementService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;
    private const decimal Eps = 0.0001m;

    public ApSettlementService(CP6Context db, IJournalEntryService journal)
    {
        _db = db;
        _journal = journal;
    }

    public async Task<FinResult> SettleAsync(Guid paymentId, IReadOnlyList<SettlementApply> applies, string user)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null) return FinResult.Fail("E-FIN-211");
        if (payment.Status != PaymentStatus.Posted) return FinResult.Fail("E-FIN-212");

        var unapplied = payment.Amount - payment.SettledAmount;
        if (applies.Sum(a => a.AppliedAmount) > unapplied + Eps) return FinResult.Fail("E-FIN-220");

        var apAcc = await RoleIdAsync("AP_CONTROL");
        if (apAcc == null) return FinResult.Fail("E-FIN-141", "AP_CONTROL");

        var now = DateTime.Now;
        foreach (var a in applies)
        {
            var inv = await _db.ApInvoices.FirstOrDefaultAsync(i => i.Id == a.InvoiceId);
            if (inv == null) return FinResult.Fail("E-FIN-202");
            if (inv.Status != ApInvoiceStatus.Posted && inv.Status != ApInvoiceStatus.PartiallySettled)
                return FinResult.Fail("E-FIN-224");
            if (inv.SupplierId != payment.SupplierId) return FinResult.Fail("E-FIN-221");
            if (a.DiscountAmount != 0m && a.DiscountAccountId == null) return FinResult.Fail("E-FIN-223");

            var open = inv.GrossAmount - inv.SettledAmount;
            var cleared = a.AppliedAmount + a.DiscountAmount;
            if (cleared > open + Eps) return FinResult.Fail("E-FIN-222");

            // 已实现汇兑损益（本位币）= 实付原币 ×（发票记账汇率 − 付款汇率）。>0 收益，<0 损失
            var fxDiff = Math.Round(a.AppliedAmount * (inv.FxRate - payment.FxRate), 2, MidpointRounding.AwayFromZero);
            // 现金折扣（本位币）按发票记账汇率折算
            var discBase = Math.Round(a.DiscountAmount * inv.FxRate, 2, MidpointRounding.AwayFromZero);

            Guid? diffEntryId = null;
            var diffType = SettlementDiffType.None;
            var diffAmount = 0m;

            if (fxDiff != 0m || discBase != 0m)
            {
                var entry = new JournalEntry
                {
                    VoucherDate = payment.PayDate,
                    Source = VoucherSource.AP,
                    SourceDocNo = $"{payment.No}#{inv.No}#DIFF",
                    Description = $"核销差额 {payment.No}↔{inv.No}",
                };

                // 现金折扣：借 应付 / 贷 折扣科目（清掉发票未付的折扣部分）
                if (discBase != 0m)
                {
                    entry.Lines.Add(ApLine(apAcc.Value, debit: discBase, partner: payment.SupplierId));
                    entry.Lines.Add(new JournalLine { AccountId = a.DiscountAccountId!.Value, Credit = discBase });
                    diffType = SettlementDiffType.CashDiscount;
                    diffAmount = discBase;
                }

                // 已实现汇兑：收益 借应付/贷汇兑收益；损失 借汇兑损失/贷应付
                if (fxDiff > 0m)
                {
                    var gain = await RoleIdAsync("FX_GAIN");
                    if (gain == null) return FinResult.Fail("E-FIN-141", "FX_GAIN");
                    entry.Lines.Add(ApLine(apAcc.Value, debit: fxDiff, partner: payment.SupplierId));
                    entry.Lines.Add(new JournalLine { AccountId = gain.Value, Credit = fxDiff });
                    if (diffType == SettlementDiffType.None) { diffType = SettlementDiffType.FxDiff; diffAmount = fxDiff; }
                }
                else if (fxDiff < 0m)
                {
                    var loss = await RoleIdAsync("FX_LOSS");
                    if (loss == null) return FinResult.Fail("E-FIN-141", "FX_LOSS");
                    entry.Lines.Add(new JournalLine { AccountId = loss.Value, Debit = -fxDiff });
                    entry.Lines.Add(ApLine(apAcc.Value, credit: -fxDiff, partner: payment.SupplierId));
                    if (diffType == SettlementDiffType.None) { diffType = SettlementDiffType.FxDiff; diffAmount = fxDiff; }
                }

                var pr = await _journal.AutoPostAsync(entry);
                if (!pr.Ok) return pr;
                diffEntryId = entry.Id;
            }

            inv.SettledAmount += cleared;
            inv.Status = inv.SettledAmount >= inv.GrossAmount - Eps
                ? ApInvoiceStatus.Settled
                : ApInvoiceStatus.PartiallySettled;
            inv.ModifyDate = now;

            _db.ApSettlements.Add(new ApSettlement
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                ApInvoiceId = inv.Id,
                SettledAmount = a.AppliedAmount,
                DiffAmount = diffAmount,
                DiffType = diffType,
                DiffAccountId = a.DiscountAccountId,
                DiffJournalEntryId = diffEntryId,
                Creator = user,
                CreateDate = now,
            });

            payment.SettledAmount += a.AppliedAmount;
        }

        payment.ModifyDate = now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    private static JournalLine ApLine(Guid accountId, decimal debit = 0m, decimal credit = 0m, string? partner = null)
        => new() { AccountId = accountId, Debit = debit, Credit = credit, PartnerId = partner };

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;
}

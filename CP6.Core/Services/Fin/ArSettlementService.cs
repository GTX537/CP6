using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收核销服务实现（章04 §3，镜像 <see cref="ApSettlementService"/>，方向相反）。
/// 错误码：302 发票不存在 / 311 收款不存在 / 312 仅已过账收款可核销 / 320 超收款余额 /
/// 321 客户不一致 / 322 发票超额核销 / 323 有折扣须给折扣科目 / 324 发票须已过账/部分核销 / 141 科目角色缺失。
/// </summary>
public class ArSettlementService : IArSettlementService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;
    private const decimal Eps = 0.0001m;

    public ArSettlementService(CP6Context db, IJournalEntryService journal)
    {
        _db = db;
        _journal = journal;
    }

    public async Task<FinResult> SettleAsync(Guid receiptId, IReadOnlyList<SettlementApply> applies, string user)
    {
        var receipt = await _db.Receipts.FirstOrDefaultAsync(p => p.Id == receiptId);
        if (receipt == null) return FinResult.Fail("E-FIN-311");
        if (receipt.Status != ReceiptStatus.Posted) return FinResult.Fail("E-FIN-312");

        var unapplied = receipt.Amount - receipt.SettledAmount;
        if (applies.Sum(a => a.AppliedAmount) > unapplied + Eps) return FinResult.Fail("E-FIN-320");

        var arAcc = await RoleIdAsync("AR_CONTROL");
        if (arAcc == null) return FinResult.Fail("E-FIN-141", "AR_CONTROL");

        var now = DateTime.Now;
        foreach (var a in applies)
        {
            var inv = await _db.ArInvoices.FirstOrDefaultAsync(i => i.Id == a.InvoiceId);
            if (inv == null) return FinResult.Fail("E-FIN-302");
            if (inv.Status != ArInvoiceStatus.Posted && inv.Status != ArInvoiceStatus.PartiallySettled)
                return FinResult.Fail("E-FIN-324");
            if (inv.CustomerId != receipt.CustomerId) return FinResult.Fail("E-FIN-321");
            if (a.DiscountAmount != 0m && a.DiscountAccountId == null) return FinResult.Fail("E-FIN-323");

            var open = inv.GrossAmount - inv.SettledAmount;
            var cleared = a.AppliedAmount + a.DiscountAmount;
            if (cleared > open + Eps) return FinResult.Fail("E-FIN-322");

            // 已实现汇兑损益（本位币）= 实收原币 ×（收款汇率 − 发票记账汇率）。>0 收益，<0 损失
            var fxDiff = Math.Round(a.AppliedAmount * (receipt.FxRate - inv.FxRate), 2, MidpointRounding.AwayFromZero);
            // 销售折扣（本位币）按发票记账汇率折算
            var discBase = Math.Round(a.DiscountAmount * inv.FxRate, 2, MidpointRounding.AwayFromZero);

            Guid? diffEntryId = null;
            var diffType = SettlementDiffType.None;
            var diffAmount = 0m;

            if (fxDiff != 0m || discBase != 0m)
            {
                var entry = new JournalEntry
                {
                    VoucherDate = receipt.ReceiptDate,
                    Source = VoucherSource.AR,
                    SourceDocNo = $"{receipt.No}#{inv.No}#DIFF",
                    Description = $"核销差额 {receipt.No}↔{inv.No}",
                };

                // 销售折扣：借 折扣科目（销售折扣/财务费用）/ 贷 应收（清掉发票未收的折扣部分）
                if (discBase != 0m)
                {
                    entry.Lines.Add(new JournalLine { AccountId = a.DiscountAccountId!.Value, Debit = discBase });
                    entry.Lines.Add(ArLine(arAcc.Value, credit: discBase, partner: receipt.CustomerId));
                    diffType = SettlementDiffType.CashDiscount;
                    diffAmount = discBase;
                }

                // 已实现汇兑：收益 借应收/贷汇兑收益；损失 借汇兑损失/贷应收
                if (fxDiff > 0m)
                {
                    var gain = await RoleIdAsync("FX_GAIN");
                    if (gain == null) return FinResult.Fail("E-FIN-141", "FX_GAIN");
                    entry.Lines.Add(ArLine(arAcc.Value, debit: fxDiff, partner: receipt.CustomerId));
                    entry.Lines.Add(new JournalLine { AccountId = gain.Value, Credit = fxDiff });
                    if (diffType == SettlementDiffType.None) { diffType = SettlementDiffType.FxDiff; diffAmount = fxDiff; }
                }
                else if (fxDiff < 0m)
                {
                    var loss = await RoleIdAsync("FX_LOSS");
                    if (loss == null) return FinResult.Fail("E-FIN-141", "FX_LOSS");
                    entry.Lines.Add(new JournalLine { AccountId = loss.Value, Debit = -fxDiff });
                    entry.Lines.Add(ArLine(arAcc.Value, credit: -fxDiff, partner: receipt.CustomerId));
                    if (diffType == SettlementDiffType.None) { diffType = SettlementDiffType.FxDiff; diffAmount = fxDiff; }
                }

                var pr = await _journal.AutoPostAsync(entry);
                if (!pr.Ok) return pr;
                diffEntryId = entry.Id;
            }

            inv.SettledAmount += cleared;
            inv.Status = inv.SettledAmount >= inv.GrossAmount - Eps
                ? ArInvoiceStatus.Settled
                : ArInvoiceStatus.PartiallySettled;
            inv.ModifyDate = now;

            _db.ArSettlements.Add(new ArSettlement
            {
                Id = Guid.NewGuid(),
                ReceiptId = receipt.Id,
                ArInvoiceId = inv.Id,
                SettledAmount = a.AppliedAmount,
                DiffAmount = diffAmount,
                DiffType = diffType,
                DiffAccountId = a.DiscountAccountId,
                DiffJournalEntryId = diffEntryId,
                Creator = user,
                CreateDate = now,
            });

            receipt.SettledAmount += a.AppliedAmount;
        }

        receipt.ModifyDate = now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    private static JournalLine ArLine(Guid accountId, decimal debit = 0m, decimal credit = 0m, string? partner = null)
        => new() { AccountId = accountId, Debit = debit, Credit = credit, PartnerId = partner };

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;
}

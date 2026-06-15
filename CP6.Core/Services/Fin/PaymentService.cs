using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 付款服务实现（章03 §3②/§6.1）。
/// 错误码：210 银行账户不存在 / 211 付款不存在 / 212 仅已过账可撤销。
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly CP6Context _db;
    private readonly IAutoVoucherEngine _engine;
    private readonly IJournalEntryService _journal;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "PAY";

    public PaymentService(CP6Context db, IAutoVoucherEngine engine, IJournalEntryService journal, IFinSequenceService seq)
    {
        _db = db;
        _engine = engine;
        _journal = journal;
        _seq = seq;
    }

    public Task<Payment?> GetAsync(Guid id) => _db.Payments.FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<Payment>> ListAsync(string? supplierId = null, PaymentStatus? status = null)
    {
        var q = _db.Payments.AsQueryable();
        if (!string.IsNullOrEmpty(supplierId)) q = q.Where(x => x.SupplierId == supplierId);
        if (status is PaymentStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.PayDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public async Task<FinResult> PayAsync(Payment payment, string user)
    {
        var bank = await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == payment.BankAccountId);
        if (bank == null) return FinResult.Fail("E-FIN-210");

        payment.Id = payment.Id == Guid.Empty ? Guid.NewGuid() : payment.Id;
        payment.No = await _seq.NextAsync(SeqKey, payment.PayDate);
        if (payment.FxRate <= 0m) payment.FxRate = 1m;
        payment.Status = PaymentStatus.Posted;
        payment.Creator = user;
        payment.CreateDate = DateTime.Now;

        var evt = new FinBizEvent
        {
            EventType = payment.IsPrepayment ? "AP.Prepayment" : "AP.Payment",
            Source = VoucherSource.AP,
            SourceDocNo = payment.No,
            BizDate = payment.PayDate,
            PartnerId = payment.SupplierId,
            Description = $"付款 {payment.No}",
            CurrencyCd = payment.CurrencyCd,
            FxRate = string.IsNullOrEmpty(payment.CurrencyCd) ? null : payment.FxRate,
        };
        evt.HeaderAmounts["Amount"] = payment.Amount;
        evt.HeaderGuids["BankGlAccountId"] = bank.GlAccountId;

        var r = await _engine.GenerateAsync(evt);
        if (!r.Ok) return r;

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(j =>
            j.Source == VoucherSource.AP && j.SourceDocNo == payment.No && j.Status == JournalStatus.Posted);
        payment.JournalEntryId = entry?.Id;
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> ReversePaymentAsync(Guid paymentId, string user, string reason)
    {
        var p = await _db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId);
        if (p == null) return FinResult.Fail("E-FIN-211");
        if (p.Status != PaymentStatus.Posted) return FinResult.Fail("E-FIN-212");

        // ① 解核销：还原发票欠款 + 反冲差额凭证（§6.1 顺序：先解核销再红冲）
        var settlements = await _db.ApSettlements.Where(s => s.PaymentId == p.Id).ToListAsync();
        foreach (var s in settlements)
        {
            var inv = await _db.ApInvoices.FirstOrDefaultAsync(i => i.Id == s.ApInvoiceId);
            if (inv != null)
            {
                inv.SettledAmount = Math.Max(0m, inv.SettledAmount - s.SettledAmount);
                inv.Status = RecomputeStatus(inv);
                inv.ModifyDate = DateTime.Now;
            }
            if (s.DiffJournalEntryId is Guid djid)
                await _journal.ReverseAsync(djid, user, $"撤销付款 {p.No} 差额冲销", autoPost: true);
            _db.ApSettlements.Remove(s);
        }
        p.SettledAmount = 0m;

        // ② 红冲付款凭证（系统触发→直过）
        if (p.JournalEntryId is Guid jid)
        {
            var rev = await _journal.ReverseAsync(jid, user, reason, autoPost: true);
            if (!rev.Ok) return rev;
        }

        // ③ 状态
        p.Status = PaymentStatus.Reversed;
        p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    /// <summary>据已核销额重算发票状态（撤销/部分核销共用）。</summary>
    private static ApInvoiceStatus RecomputeStatus(ApInvoice inv)
        => inv.SettledAmount <= 0m ? ApInvoiceStatus.Posted
           : inv.SettledAmount >= inv.GrossAmount ? ApInvoiceStatus.Settled
           : ApInvoiceStatus.PartiallySettled;
}

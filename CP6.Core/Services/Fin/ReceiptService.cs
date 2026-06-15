using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 收款服务实现（章04 §3，镜像 <see cref="PaymentService"/>）。
/// 错误码：310 银行账户不存在 / 311 收款不存在 / 312 仅已过账可撤销。
/// </summary>
public class ReceiptService : IReceiptService
{
    private readonly CP6Context _db;
    private readonly IAutoVoucherEngine _engine;
    private readonly IJournalEntryService _journal;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "RCP";

    public ReceiptService(CP6Context db, IAutoVoucherEngine engine, IJournalEntryService journal, IFinSequenceService seq)
    {
        _db = db;
        _engine = engine;
        _journal = journal;
        _seq = seq;
    }

    public Task<Receipt?> GetAsync(Guid id) => _db.Receipts.FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<Receipt>> ListAsync(string? customerId = null, ReceiptStatus? status = null)
    {
        var q = _db.Receipts.AsQueryable();
        if (!string.IsNullOrEmpty(customerId)) q = q.Where(x => x.CustomerId == customerId);
        if (status is ReceiptStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.ReceiptDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public async Task<FinResult> ReceiveAsync(Receipt receipt, string user)
    {
        var bank = await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == receipt.BankAccountId);
        if (bank == null) return FinResult.Fail("E-FIN-310");

        receipt.Id = receipt.Id == Guid.Empty ? Guid.NewGuid() : receipt.Id;
        receipt.No = await _seq.NextAsync(SeqKey, receipt.ReceiptDate);
        if (receipt.FxRate <= 0m) receipt.FxRate = 1m;
        receipt.Status = ReceiptStatus.Posted;
        receipt.Creator = user;
        receipt.CreateDate = DateTime.Now;

        var evt = new FinBizEvent
        {
            EventType = receipt.IsAdvance ? "AR.Advance" : "AR.Receipt",
            Source = VoucherSource.AR,
            SourceDocNo = receipt.No,
            BizDate = receipt.ReceiptDate,
            PartnerId = receipt.CustomerId,
            Description = $"收款 {receipt.No}",
            CurrencyCd = receipt.CurrencyCd,
            FxRate = string.IsNullOrEmpty(receipt.CurrencyCd) ? null : receipt.FxRate,
        };
        evt.HeaderAmounts["Amount"] = receipt.Amount;
        evt.HeaderGuids["BankGlAccountId"] = bank.GlAccountId;

        var r = await _engine.GenerateAsync(evt);
        if (!r.Ok) return r;

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(j =>
            j.Source == VoucherSource.AR && j.SourceDocNo == receipt.No && j.Status == JournalStatus.Posted);
        receipt.JournalEntryId = entry?.Id;
        _db.Receipts.Add(receipt);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> ReverseReceiptAsync(Guid receiptId, string user, string reason)
    {
        var rcp = await _db.Receipts.FirstOrDefaultAsync(x => x.Id == receiptId);
        if (rcp == null) return FinResult.Fail("E-FIN-311");
        if (rcp.Status != ReceiptStatus.Posted) return FinResult.Fail("E-FIN-312");

        // ① 解核销：还原发票欠款 + 反冲差额凭证（§6.1 顺序：先解核销再红冲）
        var settlements = await _db.ArSettlements.Where(s => s.ReceiptId == rcp.Id).ToListAsync();
        foreach (var s in settlements)
        {
            var inv = await _db.ArInvoices.FirstOrDefaultAsync(i => i.Id == s.ArInvoiceId);
            if (inv != null)
            {
                inv.SettledAmount = Math.Max(0m, inv.SettledAmount - s.SettledAmount);
                inv.Status = RecomputeStatus(inv);
                inv.ModifyDate = DateTime.Now;
            }
            if (s.DiffJournalEntryId is Guid djid)
                await _journal.ReverseAsync(djid, user, $"撤销收款 {rcp.No} 差额冲销", autoPost: true);
            _db.ArSettlements.Remove(s);
        }
        rcp.SettledAmount = 0m;

        // ② 红冲收款凭证（系统触发→直过）
        if (rcp.JournalEntryId is Guid jid)
        {
            var rev = await _journal.ReverseAsync(jid, user, reason, autoPost: true);
            if (!rev.Ok) return rev;
        }

        // ③ 状态
        rcp.Status = ReceiptStatus.Reversed;
        rcp.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    /// <summary>据已核销额重算发票状态（撤销/部分核销共用）。</summary>
    private static ArInvoiceStatus RecomputeStatus(ArInvoice inv)
        => inv.SettledAmount <= 0m ? ArInvoiceStatus.Posted
           : inv.SettledAmount >= inv.GrossAmount ? ArInvoiceStatus.Settled
           : ArInvoiceStatus.PartiallySettled;
}

using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收发票服务实现（章04 §2）。
/// 错误码：302 发票不存在 / 303 仅草稿可过账 / 304 无明细行。
/// 收入凭证 Source=AR；成本结转凭证 Source=Cost（与收入分开幂等，同 No 不冲突）。
/// </summary>
public class ArInvoiceService : IArInvoiceService
{
    private readonly CP6Context _db;
    private readonly IAutoVoucherEngine _engine;
    private readonly IJournalEntryService _journal;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "AR";

    public ArInvoiceService(CP6Context db, IAutoVoucherEngine engine, IJournalEntryService journal, IFinSequenceService seq)
    {
        _db = db;
        _engine = engine;
        _journal = journal;
        _seq = seq;
    }

    public Task<ArInvoice?> GetAsync(Guid id) =>
        _db.ArInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<ArInvoice>> ListAsync(string? customerId = null, ArInvoiceStatus? status = null)
    {
        var q = _db.ArInvoices.Include(x => x.Lines).AsQueryable();
        if (!string.IsNullOrEmpty(customerId)) q = q.Where(x => x.CustomerId == customerId);
        if (status is ArInvoiceStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public async Task<FinResult> CreateAsync(ArInvoice invoice, string user)
    {
        if (invoice.Lines.Count == 0) return FinResult.Fail("E-FIN-304");

        var taxIds = invoice.Lines.Where(l => l.TaxCodeId != null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var taxes = taxIds.Count == 0
            ? new Dictionary<Guid, TaxCode>()
            : await _db.TaxCodes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        // 销项税：恒按税率计提（不涉可抵扣）
        decimal net = 0m, tax = 0m;
        foreach (var line in invoice.Lines)
        {
            line.TaxAmount = line.TaxCodeId is Guid tid && taxes.TryGetValue(tid, out var tc)
                ? Math.Round(line.Amount * tc.Rate, 2, MidpointRounding.AwayFromZero)
                : 0m;
            net += line.Amount;
            tax += line.TaxAmount;
        }
        invoice.NetAmount = net;
        invoice.TaxAmount = tax;
        invoice.GrossAmount = net + tax;

        invoice.Id = invoice.Id == Guid.Empty ? Guid.NewGuid() : invoice.Id;
        invoice.No = await _seq.NextAsync(SeqKey, invoice.InvoiceDate);
        invoice.Status = ArInvoiceStatus.Draft;
        if (invoice.FxRate <= 0m) invoice.FxRate = 1m;
        invoice.Creator = user;
        invoice.CreateDate = DateTime.Now;

        var n = 1;
        foreach (var line in invoice.Lines)
        {
            line.InvoiceId = invoice.Id;
            if (line.LineNo == 0) line.LineNo = n;
            n++;
        }

        _db.ArInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> PostAsync(Guid invoiceId, string user)
    {
        var inv = await _db.ArInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (inv == null) return FinResult.Fail("E-FIN-302");
        if (inv.Status != ArInvoiceStatus.Draft) return FinResult.Fail("E-FIN-303");

        // ① 收入确认（借应收/贷收入+销项税）—— 原币按销售汇率换算
        var rev = new FinBizEvent
        {
            EventType = "AR.Revenue", Source = VoucherSource.AR, SourceDocNo = inv.No,
            BizDate = inv.InvoiceDate, PartnerId = inv.CustomerId, Description = $"应收发票 {inv.No}",
            CurrencyCd = inv.CurrencyCd, FxRate = string.IsNullOrEmpty(inv.CurrencyCd) ? null : inv.FxRate,
        };
        rev.HeaderAmounts["GrossAmount"] = inv.GrossAmount;
        rev.HeaderAmounts["NetAmount"] = inv.NetAmount;
        rev.HeaderAmounts["TaxAmount"] = inv.TaxAmount;
        var r1 = await _engine.GenerateAsync(rev);
        if (!r1.Ok) return r1;
        inv.JournalEntryId = (await _db.JournalEntries.FirstOrDefaultAsync(j =>
            j.Source == VoucherSource.AR && j.SourceDocNo == inv.No && j.Status == JournalStatus.Posted))?.Id;

        // ② 成本结转（借COGS/贷FG）—— 成本为本位币（我方成本，不随销售外币）
        if (inv.CostAmount > 0m)
        {
            var cogs = new FinBizEvent
            {
                EventType = "AR.Cogs", Source = VoucherSource.Cost, SourceDocNo = inv.No,
                BizDate = inv.InvoiceDate, Description = $"成本结转 {inv.No}",
            };
            cogs.HeaderAmounts["Amount"] = inv.CostAmount;
            var r2 = await _engine.GenerateAsync(cogs);
            if (!r2.Ok) return r2;
            inv.CostJournalEntryId = (await _db.JournalEntries.FirstOrDefaultAsync(j =>
                j.Source == VoucherSource.Cost && j.SourceDocNo == inv.No && j.Status == JournalStatus.Posted))?.Id;
        }

        inv.Status = ArInvoiceStatus.Posted;
        inv.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<(FinResult Result, Guid? InvoiceId, string? No)> CreateFromShipmentAsync(FinShipmentInvoiceRequest request, string user)
    {
        // 幂等：同出货号已开票→返回既有
        var existing = await _db.ArInvoices.FirstOrDefaultAsync(x => x.ShipmentId == request.ShipmentId && !x.IsCreditMemo);
        if (existing != null) return (FinResult.Pass(), existing.Id, existing.No);

        var inv = new ArInvoice
        {
            CustomerId = request.CustomerId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CurrencyCd = request.CurrencyCd,
            FxRate = request.FxRate ?? 1m,
            CostAmount = request.EstimatedCost,
            ShipmentId = request.ShipmentId,
            OrderId = request.OrderId,
            Lines = request.Lines.Select(l => new ArInvoiceLine
            {
                ItemId = l.ItemId, Qty = l.Qty, UnitPrice = l.UnitPrice,
                Amount = Math.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero),
                TaxCodeId = l.TaxCodeId, RevenueAccountId = l.RevenueAccountId, CostCenterId = l.CostCenterId,
            }).ToList(),
        };

        var created = await CreateAsync(inv, user);
        if (!created.Ok) return (created, null, null);
        var posted = await PostAsync(inv.Id, user);
        return (posted, inv.Id, inv.No);
    }

    public async Task<FinResult> ReverseAsync(Guid invoiceId, string user, string reason)
    {
        var inv = await _db.ArInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (inv == null) return FinResult.Fail("E-FIN-302");
        if (inv.Status != ArInvoiceStatus.Posted && inv.Status != ArInvoiceStatus.PartiallySettled)
            return FinResult.Fail("E-FIN-303");

        if (inv.JournalEntryId is Guid jid)
        {
            var r = await _journal.ReverseAsync(jid, user, reason, autoPost: true);
            if (!r.Ok) return r;
        }
        if (inv.CostJournalEntryId is Guid cid)
        {
            var r = await _journal.ReverseAsync(cid, user, $"{reason}（成本结转）", autoPost: true);
            if (!r.Ok) return r;
        }

        inv.Status = ArInvoiceStatus.Reversed;
        inv.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
}

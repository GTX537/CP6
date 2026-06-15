using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付发票服务实现（章03 §3①）。
/// 错误码：201 供应商发票号重复 / 202 发票不存在 / 203 仅草稿可过账 / 204 无明细行。
/// </summary>
public class ApInvoiceService : IApInvoiceService
{
    private readonly CP6Context _db;
    private readonly IAutoVoucherEngine _engine;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "AP";

    public ApInvoiceService(CP6Context db, IAutoVoucherEngine engine, IFinSequenceService seq)
    {
        _db = db;
        _engine = engine;
        _seq = seq;
    }

    public Task<ApInvoice?> GetAsync(Guid id) =>
        _db.ApInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<ApInvoice>> ListAsync(string? supplierId = null, ApInvoiceStatus? status = null)
    {
        var q = _db.ApInvoices.Include(x => x.Lines).AsQueryable();
        if (!string.IsNullOrEmpty(supplierId)) q = q.Where(x => x.SupplierId == supplierId);
        if (status is ApInvoiceStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public async Task<FinResult> CreateAsync(ApInvoice invoice, string user)
    {
        // 防重：同供应商同发票号（红字/空号不约束，与过滤唯一索引一致）
        if (!string.IsNullOrEmpty(invoice.SupplierInvoiceNo) && !invoice.IsCreditMemo)
        {
            var dup = await _db.ApInvoices.AnyAsync(x =>
                x.SupplierId == invoice.SupplierId &&
                x.SupplierInvoiceNo == invoice.SupplierInvoiceNo &&
                !x.IsCreditMemo);
            if (dup) return FinResult.Fail("E-FIN-201", invoice.SupplierInvoiceNo!);
        }

        if (invoice.Lines.Count == 0) return FinResult.Fail("E-FIN-204");

        // 行级算税：可抵扣→独立进项税；不可抵扣→并入成本，不单列
        var taxIds = invoice.Lines.Where(l => l.TaxCodeId != null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var taxes = taxIds.Count == 0
            ? new Dictionary<Guid, TaxCode>()
            : await _db.TaxCodes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        decimal net = 0m, tax = 0m;
        foreach (var line in invoice.Lines)
        {
            if (line.TaxCodeId is Guid tid && taxes.TryGetValue(tid, out var tc))
            {
                var t = Math.Round(line.Amount * tc.Rate, 2, MidpointRounding.AwayFromZero);
                if (tc.Recoverable) { line.TaxAmount = t; tax += t; }
                else { line.Amount += t; line.TaxAmount = 0m; }   // 不可抵扣并入成本行
            }
            else
            {
                line.TaxAmount = 0m;
            }
            net += line.Amount;
        }

        invoice.NetAmount = net;
        invoice.TaxAmount = tax;
        invoice.GrossAmount = net + tax;

        invoice.Id = invoice.Id == Guid.Empty ? Guid.NewGuid() : invoice.Id;
        invoice.No = await _seq.NextAsync(SeqKey, invoice.InvoiceDate);
        invoice.Status = ApInvoiceStatus.Draft;
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

        _db.ApInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> PostAsync(Guid invoiceId, string user)
    {
        var inv = await _db.ApInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (inv == null) return FinResult.Fail("E-FIN-202");
        if (inv.Status != ApInvoiceStatus.Draft) return FinResult.Fail("E-FIN-203");

        var evt = new FinBizEvent
        {
            EventType = "AP.InvoicePosted",
            Source = VoucherSource.AP,
            SourceDocNo = inv.No,
            BizDate = inv.InvoiceDate,
            PartnerId = inv.SupplierId,
            Description = $"应付发票 {inv.No}",
            CurrencyCd = inv.CurrencyCd,
            FxRate = string.IsNullOrEmpty(inv.CurrencyCd) ? null : inv.FxRate,
        };
        evt.HeaderAmounts["GrossAmount"] = inv.GrossAmount;
        evt.HeaderAmounts["TaxAmount"] = inv.TaxAmount;
        foreach (var line in inv.Lines)
        {
            var dl = new FinBizEventLine { CostCenterId = line.CostCenterId };
            dl.Guids["ExpenseAccountId"] = line.ExpenseAccountId;
            dl.Amounts["Amount"] = line.Amount;
            evt.DocLines.Add(dl);
        }

        var r = await _engine.GenerateAsync(evt);
        if (!r.Ok) return r;

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(j =>
            j.Source == VoucherSource.AP && j.SourceDocNo == inv.No && j.Status == JournalStatus.Posted);
        inv.JournalEntryId = entry?.Id;
        inv.Status = ApInvoiceStatus.Posted;
        inv.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
}

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

    internal bool Owns(CP6Context context) => ReferenceEquals(_db, context);

    public Task<ArInvoice?> GetAsync(Guid id) =>
        _db.ArInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<ArInvoice>> ListAsync(string? customerId = null, ArInvoiceStatus? status = null)
    {
        var q = _db.ArInvoices.Include(x => x.Lines).AsQueryable();
        if (!string.IsNullOrEmpty(customerId)) q = q.Where(x => x.CustomerId == customerId);
        if (status is ArInvoiceStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.No).ToListAsync();
    }

    public Task<FinResult> CreateAsync(ArInvoice invoice, string user) =>
        WriteDraftAsync(invoice, user, validateLinks: true);

    private async Task<FinResult> WriteDraftAsync(ArInvoice invoice, string user, bool validateLinks)
    {
        if (invoice.Lines.Count == 0) return FinResult.Fail("E-FIN-304");

        var taxIds = invoice.Lines.Where(l => l.TaxCodeId != null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var taxes = taxIds.Count == 0
            ? new Dictionary<Guid, TaxCode>()
            : await _db.TaxCodes.Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        var native = _db.Database.IsNpgsql();
        if (native && validateLinks)
        {
            var links = await ValidateDraftLinksAsync(invoice, taxes);
            if (!links.Ok) return links;
        }

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

        if (native)
        {
            invoice.TenantId = _db.CurrentTenantId;
            invoice.JournalEntryId = null;
            invoice.CostJournalEntryId = null;
        }

        invoice.Id = invoice.Id == Guid.Empty ? Guid.NewGuid() : invoice.Id;
        invoice.No = await _seq.NextAsync(SeqKey, invoice.InvoiceDate);
        invoice.Status = ArInvoiceStatus.Draft;
        if (invoice.FxRate <= 0m) invoice.FxRate = 1m;
        invoice.Creator = user;
        invoice.CreateDate = DateTime.Now;

        var n = 1;
        foreach (var line in invoice.Lines)
        {
            if (native) line.TenantId = invoice.TenantId;
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

        // ① 收入确认（借应收/贷收入+销项税）；红字走 AR.CreditMemo（借收入+销项税/贷应收，方向反向冲减应收）
        var rev = new FinBizEvent
        {
            EventType = inv.IsCreditMemo ? "AR.CreditMemo" : "AR.Revenue", Source = VoucherSource.AR, SourceDocNo = inv.No,
            BizDate = inv.InvoiceDate, PartnerId = inv.CustomerId,
            Description = inv.IsCreditMemo ? $"销售红字 {inv.No}" : $"应收发票 {inv.No}",
            CurrencyCd = inv.CurrencyCd, FxRate = string.IsNullOrEmpty(inv.CurrencyCd) ? null : inv.FxRate,
        };
        rev.HeaderAmounts["GrossAmount"] = inv.GrossAmount;
        rev.HeaderAmounts["NetAmount"] = inv.NetAmount;
        rev.HeaderAmounts["TaxAmount"] = inv.TaxAmount;
        var r1 = await _engine.GenerateAsync(rev);
        if (!r1.Ok) return r1;
        inv.JournalEntryId = (await _db.JournalEntries.FirstOrDefaultAsync(j =>
            j.Source == VoucherSource.AR && j.SourceDocNo == inv.No && j.Status == JournalStatus.Posted))?.Id;

        // ② 成本结转（借COGS/贷FG）；红字走 AR.CogsReversal（借FG/贷COGS，货退回入库）—— 成本为本位币（我方成本，不随销售外币）
        var hasCost = inv.CostAmount > 0m;
        if (hasCost)
        {
            var cogs = new FinBizEvent
            {
                EventType = inv.IsCreditMemo ? "AR.CogsReversal" : "AR.Cogs", Source = VoucherSource.Cost, SourceDocNo = inv.No,
                BizDate = inv.InvoiceDate, Description = inv.IsCreditMemo ? $"成本回冲 {inv.No}" : $"成本结转 {inv.No}",
            };
            cogs.HeaderAmounts["Amount"] = inv.CostAmount;
            var r2 = await _engine.GenerateAsync(cogs);
            if (!r2.Ok) return r2;
        }
        // A persisted cost leg remains authoritative for this source number even when the draft now shows zero.
        if (hasCost || _db.Database.IsNpgsql())
            inv.CostJournalEntryId = (await _db.JournalEntries.FirstOrDefaultAsync(j =>
                j.Source == VoucherSource.Cost && j.SourceDocNo == inv.No && j.Status == JournalStatus.Posted))?.Id;

        inv.Status = ArInvoiceStatus.Posted;
        inv.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    private async Task<FinResult> ValidateDraftLinksAsync(ArInvoice invoice, IReadOnlyDictionary<Guid, TaxCode> taxes)
    {
        if (invoice.Lines.Any(line => line.TaxCodeId is Guid id && id != Guid.Empty && !taxes.ContainsKey(id)))
            return FinResult.Fail("E-FIN-205");

        var centers = invoice.Lines.Select(line => line.CostCenterId.GetValueOrDefault()).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (centers.Length != 0 && await _db.CostCenters.CountAsync(center => centers.Contains(center.Id)) != centers.Length)
            return FinResult.Fail("E-FIN-206");

        if (invoice.OriginInvoiceId is Guid origin && origin != Guid.Empty
            && !await _db.ArInvoices.AnyAsync(existing => existing.Id == origin))
            return FinResult.Fail("E-FIN-302");

        // Historical callers also use unregistered credit note IDs as correlation keys.
        if (invoice.CreditNoteId is Guid credit && credit != Guid.Empty
            && await _db.CreditNotes.IgnoreQueryFilters().AnyAsync(note => note.Id == credit && note.TenantId != _db.CurrentTenantId))
            return FinResult.Fail("E-FIN-305");

        var accounts = invoice.Lines.Select(line => line.RevenueAccountId.GetValueOrDefault()).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (accounts.Length != 0)
        {
            var owned = await _db.GlAccounts.Where(account => accounts.Contains(account.Id)).Select(account => account.Id).ToListAsync();
            if (owned.Count != accounts.Length)
            {
                var index = invoice.Lines.FindIndex(line => line.RevenueAccountId is Guid id && id != Guid.Empty && !owned.Contains(id));
                var lineNo = invoice.Lines[index].LineNo;
                return FinResult.Fail("E-FIN-108", lineNo == 0 ? index + 1 : lineNo);
            }
        }
        return FinResult.Pass();
    }

    public async Task<(FinResult Result, Guid? InvoiceId, string? No)> CreateFromShipmentAsync(FinShipmentInvoiceRequest request, string user)
    {
        // 幂等：同出货号已开票→返回既有
        var existing = await _db.ArInvoices.FirstOrDefaultAsync(x => x.ShipmentId == request.ShipmentId && !x.IsCreditMemo);
        if (existing != null)
        {
            if (!_db.Database.IsNpgsql()) return (FinResult.Pass(), existing.Id, existing.No);
            if (existing.CustomerId != request.CustomerId || (existing.OrderId ?? "") != (request.OrderId ?? ""))
                return (FinResult.Fail("E-FIN-303"), existing.Id, existing.No);
            if (existing.Status != ArInvoiceStatus.Draft) return (FinResult.Pass(), existing.Id, existing.No);

            // The stored draft and an already posted original leg belong to the first attempt.
            // PostAsync and the existing engine complete the missing leg using that draft.
            var revenue = await ShipmentVoucherAsync(existing.No, VoucherSource.AR, existing.JournalEntryId);
            var costVoucher = await ShipmentVoucherAsync(existing.No, VoucherSource.Cost, existing.CostJournalEntryId);
            if (ConflictingDraftVoucher(revenue, existing.JournalEntryId) || ConflictingDraftVoucher(costVoucher, existing.CostJournalEntryId))
                return (FinResult.Fail("E-FIN-120"), existing.Id, existing.No);
            return (await PostAsync(existing.Id, user), existing.Id, existing.No);
        }

        // F3-D3 成本切真实：有来源工单且已归集成本单时，成本 = FG 单位成本 × 出货数；否则回退估算成本
        var cost = request.EstimatedCost;
        if (!string.IsNullOrEmpty(request.WorkOrderNo))
        {
            var cs = await _db.CostSheets.AsNoTracking().FirstOrDefaultAsync(s => s.WorkOrderNo == request.WorkOrderNo);
            if (cs != null && cs.FgUnitCost > 0m)
                cost = Math.Round(cs.FgUnitCost * request.Lines.Sum(l => l.Qty), 2, MidpointRounding.AwayFromZero);
        }

        var inv = new ArInvoice
        {
            CustomerId = request.CustomerId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CurrencyCd = request.CurrencyCd,
            FxRate = request.FxRate ?? 1m,
            CostAmount = cost,
            ShipmentId = request.ShipmentId,
            OrderId = request.OrderId,
            Lines = request.Lines.Select(l => new ArInvoiceLine
            {
                ItemId = l.ItemId, Qty = l.Qty, UnitPrice = l.UnitPrice,
                Amount = Math.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero),
                TaxCodeId = l.TaxCodeId, RevenueAccountId = l.RevenueAccountId, CostCenterId = l.CostCenterId,
            }).ToList(),
        };

        var created = await WriteDraftAsync(inv, user, validateLinks: false);
        if (!created.Ok) return (created, null, null);
        var posted = await PostAsync(inv.Id, user);
        return (posted, inv.Id, inv.No);
    }

    private static bool ConflictingDraftVoucher(JournalEntry? voucher, Guid? linkedId)
        => voucher is null ? linkedId.HasValue : voucher.Status != JournalStatus.Posted;

    private async Task<JournalEntry?> ShipmentVoucherAsync(string invoiceNo, VoucherSource source, Guid? linkedId,
        CancellationToken ct = default)
    {
        var original = await _db.JournalEntries.SingleOrDefaultAsync(j => j.Source == source && j.SourceDocNo == invoiceNo
            && j.ReverseOfId == null, ct);
        return linkedId is not null && original?.Id != linkedId ? null : original;
    }

    // Recover an original shipment cancellation without changing the manual invoice reversal contract.
    internal async Task<FinResult> ReverseShipmentAsync(ArInvoice invoice, string user, CancellationToken ct)
    {
        if (invoice.Status is not (ArInvoiceStatus.Posted or ArInvoiceStatus.PartiallySettled or ArInvoiceStatus.Reversed))
            return FinResult.Fail("E-FIN-303");
        var revenue = await ShipmentVoucherAsync(invoice.No, VoucherSource.AR, invoice.JournalEntryId, ct);
        if (revenue is null) return FinResult.Fail("E-FIN-130");
        var first = await ReverseShipmentVoucherAsync(revenue, invoice.Status, user, "出货取消", ct);
        if (!first.Ok) return first;

        var cost = await ShipmentVoucherAsync(invoice.No, VoucherSource.Cost, invoice.CostJournalEntryId, ct);
        if (cost is null && (invoice.CostJournalEntryId.HasValue || invoice.CostAmount > 0m)) return FinResult.Fail("E-FIN-130");
        if (cost is not null)
        {
            var second = await ReverseShipmentVoucherAsync(cost, invoice.Status, user, "出货取消（成本结转）", ct);
            if (!second.Ok) return second;
        }
        if (invoice.Status == ArInvoiceStatus.Reversed) return FinResult.Pass();

        invoice.JournalEntryId = revenue.Id;
        invoice.CostJournalEntryId = cost?.Id;
        invoice.Status = ArInvoiceStatus.Reversed;
        invoice.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return FinResult.Pass();
    }

    private async Task<FinResult> ReverseShipmentVoucherAsync(JournalEntry original, ArInvoiceStatus invoiceStatus,
        string user, string reason, CancellationToken ct)
    {
        if (original.Status == JournalStatus.Reversed && original.ReversedById is Guid reversalId)
        {
            var confirmed = await _db.JournalEntries.AnyAsync(j => j.TenantId == original.TenantId && j.Id == reversalId
                && j.ReverseOfId == original.Id && j.Source == VoucherSource.Reversal && j.SourceDocNo == original.No
                && j.Status == JournalStatus.Posted, ct);
            return confirmed ? FinResult.Pass() : FinResult.Fail("E-FIN-120");
        }
        return original.Status == JournalStatus.Posted && invoiceStatus != ArInvoiceStatus.Reversed
            ? await _journal.ReverseAsync(original.Id, user, reason, autoPost: true)
            : FinResult.Fail("E-FIN-120");
    }

    public async Task<(FinResult Result, Guid? InvoiceId, string? No)> CreateCreditMemoAsync(FinCreditMemoRequest request, string user)
    {
        // 幂等：同退货单已开红字→返回既有
        var existing = await _db.ArInvoices.FirstOrDefaultAsync(x => x.CreditNoteId == request.CreditNoteId && x.IsCreditMemo);
        if (existing != null)
        {
            var result = _db.Database.IsNpgsql() && existing.Status == ArInvoiceStatus.Draft
                ? await PostAsync(existing.Id, user)
                : FinResult.Pass();
            return (result, existing.Id, existing.No);
        }

        var inv = new ArInvoice
        {
            CustomerId = request.CustomerId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CurrencyCd = request.CurrencyCd,
            FxRate = request.FxRate ?? 1m,
            CostAmount = request.EstimatedCost,
            IsCreditMemo = true,
            CreditNoteId = request.CreditNoteId,
            OriginInvoiceId = request.OriginInvoiceId,
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

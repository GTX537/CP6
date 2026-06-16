using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>询价服务实现（RFQ，采购 章06 §2/§3）。从 PR 发起 + 邀供应商 + 收报价；比价/选定/转 PO 见后续任务。</summary>
public class RfqService : IRfqService
{
    private const string SeqKey = "RFQ";

    private readonly CP6Context _db;
    private readonly ISeqService _seq;

    public RfqService(CP6Context db, ISeqService seq)
    {
        _db = db;
        _seq = seq;
    }

    /// <inheritdoc />
    public async Task<Rfq?> GetAsync(string rfqNo)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted);
        if (rfq == null) return null;

        rfq.Lines = await _db.RfqLines
            .Where(l => l.RfqNo == rfqNo && !l.IsDeleted)
            .OrderBy(l => l.LineNo).ToListAsync();
        rfq.Suppliers = await _db.RfqSuppliers
            .Where(s => s.RfqNo == rfqNo && !s.IsDeleted)
            .OrderBy(s => s.SupplierId).ToListAsync();
        rfq.Quotes = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && !q.IsDeleted)
            .OrderBy(q => q.LineNo).ThenBy(q => q.SupplierId).ToListAsync();
        return rfq;
    }

    /// <inheritdoc />
    public async Task<List<Rfq>> ListAsync(RfqStatus? status = null)
    {
        var q = _db.Rfqs.Where(r => !r.IsDeleted);
        if (status.HasValue) q = q.Where(r => r.Status == status.Value);
        return await q.OrderByDescending(r => r.RfqDate).ThenByDescending(r => r.RfqNo).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Rfq> CreateFromPrAsync(string prNo, string? userName)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-067"); // PR 不存在

        // 可询价行：有效（Status==0）∧ 无建议供应商（走 RFQ）∧ 未转出
        var eligible = await _db.PurchaseRequestLines
            .Where(l => l.PrNo == prNo && !l.IsDeleted
                        && l.Status == 0
                        && (l.SuggestSupplierId == null || l.SuggestSupplierId == "")
                        && (l.ConvertedPoNo == null || l.ConvertedPoNo == ""))
            .OrderBy(l => l.LineNo).ToListAsync();
        if (eligible.Count == 0) throw new InvalidOperationException("E-PUR-060"); // 无可询价行

        var now = DateTime.Now;
        var rfq = new Rfq
        {
            RfqNo = await _seq.NextAsync(SeqKey),
            RfqDate = now,
            Status = RfqStatus.Draft,
            Buyer = userName,
            SourcePrNo = prNo,
            Creator = userName,
            CreateDate = now,
        };

        var lineNo = 0;
        foreach (var pl in eligible)
        {
            rfq.Lines.Add(new RfqLine
            {
                RfqNo = rfq.RfqNo,
                LineNo = ++lineNo,
                ItemId = pl.ItemId,
                Qty = pl.Qty,
                UnitCd = pl.UnitCd,
                RequiredDate = pl.RequiredDate,
                SourcePrNo = prNo,             // 行级追溯回 PR
                SourcePrLineNo = pl.LineNo,
                Creator = userName,
                CreateDate = now,
            });
        }

        _db.Rfqs.Add(rfq);
        _db.RfqLines.AddRange(rfq.Lines);
        await _db.SaveChangesAsync();
        return rfq;
    }

    /// <inheritdoc />
    public async Task<Rfq> AddSuppliersAsync(string rfqNo, IEnumerable<string> supplierIds, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var existing = await _db.RfqSuppliers
            .Where(s => s.RfqNo == rfqNo && !s.IsDeleted)
            .Select(s => s.SupplierId).ToListAsync();
        var alreadyInvited = new HashSet<string>(existing);

        var now = DateTime.Now;
        var added = new List<RfqSupplier>();
        foreach (var sid in supplierIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            if (alreadyInvited.Contains(sid)) continue; // 幂等：不重复加

            var supplier = await _db.BusinessPartners.FirstOrDefaultAsync(b => b.BpCd == sid && !b.IsDeleted)
                           ?? throw new InvalidOperationException("E-PUR-062"); // 供应商不存在
            if (!supplier.SupplierFlg) throw new InvalidOperationException("E-PUR-063"); // 非发注先

            added.Add(new RfqSupplier
            {
                RfqNo = rfqNo,
                SupplierId = supplier.BpCd,
                SupplierName = supplier.BpName,
                InviteStatus = RfqInviteStatus.Invited,
                Creator = userName,
                CreateDate = now,
            });
            alreadyInvited.Add(sid);
        }

        if (added.Count > 0)
        {
            _db.RfqSuppliers.AddRange(added);
            if (rfq.Status == RfqStatus.Draft)
                rfq.Status = RfqStatus.Inviting;
            rfq.Modifier = userName;
            rfq.ModifyDate = now;
            await _db.SaveChangesAsync();
        }

        return (await GetAsync(rfqNo))!;
    }

    /// <inheritdoc />
    public async Task<Rfq> RecordQuoteAsync(string rfqNo, string supplierId,
        IEnumerable<RfqQuoteLineDto> quoteLines, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var invited = await _db.RfqSuppliers
            .FirstOrDefaultAsync(s => s.RfqNo == rfqNo && s.SupplierId == supplierId && !s.IsDeleted)
            ?? throw new InvalidOperationException("E-PUR-064"); // 供应商未被邀请

        var lines = quoteLines?.ToList() ?? new List<RfqQuoteLineDto>();
        if (lines.Count == 0) throw new InvalidOperationException("E-PUR-066"); // 无报价行

        var validLineNos = new HashSet<int>(await _db.RfqLines
            .Where(l => l.RfqNo == rfqNo && !l.IsDeleted)
            .Select(l => l.LineNo).ToListAsync());

        var existingQuotes = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && q.SupplierId == supplierId && !q.IsDeleted)
            .ToListAsync();

        var now = DateTime.Now;
        foreach (var ql in lines)
        {
            if (!validLineNos.Contains(ql.LineNo))
                throw new InvalidOperationException("E-PUR-065"); // 报价行非询价行

            var quote = existingQuotes.FirstOrDefault(q => q.LineNo == ql.LineNo);
            if (quote == null)
            {
                quote = new RfqQuote
                {
                    RfqNo = rfqNo,
                    SupplierId = supplierId,
                    LineNo = ql.LineNo,
                    Creator = userName,
                    CreateDate = now,
                };
                _db.RfqQuotes.Add(quote);
                existingQuotes.Add(quote);
            }
            else
            {
                quote.Modifier = userName;
                quote.ModifyDate = now;
            }

            quote.QuotedPrice = ql.QuotedPrice;
            quote.CurrencyCd = ql.CurrencyCd;
            quote.LeadDays = ql.LeadDays;
            quote.ValidUntil = ql.ValidUntil;
        }

        invited.InviteStatus = RfqInviteStatus.Quoted;
        invited.Modifier = userName;
        invited.ModifyDate = now;

        if (rfq.Status is RfqStatus.Draft or RfqStatus.Inviting)
            rfq.Status = RfqStatus.Quoting;
        rfq.Modifier = userName;
        rfq.ModifyDate = now;

        await _db.SaveChangesAsync();
        return (await GetAsync(rfqNo))!;
    }
}

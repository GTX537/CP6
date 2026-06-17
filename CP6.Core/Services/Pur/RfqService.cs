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
    private readonly IPurchaseOrderService _po;
    private readonly ISupplierPriceService _price;

    public RfqService(CP6Context db, ISeqService seq, IPurchaseOrderService po, ISupplierPriceService price)
    {
        _db = db;
        _seq = seq;
        _po = po;
        _price = price;
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
        var list = await q.OrderByDescending(r => r.RfqDate).ThenByDescending(r => r.RfqNo).ToListAsync();
        if (list.Count == 0) return list;

        // 批量载入行/被邀供应商（3 次查询，避免 N+1），列表行展示「行数/供应商数」
        var rfqNos = list.Select(r => r.RfqNo).ToList();
        var linesByRfq = (await _db.RfqLines.Where(l => rfqNos.Contains(l.RfqNo) && !l.IsDeleted).ToListAsync())
            .GroupBy(l => l.RfqNo).ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).ToList());
        var supsByRfq = (await _db.RfqSuppliers.Where(s => rfqNos.Contains(s.RfqNo) && !s.IsDeleted).ToListAsync())
            .GroupBy(s => s.RfqNo).ToDictionary(g => g.Key, g => g.OrderBy(s => s.SupplierId).ToList());

        foreach (var r in list)
        {
            r.Lines = linesByRfq.TryGetValue(r.RfqNo, out var ls) ? ls : new();
            r.Suppliers = supsByRfq.TryGetValue(r.RfqNo, out var ss) ? ss : new();
        }
        return list;
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

    /// <inheritdoc />
    public async Task<Rfq> RankQuotesAsync(string rfqNo, DateTime? asOf, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var at = asOf ?? DateTime.Now;
        var quotes = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && !q.IsDeleted)
            .ToListAsync();

        foreach (var lineGroup in quotes.GroupBy(q => q.LineNo))
        {
            // 剔除过期（ValidUntil 有值且早于 asOf）→ Rank=0 不参与
            var expired = lineGroup.Where(q => q.ValidUntil != null && q.ValidUntil < at).ToList();
            foreach (var e in expired) e.Rank = 0;

            // 非过期者：价格升序 → 交期升序（null 垫底）→ 供应商 CD（稳定）
            var ranked = lineGroup
                .Where(q => q.ValidUntil == null || q.ValidUntil >= at)
                .OrderBy(q => q.QuotedPrice)
                .ThenBy(q => q.LeadDays ?? int.MaxValue)
                .ThenBy(q => q.SupplierId)
                .ToList();

            var rank = 0;
            foreach (var q in ranked) q.Rank = ++rank;
        }

        var now = DateTime.Now;
        rfq.Modifier = userName;
        rfq.ModifyDate = now;
        await _db.SaveChangesAsync();
        return (await GetAsync(rfqNo))!;
    }

    /// <inheritdoc />
    public async Task<Rfq> SelectAsync(string rfqNo, IEnumerable<RfqSelectionDto> selections, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var now = DateTime.Now;
        var picks = selections?.ToList() ?? new List<RfqSelectionDto>();

        var quotes = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && !q.IsDeleted)
            .ToListAsync();

        foreach (var pick in picks)
        {
            var quote = quotes.FirstOrDefault(q => q.LineNo == pick.LineNo && q.SupplierId == pick.SupplierId)
                        ?? throw new InvalidOperationException("E-PUR-069"); // 报价不存在
            if (quote.ValidUntil != null && quote.ValidUntil < now)
                throw new InvalidOperationException("E-PUR-068"); // 选中报价已过期

            // 一行一选：改选同行先清掉该行先前选中
            foreach (var sib in quotes.Where(q => q.LineNo == pick.LineNo && q.SupplierId != pick.SupplierId && q.IsSelected))
            {
                sib.IsSelected = false;
                sib.Modifier = userName;
                sib.ModifyDate = now;
            }
            quote.IsSelected = true;
            quote.Modifier = userName;
            quote.ModifyDate = now;
        }

        // 所有询价行都有选中 → 推 Selected
        var lineNos = await _db.RfqLines
            .Where(l => l.RfqNo == rfqNo && !l.IsDeleted)
            .Select(l => l.LineNo).ToListAsync();
        var selectedLineNos = quotes.Where(q => q.IsSelected).Select(q => q.LineNo).Distinct().ToHashSet();
        if (lineNos.Count > 0 && lineNos.All(selectedLineNos.Contains))
            rfq.Status = RfqStatus.Selected;

        rfq.Modifier = userName;
        rfq.ModifyDate = now;
        await _db.SaveChangesAsync();
        return (await GetAsync(rfqNo))!;
    }

    /// <inheritdoc />
    public async Task<Rfq> WriteBackPricesAsync(string rfqNo, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var selected = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && q.IsSelected && !q.IsDeleted)
            .ToListAsync();
        var lineItem = await _db.RfqLines
            .Where(l => l.RfqNo == rfqNo && !l.IsDeleted)
            .ToDictionaryAsync(l => l.LineNo, l => l.ItemId);

        var today = DateTime.Now.Date; // 归一到当天 0 点 → 同日重跑业务键命中、幂等不增行
        foreach (var q in selected)
        {
            if (!lineItem.TryGetValue(q.LineNo, out var itemId)) continue;
            await _price.UpsertAsync(new SupplierPrice
            {
                SupplierId = q.SupplierId,
                ItemId = itemId,
                Price = q.QuotedPrice,
                CurrencyCd = q.CurrencyCd,
                MinQty = 0m,
                ValidFrom = today,
                ValidTo = q.ValidUntil,
                Source = "rfq",                 // ★标明来自询价（与手工维护区分）
            }, userName);
        }

        var now = DateTime.Now;
        rfq.Modifier = userName;
        rfq.ModifyDate = now;
        await _db.SaveChangesAsync();
        return (await GetAsync(rfqNo))!;
    }

    /// <inheritdoc />
    public async Task<List<string>> ConvertToPoAsync(string rfqNo, string? userName)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.RfqNo == rfqNo && !r.IsDeleted)
                  ?? throw new InvalidOperationException("E-PUR-061"); // RFQ 不存在

        var selected = await _db.RfqQuotes
            .Where(q => q.RfqNo == rfqNo && q.IsSelected && !q.IsDeleted)
            .ToListAsync();
        if (selected.Count == 0) throw new InvalidOperationException("E-PUR-070"); // 无选中报价

        var now = DateTime.Now;
        // 转 PO 第二道关：选中报价不得已过期（防"选定后才过期"的废价下单）
        foreach (var q in selected)
            if (q.ValidUntil != null && q.ValidUntil < now)
                throw new InvalidOperationException("E-PUR-068"); // 选中报价已过期

        var lines = await _db.RfqLines
            .Where(l => l.RfqNo == rfqNo && !l.IsDeleted)
            .ToDictionaryAsync(l => l.LineNo, l => l);

        var poNos = new List<string>();
        // 按选中供应商分组拆 PO（同供应商的行合一张；不同行可花落不同家）
        foreach (var grp in selected.GroupBy(q => q.SupplierId))
        {
            var dto = new PoCreateDto
            {
                SupplierId = grp.Key,
                Type = 1,
                OrderDate = rfq.RfqDate,
                SourceRfqNo = rfqNo,             // 行级追溯回 RFQ
                Remarks = $"RFQ {rfqNo}",
                Lines = grp.Select(q => new PoLineCreateDto
                {
                    ItemId = lines[q.LineNo].ItemId,
                    Qty = lines[q.LineNo].Qty,
                    UnitPrice = q.QuotedPrice,   // ★成交价=询价价，不再走价表取价
                    RequiredDate = lines[q.LineNo].RequiredDate,
                }).ToList(),
            };
            var po = await _po.CreateAsync(dto, userName);
            poNos.Add(po.PoNo);
        }

        // 全部询价行都已选中转出 → RFQ 推 Converted
        var allLineNos = lines.Keys.ToHashSet();
        var selectedLineNos = selected.Select(q => q.LineNo).Distinct().ToHashSet();
        if (allLineNos.Count > 0 && allLineNos.All(selectedLineNos.Contains))
            rfq.Status = RfqStatus.Converted;

        rfq.Modifier = userName;
        rfq.ModifyDate = now;
        await _db.SaveChangesAsync();
        return poNos;
    }
}

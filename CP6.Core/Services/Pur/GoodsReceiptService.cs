using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>收货服务实现（采购 章03）。双基准 + WMS 委托 + 回写 PO 三累计锚。</summary>
public class GoodsReceiptService : IGoodsReceiptService
{
    private const string SeqKey = "GR";
    private const string ConfirmationOwner = "Main.Pur.GoodsReceipt";
    private const string ConfirmationContract = "Main.Pur.GoodsReceipt.Confirmation.v1";
    private const string AccrualBasis = "1";    // 着荷基准（收货即验收）

    private readonly CP6Context _db;
    private readonly IWmsReceiveService _wmsReceive;
    private readonly IWmsQcQuery _wmsQc;
    private readonly IPurchaseOrderService _po;
    private readonly ISeqService _seq;
    private readonly OperationsOwnerV1? _owner;

    public GoodsReceiptService(CP6Context db, IWmsReceiveService wmsReceive, IWmsQcQuery wmsQc,
        IPurchaseOrderService po, ISeqService seq, OperationsOwnerV1? owner = null)
    {
        _db = db;
        _wmsReceive = wmsReceive;
        _wmsQc = wmsQc;
        _po = po;
        _seq = seq;
        _owner = owner;
    }

    /// <inheritdoc />
    public async Task<GoodsReceipt?> GetAsync(string grNo)
    {
        var gr = await _db.GoodsReceipts.FirstOrDefaultAsync(g => g.GrNo == grNo && !g.IsDeleted);
        if (gr != null)
            gr.Lines = await _db.GoodsReceiptLines
                .Where(l => l.GrNo == grNo && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync();
        return gr;
    }

    /// <inheritdoc />
    public async Task<List<GoodsReceipt>> ListAsync(string? poNo = null, string? supplierId = null)
    {
        var q = _db.GoodsReceipts.Where(g => !g.IsDeleted);
        if (!string.IsNullOrWhiteSpace(poNo)) q = q.Where(g => g.PoNo == poNo);
        if (!string.IsNullOrWhiteSpace(supplierId)) q = q.Where(g => g.SupplierId == supplierId);
        return await q.OrderByDescending(g => g.ReceiptDate).ThenByDescending(g => g.GrNo).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<GoodsReceipt> ConfirmReceiveAsync(GrCreateDto dto, string? userName, Guid? operationId = null)
    {
        if (operationId == Guid.Empty)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED", 400);
        if (!_db.Database.IsNpgsql())
            return await ConfirmReceiveCoreAsync(dto, userName, request => _wmsReceive.ReceiveAsync(request, userName));
        if (_wmsReceive is not WmsReceiveServiceAdapter warehouse)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

        var journal = new MainOwnerOperationJournalV1(_db);
        var input = operationId.HasValue ? MainOwnerOperationJournalV1.Encode(dto) : null;
        MainOwnerOperationJournalV1.Original? original = null;
        var effects = new List<StockTransaction>();
        Func<Task>? publish = null;
        var receipt = await Owner.ExecuteAsync(OperationsOwnerV1.Inbound, "add", true, async (access, ct) =>
        {
            if (original is not null)
                return await DiscloseConfirmationAsync(original, access, input, ct);
            access.RequirePurchaseReceipt();
            var confirmed = await ConfirmReceiveCoreAsync(dto, access.Actor, async request =>
            {
                var received = await warehouse.ReceiveWithinPurchaseAsync(_db, access, request, effects, ct);
                publish = () => warehouse.PublishPurchaseReceiptAsync(received.ReceiptNo, request.WarehouseCd!, effects);
                return received.Result;
            });
            if (operationId.HasValue)
                await journal.Store(ConfirmationOwner, ConfirmationContract, operationId.Value, access.ActorId, input,
                    confirmed, [confirmed.Id], new { confirmed.GrNo, confirmed.PoNo, confirmed.WmsInboundNo }, ct);
            return confirmed;
        }, actionAtFence: operationId.HasValue ? SelectAction : null);
        if (publish is not null) await publish();
        return receipt;

        async Task<string> SelectAction(CancellationToken ct)
        {
            original = await journal.Read(ConfirmationOwner, operationId!.Value, ct);
            return original is null ? "add" : "query";
        }
    }

    public async Task<GoodsReceiptConfirmationScope> ConfirmationScopeAsync(CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql())
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        var access = await Owner.AuthorityAsync(OperationsOwnerV1.Inbound, "query", ct);
        return new(_db.CurrentTenantId, access.Actor.UserId);
    }

    public Task<GoodsReceipt?> ReadConfirmationAsync(Guid operationId, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED", 400);
        return Owner.ExecuteAsync<GoodsReceipt?>(OperationsOwnerV1.Inbound, "query", false, async (access, token) =>
        {
            var original = await new MainOwnerOperationJournalV1(_db).Read(ConfirmationOwner, operationId, token);
            return original is null ? null : await DiscloseConfirmationAsync(original, access, null, token);
        }, ct, readOnlyAuthorityFence: true);
    }

    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private static async Task<GoodsReceipt> DiscloseConfirmationAsync(MainOwnerOperationJournalV1.Original original,
        OperationsAccessV1 access, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != ConfirmationContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (input is not null && !MainOwnerOperationJournalV1.Matches(original, input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var receipt = JsonSerializer.Deserialize<GoodsReceipt>(original.Result);
        if (receipt is null || receipt.WmsInboundNo is null)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await access.RequireInbound(receipt.WmsInboundNo, false, ct);
        access.AtCommit(token => access.RequireInbound(receipt.WmsInboundNo, false, token));
        return receipt;
    }

    private async Task<GoodsReceipt> ConfirmReceiveCoreAsync(GrCreateDto dto, string? userName,
        Func<WmsReceiveRequest, Task<WmsReceiveResult>> receive)
    {
        if (dto.Lines == null || dto.Lines.Count == 0) throw new InvalidOperationException("E-PUR-030"); // 至少一行

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == dto.PoNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027"); // PO 不存在
        // 仅已确认（含收货/开票进行中）可收货；草稿/送审/取消/关闭不可
        if (po.Status is not (PoStatus.Confirmed or PoStatus.PartiallyReceived or PoStatus.Received or PoStatus.PartiallyInvoiced))
            throw new InvalidOperationException("E-PUR-032"); // PO 未确认或不可收货

        var poLines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == dto.PoNo && !l.IsDeleted && l.Status != 9).ToListAsync();

        var accrual = po.PostingBasis == AccrualBasis;   // 着荷基准

        var gr = new GoodsReceipt
        {
            GrNo = await _seq.NextAsync(SeqKey),
            PoNo = po.PoNo,
            SupplierId = po.SupplierId,
            ReceiptDate = dto.ReceiptDate ?? DateTime.Now,
            PostingBasis = po.PostingBasis,
            WarehouseCd = dto.WarehouseCd,
            Remarks = dto.Remarks,
            Status = accrual ? GrStatus.Completed : GrStatus.Inspecting,
            Creator = userName,
            CreateDate = DateTime.Now,
        };

        // 校验 + 超收挡
        var wmsLines = new List<WmsReceiveLine>();
        var lineNo = 0;
        var requestedByLine = new Dictionary<int, decimal>();
        foreach (var ld in dto.Lines)
        {
            if (ld.ReceivedQty <= 0) throw new InvalidOperationException("E-PUR-026"); // 数量须>0
            var poLine = poLines.FirstOrDefault(l => l.LineNo == ld.PoLineNo)
                         ?? throw new InvalidOperationException("E-PUR-034"); // PO 行不存在
            var remaining = poLine.Qty - poLine.ReceivedQty - requestedByLine.GetValueOrDefault(ld.PoLineNo);
            if (ld.ReceivedQty > remaining)
                throw new InvalidOperationException("E-PUR-031");
            requestedByLine[ld.PoLineNo] = requestedByLine.GetValueOrDefault(ld.PoLineNo) + ld.ReceivedQty; // 超收（超出订购量；容差细化随章04 MatchTolerance）

            gr.Lines.Add(new GoodsReceiptLine
            {
                GrNo = gr.GrNo,
                LineNo = ++lineNo,
                PoLineNo = poLine.LineNo,
                ItemId = poLine.ItemId,
                ReceivedQty = ld.ReceivedQty,
                AcceptedQty = accrual ? ld.ReceivedQty : 0m,
                RejectedQty = 0m,
                QcStatus = accrual ? "NONE" : "PENDING",
                Creator = userName,
                CreateDate = DateTime.Now,
            });
            wmsLines.Add(new WmsReceiveLine { PoLineNo = poLine.LineNo, ItemId = poLine.ItemId, Qty = ld.ReceivedQty });
        }

        // 委托 WMS 物理入库（库存唯一真相在 WMS）
        var wms = await receive(new WmsReceiveRequest
        {
            PoNo = po.PoNo, SupplierId = po.SupplierId, WarehouseCd = dto.WarehouseCd, Lines = wmsLines,
        });
        gr.WmsInboundNo = wms.WmsInboundNo;
        foreach (var gl in gr.Lines)
            gl.WmsReceiptDetailRef = wms.LineRefs.FirstOrDefault(r => r.PoLineNo == gl.PoLineNo)?.WmsReceiptDetailRef;

        // 回写 PO 三累计锚：ReceivedQty 累加；着荷→AcceptedQty 同步累加
        foreach (var gl in gr.Lines)
        {
            var poLine = poLines.First(l => l.LineNo == gl.PoLineNo);
            poLine.ReceivedQty += gl.ReceivedQty;
            if (accrual) poLine.AcceptedQty += gl.ReceivedQty;
            poLine.ModifyDate = DateTime.Now;
        }

        _db.GoodsReceipts.Add(gr);
        _db.GoodsReceiptLines.AddRange(gr.Lines);
        await _db.SaveChangesAsync();

        await _po.RefreshStatusAsync(po.PoNo);
        return gr;
    }

    /// <inheritdoc />
    public async Task<GoodsReceipt> ApplyQcResultAsync(string grNo, string? userName)
    {
        var gr = await _db.GoodsReceipts.FirstOrDefaultAsync(g => g.GrNo == grNo && !g.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-035"); // 收货单不存在
        if (gr.Status != GrStatus.Inspecting) throw new InvalidOperationException("E-PUR-036"); // 非检验中（着荷免检/已完成）

        var grLines = await _db.GoodsReceiptLines.Where(l => l.GrNo == grNo && !l.IsDeleted).ToListAsync();
        var poLines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == gr.PoNo && !l.IsDeleted).ToListAsync();

        var verdicts = await _wmsQc.QueryByReceiptAsync(gr.WmsInboundNo ?? string.Empty);

        var allResolved = true;
        foreach (var gl in grLines)
        {
            if (gl.QcStatus is "PASS" or "FAIL" or "NONE") continue;   // 已判定行跳过

            var v = verdicts.FirstOrDefault(x => x.PoLineNo == gl.PoLineNo);
            decimal accepted, rejected;
            string status;
            if (v == null)                          // 桩/无质检信息 → 默认全合格
            {
                accepted = gl.ReceivedQty; rejected = 0m; status = "PASS";
            }
            else if (v.QcStatus == "PENDING")       // 仍待检 → 不动
            {
                allResolved = false; continue;
            }
            else if (v.QcStatus == "FAIL")          // 整行不良
            {
                accepted = 0m; rejected = gl.ReceivedQty; status = "FAIL";
            }
            else                                    // PASS：收货量 − 不良 = 合格
            {
                rejected = Math.Min(v.RejectedQty, gl.ReceivedQty);
                accepted = gl.ReceivedQty - rejected; status = "PASS";
            }

            gl.AcceptedQty = accepted;
            gl.RejectedQty = rejected;
            gl.QcStatus = status;
            gl.ModifyDate = DateTime.Now;

            var poLine = poLines.FirstOrDefault(l => l.LineNo == gl.PoLineNo);
            if (poLine != null && accepted > 0)
            {
                poLine.AcceptedQty += accepted;
                poLine.ModifyDate = DateTime.Now;
            }
        }

        if (allResolved) gr.Status = GrStatus.Completed;
        gr.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        await _po.RefreshStatusAsync(gr.PoNo);
        return gr;
    }
}

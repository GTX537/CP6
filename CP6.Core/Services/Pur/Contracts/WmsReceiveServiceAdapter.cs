using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 物理入库适配器（P-D1 真实实现）。采购 GR 确认收货 → 委托 WMS 完整入库流程
/// （<see cref="IInboundService"/> 预定 CreateOrder→确定 ConfirmOrder→实绩 ConfirmReceipt），
/// 库存真增加（StockMovementService IN），返回真实入库号。替换 <see cref="StubWmsReceiveService"/>。
/// <para>
/// InboundOrder 留 PoNo 钩子；收货明细落约定收货暂存位 <c>RECV</c>（后续上架移正式库位）。
/// 返回 WmsInboundNo = InboundOrder.InboundNo，供检收 <see cref="IWmsQcQuery"/> 按 InboundNo 查质检判定。
/// </para>
/// </summary>
public class WmsReceiveServiceAdapter : IWmsReceiveService
{
    private readonly IInboundService _inbound;
    public WmsReceiveServiceAdapter(IInboundService inbound) => _inbound = inbound;

    private const string ReceivingLocation = "RECV";   // 收货暂存库位（后续上架移正式位）
    private const int PurchaseInboundType = 1;          // InboundType 1=购买入库

    /// <inheritdoc />
    public async Task<WmsReceiveResult> ReceiveAsync(WmsReceiveRequest request, string? userName)
    {
        var inboundNo = await _inbound.CreateOrderAsync(PurchaseOrder(request), userName);
        await _inbound.ConfirmOrderAsync(inboundNo, userName);
        var receipt = PurchaseReceipt(request);
        receipt.InboundNo = inboundNo;
        var receiptNo = await _inbound.ConfirmReceiptAsync(receipt, userName);
        return Result(request, inboundNo, receiptNo);
    }

    internal async Task<(WmsReceiveResult Result, string ReceiptNo)> ReceiveWithinPurchaseAsync(CP6Context db,
        OperationsAccessV1 access, WmsReceiveRequest request, List<StockTransaction> effects, CancellationToken ct)
    {
        var (inbound, receipt) = await NativeInbound.ReceivePurchaseWithinOwnerAsync(db, access, PurchaseOrder(request),
            PurchaseReceipt(request), effects, ct);
        return (Result(request, inbound, receipt), receipt);
    }

    internal Task PublishPurchaseReceiptAsync(string receiptNo, string warehouse, List<StockTransaction> effects)
        => NativeInbound.PublishReceipt(receiptNo, warehouse, effects);

    private InboundService NativeInbound => _inbound as InboundService
        ?? throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private static InboundOrderDto PurchaseOrder(WmsReceiveRequest request) => new()
    {
        PoNo = request.PoNo,
        InboundType = PurchaseInboundType,
        SupplierCd = request.SupplierId,
        WarehouseCd = request.WarehouseCd ?? string.Empty,
        ExpectedArrivalDate = DateTime.Now,
        Details = request.Lines.Select((l, index) => new InboundOrderDetailDto
        {
            LineNo = index + 1, ProductCd = l.ItemId, ExpectedQty = l.Qty,
        }).ToList(),
    };

    private static InboundReceiptDto PurchaseReceipt(WmsReceiveRequest request) => new()
    {
        SourceType = InboundSourceType.Purchase,
        WarehouseCd = request.WarehouseCd ?? string.Empty,
        Details = request.Lines.Select((l, index) => new InboundReceiptDetailDto
        {
            // InboundService numbers its own plan lines; a partial PO selection need not be contiguous.
            LineNo = index + 1, RefOrderLineNo = index + 1, ProductCd = l.ItemId,
            LotNo = "", ReceivedQty = l.Qty, LocationCd = ReceivingLocation,
        }).ToList(),
    };

    private static WmsReceiveResult Result(WmsReceiveRequest request, string inboundNo, string receiptNo) => new()
    {
        WmsInboundNo = inboundNo,   // 用 InboundNo（检收按 InboundNo 查 QcInspection）
        LineRefs = request.Lines.Select(l => new WmsReceiveLineRef
        {
            PoLineNo = l.PoLineNo, WmsReceiptDetailRef = receiptNo,
        }).ToList(),
    };
}

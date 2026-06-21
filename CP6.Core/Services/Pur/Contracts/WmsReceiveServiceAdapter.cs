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
        var warehouse = request.WarehouseCd ?? string.Empty;

        // 1. 建入库预定（PoNo 钩子）
        var inboundNo = await _inbound.CreateOrderAsync(new InboundOrderDto
        {
            PoNo = request.PoNo,
            InboundType = PurchaseInboundType,
            SupplierCd = request.SupplierId,
            WarehouseCd = warehouse,
            ExpectedArrivalDate = DateTime.Now,
            Details = request.Lines.Select(l => new InboundOrderDetailDto
            {
                LineNo = l.PoLineNo, ProductCd = l.ItemId, ExpectedQty = l.Qty,
            }).ToList(),
        }, userName);

        // 2. 确定预定（Draft→Confirmed）
        await _inbound.ConfirmOrderAsync(inboundNo, userName);

        // 3. 确认入库实绩（StockMovementService IN，库存真增加，落收货暂存位）
        var receiptNo = await _inbound.ConfirmReceiptAsync(new InboundReceiptDto
        {
            InboundNo = inboundNo,
            SourceType = InboundSourceType.Purchase,
            WarehouseCd = warehouse,
            Details = request.Lines.Select(l => new InboundReceiptDetailDto
            {
                LineNo = l.PoLineNo, RefOrderLineNo = l.PoLineNo, ProductCd = l.ItemId,
                LotNo = "", ReceivedQty = l.Qty, LocationCd = ReceivingLocation,
            }).ToList(),
        }, userName);

        return new WmsReceiveResult
        {
            WmsInboundNo = inboundNo,   // 用 InboundNo（检收按 InboundNo 查 QcInspection）
            LineRefs = request.Lines.Select(l => new WmsReceiveLineRef
            {
                PoLineNo = l.PoLineNo, WmsReceiptDetailRef = receiptNo,
            }).ToList(),
        };
    }
}

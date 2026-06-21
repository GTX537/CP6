using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// WMS 物理出库适配器（P2-D3 真实实现）。把外注发支給材委托映射到 WMS 的轻量库存变动
/// <see cref="IStockMovementService.ApplyAsync"/>（TxnType=OUT）——库存唯一真相在 WMS，采购不双写。
/// 不走完整出库单（引当/拣货/梱包）流程，外注发料是"资产位移"，直接按 物料 选库存源 OUT 扣减。
/// </summary>
public class WmsIssueServiceAdapter : IWmsIssueService
{
    private readonly IStockMovementService _stockMovement;
    private readonly CP6Context _db;

    public WmsIssueServiceAdapter(IStockMovementService stockMovement, CP6Context db)
    {
        _stockMovement = stockMovement;
        _db = db;
    }

    private const string SubcontractRelatedType = "SUBCONTRACT";   // StockTransaction.RelatedType：区分外注支給，非销售/生产领料
    private const string InsufficientStockError = "E-PUR-080";     // 支給材库存不足/无库存（WMS 不足异常的采购侧本地化码）

    /// <inheritdoc />
    public async Task<WmsIssueResult> IssueAsync(WmsIssueRequest request, string? userName)
    {
        // 选库存源：该物料（可选限定仓库）可用量最大的一行整批扣减（外注支給材通常从主料仓整批发，不做多行拆分）
        var q = _db.Stocks.Where(s => s.ProductCd == request.ItemId && !s.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.WarehouseCd))
            q = q.Where(s => s.WarehouseCd == request.WarehouseCd);
        var source = await q.OrderByDescending(s => s.AvailableQty).ThenByDescending(s => s.PhysicalQty).FirstOrDefaultAsync()
                     ?? throw new InvalidOperationException(InsufficientStockError);   // 无支給材库存可发

        try
        {
            var txnNo = await _stockMovement.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = source.WarehouseCd,
                LocationCd = source.LocationCd,
                ProductCd = source.ProductCd,
                LotNo = source.LotNo,
                Qty = request.Qty,
                RelatedNo = request.RefNo,
                RelatedType = SubcontractRelatedType,
                OperatorCd = userName,
                Remark = $"外注支給材 {request.RefNo}",
            });
            return new WmsIssueResult { IssueNo = txnNo, IssuedQty = request.Qty };
        }
        catch (InsufficientStockException)
        {
            throw new InvalidOperationException(InsufficientStockError);   // WMS 不足 → 采购本地化错误码（走现有错误处理链）
        }
    }
}

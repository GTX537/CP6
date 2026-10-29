using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

internal sealed class ReturnCorrectionTargetsV1(CP6Context db)
{
    internal async Task<ReturnCorrectionTargetV1[]> Read(ReturnQualityChoiceV1[] sources,CancellationToken ct)
    {
        var result =new List<ReturnCorrectionTargetV1>();
        foreach (var source in sources)
        {
            var s =source.Source; var line =s.LineId.ToString();
            var body =await db.Database.SqlQuery<byte[]>($"""
                SELECT p.body AS "Value" FROM cp6_stock_return_v1.plans p WHERE p.tenant_id={db.CurrentTenantId}
                  AND EXISTS(SELECT FROM cp6_stock_return_v1.root_receipts r WHERE r.tenant_id=p.tenant_id AND r.plan_id=p.plan_id AND r.line_key={line})
                """).SingleAsync(ct);
            var original =ReturnCorrectionStoreV1.Decode<StockRmaPlanV1>(body).Lines.Single(x => x.Line.Input.LineId ==s.LineId);
            var l =original.Line.Location; var data =original.Source;
            var pool =await db.Stocks.AsNoTracking().SingleAsync(x => x.WarehouseCd ==l.WarehouseCode && x.LocationCd ==l.LocationCode && x.ProductCd ==data.ProductCode && x.LotNo ==data.LotCode && !x.IsDeleted,ct);
            var key =new ReturnDispositionStockKeyV2(pool.Id,l.WarehouseId,l.LocationId,l.WarehouseCode,l.LocationCode,data.ProductCode,data.LotCode);
            var maps =data.RootMap;
            if (maps.Length ==0)
            {
                var root =await db.Database.SqlQuery<ReturnDispositionStockV2.RootRow>($"SELECT root_id AS \"Root\",range_from AS \"From\",range_to AS \"To\" FROM cp6_stock_return_v1.root_receipts WHERE tenant_id={db.CurrentTenantId} AND line_key={line}").SingleAsync(ct);
                var offset =root.From; var entries =new List<RmaRootRangeMapV1>();
                foreach (var range in Ranges.Read(s.Receipt.ReceivedRanges))
                { entries.Add(new(Ranges.Text(range.From),Ranges.Text(range.To),root.Root,Ranges.Text(offset),Ranges.Text(offset+range.To-range.From))); offset +=range.To-range.From; }
                maps =entries.ToArray();
            }
            var movement =Guid.Parse(s.Receipt.MovementRef.Id);
            result.Add(new("PHYSICAL_RECEIPT",s.LineId,s.Reference,"RECEIPT","wms-inbound-receipt",s,null,movement,[movement],
                s.Receipt.ReceivedRanges,maps,data.Dimensions,key,null,null));
            foreach (var row in await new ReturnDispositionStoreV2(db).Rows().Where(x => x.Line ==s.LineId && x.State =="APPLIED").ToArrayAsync(ct))
            {
                var p =ReturnDispositionStoreV2.Decode<ReturnDispositionPreparationV2>(row.Body); var receipt =ReturnDispositionStoreV2.Decode<ReturnDispositionReceiptV2>(row.Result!);
                var ids =receipt.StockResult.Result!.RelatedMovementIds;
                var destination =p.Stock.Destination;
                if (destination !=null) destination =destination with { PoolId =await db.Stocks.Where(x => x.WarehouseCd ==destination.Warehouse && x.LocationCd ==destination.Location && x.ProductCd ==destination.Product && x.LotNo ==destination.Lot && !x.IsDeleted).Select(x => x.Id).SingleAsync(ct) };
                result.Add(new("DISPOSITION",p.WorkId,receipt.Reference,p.Definition.Kind,p.Definition.Resource,s,p.WorkId,ids[0],ids,p.Ranges,maps,
                    p.Stock.Dimensions,p.Stock.Source,destination,receipt.QualityReleaseRef));
            }
        }
        return result.ToArray();
    }
    internal static ReturnStockSpanV1[] Spans(ReturnCorrectionTargetV1 target,FulfilmentRangeV1[] ranges)
    {
        var result =new List<ReturnStockSpanV1>();
        foreach (var r in Ranges.Read(ranges)) foreach (var m in target.RootMap)
        {
            var from =Math.Max(r.From,StockRmaPostingV1.Number(m.RmaFrom)); var to =Math.Min(r.To,StockRmaPostingV1.Number(m.RmaTo));
            if (from <to) result.Add(new(m.RootId,Ranges.Text(StockRmaPostingV1.Number(m.RootFrom)+from-StockRmaPostingV1.Number(m.RmaFrom)),Ranges.Text(StockRmaPostingV1.Number(m.RootFrom)+to-StockRmaPostingV1.Number(m.RmaFrom))));
        }
        return result.ToArray();
    }
    internal static async Task Scope(OperationsAccessV1 access,ReturnCorrectionTargetV1 target,CancellationToken ct)
    {
        if (!await access.Outbounds().AnyAsync(x => x.Id ==target.Source.OutboundId,ct)) throw Error("OBJECT_FORBIDDEN",403);
        var source =target.SourcePool; await access.RequireStockKey(source.Warehouse,source.Location,source.Product,source.Lot,false,ct);
        if (target.DestinationPool is {} destination) await access.RequireStockKey(destination.Warehouse,destination.Location,destination.Product,destination.Lot,false,ct);
    }
}

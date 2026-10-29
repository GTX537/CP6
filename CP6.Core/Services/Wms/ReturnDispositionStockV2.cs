using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

internal sealed class ReturnDispositionStockV2(CP6Context db,StockMovementService stock,StockQcService qc)
{
    internal sealed class RootRow
    {
        public Guid Root { get; init; }
        public decimal From { get; init; }
        public decimal To { get; init; }
    }
    private sealed class SequenceRow { public Guid Movement { get; init; } public long Sequence { get; init; } }
    private Task<Stock?> Pool(ReturnDispositionStockKeyV2 key,CancellationToken ct) => db.Stocks.AsNoTracking().SingleOrDefaultAsync(x =>
        x.WarehouseCd ==key.Warehouse && x.LocationCd ==key.Location && x.ProductCd ==key.Product && x.LotNo ==key.Lot && !x.IsDeleted,ct);
    private async Task<long> Revision(Guid pool,CancellationToken ct) => await db.Database.SqlQuery<long>($"SELECT coalesce(max(sequence),0) AS \"Value\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={db.CurrentTenantId} AND pool_id={pool}").SingleAsync(ct);
    private async Task<ReturnStockPoolVersionV1[]> Pools(ReturnDispositionStockKeyV2 source,ReturnDispositionStockKeyV2? destination,CancellationToken ct)
    {
        var values =new List<ReturnStockPoolVersionV1>();
        foreach (var key in destination ==null ? new[] { source } : new[] { source,destination })
        {
            var row =await Pool(key,ct); if (row !=null) values.Add(new(row.Id,(await Revision(row.Id,ct)).ToString(CultureInfo.InvariantCulture)));
        }
        return values.OrderBy(x => x.PoolId).ToArray();
    }
    private async Task<FulfilmentRefV1> Snapshot(ReturnDispositionStockKeyV2 source,ReturnDispositionStockKeyV2? destination,CancellationToken ct)
    {
        var a =await Pool(source,ct); var b =destination ==null ? null : await Pool(destination,ct);
        return RmaInboundAdapterV1.Ref("Main.Wms.ReturnStockSnapshot.v2",source.PoolId.ToString(),new { Source =a,Destination =b,Pools =await Pools(source,destination,ct) });
    }
    private async Task RequireLineage(ReturnDispositionStockKeyV2 key,Guid sourceMovement,CancellationToken ct)
    {
        var first =await db.Database.SqlQuery<long>($"SELECT sequence AS \"Value\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={db.CurrentTenantId} AND movement_id={sourceMovement}").SingleAsync(ct);
        var later =await db.Database.SqlQuery<SequenceRow>($"SELECT movement_id AS \"Movement\",sequence AS \"Sequence\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={db.CurrentTenantId} AND pool_id={key.PoolId} AND sequence>{first}").ToArrayAsync(ct);
        var ids =later.Select(x => x.Movement).ToArray();
        var negative =await db.StockTransactions.AsNoTracking().Where(x => ids.Contains(x.Id) && (x.TxnType ==WmsTxnType.OUT || x.Qty <0)).Select(x => x.Id).ToArrayAsync(ct);
        if (negative.Length ==0) return;
        var known =await db.Database.SqlQuery<Guid>($"SELECT movement_id AS \"Value\" FROM cp6_return_disposition_v2.stock_movements WHERE tenant_id={db.CurrentTenantId} AND movement_id=ANY({negative})").ToArrayAsync(ct);
        if (negative.Except(known).Any()) throw Error("RETURN_STOCK_LINEAGE_CHANGED",409);
    }
    private static ReturnStockSpanV1[] Map(RmaInboundSourceFactsV1 source,RmaQualitySourceV1 receipt,FulfilmentRangeV1[] ranges,RootRow[] actualRoots)
    {
        var maps =source.RootMap;
        if (maps.Length ==0)
        {
            var root =actualRoots.Single(); var cursor =root.From; var values =new List<RmaRootRangeMapV1>();
            foreach (var r in Ranges.Read(receipt.Receipt.ReceivedRanges).OrderBy(x => x.From))
            {
                values.Add(new(Ranges.Text(r.From),Ranges.Text(r.To),root.Root,Ranges.Text(cursor),Ranges.Text(cursor+r.To-r.From)));
                cursor +=r.To-r.From;
            }
            maps =values.ToArray();
        }
        var result =new List<ReturnStockSpanV1>();
        foreach (var r in Ranges.Read(ranges)) foreach (var m in maps)
        {
            var from =Math.Max(r.From,StockRmaPostingV1.Number(m.RmaFrom)); var to =Math.Min(r.To,StockRmaPostingV1.Number(m.RmaTo));
            if (from <to) result.Add(new(m.RootId,Ranges.Text(StockRmaPostingV1.Number(m.RootFrom)+from-StockRmaPostingV1.Number(m.RmaFrom)),
                Ranges.Text(StockRmaPostingV1.Number(m.RootFrom)+to-StockRmaPostingV1.Number(m.RmaFrom))));
        }
        return result.ToArray();
    }
    internal async Task<ReturnDispositionStockPlanV2> Prepare(OperationsAccessV1 access,Guid work,ReturnDispositionDefinitionV2 definition,
        RmaQualitySourceV1 source,ReturnQualityDecisionV1 decision,ReturnDispositionOptionV2 option,FulfilmentRangeV1[] ranges,FulfilmentQtyV1 quantity,string reason,CancellationToken ct)
    {
        var line =source.LineId.ToString();
        var body =await db.Database.SqlQuery<byte[]>($"""
            SELECT p.body AS "Value" FROM cp6_stock_return_v1.plans p WHERE p.tenant_id={db.CurrentTenantId}
            AND EXISTS(SELECT FROM cp6_stock_return_v1.root_receipts r WHERE r.tenant_id=p.tenant_id AND r.plan_id=p.plan_id AND r.line_key={line})
            """).SingleAsync(ct);
        var inbound =ReturnDispositionStoreV2.Decode<StockRmaPlanV1>(body).Lines.Single(x => x.Line.Input.LineId ==source.LineId);
        var l =inbound.Line.Location; var data =inbound.Source;
        var pool =await db.Stocks.AsNoTracking().SingleAsync(x => x.WarehouseCd ==l.WarehouseCode && x.LocationCd ==l.LocationCode && x.ProductCd ==data.ProductCode && x.LotNo ==data.LotCode && !x.IsDeleted,ct);
        var key =new ReturnDispositionStockKeyV2(pool.Id,l.WarehouseId,l.LocationId,l.WarehouseCode,l.LocationCode,data.ProductCode,data.LotCode);
        ReturnDispositionStockKeyV2? target =null;
        if (option.DestinationLocationId is {} destination)
        {
            var location =await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id ==destination && !x.IsDeleted,ct) ?? throw Error("DISPOSITION_DESTINATION_NOT_FOUND",404);
            var warehouse =await db.Warehouses.AsNoTracking().SingleAsync(x => x.WarehouseCd ==location.WarehouseCd && !x.IsDeleted,ct);
            if (destination ==key.LocationId || definition.Kind =="RESALE_STOCK" && !location.IsPickable) throw Error("DISPOSITION_DESTINATION_INVALID",422);
            target =new(Guid.Empty,warehouse.Id,location.Id,warehouse.WarehouseCd,location.LocationCd,data.ProductCode,data.LotCode);
            target =target with { PoolId =(await Pool(target,ct))?.Id ?? Guid.Empty };
        }
        await Validate(access,key,target,source,definition.Kind,StockRmaPostingV1.Number(quantity.Value),ct);
        var roots =await db.Database.SqlQuery<RootRow>($"SELECT root_id AS \"Root\",range_from AS \"From\",range_to AS \"To\" FROM cp6_stock_return_v1.root_receipts WHERE tenant_id={db.CurrentTenantId} AND line_key={line}").ToArrayAsync(ct);
        var spans =Map(data,source,ranges,roots); var at =DateTimeOffset.UtcNow;
        var sourceUse =new ReturnStockWireSourceV1(definition.Owner,"RMA_DISPOSITION",work.ToString(),line,work.ToString(),"1");
        var occurrence =new ReturnStockOccurrenceV1(definition.Owner,work.ToString(),work.ToString());
        var allowance =RmaInboundAdapterV1.Ref(definition.Owner,work+":allowance",new { Source =source.Reference,decision.DecisionRef,ranges,quantity,Option =option.Reference });
        var scope =new ReturnStockScopeV1("RMA-DISPOSITION:"+db.CurrentTenantId,source.Reference,Digest(new { source.Reference,ranges }));
        var authority =RmaInboundAdapterV1.Ref(definition.Owner,work+":authority",new { option.AuthorityRef,Actor =access.ActorId,decision.DecisionRef });
        var manifest =new ReturnStockManifestV1("STK-SOURCE-EXECUTION-1",sourceUse,definition.PostingKind,definition.Action,at,
            [new(line,sourceUse,occurrence,quantity.Value,data.Dimensions.BaseUomRef,quantity.Value,data.Dimensions.BaseUomRef,allowance,Digest(new { sourceUse,ranges,spans,option.Reference }))],authority,scope);
        var manifestRef =RmaInboundAdapterV1.Ref(definition.Owner,work+":manifest",manifest);
        var sourcePlace =new ReturnStockPlaceV1("LOCATION",key.WarehouseId,key.LocationId);
        var use =new ReturnStockSourceUseV1(sourceUse,allowance,data.Dimensions.BaseUomRef,null);
        object postingLine =definition.PostingKind switch {
            "MOVE" => new { clientLineKey =line,spans,sourcePlace,destination =new ReturnStockPlaceV1("LOCATION",target!.WarehouseId,target.LocationId),inputQuantity =quantity.Value,sourceUse =use,retainedClaimIds =Array.Empty<Guid>() },
            "SHIP" => new { clientLineKey =line,spans,sourcePlace,destination =new { kind ="EXTERNAL",custodyReceiptRef =option.ExternalCustodyRef },inputQuantity =quantity.Value,sourceUse =use,reservationMode ="AUTHORIZED_FREE",claimSpends =Array.Empty<object>(),freeIssueAuthority =option.AuthorityRef },
            _ => new { clientLineKey =line,direction ="LOSS",sourceUse =use,dimensions =(object?)null,place =sourcePlace,inputQuantity =quantity.Value,spans,countOrDisposalDecision =option.BusinessRef }
        };
        var request =new ReturnStockPlanRequestV1(sourceUse,manifestRef,at,new(definition.Action,data.Dimensions.OriginPurposeRef,null,null),await Pools(key,target,ct),
            JsonSerializer.SerializeToElement(new { kind =definition.PostingKind,lines =new[] { postingLine } },RmaFormalStoreV1.Json),reason);
        return new(key,target,data.Dimensions,spans,await Snapshot(key,target,ct),manifest,request,new(work,"1",Digest(request)),new(work,"1",Digest(new { request,manifest })));
    }
    private async Task Validate(OperationsAccessV1 access,ReturnDispositionStockKeyV2 source,ReturnDispositionStockKeyV2? destination,
        RmaQualitySourceV1 receipt,string kind,decimal quantity,CancellationToken ct)
    {
        await access.RequireStockKey(source.Warehouse,source.Location,source.Product,source.Lot,false,ct);
        await access.GuardNewLocationUse(source.Warehouse,source.Location,ct);
        var pool =(await Pool(source,ct))!;
        if (pool.RecallFlag) throw Error("RETURN_STOCK_RECALL_BLOCKED",409);
        if (pool.PhysicalQty <quantity || pool.AvailableQty <quantity) throw Error("RETURN_STOCK_QUANTITY_UNAVAILABLE",409);
        await RequireLineage(source,Guid.Parse(receipt.Receipt.MovementRef.Id),ct);
        if (destination !=null)
        {
            await access.RequireStockKey(destination.Warehouse,destination.Location,destination.Product,destination.Lot,true,ct);
            await access.GuardNewLocationUse(destination.Warehouse,destination.Location,ct);
            var location =await db.Locations.AsNoTracking().SingleAsync(x => x.Id ==destination.LocationId,ct);
            if (kind =="RESALE_STOCK" && !location.IsPickable) throw Error("DISPOSITION_DESTINATION_INVALID",422);
            var total =await db.Stocks.Where(x => x.WarehouseCd ==destination.Warehouse && x.LocationCd ==destination.Location && !x.IsDeleted).SumAsync(x => x.PhysicalQty,ct);
            if (location.CapacityQty >0 && total+location.ReservedCapacityQty+quantity >location.CapacityQty) throw Error("RETURN_LOCATION_CAPACITY_EXCEEDED",409);
        }
    }
    internal async Task<(ReturnStockResponseV1 StockResult,FulfilmentRefV1? QualityRelease)> Commit(OperationsAccessV1 access,ReturnDispositionPreparationV2 p,Guid command,CancellationToken ct)
    {
        var plan =p.Stock; var quantity =StockRmaPostingV1.Number(p.Quantity.Value);
        if (plan.PoolSnapshotRef !=await Snapshot(plan.Source,plan.Destination,ct)) throw Error("STOCK_POOL_VERSION_CONFLICT",409);
        await Validate(access,plan.Source,plan.Destination,p.Source,p.Definition.Kind,quantity,ct);
        var release =p.Definition.Kind =="RESALE_STOCK" ? await qc.ReleaseReturnRangeAsync(access,p,ct) : null;
        using var inheritance =stock.InheritTransactions(access);
        var numbers =await stock.ApplyReturnDispositionWithinOwnerAsync(p,access.Actor,release,ct);
        var movements =await db.StockTransactions.AsNoTracking().Where(x => numbers.Contains(x.TxnNo)).OrderBy(x => x.TxnNo).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var movement in movements)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_disposition_v2.stock_movements(tenant_id,work_id,movement_id) VALUES({db.CurrentTenantId},{p.WorkId},{movement})",ct);
        var ledger =await StockMovementSequenceV1.Entries(db,movements,ct); var pools =await Pools(plan.Source,plan.Destination,ct); var id =Guid.NewGuid();
        var result =new ReturnStockResultV1(new("Main.Wms.StockDisposition.v2",id.ToString(),"1"),movements.Length ==1 ? movements[0] : null,
            null,null,null,null,movements,null,null,ledger,[],pools,Digest(new { p.WorkId,plan.Spans,movements,ledger,pools,release }));
        return (new(id,new(p.Definition.Owner,p.WorkId.ToString(),command),"SUCCEEDED","COMMITTED",result,"APPLIED",false,[],[],"1",[],true,p.Option.Simulation),release);
    }
}

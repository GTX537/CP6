using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

internal sealed class ReturnCorrectionStockV1(CP6Context db,StockMovementService stock,StockQcService qc)
{
    private readonly ReturnDispositionStockV2 pools =new(db,stock,qc);
    private async Task Validate(OperationsAccessV1 access,ReturnCorrectionCandidateV1 c,CancellationToken ct)
    {
        var target =c.Target; var source =target.SourcePool; var destination =target.DestinationPool;
        await ReturnCorrectionTargetsV1.Scope(access,target,ct);
        foreach (var key in destination ==null ? new[] { source } : new[] { source,destination })
        {
            await access.GuardNewLocationUse(key.Warehouse,key.Location,ct);
            var pool =(await pools.Pool(key,ct))!;
            if (pool.RecallFlag) throw Error("RETURN_STOCK_RECALL_BLOCKED",409);
            // Native moves without exact root attribution make the original range unprovable.
            var first =key ==source ? Guid.Parse(target.Source.Receipt.MovementRef.Id) : await db.Database.SqlQuery<Guid>($"SELECT movement_id AS \"Value\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={db.CurrentTenantId} AND pool_id={key.PoolId} AND movement_id=ANY({target.MovementIds})").SingleAsync(ct);
            await pools.RequireLineage(key,first,ct);
        }
        var amount =StockRmaPostingV1.Number(c.Quantity.Value);
        var outgoing =destination ?? (target.Axis =="PHYSICAL_RECEIPT" ? source : null);
        if (outgoing !=null)
        {
            var pool =(await pools.Pool(outgoing,ct))!;
            if (pool.PhysicalQty <amount || pool.AvailableQty <amount) throw Error("RETURN_STOCK_QUANTITY_UNAVAILABLE",409);
        }
        if (target.Axis =="DISPOSITION")
        {
            var location =await db.Locations.AsNoTracking().SingleAsync(x => x.Id ==source.LocationId,ct);
            var quantity =await db.Stocks.Where(x => x.WarehouseCd ==source.Warehouse && x.LocationCd ==source.Location && !x.IsDeleted).SumAsync(x => x.PhysicalQty,ct);
            if (location.CapacityQty >0 && quantity+location.ReservedCapacityQty+amount >location.CapacityQty) throw Error("RETURN_LOCATION_CAPACITY_EXCEEDED",409);
        }
    }
    internal async Task<ReturnCorrectionPreparationV1> Prepare(OperationsAccessV1 access,ReturnCorrectionCandidateV1 c,ReturnCorrectionApprovalV1 approval,CancellationToken ct)
    {
        await Validate(access,c,ct); var target =c.Target; var at =DateTimeOffset.UtcNow;
        var snapshot =await pools.Snapshot(target.SourcePool,target.DestinationPool,ct); var versions =await pools.Pools(target.SourcePool,target.DestinationPool,ct);
        var spans =ReturnCorrectionTargetsV1.Spans(target,c.Ranges);
        var closure =RmaInboundAdapterV1.Ref("Main.Wms.SourceCorrectionScopeClosed.v1",c.Id.ToString(),new { Original =target.Reference,c.Ranges,c.ContentDigest });
        var key ="RMA-CORRECTION:"+c.Id+":"+c.Version;
        return new(RmaInboundAdapterV1.Ref("Main.Wms.StockCorrectionIntent.v1",c.Id.ToString(),new { c.ContentDigest,snapshot,versions,spans,approval.Reference,closure,key,at }),key,snapshot,versions,spans,approval.Reference,closure,at);
    }
    internal async Task<(ReturnStockResponseV1 Stock,FulfilmentRefV1? Quality)> Apply(OperationsAccessV1 access,ReturnCorrectionCandidateV1 c,ReturnCorrectionPreparationV1 p,Guid command,CancellationToken ct)
    {
        var target =c.Target;
        if (p.SnapshotRef !=await pools.Snapshot(target.SourcePool,target.DestinationPool,ct)) throw Error("STOCK_POOL_VERSION_CONFLICT",409);
        await Validate(access,c,ct);
        var release =target.Kind =="RESALE_STOCK" ? await qc.CorrectReturnReleaseAsync(access,c,ct) : null;
        using var inheritance =stock.InheritTransactions(access);
        var numbers =await stock.ApplyReturnCorrectionWithinOwnerAsync(c,access.Actor,release,ct);
        var movements =await db.StockTransactions.AsNoTracking().Where(x => numbers.Contains(x.TxnNo)).OrderBy(x => x.TxnNo).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var movement in movements)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.stock_movements(tenant_id,proposal_id,movement_id) VALUES({db.CurrentTenantId},{c.Id},{movement})",ct);
        var ledger =await StockMovementSequenceV1.Entries(db,movements,ct); var versions =await pools.Pools(target.SourcePool,target.DestinationPool,ct);
        var id =Guid.NewGuid();
        var result =new ReturnStockResultV1(new("Main.Wms.StockCorrection.v1",id.ToString(),"1"),movements.Length ==1 ? movements[0] : null,
            null,null,c.Id,null,movements,null,null,ledger,[],versions,Digest(new { c.ContentDigest,p.Spans,movements,ledger,versions,release }));
        return (new(id,new("Main.Wms.ReturnCorrection.v1",c.Id.ToString(),command),"SUCCEEDED","COMMITTED",result,"APPLIED",false,[],[],"1",[],true,true),release);
    }
}

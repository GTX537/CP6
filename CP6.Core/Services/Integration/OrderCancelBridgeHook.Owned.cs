using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Core.Services.Integration;

public partial class OrderCancelBridgeHook
{
    // Bounded delegation from an already authorized Order.cancel inside the same Owner transaction.
    // It does not grant general warehouse/manufacturing actions or accept arbitrary child keys.
    internal async Task<OwnedCascade> PrepareOwnedAsync(CP6Context db,string number,string actor,StockTransactionInheritanceV1 inheritance)
    {
        if(!ReferenceEquals(_db,db) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction==null
            || _outboundService is not OutboundService outbound || !outbound.OwnsCancellation(db)
            || _woService is not WorkOrderService wo || !wo.OwnsCancellation(db))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        // These legacy links are soft references, so locking only the order cannot fence a new child.
        // A short table write fence covers raw SQL and legacy writers too. No external I/O within it.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'");
        var tables=new[]{Table<WorkOrder>(db),Table<OutboundOrder>(db),Table<OutboundOrderDetail>(db),Table<WmsSequence>(db)};
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE "+string.Join(",",tables)+" IN SHARE ROW EXCLUSIVE MODE");
        var orders=await db.WorkOrders.AsNoTracking().Where(w=>w.WebOrderNo==number && !w.IsDeleted).OrderBy(w=>w.Id).ToListAsync();
        var keys=orders.Select(w=>w.WorkOrderNo).ToArray();
        var outbounds=await db.OutboundOrders.AsNoTracking().Where(o=>!o.IsDeleted
            && (o.WebOrderNo==number || (o.WorkOrderNo!=null && keys.Contains(o.WorkOrderNo)))).OrderBy(o=>o.Id).ToListAsync();
        var ids=outbounds.Select(o=>o.Id).ToArray();
        // Native PG comparisons are authoritative; never infer identity from folded CLR strings.
        if(await db.OutboundOrders.AnyAsync(o=>ids.Contains(o.Id) &&
            ((o.WebOrderNo!=null && o.WebOrderNo!="" && o.WebOrderNo!=number)
             || (o.WorkOrderNo!=null && o.WorkOrderNo!="" && !keys.Contains(o.WorkOrderNo)))))
            throw new OrderPriceCorrectionException("CASCADE_OWNER_CONFLICT",403);
        var edges=await (from o in db.OutboundOrders.AsNoTracking()
                         join w in db.WorkOrders.AsNoTracking() on o.WorkOrderNo equals w.WorkOrderNo
                         where ids.Contains(o.Id) && !w.IsDeleted && w.WebOrderNo==number
                         select new {Outbound=o.Id,WorkOrder=w.Id}).ToArrayAsync();
        inheritance.RequireStockInheritanceContext(db);
        return new(this,orders,outbounds,edges.Select(e=>(e.Outbound,e.WorkOrder)).ToArray(),actor,inheritance);
    }
    private static string Table<T>(CP6Context db) where T:class
    {
        var entity=db.Model.FindEntityType(typeof(T))!;
        return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!,entity.GetSchema()??"public");
    }
    internal sealed class OwnedCascade
    {
        private readonly OrderCancelBridgeHook owner;
        private readonly List<WorkOrder> workOrders;
        private readonly List<OutboundOrder> outbounds;
        private readonly (Guid Outbound,Guid WorkOrder)[] edges;
        private readonly string actor;
        private readonly StockTransactionInheritanceV1 inheritance;
        private readonly List<(string Number,bool Shipping,List<StockTransaction> Transactions)> notifications=[];
        internal OwnedCascade(OrderCancelBridgeHook owner,List<WorkOrder> workOrders,List<OutboundOrder> outbounds,
            (Guid Outbound,Guid WorkOrder)[] edges,string actor,StockTransactionInheritanceV1 inheritance)
        {this.owner=owner;this.workOrders=workOrders;this.outbounds=outbounds;this.edges=edges;this.actor=actor;this.inheritance=inheritance;}
        internal async Task<OrderCancelHookResult> RunAsync(bool execute,Guid correlationId)
        {
            var db=owner._db;
            var wps=workOrders.Select(w=>new WorkOrderProbe {WorkOrderNo=w.WorkOrderNo,Status=w.Status,
                AutoCancellable=WorkOrderStatus.IsCancellable(w.Status),Cancelled=w.Status==WorkOrderStatus.Cancelled?true:null}).ToList();
            var ops=outbounds.Select(o=>new OutboundProbe {OutboundNo=o.OutboundNo,Status=o.Status,OutboundType=o.OutboundType,
                AutoCancellable=o.Status is OutboundOrderStatus.Draft or OutboundOrderStatus.Confirmed or OutboundOrderStatus.Allocated,
                Cancelled=o.Status==OutboundOrderStatus.Cancelled?true:null}).ToList();
            if(!execute)return new(){Success=true,WorkOrders=wps,Outbounds=ops};
            for(int i=0;i<outbounds.Count;i++)
            {
                var row=outbounds[i];var probe=ops[i];if(!probe.AutoCancellable || probe.Cancelled==true)continue;
                var prior=db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity.Id).ToHashSet();
                var succeeded=await AttemptAsync(async()=>
                {
                    var details=await db.OutboundOrderDetails.AsNoTracking().Where(d=>d.OutboundNo==row.OutboundNo && !d.IsDeleted).OrderBy(d=>d.Id).ToListAsync();
                    // Outbound/line membership is frozen. Release only its stored allocation, never caller amounts.
                    var releases=new Dictionary<Guid,decimal>();
                    foreach(var d in details)
                    {
                        if(d.ShippedQty!=0 || d.AllocatedQty<0 || (row.Status!=OutboundOrderStatus.Allocated && d.AllocatedQty!=0))throw new InvalidOperationException("CANCEL_ALLOCATION_INVALID");
                        if(row.Status!=OutboundOrderStatus.Allocated || d.AllocatedQty==d.ShippedQty)continue;
                        var warehouse=d.WarehouseCd??row.WarehouseCd;var location=d.LocationCd??"";var lot=d.LotNo??"";
                        var stock=await db.Stocks.AsNoTracking().SingleOrDefaultAsync(s=>!s.IsDeleted && s.WarehouseCd==warehouse
                            && s.LocationCd==location && s.ProductCd==d.ProductCd && s.LotNo==lot);
                        if(stock==null)throw new InvalidOperationException("CANCEL_ALLOCATION_INVALID");
                        releases[stock.Id]=releases.GetValueOrDefault(stock.Id)+d.AllocatedQty-d.ShippedQty;
                    }
                    foreach(var release in releases.OrderBy(x=>x.Key))
                    {
                        var locked=await db.Stocks.FromSqlRaw($"SELECT * FROM {Table<Stock>(db)} WHERE \"TenantId\"=@tenant AND \"Id\"=@id FOR UPDATE",
                            new NpgsqlParameter("tenant",db.CurrentTenantId),new NpgsqlParameter("id",release.Key)).AsNoTracking().SingleAsync();
                        if(locked.AllocatedQty<release.Value)throw new InvalidOperationException("CANCEL_ALLOCATION_INVALID");
                    }
                    await ((OutboundService)owner._outboundService).CancelFromOrderAsync(row.OutboundNo,actor,inheritance);
                });
                probe.Cancelled=succeeded;probe.Message=succeeded?"Cancelled":"CANCEL_CHILD_FAILED";
                if(succeeded)notifications.Add((row.OutboundNo,row.OutboundType==OutboundType.Shipping,
                    db.ChangeTracker.Entries<StockTransaction>().Where(e=>!prior.Contains(e.Entity.Id)).Select(e=>e.Entity).ToList()));
            }
            for(int i=0;i<workOrders.Count;i++)
            {
                var row=workOrders[i];var probe=wps[i];if(!probe.AutoCancellable || probe.Cancelled==true)continue;
                // A failed/active material outbound keeps its WO active for reconciliation.
                if(edges.Where(e=>e.WorkOrder==row.Id).Any(e=>ops[outbounds.FindIndex(o=>o.Id==e.Outbound)].Cancelled!=true))
                {probe.Cancelled=false;probe.Message="CANCEL_DEPENDENCY_NOT_RELEASED";continue;}
                probe.Cancelled=await AttemptAsync(async()=>
                {
                    if(row.CompletedQty!=0 || row.DefectQty!=0 || row.ActualStartDate!=null)
                        throw new InvalidOperationException("CANCEL_ALREADY_PROCESSED");
                    await ((WorkOrderService)owner._woService).CancelFromOrderAsync(row.WorkOrderNo,"受注取消連動",actor);
                });
                probe.Message=probe.Cancelled==true?"Cancelled":"CANCEL_CHILD_FAILED";
            }
            return new(){Success=true,FullyCascaded=wps.All(w=>w.Cancelled==true)&&ops.All(o=>o.Cancelled==true),
                WorkOrders=wps,Outbounds=ops,Message="Cancellation results are committed with the parent order"};
        }
        private async Task<bool> AttemptAsync(Func<Task> action)
        {
            var db=owner._db;var tx=db.Database.CurrentTransaction!;
            db.ChangeTracker.DetectChanges();
            var snapshot=db.ChangeTracker.Entries().Select(e=>new {e.Entity,e.State,Current=e.CurrentValues.Clone(),Original=e.OriginalValues.Clone()}).ToArray();
            var ownership=inheritance.Checkpoint();
            await tx.CreateSavepointAsync("order_cancel_child");
            try {await action();await tx.ReleaseSavepointAsync("order_cancel_child");return true;}
            catch(Exception error) when(!Fatal(error))
            {
                await tx.RollbackToSavepointAsync("order_cancel_child");
                inheritance.RestoreAfterSavepointRollback(ownership);
                var existing=snapshot.Select(e=>e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                foreach(var entry in db.ChangeTracker.Entries().ToArray())if(!existing.Contains(entry.Entity))entry.State=EntityState.Detached;
                foreach(var saved in snapshot)
                {var entry=db.Entry(saved.Entity);entry.CurrentValues.SetValues(saved.Current);entry.OriginalValues.SetValues(saved.Original);entry.State=saved.State;}
                await tx.ReleaseSavepointAsync("order_cancel_child");
                owner._logger.LogError(error,"Order cancellation child rolled back; dependent operations will not proceed");
                return false;
            }
        }
        private static bool Fatal(Exception error) => error is OrderPriceCorrectionException or CurrentPermissionUnavailableException or OperationCanceledException
            || error is PostgresException {SqlState:"40P01" or "40001" or "55P03"}
            || error.InnerException!=null && Fatal(error.InnerException);
        internal async Task PublishCommittedAsync()
        {
            foreach(var item in notifications)
                try {await ((OutboundService)owner._outboundService).PublishOrderCancellationAsync(item.Number,item.Shipping,actor,item.Transactions);}
                catch(Exception error){owner._logger.LogError(error,"Order cancellation notification failed after confirmed commit for {Number}",item.Number);}
        }
    }
}

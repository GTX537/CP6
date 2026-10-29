using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

internal sealed record StockRmaLineV1(RmaPhysicalLineV1 Line, RmaInboundSourceFactsV1 Source, ReturnStockSpanV1[] Spans);
internal sealed record StockRmaPlanV1(Guid Id, Guid Work, RmaInboundPreparationV1 Preparation, StockRmaLineV1[] Lines);

// Typed WMS-IN participant in the existing OperationsOwner transaction. It never starts or commits a second UoW.
internal sealed class StockRmaPostingV1(CP6Context db, StockMovementService stock)
{
    private sealed class ReturnedRange
    {
        public Guid Root { get; init; }
        public decimal From { get; init; }
        public decimal To { get; init; }
    }
    internal static decimal Number(string s) => decimal.Parse(s,CultureInfo.InvariantCulture);
    internal async Task<ReturnStockPoolVersionV1[]> Pools(StockRmaLineV1[] lines, CancellationToken ct)
    {
        var values = new Dictionary<Guid,ReturnStockPoolVersionV1>();
        foreach (var item in lines)
        {
            var l = item.Line.Location; var s = item.Source;
            var pool = await db.Stocks.AsNoTracking().SingleOrDefaultAsync(x => x.WarehouseCd == l.WarehouseCode && x.LocationCd == l.LocationCode
                && x.ProductCd == s.ProductCode && x.LotNo == s.LotCode && !x.IsDeleted,ct);
            if (pool == null) continue;
            if (pool.UnitCd != s.Dimensions.BaseUomRef.Id || pool.OwnerType != s.OwnerType || pool.OwnerCd != s.OwnerCode)
                throw Error("RETURN_DESTINATION_POOL_IDENTITY_CONFLICT",409);
            var revision = await db.Database.SqlQuery<long>($"SELECT coalesce(max(sequence),0) AS \"Value\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={db.CurrentTenantId} AND pool_id={pool.Id}").SingleAsync(ct);
            values[pool.Id] = new(pool.Id,revision.ToString(CultureInfo.InvariantCulture));
        }
        return values.Values.OrderBy(x => x.PoolId).ToArray();
    }
    internal async Task ValidateRoots(StockRmaLineV1[] lines, CancellationToken ct)
    {
        var all = lines.SelectMany(x => x.Spans).ToArray(); var roots = all.Select(x => x.RootId).Distinct().ToArray();
        var saved = await db.Database.SqlQuery<ReturnedRange>($"""
            SELECT root_id AS "Root",range_from AS "From",range_to AS "To" FROM cp6_stock_return_v1.root_receipts
            WHERE tenant_id={db.CurrentTenantId} AND root_id=ANY({roots})
            """).ToArrayAsync(ct);
        foreach (var group in all.GroupBy(x => x.RootId))
        {
            var pending = group.Select(x => new Ranges.Segment(Number(x.From),Number(x.To))).ToArray();
            if (pending.Sum(x => x.To-x.From) != Ranges.Measure(pending)) throw Error("RETURN_ROOT_RANGE_OVERLAP",409);
            var history = lines.SelectMany(x => x.Source.PriorReturnedSpans).Where(x => x.RootId == group.Key).Select(x => new Ranges.Segment(Number(x.From),Number(x.To)));
            if (Ranges.Intersects(pending,history.Concat(saved.Where(x => x.Root == group.Key).Select(x => new Ranges.Segment(x.From,x.To)))))
                throw Error("RETURN_ROOT_ALREADY_RECEIVED",409);
        }
    }
    internal Task Save(StockRmaPlanV1 plan, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_stock_return_v1.plans(tenant_id,plan_id,work_id,reference,intent_ref,body)
        VALUES({db.CurrentTenantId},{plan.Id},{plan.Work},{Encode(plan.Preparation.StockPostingPlanRef)},{Encode(plan.Preparation.FrozenIntentRef)},{Encode(plan)})
        """,ct);
    internal async Task<StockRmaPlanV1> Read(Guid id, CancellationToken ct)
        => JsonSerializer.Deserialize<StockRmaPlanV1>(await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_return_v1.plans WHERE tenant_id={db.CurrentTenantId} AND plan_id={id}").SingleAsync(ct),RmaFormalStoreV1.Json)!;
    internal async Task<ReturnStockResponseV1?> Query(Guid id, CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_return_v1.operations WHERE tenant_id={db.CurrentTenantId} AND plan_id={id}").SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<ReturnStockResponseV1>(bytes,RmaFormalStoreV1.Json)!;
    }
    internal async Task<ReturnStockResponseV1> Commit(StockRmaPlanV1 plan, OperationsAccessV1 access, Guid command, string receiptNo, CancellationToken ct)
    {
        var previous = await Query(plan.Id,ct);
        if (previous != null) throw Error("STOCK_EXECUTION_ALREADY_TERMINAL",409);
        using var inheritance = stock.InheritTransactions(access);
        var movements = new List<Guid>(); var roots = new List<ReturnStockRootV1>();
        foreach (var item in plan.Lines)
        {
            var line = item.Line; var source = item.Source;
            var txnNo = await stock.ApplyWithinOwnerAsync(new StockMovementRequest { TxnType = WmsTxnType.IN,
                WarehouseCd = line.Location.WarehouseCode,LocationCd = line.Location.LocationCode,ProductCd = source.ProductCode,
                LotNo = source.LotCode,Qty = Number(line.Input.Quantity.Value),UnitCd = source.Dimensions.BaseUomRef.Id,
                OwnerType = source.OwnerType,OwnerCd = source.OwnerCode,RelatedType = "INBOUND-RMA",RelatedNo = receiptNo,
                OperatorCd = access.Actor,ReceiveDate = line.Input.OccurredAt.UtcDateTime },ct);
            var txn = await db.StockTransactions.SingleAsync(x => x.TxnNo == txnNo,ct); movements.Add(txn.Id);
            var spans = source.RootMap.Length == 0 ? new[] { new ReturnStockSpanV1(Guid.NewGuid(),"0",line.Input.Quantity.Value) } : item.Spans;
            foreach (var group in spans.GroupBy(x => x.RootId)) roots.Add(new(line.Input.LineId.ToString(),group.Key,Ranges.Text(group.Sum(x => Number(x.To)-Number(x.From)))));
            for (var i = 0; i < spans.Length; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_stock_return_v1.root_receipts(tenant_id,root_id,plan_id,line_key,ordinal,range_from,range_to,movement_id,body)
                    VALUES({db.CurrentTenantId},{spans[i].RootId},{plan.Id},{line.Input.LineId.ToString()},{i},{Number(spans[i].From)},{Number(spans[i].To)},{txn.Id},
                      {Encode(new { Span = spans[i],source.Dimensions,source.Reference,source.OriginalMovementRef,PhysicalLine = line.Reference,NativeMovementId = txn.Id })})
                    """,ct);
        }
        var ledger = await StockMovementSequenceV1.Entries(db,movements.ToArray(),ct);
        var pools = await Pools(plan.Lines,ct); var id = Guid.NewGuid();
        var result = new ReturnStockResultV1(new("Main.Wms.StockReturn.v1",id.ToString(),"1"),movements.Count == 1 ? movements[0] : null,
            null,null,null,null,movements.ToArray(),null,null,ledger,roots.ToArray(),pools,Digest(new { plan.Id,movements,roots,ledger,pools }));
        var response = new ReturnStockResponseV1(id,new("WMS-IN-01",plan.Preparation.StockPostingPlanRequest.Source.ExecutionKey,command),
            "SUCCEEDED","COMMITTED",result,"APPLIED",false,[],[],"1",[],true,plan.Lines.Any(x => x.Source.Simulation));
        await Store(plan.Id,response,ct); return response;
    }
    internal async Task<FulfilmentRefV1> Seal(StockRmaPlanV1 plan, Guid command, string reason, CancellationToken ct)
    {
        if (await Query(plan.Id,ct) != null) throw Error("STOCK_EXECUTION_ALREADY_TERMINAL",409);
        var proof = new FulfilmentRefV1("Main.Wms.StockNoEffect.v1",plan.Id.ToString(),"1",Digest(new { plan.Id,command,reason,State = "SEALED_NO_EFFECT" }));
        await Store(plan.Id,new(Guid.NewGuid(),new("WMS-IN-01",plan.Preparation.StockPostingPlanRequest.Source.ExecutionKey,command),
            "SEALED_NO_EFFECT","NO_NEW_EFFECT",null,"SEALED_NO_EFFECT",false,[],[],"1",[],true,plan.Lines.Any(x => x.Source.Simulation)),ct);
        return proof;
    }
    private Task Store(Guid plan, ReturnStockResponseV1 result, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_return_v1.operations(tenant_id,plan_id,body) VALUES({db.CurrentTenantId},{plan},{Encode(result)})",ct);
}

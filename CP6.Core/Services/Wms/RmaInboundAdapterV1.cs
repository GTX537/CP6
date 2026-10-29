using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

/// <summary>WMS-IN-01 physical-return participant. RMA supplies accepted facts; only Stock writes quantities.</summary>
public sealed class RmaInboundAdapterV1(CP6Context db, IStockMovementService stock, IWmsSequenceService sequence, IRmaInboundSourcesV1 sources)
{
    private readonly StockRmaPostingV1 posting = new(db,(StockMovementService)stock);
    private static JsonElement Wire<T>(T value) => JsonSerializer.SerializeToElement(value,RmaFormalStoreV1.Json);
    internal static FulfilmentRefV1 Ref(string owner, string id, object value) => new(owner,id,"1",Digest(value));
    internal static FulfilmentRefV1 PreparationRef(RmaInboundPreparationV1 p) => Ref("WMS-IN-01.Preparation",p.StockPostingPlanRef.Id.ToString(),p);
    private static ReturnStockSpanV1[] Map(StockRmaLineV1 item)
    {
        if (item.Source.RootMap.Length == 0) return [];
        var ranges = item.Line.Input.Ranges.Select(x => new Ranges.Segment(StockRmaPostingV1.Number(x.From),StockRmaPostingV1.Number(x.To))).ToArray();
        var map = item.Source.RootMap;
        if (!Ranges.Subset(ranges,map.Select(x => new Ranges.Segment(StockRmaPostingV1.Number(x.RmaFrom),StockRmaPostingV1.Number(x.RmaTo)))))
            throw Error("RETURN_SOURCE_MAP_INCOMPLETE",409);
        return (from r in ranges from m in map
                let start = Math.Max(r.From,StockRmaPostingV1.Number(m.RmaFrom)) let end = Math.Min(r.To,StockRmaPostingV1.Number(m.RmaTo))
                where start < end let offset = StockRmaPostingV1.Number(m.RootFrom)-StockRmaPostingV1.Number(m.RmaFrom)
                select new ReturnStockSpanV1(m.RootId,Ranges.Text(start+offset),Ranges.Text(end+offset))).ToArray();
    }
    internal async Task<RmaInboundPreparationV1[]> Prepare(OperationsAccessV1 parent, RmaTrustedScopeV1 scope, FulfilmentRefV1 rma,
        FulfilmentRefV1 workRef, RmaPhysicalWorkOriginalV1 work, int revision, string reason, CancellationToken ct)
    {
        var access = await parent.ActionAsync(OperationsOwnerV1.Receipt,"post",ct);
        var items = new List<StockRmaLineV1>();
        foreach (var line in work.Lines)
        {
            await access.RequireLocation(line.Location.WarehouseCode,line.Location.LocationCode,ct);
            var outbound = Guid.Parse(line.Share.Original.ShipmentRef.Id);
            if (!await access.Outbounds().AnyAsync(x => x.Id == outbound,ct)) throw Error("OBJECT_FORBIDDEN",403);
            var source = await sources.ReadAsync(db.CurrentTenantId,scope.SiteId,line,ct);
            var item = new StockRmaLineV1(line,source,[]); items.Add(item with { Spans = Map(item) });
        }
        await posting.ValidateRoots(items.ToArray(),ct);
        var result = new List<RmaInboundPreparationV1>();
        // A Stock plan has one posting kind and one original movement. Multiple plans remain one physical-work transaction.
        foreach (var group in items.GroupBy(x => x.Source.OriginalMovementRef))
        {
            var lines = group.ToArray(); var id = Guid.NewGuid(); var original = group.Key;
            var kind = original == null ? "RECEIPT" : "RETURN"; var action = original == null ? "RECEIVE" : "RETURN";
            var source = new ReturnStockWireSourceV1("WMS-IN-01","RMA_RECEIPT",work.ApplicationId.ToString(),id.ToString(),id.ToString(),"1");
            var bindings = new List<RmaInboundBindingV1>(); var stockLines = new List<object>(); var parts = new List<ReturnStockPartV1>();
            foreach (var item in lines)
            {
                var l = item.Line; var s = item.Source; var destination = new ReturnStockPlaceV1("LOCATION",l.Location.WarehouseId,l.Location.LocationId);
                var allowance = Ref("WMS-IN-01.Allowance",l.Input.LineId.ToString(),new { l.Reference,l.Share.RangeClaimRef,l.Input.Ranges });
                var sourceUse = new ReturnStockSourceUseV1(source,allowance,s.Dimensions.BaseUomRef,null);
                object line; JsonElement route;
                if (original == null)
                {
                    var quality = new ReturnStockQualityV1("REQUIRES_INSPECTION",s.QualityBasisRef);
                    line = new ReturnStockReceiptLineV1(l.Input.LineId.ToString(),s.Dimensions,destination,l.Input.Quantity.Value,sourceUse,quality);
                    route = Wire(new { route = "RECEIPT_NEW_SOURCE",originalMovementRef = (object?)null,newSourceReasonRef = s.NewSourceReasonRef,dimensions = s.Dimensions,destination,qualityBasis = quality });
                }
                else
                {
                    var quality = new ReturnStockReturnQualityV1("PENDING_INSPECTION",s.QualityBasisRef);
                    line = new ReturnStockReturnLineV1(l.Input.LineId.ToString(),item.Spans,s.SourcePlace!.Value,destination,l.Input.Quantity.Value,sourceUse,[],quality);
                    route = Wire(new { route = "RETURN_ORIGINAL_ROOT",originalMovementRef = original,rootSpans = item.Spans,returnQuality = quality,destination });
                }
                stockLines.Add(line);
                parts.Add(new(l.Input.LineId.ToString(),source,new("WMS-IN-01",l.Input.LineId.ToString(),"RECEIVE"),l.Input.Quantity.Value,s.Dimensions.BaseUomRef,
                    l.Input.Quantity.Value,s.Dimensions.BaseUomRef,allowance,Digest(line)));
                var map = Ref("WMS-IN-01.SourceRangeMap",l.Input.LineId.ToString(),new { s.Reference,l.Input.Ranges,item.Spans,s.Dimensions });
                var binding = Ref("WMS-IN-01.Binding",l.Input.LineId.ToString(),new { work.ApplicationRef,work.AuthorizationRef,l.Input.ReturnShareRef,map,route });
                bindings.Add(new(binding,rma,work.ApplicationRef,work.AuthorizationRef,l.Input.ReturnShareRef,l.Share.RangeClaimRef,
                    l.Input.Ranges.Select(x => new FulfilmentRangeV1(x.From,x.To)).ToArray(),l.Input.Quantity,map,null,route));
            }
            var at = work.RecordedAt; var scopeRef = Ref("Main.Wms.RmaScope.v1",scope.WarehouseId.ToString(),scope);
            var authority = Ref("WMS-IN-01.Authority",id.ToString(),new { work.AuthorizationRef,scopeRef,PermissionVersion = access.Authorities.Last().Source.SourcePermissionVersion });
            var target = scope.EnvironmentId+":"+scope.Mode+":"+scope.SiteId;
            var manifest = new ReturnStockManifestV1("STK-SOURCE-EXECUTION-1",source,kind,action,at,parts.ToArray(),authority,new(target,scopeRef,Digest(lines.Select(x => x.Line.Reference))));
            var manifestRef = Ref("WMS-IN-01.ExecutionManifest",id.ToString(),manifest);
            var data = original == null ? Wire(new { kind,lines = stockLines }) : Wire(new { kind,originalMovementRef = original,lines = stockLines });
            var request = new ReturnStockPlanRequestV1(source,manifestRef,at,new(action,lines[0].Source.Dimensions.OriginPurposeRef,null,null),await posting.Pools(lines,ct),data,reason);
            var planRef = new ReturnStockPlanRefV1(id,"1",Digest(request));
            var intentRef = new ReturnStockPlanRefV1(id,"1",Digest(new { request,manifest }));
            var prep = new RmaInboundPreparationV1(Ref("WMS-IN-01.Adapter","WMS-IN-RMA-ADAPTER-1",new { Version = 1 }),workRef,revision,bindings.ToArray(),
                manifestRef,manifest,Digest(manifest),request,planRef,intentRef,target,"COMPLETE",[],true,
                Ref("Main.PostgreSql.Database.v1",Digest(db.Database.GetDbConnection().Database),new { Database = db.Database.GetDbConnection().Database }),
                Ref("Main.OperationsOwner.v1","WMS-IN-01",new { Participant = "WMS-IN-01",Transaction = "OperationsOwnerV1" }));
            await posting.Save(new(id,work.WorkId,prep,lines),ct); result.Add(prep);
        }
        return result.ToArray();
    }
    internal async Task<RmaInboundReceiptResultV1[]> Commit(OperationsAccessV1 parent, RmaInboundPreparationV1[] preparations, Guid command,
        FulfilmentRefV1 currentHead, CancellationToken ct)
    {
        var access = await parent.ActionAsync(OperationsOwnerV1.Receipt,"post",ct); var plans = new List<StockRmaPlanV1>();
        foreach (var preparation in preparations)
        {
            var plan = await posting.Read(preparation.StockPostingPlanRef.Id,ct); plans.Add(plan);
            if (!plan.Preparation.StockPostingPlanRequest.ExpectedPools.SequenceEqual(await posting.Pools(plan.Lines,ct))) throw Error("STOCK_POOL_VERSION_CONFLICT",409);
        }
        await posting.ValidateRoots(plans.SelectMany(x => x.Lines).ToArray(),ct);
        foreach (var outbound in plans.SelectMany(x => x.Lines).Select(x => Guid.Parse(x.Line.Share.Original.ShipmentRef.Id)).Distinct())
            if (!await access.Outbounds().AnyAsync(x => x.Id == outbound,ct)) throw Error("OBJECT_FORBIDDEN",403);
        foreach (var location in plans.SelectMany(x => x.Lines).GroupBy(x => x.Line.Location.LocationId))
        {
            var l = location.First().Line.Location;
            await access.GuardNewLocationUse(l.WarehouseCode,l.LocationCode,ct);
            var master = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == l.LocationId && !x.IsDeleted,ct);
            var physical = await db.Stocks.Where(x => x.WarehouseCd == l.WarehouseCode && x.LocationCd == l.LocationCode && !x.IsDeleted).SumAsync(x => x.PhysicalQty,ct);
            if (master.CapacityQty > 0 && physical + master.ReservedCapacityQty + location.Sum(x => StockRmaPostingV1.Number(x.Line.Input.Quantity.Value)) > master.CapacityQty)
                throw Error("RETURN_LOCATION_CAPACITY_EXCEEDED",409);
        }
        var results = new List<RmaInboundReceiptResultV1>();
        foreach (var plan in plans)
        {
            var receiptNo = await sequence.NextAsync("RC"); var at = DateTimeOffset.UtcNow;
            var receipt = new InboundReceipt { Id = Guid.NewGuid(),ReceiptNo = receiptNo,SourceType = "RMA",Status = InboundReceiptStatus.Confirmed,
                WarehouseCd = plan.Lines[0].Line.Location.WarehouseCode,ReceiveDateTime = at.UtcDateTime,OperatorCd = access.Actor,Creator = access.Actor };
            db.InboundReceipts.Add(receipt); await db.SaveChangesAsync(ct);
            var response = await posting.Commit(plan,access,command,receiptNo,ct); var stockResult = response.Result!;
            var operation = Ref("WMS-IN-01",receipt.Id.ToString(),new { ReceiptId = receipt.Id,receiptNo,PlanId = plan.Id,stockResult.Receipt });
            var stockReceipt = Ref(stockResult.Receipt.Owner,stockResult.Receipt.Id,stockResult);
            var physical = new List<RmaPhysicalReceiptV1>();
            for (var i = 0; i < plan.Lines.Length; i++)
            {
                var item = plan.Lines[i]; var l = item.Line;
                var txn = await db.StockTransactions.AsNoTracking().SingleAsync(x => x.Id == stockResult.RelatedMovementIds[i],ct);
                db.InboundReceiptDetails.Add(new() { Id = Guid.NewGuid(),ReceiptNo = receiptNo,LineNo = i+1,ProductCd = item.Source.ProductCode,
                    LotNo = item.Source.LotCode,ReceivedQty = StockRmaPostingV1.Number(l.Input.Quantity.Value),UnitCd = item.Source.Dimensions.BaseUomRef.Id,
                    LocationCd = l.Location.LocationCode,StockTxnNo = txn.TxnNo,Creator = access.Actor });
                var movement = new FulfilmentRefV1("Main.Wms.StockMovement.v1",txn.Id.ToString(),Convert.ToBase64String(txn.RowVersion),Digest(new { txn.Id,txn.TxnNo,txn.Qty }));
                physical.Add(new(operation,l.Input.ReturnShareRef,l.Input.LineId.ToString(),movement,l.Location.WarehouseId,l.Location.LocationId,l.Location.OptionRef,
                    l.Input.Ranges.Select(x => new FulfilmentRangeV1(x.From,x.To)).ToArray(),l.Input.Quantity,l.Input.LotRefs,l.Input.SerialRefs,l.Input.LpnRefs,
                    l.Input.OccurredAt,at,"POSTED",stockReceipt,operation));
            }
            await db.SaveChangesAsync(ct);
            foreach (var outbound in plan.Lines.Select(x => Guid.Parse(x.Line.Share.Original.ShipmentRef.Id)).Distinct())
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.inbound_receipt_sources(tenant_id,receipt_id,outbound_id) VALUES({db.CurrentTenantId},{receipt.Id},{outbound})",ct);
            access.AtCommit(t => access.RequireReceipt(receiptNo,t));
            var p = plan.Preparation; var prepRef = PreparationRef(p);
            var digest = Digest(new { operation,prepRef,physical,response });
            var scopeRef = p.ExecutionManifest.Scope.DefinitionRef!;
            results.Add(new("WMS-IN-01",receipt.Id.ToString(),"1",digest,currentHead,
                Wire(new { owner = "WMS-IN-01",contractRef = p.AdapterContractRef,scopeRef,businessTargetRef = p.WorkRef,
                    sourceVersionRefs = p.Bindings.Select(x => x.SourceRangeMapRef).ToArray(),queryKey = plan.Id.ToString(),expectedHeadRef = currentHead,expectedResultDigest = digest }),
                prepRef,p.Bindings.Select(x => x.BindingRef).ToArray(),p.ManifestRef,p.ManifestContentDigest,operation,
                plan.Lines.Select(x => x.Line.Reference).ToArray(),physical.ToArray(),response,
                Ref("WMS-IN-01.SourceRangeMaps",plan.Id.ToString(),p.Bindings.Select(x => x.SourceRangeMapRef)),[],at,at));
        }
        return results.ToArray();
    }
    internal async Task<FulfilmentRefV1[]> Seal(RmaInboundPreparationV1[] preparations, Guid command, string reason, CancellationToken ct)
    {
        var proofs = new List<FulfilmentRefV1>();
        foreach (var p in preparations) proofs.Add(await posting.Seal(await posting.Read(p.StockPostingPlanRef.Id,ct),command,reason,ct));
        return proofs.ToArray();
    }
    internal async Task<JsonElement[]> Query(RmaPhysicalWorkViewV1 work, CancellationToken ct)
    {
        var observedAt = DateTimeOffset.UtcNow;
        if (work.Preparations.Length == 0)
            return [work.NoEffectProofRef == null
                ? Wire(new { status = "PENDING",result = (object?)null,workRef = work.WorkRef,observedAt,reasonCodes = new[] { "NOT_PREPARED" } })
                : Wire(new { status = "SEALED_NO_EFFECT",result = (object?)null,proofRef = work.NoEffectProofRef,observedAt,reasonCodes = new[] { "SEALED_BEFORE_PREPARATION" } })];
        var outcomes = new List<JsonElement>();
        foreach (var preparation in work.Preparations)
        {
            var operation = await posting.Query(preparation.StockPostingPlanRef.Id,ct);
            if (operation == null)
            {
                var request = new RmaInboundQueryV1(new(new("WMS-IN-01",preparation.StockPostingPlanRequest.Source.ExecutionKey),preparation.StockPostingPlanRef.Digest,null),PreparationRef(preparation));
                outcomes.Add(Wire(new { status = "UNKNOWN",result = (object?)null,query = request,observedAt,canInferNoEffect = false,reasonCodes = new[] { "STOCK_OPERATION_NOT_OBSERVED" } }));
            }
            else if (operation.Status == "SEALED_NO_EFFECT")
                outcomes.Add(Wire(new { status = "SEALED_NO_EFFECT",result = (object?)null,proofRef = work.NoEffectProofRef!,observedAt,reasonCodes = new[] { "STOCK_EXECUTION_SEALED" } }));
            else
                outcomes.Add(Wire(new { status = "FOUND",result = work.Results.Single(x => x.PreparationRef == PreparationRef(preparation)),observedAt }));
        }
        return outcomes.ToArray();
    }
    internal async Task ReadResults(OperationsAccessV1 parent, RmaInboundReceiptResultV1[] results, CancellationToken ct)
    {
        if (results.Length == 0) return;
        var access = await parent.SourceAsync(OperationsOwnerV1.Receipt,ct);
        foreach (var result in results)
        {
            var id = Guid.Parse(result.ReceiptOperationRef.Id);
            var receipt = await db.InboundReceipts.AsNoTracking().SingleAsync(x => x.Id == id,ct);
            await access.RequireReceipt(receipt.ReceiptNo,ct);
        }
    }
}

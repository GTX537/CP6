using System.Numerics;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms.StockKernel;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.StockKernel.StockDirectMoveValuesV1;
namespace CP6.Core.Services.Wms.Transfer;

public sealed record TransferRootRangeV1(string SegmentKey,string DemandFrom,string DemandTo,string RequestFrom,string RequestTo,StockSpanV1 RootSpan);
public sealed record TransferRootRequestV1(StockRefV1 Ref,StockRefV1 SourceDemandRef,StockRefV1 StageRef,string OriginalBusinessKey,
    StockRefV1 OrderRef,StockRefV1 SelectionRef,Guid CreatorId,Guid? DepartmentId,StockRefV1 FromLocationRef,StockRefV1 ToLocationRef,
    string Quantity,StockRefV1 BaseUomRef,TransferRootRangeV1[] RangeMap,Guid OperationId);
public sealed record TransferRootStateV1(TransferRootRequestV1 Request,string State,Guid? ApproverId,Guid? PerformerId,
    string? Device,string? OccurrenceKey,StockMoveReceiptV1? Receipt);

// REP's accepted direct-MOVE adapter. Request, exact SourceDemand budget, complete one-Stage
// mapping and Stock outcome share the parent's Main transaction. No old MobileTaskV2 calls.
internal sealed class TransferRootMoveV1(CP6Context db,Guid environment,OperationsOwnerV1 authority)
{
    private Guid Tenant=>db.CurrentTenantId;
    private StockDirectMoveV1 Stock=>new(db,environment);
    private static StockRefV1 Ref(string kind,object body)=>new("WMS_TRANSFER",Guid.NewGuid().ToString("D"),"1","TRANSFER-ROOT/"+kind,"1",Hash(body));
    internal async Task Fence(CancellationToken ct)
    {
        if(db.Database.CurrentTransaction==null)throw Error("TRANSFER_TRANSACTION_REQUIRED",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_transfer_root_v1.requests,cp6_transfer_root_v1.heads IN SHARE ROW EXCLUSIVE MODE",ct);
        await Stock.Fence(true,ct);
    }
    private sealed record StateRow(byte[] Body,string State,Guid? ApproverId,Guid? PerformerId,string? Device,string? OccurrenceKey,byte[]? Receipt);
    internal async Task<TransferRootStateV1?> Find(string key,CancellationToken ct)
    {
        var row=await db.Database.SqlQuery<StateRow>($"""
            SELECT r.body AS "Body",h.state AS "State",h.approver_id AS "ApproverId",h.performer_id AS "PerformerId",h.device AS "Device",h.occurrence_key AS "OccurrenceKey",h.receipt AS "Receipt"
            FROM cp6_transfer_root_v1.requests r JOIN cp6_transfer_root_v1.heads h USING(tenant_id,environment_id,canonical)
            WHERE r.tenant_id={Tenant} AND r.environment_id={environment} AND r.canonical={key}
            """).SingleOrDefaultAsync(ct);
        return row==null?null:new(Decode<TransferRootRequestV1>(row.Body),row.State,row.ApproverId,row.PerformerId,row.Device,row.OccurrenceKey,row.Receipt==null?null:Decode<StockMoveReceiptV1>(row.Receipt));
    }
    internal static StockMoveInputV1 MoveInput(TransferRootRequestV1 r)=>new(r.OriginalBusinessKey,r.OperationId,r.SourceDemandRef,r.StageRef,r.FromLocationRef,r.ToLocationRef,r.RangeMap.Select(x=>x.RootSpan).ToArray());
    internal async Task<TransferRootStateV1> Register(string key,StockRefV1 order,StockRefV1 selection,Guid actor,Guid? department,
        StockRefV1 from,StockRefV1 to,StockRefV1 baseUnit,StockSpanV1[] spans,Guid operation,CancellationToken ct)
    {
        await Fence(ct);var old=await Find(key,ct);
        if(old!=null)
        {
            var r=old.Request;
            if(r.OrderRef!=order||r.SelectionRef!=selection||r.FromLocationRef!=from||r.ToLocationRef!=to||r.BaseUomRef!=baseUnit||r.OperationId!=operation||!r.RangeMap.Select(x=>x.RootSpan).SequenceEqual(spans))throw Error("TRANSFER_ORIGINAL_RANGE_CONFLICT");
            return old;
        }
        BigInteger offset=0;var ranges=new List<TransferRootRangeV1>();
        foreach(var span in spans)
        {var next=offset+Amount(span);ranges.Add(new("segment-"+ranges.Count,Text(offset),Text(next),Text(offset),Text(next),span));offset=next;}
        Number(Text(offset),positive:true);
        var demand=Ref("source-demand",new{order,selection,quantity=Text(offset),from,to,baseUnit});
        var request=Ref("request",new{order,selection,demand,ranges});var stage=Ref("stage",new{request,ranges});
        var body=new TransferRootRequestV1(request,demand,stage,key,order,selection,actor,department,from,to,Text(offset),baseUnit,ranges.ToArray(),operation);
        await Stock.Check(MoveInput(body),ct);
        foreach(var span in spans)if((await Stock.Root(span.RootRef,ct)).Root.Dimension.BaseUomRef!=baseUnit)throw Error("TRANSFER_BASE_UNIT_MISMATCH",422);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_root_v1.requests VALUES({Tenant},{environment},{key},{order.Owner},{order.Id},{selection.Version},{Bytes(body)})",ct);
        foreach(var range in ranges)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_root_v1.source_ranges VALUES({Tenant},{environment},{key},{range.SegmentKey},{range.DemandFrom}::numeric,{range.DemandTo}::numeric,{range.RequestFrom}::numeric,{range.RequestTo}::numeric,{Key(range.RootSpan.RootRef)},{range.RootSpan.From}::numeric,{range.RootSpan.To}::numeric)",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_root_v1.heads(tenant_id,environment_id,canonical,state) VALUES({Tenant},{environment},{key},'APPROVAL_PENDING')",ct);
        await Event(key,"REGISTERED",body,ct);return new(body,"APPROVAL_PENDING",null,null,null,null,null);
    }
    private async Task Visible(OperationsAccessV1 a,TransferRootRequestV1 r,CancellationToken ct)
    {
        await a.RequireReplenishPlanningDocument(r.CreatorId,r.DepartmentId,ct);
        var from=await Stock.Location(r.FromLocationRef,ct);var to=await Stock.Location(r.ToLocationRef,ct);
        var ids=new[]{from.NativeLocationId,to.NativeLocationId};
        if(await a.TransferLocations().CountAsync(x=>ids.Contains(x.Id),ct)!=2)throw Error("TRANSFER_LOCATION_FORBIDDEN",403);
    }
    private async Task CurrentApproval(TransferRootStateV1 v,CancellationToken ct)
    {
        if(v.ApproverId==null)throw Error("TRANSFER_APPROVAL_REQUIRED");
        var current=await authority.TransferApprovalAuthorityAsync(v.ApproverId.Value,ct);
        await Visible(new OperationsAccessV1(db,authority,current),v.Request,ct);
    }
    internal async Task<TransferRootStateV1> Approve(string key,OperationsAccessV1 parent,CancellationToken ct)
    {
        await Fence(ct);var v=await Find(key,ct)??throw Error("TRANSFER_REQUEST_NOT_FOUND",404);
        var grant=await authority.AuthorityAsync(OperationsOwnerV1.TransferRequest,"approve",ct);parent.Authorities.Add(grant);
        var access=new OperationsAccessV1(db,authority,grant);await Visible(access,v.Request,ct);
        if(parent.ActorId==v.Request.CreatorId)throw Error("TRANSFER_INDEPENDENT_APPROVER_REQUIRED",403);
        if(v.State!="APPROVAL_PENDING")throw Error("TRANSFER_APPROVAL_STATE_INVALID");
        await Stock.Check(MoveInput(v.Request),ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_root_v1.heads SET state='PREPARED',approver_id={parent.ActorId} WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={key}",ct);
        await Event(key,"APPROVED",new{actor=parent.ActorId,v.Request.Ref},ct);return v with{State="PREPARED",ApproverId=parent.ActorId};
    }
    internal async Task<TransferRootStateV1> Dispatch(string key,OperationsAccessV1 parent,string device,string occurrence,CancellationToken ct)
    {
        await Fence(ct);var v=await Find(key,ct)??throw Error("TRANSFER_REQUEST_NOT_FOUND",404);
        var grant=await authority.AuthorityAsync(OperationsOwnerV1.TransferRequest,"prepare",ct);parent.Authorities.Add(grant);
        await Visible(new OperationsAccessV1(db,authority,grant),v.Request,ct);await CurrentApproval(v,ct);
        parent.AtCommit(t=>CurrentApproval(v,t));
        if(v.State!="PREPARED")throw Error("TRANSFER_DISPATCH_STATE_INVALID");
        await Stock.Check(MoveInput(v.Request),ct);
        // The server's registered device / performer relation is the authority, not a body-supplied actor.
        if(!await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_transfer_root_v1.devices WHERE tenant_id={Tenant} AND environment_id={environment} AND device={device} AND performer_id={parent.ActorId} AND enabled) AS \"Value\"").SingleAsync(ct))throw Error("TRANSFER_DEVICE_NOT_REGISTERED",403);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_root_v1.occurrences VALUES({Tenant},{environment},{device},{occurrence},{key})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_root_v1.heads SET state='TASK_ISSUED',performer_id={parent.ActorId},device={device},occurrence_key={occurrence} WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={key}",ct);
        await Event(key,"DISPATCHED",new{actor=parent.ActorId,device,occurrence},ct);return v with{State="TASK_ISSUED",PerformerId=parent.ActorId,Device=device,OccurrenceKey=occurrence};
    }
    internal async Task<TransferRootStateV1> Complete(string key,OperationsAccessV1 parent,CancellationToken ct)
    {
        await Fence(ct);var v=await Find(key,ct)??throw Error("TRANSFER_REQUEST_NOT_FOUND",404);
        var grant=await authority.AuthorityAsync(OperationsOwnerV1.TransferRequest,"execute",ct);parent.Authorities.Add(grant);await Visible(new OperationsAccessV1(db,authority,grant),v.Request,ct);
        if(v.State is "POSTED" or "CLOSED_NO_EFFECT")return v;
        if(v.State!="TASK_ISSUED")throw Error("TRANSFER_EXECUTION_STATE_INVALID");
        if(v.PerformerId!=parent.ActorId)throw Error("TRANSFER_PERFORMER_REQUIRED",403);
        await CurrentApproval(v,ct);parent.AtCommit(t=>CurrentApproval(v,t));
        if(!await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM cp6_transfer_root_v1.devices WHERE tenant_id={Tenant} AND environment_id={environment} AND device={v.Device} AND performer_id={parent.ActorId} AND enabled) AS \"Value\"").SingleAsync(ct))throw Error("TRANSFER_DEVICE_NOT_REGISTERED",403);
        var receipt=await Stock.Move(MoveInput(v.Request),ct);
        var state=receipt.State=="POSTED"?"POSTED":"CLOSED_NO_EFFECT";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_root_v1.heads SET state={state},receipt={Bytes(receipt)} WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={key}",ct);
        await Event(key,state,receipt,ct);return v with{State=state,Receipt=receipt};
    }
    internal async Task<TransferRootStateV1> Stop(string key,OperationsAccessV1 parent,bool physicalNotOccurred,CancellationToken ct)
    {
        await Fence(ct);var v=await Find(key,ct)??throw Error("TRANSFER_REQUEST_NOT_FOUND",404);
        var grant=await authority.AuthorityAsync(OperationsOwnerV1.TransferRequest,"close-stage",ct);parent.Authorities.Add(grant);await Visible(new OperationsAccessV1(db,authority,grant),v.Request,ct);
        if(v.State is "POSTED" or "CLOSED_NO_EFFECT")return v;
        if(v.State=="TASK_ISSUED"&&!physicalNotOccurred)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_root_v1.heads SET state='RECONCILIATION' WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={key}",ct);
            await Event(key,"RECONCILIATION",new{actor=parent.ActorId},ct);return v with{State="RECONCILIATION"};
        }
        if(v.State=="RECONCILIATION")throw Error("TRANSFER_ACTUAL_DISPOSITION_REQUIRED");
        // Source closure and Stock's permanent no-effect decision commit together. No new execution survives this fence.
        var receipt=await Stock.Seal(MoveInput(v.Request),ct);var state=receipt.State=="POSTED"?"POSTED":"CLOSED_NO_EFFECT";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_transfer_root_v1.heads SET state={state},receipt={Bytes(receipt)} WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={key}",ct);
        await Event(key,state,receipt,ct);return v with{State=state,Receipt=receipt};
    }
    private Task<int> Event(string key,string action,object body,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_transfer_root_v1.events VALUES({Tenant},{environment},{Guid.NewGuid()},{key},{action},{Bytes(body)})",ct);
}

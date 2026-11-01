using System.Numerics;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms.StockKernel;
using CP6.Core.Services.Wms.Transfer;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
using Store=CP6.Core.Services.Wms.Replenish.ReplenishPlanningStoreV1;
using K=CP6.Core.Services.Wms.StockKernel.StockDirectMoveValuesV1;
namespace CP6.Core.Services.Wms.Replenish;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepStockOpeningV1(RepScopeV1 Scope,StockOpeningV1 Opening,string Device);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RepExecutionCommandV1(RepCommandV1 Command,[property:JsonRequired]string? Device,[property:JsonRequired]string? OccurrenceKey,[property:JsonRequired]bool PhysicalNotOccurred);
public sealed record RepTransferLinkV1(string Owner,string OriginalBusinessKey,RepRefV1? RequestRef,RepRefV1[] StageRefs,RepRefV1[] ReceiptRefs,string Coverage,string State);

public sealed partial class ReplenishPlanningOwnerV1
{
    private StockDirectMoveV1 Stock=>new(db,scope.EnvironmentId);
    private TransferRootMoveV1 Transfer=>new(db,scope.EnvironmentId,owner);
    private static StockRefV1 ToStock(RepRefV1 r)=>new(r.Owner,r.Id,r.Version,r.DigestDomain,r.CodecVersion,r.Digest);
    private static RepRefV1 FromStock(StockRefV1 r)=>new(r.Owner,r.Id,r.Version,r.DigestDomain,r.CodecVersion,r.Digest);
    public Task<RepRefV1> OpenSimulatedStockAsync(RepStockOpeningV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("REP_SIMULATION_DISABLED",404);
        if(input?.Scope==null||input.Opening==null)throw Error("REP_INPUT_REQUIRED");Key(input.Device);
        return Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,t);a.RequireReplenishSimulationScope();var s=Persistence;await s.Fence(true,t);
            var o=await s.Observation(Hash(input.Scope),t);await Visible(a,o,a.ActorId,o.DepartmentId,t);
            if(input.Opening.Locations.Any(x=>!o.Locations.Any(l=>ToStock(l.Ref)==x.Ref&&l.NativeLocationId==x.NativeLocationId)))throw Error("REP_STOCK_SCOPE_MISMATCH",422);
            await Transfer.Fence(t);await Stock.OpenSimulation(input.Opening,t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_transfer_root_v1.devices VALUES({db.CurrentTenantId},{scope.EnvironmentId},{input.Device},{a.ActorId},true)
                ON CONFLICT(tenant_id,environment_id,device,performer_id) DO NOTHING
                """,t);
            return await RefreshStock(s,o,t);
        },ct);
    }
    private async Task<RepRefV1> RefreshStock(Store s,RepObservationV1 o,CancellationToken ct)
    {
        var all=await Stock.Snapshot(ct);var roots=new List<RepRootObservationV1>();
        foreach(var template in o.Roots)
        {
            var current=all.SingleOrDefault(x=>FromStock(x.Root.Ref)==template.Span.RootRef);if(current==null){roots.Add(template);continue;}
            var observation=FromStock(K.Reference("ROOT-OBSERVATION",Guid.ParseExact(K.Hash(new{current.Root.Ref,current.Revision})[..32],"N"),new{current.Root.Ref,current.Revision}));
            foreach(var slice in current.Slices.Where(x=>K.Number(x.From)<Number(template.Span.To)&&Number(template.Span.From)<K.Number(x.To)))
                roots.Add(template with{LocationRef=FromStock(slice.LocationRef),Span=template.Span with{From=Text(BigInteger.Max(Number(template.Span.From),K.Number(slice.From))),To=Text(BigInteger.Min(Number(template.Span.To),K.Number(slice.To)))},
                    StockObservationRef=observation,Movable=current.Root.Movable,SourceRuleRefs=FromStock(slice.LocationRef)==template.LocationRef?template.SourceRuleRefs:[]});
        }
        var targets=new List<RepTargetObservationV1>();
        foreach(var target in o.Targets)
        {
            // The complete simulated opening owns the entire fresh location/product scope; no old balances are inferred.
            var location=await Stock.Location(ToStock(target.LocationRef),ct);
            BigInteger quantity=0;
            foreach(var current in all.Where(x=>FromStock(x.Root.Dimension.ProductRef)==target.ProductRef&&FromStock(x.Root.Dimension.TechnicalRef)==target.TechnicalRef&&FromStock(x.Root.Dimension.OwnerRef)==target.OwnerRef&&FromStock(x.Root.Dimension.BaseUomRef)==target.BaseUomRef))
                foreach(var slice in current.Slices.Where(x=>x.LocationRef==location.Ref))quantity+=K.Number(slice.To)-K.Number(slice.From);
            var capacities=new List<RepCapacityDomainV1>();
            foreach(var domain in location.CapacityDomainRefs)
            {var (c,occupied)=await Stock.CapacitySnapshot(domain,ct);capacities.Add(new(FromStock(c.DomainRef),FromStock(c.MetricRef),FromStock(c.UomRef),c.Mode,c.Mode=="LIMITED"?Text(K.Number(c.Limit,total:true)-K.Number(occupied,total:true)):null));}
            targets.Add(target with{OnHand=Q(quantity,target.BaseUomRef),Initialized=true,Coverage="COMPLETE",CapacityKnown=capacities.All(x=>x.Mode!="UNKNOWN"),CapacityDomains=capacities.ToArray()});
        }
        var cut=FromStock(K.Reference("REP-CUT",Guid.NewGuid(),new{roots,targets}));
        await s.Publish(o with{CutRef=cut,ObservedAt=DateTimeOffset.UtcNow,Roots=roots.ToArray(),Targets=targets.ToArray()},ct);return cut;
    }
    private async Task<RepPrepareRegistrationV1> Registration(Store s,Guid id,CancellationToken ct)
    {
        var registration=await db.Database.SqlQuery<Guid?>($"SELECT registration_id AS \"Value\" FROM cp6_replenish_v1.prepare_registrations WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND order_id={id}").SingleOrDefaultAsync(ct)??throw Error("REP_PREPARE_REQUIRED",409);
        return Store.Decode<RepPrepareRegistrationV1>((await s.Read(registration,"prepare-registration",ct)).Body);
    }
    public Task<RepOrderV1> PrepareTransferAsync(Guid id,RepCommandV1 c,CancellationToken ct=default)
    {
        if(c==null)throw Error("REP_INPUT_REQUIRED");
        return Command(c,"prepare",new{kind="transfer-prepare",id,command=c},async(a,s,t)=>
        {
            var doc=await s.CurrentOrder(id,t);await Visible(a,s,doc,t);var order=Store.Decode<RepOrderV1>(doc.Body);Revision(order,c);
            if(order.Lifecycle!="REGISTERED")throw Error("REP_TRANSFER_PREPARE_STATE_INVALID",409);
            var registration=await Registration(s,id,t);var i=order.Input;
            var observation=await s.Observation(doc.ScopeKey,t);var rule=Rule(observation,i);
            if(rule.Ref!=registration.RuleRef||rule.ValidFrom>DateTimeOffset.UtcNow||rule.ValidTo<=DateTimeOffset.UtcNow)throw Error("REP_RULE_STALE",412);
            await CheckSource(s,observation,rule,i,[],true,t);
            foreach(var span in i.Source.Spans)
            {
                var root=(await Stock.Root(ToStock(span.RootRef),t)).Root;
                if(FromStock(root.Dimension.ProductRef)!=i.ProductRef||FromStock(root.Dimension.TechnicalRef)!=i.TechnicalRef||FromStock(root.Dimension.OwnerRef)!=i.OwnerRef||FromStock(root.Dimension.BaseUomRef)!=i.BaseUomRef)throw Error("REP_STOCK_DIMENSION_MISMATCH",422);
            }
            var state=await Transfer.Register(registration.DispatchKey,ToStock(registration.OrderRef),ToStock(registration.SelectionRef),doc.CreatorId,doc.DepartmentId,
                ToStock(i.Source.SourceLocationRef),ToStock(i.ToLocationRef),ToStock(i.BaseUomRef),i.Source.Spans.Select(x=>new StockSpanV1(ToStock(x.RootRef),x.From,x.To)).ToArray(),registration.OperationId,t);
            var updated=await SaveExecution(s,doc,order,state,t);return(updated.Snapshot,updated.Order);
        },ct,create:false);
    }
    public Task<RepOrderV1> ExecutionAsync(Guid id,string action,RepExecutionCommandV1 c,CancellationToken ct=default)
    {
        if(c?.Command==null)throw Error("REP_INPUT_REQUIRED");
        if(action=="dispatch"){Key(c.Device);Key(c.OccurrenceKey);}
        var required=action switch{"approve"=>"prepare","dispatch" or "complete"=>"execute","stop"=>"cancel",_=>throw Error("REP_ACTION_INVALID")};
        return Command(c.Command,required,new{kind="transfer-"+action,id,input=c},async(a,s,t)=>
        {
            var doc=await s.CurrentOrder(id,t);await Visible(a,s,doc,t);var order=Store.Decode<RepOrderV1>(doc.Body);Revision(order,c.Command);
            if(action=="stop"&&order.ExecutionLink==null&&order.Lifecycle is "DRAFT" or "REGISTERED")
            {
                // Registration and this source gate share the REP transaction. With no Transfer
                // link there is no external send; a late prepare must pass this now-closed Order.
                var closed=Version(order,"CANCELLED_NO_EFFECT",order.SelectionRef,order.CurrentCutRef) with
                {Moved=Q(0,order.Input.BaseUomRef),Remaining=Q(0,order.Input.BaseUomRef),Capabilities=["READ"]};
                var snapshot=Guid.NewGuid();await s.SaveOrderVersion(id,snapshot,closed,doc.CreatorId,doc.DepartmentId,t);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_replenish_v1.events VALUES({db.CurrentTenantId},{scope.EnvironmentId},{snapshot},'CLOSED_NOT_SENT',{Bytes(closed)})",t);
                return(snapshot,closed);
            }
            var registration=await Registration(s,id,t);var key=registration.DispatchKey;
            if(action!="stop"&&order.Lifecycle is not ("MOVED" or "CANCELLED_NO_EFFECT"))
            {
                var observation=await s.Observation(doc.ScopeKey,t);var rule=Rule(observation,order.Input);
                if(rule.Ref!=registration.RuleRef||rule.ValidFrom>DateTimeOffset.UtcNow||rule.ValidTo<=DateTimeOffset.UtcNow)throw Error("REP_RULE_STALE",412);
                await CheckSource(s,observation,rule,order.Input,[],true,t);
            }
            var state=action switch
            {
                "approve"=>await Transfer.Approve(key,a,t),
                "dispatch"=>await Transfer.Dispatch(key,a,c.Device!,c.OccurrenceKey!,t),
                "complete"=>await Transfer.Complete(key,a,t),
                "stop"=>await Transfer.Stop(key,a,c.PhysicalNotOccurred,t),
                _=>throw Error("REP_ACTION_INVALID")
            };
            var updated=await SaveExecution(s,doc,order,state,t);return(updated.Snapshot,updated.Order);
        },ct,create:false);
    }
    private async Task<(Guid Snapshot,RepOrderV1 Order)> SaveExecution(Store s,Store.Document doc,RepOrderV1 order,TransferRootStateV1 state,CancellationToken ct)
    {
        var r=state.Request;var receipt=state.Receipt;
        var lifecycle=state.State switch{"APPROVAL_PENDING"=>"REGISTERED","PREPARED"=>"PREPARED","TASK_ISSUED"=>"TASK_ISSUED","POSTED"=>"MOVED","CLOSED_NO_EFFECT"=>"CANCELLED_NO_EFFECT",_=>"RECONCILIATION"};
        var linkState=state.State switch{"APPROVAL_PENDING" or "PREPARED"=>"REGISTERED","TASK_ISSUED"=>"STARTED","POSTED"=>"POSTED","CLOSED_NO_EFFECT"=>"CLOSED_NO_EFFECT",_=>"RECONCILIATION"};
        var unknown=lifecycle=="RECONCILIATION";var refs=receipt==null?Array.Empty<RepRefV1>():[FromStock(receipt.Ref)];
        var link=new RepTransferLinkV1("WMS_TRANSFER",r.OriginalBusinessKey,FromStock(r.Ref),[FromStock(r.StageRef)],refs,unknown?"UNKNOWN":"COMPLETE",linkState);
        var updated=Version(order,lifecycle,order.SelectionRef,order.CurrentCutRef) with{ExecutionLink=link,Knowledge=unknown?"UNKNOWN":"KNOWN",
            Moved=unknown?null:Q(lifecycle=="MOVED"?Number(order.Planned.Value):0,order.Input.BaseUomRef),Remaining=unknown?null:Q(lifecycle is "MOVED" or "CANCELLED_NO_EFFECT"?0:Number(order.Planned.Value),order.Input.BaseUomRef),
            StockReceiptRefs=receipt?.State=="POSTED"?refs:[],Capabilities=state.State switch{"APPROVAL_PENDING"=>["READ","APPROVE_TRANSFER","STOP"],"PREPARED"=>["READ","DISPATCH","STOP"],"TASK_ISSUED"=>["READ","COMPLETE","STOP"],_=>["READ"]}};
        if(receipt!=null)updated=updated with{CurrentCutRef=await RefreshStock(s,await s.Observation(doc.ScopeKey,ct),ct)};
        var snapshot=Guid.NewGuid();await s.SaveOrderVersion(Id(order.Ref),snapshot,updated,doc.CreatorId,doc.DepartmentId,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_replenish_v1.events VALUES({db.CurrentTenantId},{scope.EnvironmentId},{snapshot},'TRANSFER_RESULT',{Bytes(new{updated.Ref,state})})",ct);
        return(snapshot,updated);
    }
    public Task<RepTransferLinkV1?> ExecutionLinkAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
    {await Lease(a,t);var s=Persistence;await s.Fence(false,t);var doc=await s.CurrentOrder(id,t);await Visible(a,s,doc,t);
     var order=Store.Decode<RepOrderV1>(doc.Body);return order.ExecutionLink==null?null:System.Text.Json.JsonSerializer.Deserialize<RepTransferLinkV1>(System.Text.Json.JsonSerializer.Serialize(order.ExecutionLink),RepJson);},ct);
}

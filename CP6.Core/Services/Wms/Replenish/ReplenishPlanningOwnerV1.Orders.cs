using System.Globalization;
using System.Numerics;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
using Store=CP6.Core.Services.Wms.Replenish.ReplenishPlanningStoreV1;
namespace CP6.Core.Services.Wms.Replenish;

public sealed partial class ReplenishPlanningOwnerV1
{
    private static RepOrderInputV1[] DraftOptions(RepObservationV1 o)=>o.Rules.SelectMany(rule=>rule.TargetLocationRefs.SelectMany(target=>
        o.Roots.Where(r=>SameDimension(rule,r)&&r.LocationRef!=target&&r.SourceRuleRefs.Contains(rule.SourceRuleRef))
        .OrderBy(r=>r.LocationRef.Id,StringComparer.Ordinal).ThenBy(r=>r.Span.RootRef.Id,StringComparer.Ordinal).ThenBy(r=>Number(r.Span.From))
        .GroupBy(r=>r.LocationRef).Select(g=>
        {
            var first=g.First();var quantity=Q(g.Aggregate(BigInteger.Zero,(n,r)=>n+SpanAmount(r.Span)),rule.BaseUomRef);
            return new RepOrderInputV1(o.Scope,rule.ProductRef,rule.TechnicalRef,rule.OwnerRef,rule.BaseUomRef,target,
                new(g.Key,g.Select(r=>r.Span).ToArray(),quantity,o.CutRef,first.ExpiryAt,first.ReceivedAt,first.MasterConditionRef),quantity,2,"MANUAL",null,"",null);
        }))).ToArray();
    private static void ValidateDraft(RepDraftInputV1 c)
    {
        if(c?.Command==null||c.Input?.Scope==null||c.Input.Source?.Spans==null||c.Input.Quantity==null||c.Input.Source.Quantity==null)throw Error("REP_INPUT_REQUIRED");
        var i=c.Input;
        foreach(var r in new[]{i.Scope.SiteRef,i.Scope.WarehouseRef,i.ProductRef,i.TechnicalRef,i.OwnerRef,i.BaseUomRef,i.ToLocationRef,i.Source.SourceLocationRef,i.Source.StockObservationRef,i.Source.MasterConditionRef})Reference(r);
        if(i.SuggestionRowRef!=null)Reference(i.SuggestionRowRef);
        if(i.Source.SourceLocationRef==i.ToLocationRef||i.Priority is not (1 or 2)||i.Trigger is not ("MANUAL" or "ALERT")||string.IsNullOrWhiteSpace(i.Reason)||i.Reason.Length>500||i.Remarks?.Length>2000)throw Error("REP_DRAFT_INVALID");
        if(i.Quantity.UomRef!=i.BaseUomRef||i.Source.Quantity.UomRef!=i.BaseUomRef)throw Error("REP_UNIT_MISMATCH");
        var amount=Number(i.Quantity.Value,positive:true);
        if(i.Source.Spans.Length is <1 or >200)throw Error("REP_SOURCE_RANGE_REQUIRED");
        foreach(var span in i.Source.Spans){Reference(span.RootRef);SpanAmount(span);}
        if(Number(i.Source.Quantity.Value,positive:true)!=amount||i.Source.Spans.Aggregate(BigInteger.Zero,(n,x)=>n+SpanAmount(x))!=amount||Union(i.Source.Spans)!=amount)throw Error("REP_SOURCE_QUANTITY_MISMATCH");
    }
    private async Task Priority(OperationsAccessV1 a,RepOrderInputV1 input,CancellationToken ct)
    {if(input.Priority==1)a.Authorities.Add(await owner.AuthorityAsync(OperationsOwnerV1.Replenish,"expedite",ct));}
    private static RepRuleV1 Rule(RepObservationV1 o,RepOrderInputV1 i)
    {
        var rules=o.Rules.Where(r=>r.TargetLocationRefs.Contains(i.ToLocationRef)&&r.ProductRef==i.ProductRef&&r.TechnicalRef==i.TechnicalRef&&r.OwnerRef==i.OwnerRef&&r.BaseUomRef==i.BaseUomRef).ToArray();
        return rules.Length==1?rules[0]:throw Error("REP_RULE_AMBIGUOUS",422);
    }
    private static void Revision(RepOrderV1 order,RepCommandV1 command)
    {if(order.Revision!=command.ExpectedRevision)throw Error("REP_REVISION_STALE",412);}
    private static RepOrderV1 Version(RepOrderV1 order,string lifecycle,RepRefV1? selection,RepRefV1? cut)
    {
        var revision=(long.Parse(order.Revision,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture);
        return order with{Ref=order.Ref with{Version=revision,Digest=Hash(new{order.Input,lifecycle,selection,cut,revision})},Lifecycle=lifecycle,SelectionRef=selection,CurrentCutRef=cut,Revision=revision,
            Capabilities=lifecycle=="DRAFT"?["READ","EDIT_DRAFT","PREPARE"]:["READ","PREPARE"]};
    }
    public Task<RepOrderV1> CreateDraftAsync(RepDraftInputV1 c,CancellationToken ct=default)
    {
        ValidateDraft(c);
        return Command(c.Command,"add",new{kind="draft-create",input=c},async(a,s,t)=>
        {
            var o=await s.Observation(Hash(c.Input.Scope),t);await Visible(a,o,a.ActorId,o.DepartmentId,t,selectedLocations:OrderLocations(c.Input));await Priority(a,c.Input,t);a.RequireReplenishRemarksWrite(null,c.Input.Remarks);
            var rule=Rule(o,c.Input);await CheckSource(s,o,rule,c.Input,[],false,t);
            var id=Guid.NewGuid();var order=new RepOrderV1(Ref("order",id,c.Input),c.Input,null,"DRAFT","KNOWN",c.Input.Quantity,Q(0,c.Input.BaseUomRef),c.Input.Quantity,null,[],["READ","EDIT_DRAFT","PREPARE"],null,"1");
            await s.SaveOrderVersion(id,id,order,a.ActorId,o.DepartmentId,t);return(id,order);
        },ct);
    }
    public Task<RepOrderV1> EditDraftAsync(Guid id,RepDraftInputV1 c,CancellationToken ct=default)
    {
        ValidateDraft(c);
        return Command(c.Command,"edit",new{kind="draft-edit",id,input=c},async(a,s,t)=>
        {
            var doc=await s.CurrentOrder(id,t);await Visible(a,s,doc,t);var old=Store.Decode<RepOrderV1>(doc.Body);Revision(old,c.Command);
            if(old.Lifecycle!="DRAFT")throw Error("REP_DRAFT_FROZEN",409);a.RequireReplenishRemarksWrite(old.Input.Remarks,c.Input.Remarks);
            var o=await s.Observation(Hash(c.Input.Scope),t);await Visible(a,o,doc.CreatorId,o.DepartmentId,t,selectedLocations:OrderLocations(c.Input));await Priority(a,c.Input,t);
            var rule=Rule(o,c.Input);await CheckSource(s,o,rule,c.Input,[],false,t);
            var order=Version(old with{Input=c.Input,Planned=c.Input.Quantity,Moved=Q(0,c.Input.BaseUomRef),Remaining=c.Input.Quantity},"DRAFT",null,null);
            var snapshot=Guid.NewGuid();await s.SaveOrderVersion(id,snapshot,order,doc.CreatorId,o.DepartmentId,t);return(snapshot,order);
        },ct,create:false);
    }
    public Task<RepOrderV1> OrderAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
    {await Lease(a,t);var s=Persistence;await s.Fence(false,t);var d=await s.CurrentOrder(id,t);await Visible(a,s,d,t);return a.ProjectReplenishResult(Store.Decode<RepOrderV1>(d.Body));},ct);
    public Task<object> OrderHistoryAsync(Guid id,long afterRevision=0,int limit=50,CancellationToken ct=default)
    {
        if(afterRevision<0||limit is <1 or >200)throw Error("REP_PAGE_INVALID");
        return Authority.ExecuteAsync<object>(OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
        {
            await Lease(a,t);var s=Persistence;await s.Fence(false,t);await Visible(a,s,await s.CurrentOrder(id,t),t);
            var snapshots=await db.Database.SqlQuery<Guid>($"""
                SELECT snapshot_id AS "Value" FROM cp6_replenish_v1.order_versions
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND order_id={id} AND revision::bigint>{afterRevision}
                ORDER BY revision::bigint LIMIT {limit+1}
                """).ToArrayAsync(t);
            var items=new List<RepOrderV1>();foreach(var snapshot in snapshots.Take(limit))
            {var d=await s.Read(snapshot,null,t);await Visible(a,s,d,t);items.Add(a.ProjectReplenishResult(Store.Decode<RepOrderV1>(d.Body)));}
            return new{items,nextRevision=snapshots.Length>limit?items[^1].Revision:null};
        },ct);
    }
    private static async Task<RepRootObservationV1[]> CheckSource(Store s,RepObservationV1 current,RepRuleV1 rule,RepOrderInputV1 input,RepSpanV1[] reserved,bool preparing,CancellationToken ct)
    {
        var before=await s.ObservationAt(Hash(input.Scope),input.Source.StockObservationRef,ct);var chosen=new List<RepRootObservationV1>();
        foreach(var span in input.Source.Spans)
        {
            var old=before.Roots.Where(x=>x.Span.RootRef==span.RootRef&&Number(x.Span.From)<Number(span.To)&&Number(span.From)<Number(x.Span.To)).OrderBy(x=>Number(x.Span.From)).ToArray();
            if(ReplenishPlanningCalculatorV1.Subtract(span,old.Select(x=>x.Span)).Any()||old.Any(x=>x.LocationRef!=input.Source.SourceLocationRef||!SameDimension(rule,x)))throw Error("REP_SOURCE_CHANGED",412);
            var roots=current.Roots.Where(x=>x.Span.RootRef==span.RootRef&&Number(x.Span.From)<Number(span.To)&&Number(span.From)<Number(x.Span.To)).OrderBy(x=>Number(x.Span.From)).ToArray();
            if(ReplenishPlanningCalculatorV1.Subtract(span,roots.Select(x=>x.Span)).Any()||roots.Any(x=>x.LocationRef!=input.Source.SourceLocationRef||!SameDimension(rule,x)||!x.SourceRuleRefs.Contains(rule.SourceRuleRef)))throw Error("REP_SOURCE_CHANGED",412);
            foreach(var root in roots)
            {
                if(preparing&&(!root.Movable||old.Where(x=>Number(x.Span.From)<Number(root.Span.To)&&Number(root.Span.From)<Number(x.Span.To)).Any(x=>x.StockObservationRef!=root.StockObservationRef||x.MasterConditionRef!=root.MasterConditionRef)))throw Error("REP_SOURCE_CHANGED",412);
                chosen.Add(root with{Span=span with{From=Text(BigInteger.Max(Number(span.From),Number(root.Span.From))),To=Text(BigInteger.Min(Number(span.To),Number(root.Span.To)))}});
            }
            if(preparing&&reserved.Any(x=>SameRoot(x.RootRef,span.RootRef)&&Number(x.From)<Number(span.To)&&Number(span.From)<Number(x.To)))throw Error("REP_SOURCE_ALREADY_REGISTERED",409);
        }
        var first=before.Roots.First(x=>x.Span.RootRef==input.Source.Spans[0].RootRef&&Number(x.Span.From)<=Number(input.Source.Spans[0].From)&&Number(x.Span.To)>Number(input.Source.Spans[0].From));
        if(first.MasterConditionRef!=input.Source.MasterConditionRef||first.ExpiryAt!=input.Source.ExpiryAt||first.ReceivedAt!=input.Source.ReceivedAt)throw Error("REP_SOURCE_METADATA_CHANGED",412);
        return chosen.ToArray();
    }
    public Task<RepPrepareV1> PrepareAsync(Guid id,RepCommandV1 c,CancellationToken ct=default)
    {
        if(c==null)throw Error("REP_INPUT_REQUIRED");
        return Command(c,"prepare",new{kind="prepare",id,command=c},async(a,s,t)=>
        {
            var doc=await s.CurrentOrder(id,t);await Visible(a,s,doc,t);var order=Store.Decode<RepOrderV1>(doc.Body);Revision(order,c);
            if(order.Lifecycle is not ("DRAFT" or "REGISTERED"))throw Error("REP_PREPARE_STATE_INVALID",409);
            var prior=await db.Database.SqlQuery<Guid?>($"""
                SELECT prepare_id AS "Value" FROM cp6_replenish_v1.prepare_registrations
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND order_id={id} AND selection_version={order.SelectionRef?.Version??order.Revision}
                """).SingleOrDefaultAsync(t);
            if(prior is {} previous){Replayed=true;return(previous,Store.Decode<RepPrepareV1>((await s.Read(previous,"prepare",t)).Body));}
            var i=order.Input;var o=await s.Observation(doc.ScopeKey,t);await Visible(a,o,doc.CreatorId,doc.DepartmentId,t,current:true,selectedLocations:OrderLocations(i));await Priority(a,i,t);
            var rule=Rule(o,i);if(rule.ValidFrom>DateTimeOffset.UtcNow||rule.ValidTo<=DateTimeOffset.UtcNow||rule.Coverage!="COMPLETE"||o.Coverage!="COMPLETE")throw Error("REP_COMPLETE_CUT_REQUIRED",409);
            var (intents,reserved)=await Active(s,t);var slot=Slot(rule,i.ToLocationRef);
            var target=o.Targets.SingleOrDefault(x=>Slot(x)==slot)??throw Error("REP_TARGET_UNAVAILABLE",409);
            if(target.Coverage!="COMPLETE"||!target.Initialized||target.OnHand==null||!target.CapacityKnown||target.CapacityDomains.Any(x=>x.Mode=="UNKNOWN"))throw Error("REP_COMPLETE_CUT_REQUIRED",409);
            if(order.Lifecycle=="DRAFT")
            {
                var own=intents.Where(x=>x.DemandSlotKey==slot).ToArray();var external=o.Incoming.Where(x=>x.DemandSlotKey==slot&&!intents.Any(y=>y.Ref.Id==x.IntentKey)).ToArray();
                if(own.Any(x=>x.Coverage!="COMPLETE"||x.OpenIncoming==null)||external.Any(x=>x.Coverage!="COMPLETE"))throw Error("REP_COMPLETE_CUT_REQUIRED",409);
                var open=Union(external.SelectMany(x=>x.OpenSpans))+own.Aggregate(BigInteger.Zero,(n,x)=>n+Number(x.OpenIncoming!.Value,total:true));
                if(Number(target.OnHand.Value,total:true)+open+Number(i.Quantity.Value)>Number(rule.MaxQuantity.Value))throw Error("REP_DEMAND_CHANGED",409);
            }
            else
            {
                var plan=Store.Decode<RepPlanV1>((await s.Read(Id(order.SelectionRef!),"plan",t)).Body);
                var suggestion=Store.Decode<RepSuggestionV1>((await s.Read(Id(plan.SuggestionRef),"suggestion",t)).Body);
                if(suggestion.Rows.Single(x=>x.LineKey==plan.LineKey).RuleRef!=rule.Ref)throw Error("REP_RULE_STALE",412);
                reserved=reserved.SelectMany(x=>ReplenishPlanningCalculatorV1.Subtract(x,i.Source.Spans.Where(y=>SameRoot(y.RootRef,x.RootRef)))).ToArray();
            }
            var roots=await CheckSource(s,o,rule,i,reserved,true,t);
            foreach(var domain in target.CapacityDomains)
            {
                BigInteger delta=0;foreach(var root in roots)
                {
                    var rates=root.CapacityDeltas.Where(x=>x.DomainRef==domain.DomainRef&&x.MetricRef==domain.MetricRef&&x.UomRef==domain.UomRef).ToArray();
                    if(rates.Length==0)throw Error("REP_CAPACITY_CONVERSION_UNKNOWN",409);
                    foreach(var rate in rates){var value=BigInteger.DivRem(SpanAmount(root.Span)*SignedNumber(rate.Numerator),Number(rate.Denominator,positive:true),out var remainder);if(remainder!=0)throw Error("REP_CAPACITY_PRECISION_INVALID",422);delta+=value;}
                }
                if(domain.Mode=="LIMITED"&&delta>Number(domain.FreeValue,total:true))throw Error("REP_CAPACITY_INSUFFICIENT",409);
            }
            var cutId=Guid.NewGuid();var cut=Ref("cut",cutId,new{o.CutRef,intents=intents.Select(x=>x.Ref)});
            await s.Save(cutId,"cut",a.ActorId,doc.DepartmentId,doc.ScopeKey,new RepCutV1(cut,o.CutRef,intents.Select(x=>x.Ref).ToArray(),DateTimeOffset.UtcNow,o.DataKind),t);
            var selection=order.SelectionRef;
            if(selection==null)
            {
                var selectionId=Guid.NewGuid();selection=Ref("selection",selectionId,new{orderRef=order.Ref,order.Input,ruleRef=rule.Ref,cut}) with{Version=order.Revision};
                await s.Save(selectionId,"selection",doc.CreatorId,doc.DepartmentId,doc.ScopeKey,new{reference=selection,orderRef=order.Ref,input=order.Input,ruleRef=rule.Ref,cutRef=cut},t);
                order=Version(order,"REGISTERED",selection,cut);await s.SaveOrderVersion(id,Guid.NewGuid(),order,doc.CreatorId,doc.DepartmentId,t);
                var intentId=Guid.NewGuid();var intentRef=Ref("active-intent",intentId,new{order.Ref,slot,selection});
                var intent=new RepIntentV1(intentRef,slot,null,"REGISTERED_COUNTED",i.Quantity,i.Quantity,Q(0,i.BaseUomRef),Q(0,i.BaseUomRef),[new(selection.Id,order.Ref,i.Quantity,"REGISTERED")],cut,"COMPLETE");
                await s.Save(intentId,"intent",a.ActorId,doc.DepartmentId,doc.ScopeKey,intent,t);
            }
            var prepareId=Guid.NewGuid();var result=new RepPrepareV1(Ref("prepare",prepareId,new{order.Ref,selection,cut}),order.Ref,Hash(i),i.Source.Spans,[o.CutRef],"ACCEPTED_TRANSFER",null,["WAITING_TRANSFER_ADOPTION"],false);
            await s.Save(prepareId,"prepare",a.ActorId,doc.DepartmentId,doc.ScopeKey,result,t);
            var registrationId=Guid.NewGuid();var registration=new RepPrepareRegistrationV1(Ref("prepare-registration",registrationId,result),order.Ref,selection,result.Ref,
                Hash(new{tenant=db.CurrentTenantId,environment=scope.EnvironmentId,order=id,selectionVersion=selection.Version}),Guid.NewGuid(),"1","NOT_SENT",rule.Ref,cut);
            await s.Save(registrationId,"prepare-registration",a.ActorId,doc.DepartmentId,doc.ScopeKey,registration,t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_replenish_v1.prepare_registrations VALUES({db.CurrentTenantId},{scope.EnvironmentId},{id},{selection.Version},{prepareId},{registrationId});
                INSERT INTO cp6_replenish_v1.events VALUES({db.CurrentTenantId},{scope.EnvironmentId},{registrationId},'PREPARE_REGISTERED',{Bytes(registration)});
                """,t);
            return(prepareId,result);
        },ct,create:false);
    }
}

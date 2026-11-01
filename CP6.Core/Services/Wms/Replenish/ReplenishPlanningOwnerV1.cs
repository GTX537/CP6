using System.Numerics;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
using Store=CP6.Core.Services.Wms.Replenish.ReplenishPlanningStoreV1;
namespace CP6.Core.Services.Wms.Replenish;

public sealed partial class ReplenishPlanningOwnerV1(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,
    IOptions<ReplenishSimulationOptionsV1> simulation)
{
    public bool Replayed {get;private set;}
    private OperationsOwnerV1 Authority=>owner.Owns(db)&&scope.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Store Persistence=>new(db,scope.EnvironmentId);
    private async Task Lease(OperationsAccessV1 a,CancellationToken ct)
    {var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));}
    public Task<RepRefV1> PublishSimulationAsync(RepSimulationInputV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("REP_SIMULATION_DISABLED",404);
        if(input?.Observation?.DataKind!="SIMULATED")throw Error("REP_SIMULATED_INPUT_REQUIRED");
        var frozen=Store.Decode<RepObservationV1>(Bytes(input.Observation));
        return Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"simulate-source",true,async(a,t)=>
        {await Lease(a,t);a.RequireReplenishSimulationScope();await Visible(a,frozen,a.ActorId,frozen.DepartmentId,t);
         await Persistence.Publish(frozen,t);return frozen.CutRef;},ct);
    }
    private async Task Visible(OperationsAccessV1 a,RepObservationV1 observation,Guid creator,Guid? department,CancellationToken ct,bool current=false)
    {
        await a.RequireReplenishPlanningDocument(creator,department,ct);
        var ids=observation.Locations.Select(x=>x.NativeLocationId).ToArray();
        var locations=a.ReplenishPlanningLocations();
        if(current)locations=locations.Where(x=>!x.IsBlocked&&!x.IsInitializationQuarantined);
        if(await locations.CountAsync(x=>ids.Contains(x.Id),ct)!=ids.Length)throw Error("REP_LOCATION_FORBIDDEN",403);
        a.AtCommit(async t=>
        {await a.RequireReplenishPlanningDocument(creator,department,t);
         if(await locations.CountAsync(x=>ids.Contains(x.Id),t)!=ids.Length)throw Error("REP_LOCATION_FORBIDDEN",403);});
    }
    private async Task Visible(OperationsAccessV1 a,Store s,Store.Document d,CancellationToken ct)
        =>await Visible(a,await s.Observation(d.ScopeKey,ct),d.CreatorId,d.DepartmentId,ct);
    private static Guid Id(RepRefV1 r)
    {Reference(r);return r.Owner==Owner&&Guid.TryParseExact(r.Id,"D",out var id)?id:throw Error("REP_REFERENCE_INVALID");}
    private static void Exact<T>(RepRefV1 expected,T actual,RepRefV1 reference)
    {if(expected!=reference)throw Error("REP_REFERENCE_STALE",412);}
    private async Task<T> Read<T>(Guid id,string kind,CancellationToken ct)=>await Authority.ExecuteAsync(
        OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
        {await Lease(a,t);var s=Persistence;await s.Fence(false,t);var d=await s.Read(id,kind,t);await Visible(a,s,d,t);return Store.Decode<T>(d.Body);},ct);
    public Task<RepSuggestionV1> SuggestionAsync(Guid id,CancellationToken ct=default)=>Read<RepSuggestionV1>(id,"suggestion",ct);
    public Task<RepPlanV1> PlanAsync(Guid id,CancellationToken ct=default)=>Read<RepPlanV1>(id,"plan",ct);
    public Task<RepCutV1> CutAsync(Guid id,CancellationToken ct=default)=>Read<RepCutV1>(id,"cut",ct);
    public Task<RepBatchV1> BatchAsync(Guid id,CancellationToken ct=default)=>Read<RepBatchV1>(id,"batch",ct);
    public Task<RepIntentV1> IntentAsync(Guid id,CancellationToken ct=default)=>Read<RepIntentV1>(id,"intent",ct);
    public Task<object> ContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync<object>(OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
    {
        await Lease(a,t);var s=Persistence;await s.Fence(false,t);
        var keys=await db.Database.SqlQuery<string>($"""
            SELECT scope_key AS "Value" FROM cp6_replenish_v1.current_observations WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}
            """).ToArrayAsync(t);var visible=new List<object>();
        foreach(var key in keys)
        {var observation=await s.Observation(key,t);
         try{await Visible(a,observation,a.ActorId,observation.DepartmentId,t);}
         catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e)when(e.Status is 403 or 404){continue;}
         visible.Add(new{observation.Scope,observation.CutRef,observation.DataKind,observation.Coverage,observation.Rules,observation.Locations,draftOptions=DraftOptions(observation)});}
        return new{tenantId=db.CurrentTenantId,actorId=a.ActorId,environmentId=scope.EnvironmentId,scopes=visible,dependencyHealth=keys.Length==0?"OBSERVATION_UNAVAILABLE":"AVAILABLE",
            capabilities=new[]{"SUGGEST","REGISTER_BATCH","CREATE_DRAFT","EDIT_DRAFT","REGISTER_PREPARE"},dispatchCapability="WAITING_TRANSFER_ADOPTION"};
    },ct);
    private Task<T> Command<T>(RepCommandV1 meta,string action,object input,Func<OperationsAccessV1,Store,CancellationToken,Task<(Guid,T)>> body,CancellationToken ct,bool create=true)
    {
        Replayed=false;Key(meta.CommandKey);if(create&&meta.ExpectedRevision!=null)throw Error("REP_CREATE_REVISION_MUST_BE_NULL");
        if(!create)Key(meta.ExpectedRevision);
        if(meta.Reason?.Length>500)throw Error("REP_REASON_TOO_LONG");var encoded=Bytes(input);if(encoded.Length>2*1024*1024)throw Error("REP_BODY_TOO_LARGE",413);
        var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded)).ToLowerInvariant();Store.SavedCommand? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Replenish,action,true,async(a,t)=>
        {
            await Lease(a,t);var s=Persistence;
            if(original!=null)
            {Replayed=true;if(original.Action!=action||original.InputDigest!=digest)throw Error("REP_COMMAND_CONFLICT",409);
             var saved=await s.Read(original.ResultId,null,t);await Visible(a,s,saved,t);return a.ProjectReplenishResult(Store.Decode<T>(original.Body));}
            var (id,result)=await body(a,s,t);await s.SaveCommand(a.ActorId,meta.CommandKey,action,digest,id,result!,t);return a.ProjectReplenishResult(result);
        },ct,actionAtFence:async t=>
        {var s=Persistence;await s.Fence(true,t);var actor=await owner.CurrentSubjectAsync(t);
         original=await s.Command(actor,meta.CommandKey,t);return original==null?action:"query";});
    }
    private static async Task<(RepIntentV1[],RepSpanV1[])> Active(Store s,CancellationToken ct)
    {
        var docs=await s.Documents().Where(x=>x.Kind=="intent"||x.Kind=="plan").ToArrayAsync(ct);
        var intents=docs.Where(x=>x.Kind=="intent").Select(x=>Store.Decode<RepIntentV1>(x.Body)).Where(x=>x.State!="SETTLED").ToArray();
        var planIds=intents.Where(x=>x.AllocationPlanRef!=null).Select(x=>x.AllocationPlanRef!.Id).ToHashSet(StringComparer.Ordinal);
        var spans=docs.Where(x=>x.Kind=="plan"&&planIds.Contains(x.Id.ToString("D"))).SelectMany(x=>Store.Decode<RepPlanV1>(x.Body).Splits).SelectMany(x=>x.Source.Spans).ToList();
        foreach(var split in intents.Where(x=>x.AllocationPlanRef==null).SelectMany(x=>x.Splits))
            spans.AddRange(Store.Decode<RepOrderV1>((await s.CurrentOrder(Id(split.OrderRef),ct)).Body).Input.Source.Spans);
        return(intents,spans.ToArray());
    }
    public Task<RepSuggestionV1> SuggestAsync(RepSuggestInputV1 input,CancellationToken ct=default)
    {
        if(input?.Command==null||input.Scope==null||input.RuleRefs==null||input.TargetLocationRefs==null)throw Error("REP_INPUT_REQUIRED");
        Reference(input.Scope.SiteRef);Reference(input.Scope.WarehouseRef);
        if(input.RuleRefs.Length is <1 or >200||input.TargetLocationRefs.Length is <1 or >200)throw Error("REP_SELECTION_INVALID");
        foreach(var r in input.RuleRefs.Concat(input.TargetLocationRefs))Reference(r);
        if(input.RuleRefs.Distinct().Count()!=input.RuleRefs.Length||input.TargetLocationRefs.Distinct().Count()!=input.TargetLocationRefs.Length)throw Error("REP_SELECTION_DUPLICATED");
        var c=Store.Decode<RepSuggestInputV1>(Bytes(input));
        return Command(c.Command,"generate",new{kind="suggest",input=c},async(a,s,t)=>
        {
            var observation=await s.Observation(Hash(c.Scope),t);await Visible(a,observation,a.ActorId,observation.DepartmentId,t,current:true);
            var (intents,active)=await Active(s,t);var allocations=new List<ReplenishPlanningCalculatorV1.Allocation>();
            foreach(var r in c.RuleRefs.OrderBy(x=>x.Id,StringComparer.Ordinal))
            {
                var rule=observation.Rules.SingleOrDefault(x=>x.Ref==r)??throw Error("REP_RULE_STALE",412);
                foreach(var target in c.TargetLocationRefs.Where(rule.TargetLocationRefs.Contains).OrderBy(x=>x.Id,StringComparer.Ordinal))
                    allocations.Add(ReplenishPlanningCalculatorV1.Calculate(observation,rule,target,intents,active,DateTimeOffset.UtcNow));
            }
            if(allocations.Count==0||allocations.Count>200||c.TargetLocationRefs.Any(x=>!allocations.Any(v=>v.Row.TargetLocationRef==x)))throw Error("REP_RULE_TARGET_MISMATCH",422);
            if(allocations.Select(x=>x.Slot).Distinct().Count()!=allocations.Count)throw Error("REP_RULE_AMBIGUOUS",422);
            var id=Guid.NewGuid();var cutId=Guid.NewGuid();var intentRefs=intents.Select(x=>x.Ref).OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray();
            var cut=Ref("cut",cutId,new{observation.CutRef,intents=intentRefs});
            await s.Save(cutId,"cut",a.ActorId,observation.DepartmentId,Hash(c.Scope),new RepCutV1(cut,observation.CutRef,intentRefs,DateTimeOffset.UtcNow,observation.DataKind),t);
            var reference=Ref("suggestion",id,new{c.Scope,cut,rows=allocations.Select(x=>x.Row)});var rows=new List<RepRowV1>();
            foreach(var x in allocations)
            {
                if(x.Splits.Length==0){rows.Add(x.Row);continue;}
                var planId=Guid.NewGuid();var content=new{suggestionRef=reference,lineKey=x.Row.LineKey,cutRef=cut,demandSlotKey=x.Slot,
                    targetLocationRef=x.Row.TargetLocationRef,totalQuantity=x.Row.Proposed!,splits=x.Splits};
                var planRef=Ref("allocation-plan",planId,content);var plan=new RepPlanV1(planRef,reference,x.Row.LineKey,cut,x.Slot,x.Row.TargetLocationRef,x.Row.Proposed!,x.Splits,planRef.Digest);
                await s.Save(planId,"plan",a.ActorId,observation.DepartmentId,Hash(c.Scope),plan,t);rows.Add(x.Row with{AllocationPlanRef=planRef});
            }
            var result=new RepSuggestionV1(reference,cut,rows.ToArray(),rows.All(x=>x.Coverage=="COMPLETE")?"COMPLETE":rows.All(x=>x.Coverage=="UNKNOWN")?"UNKNOWN":"PARTIAL");
            await s.Save(id,"suggestion",a.ActorId,observation.DepartmentId,Hash(c.Scope),result,t);return(id,result);
        },ct);
    }
    public Task<RepBatchV1> GenerateAsync(RepBatchInputV1 input,CancellationToken ct=default)
    {
        if(input?.Command==null||input.SelectedPlans==null||input.SelectedPlans.Length is <1 or >200)throw Error("REP_SELECTION_INVALID");Id(input.SuggestionRef);
        foreach(var x in input.SelectedPlans){Key(x.LineKey);Id(x.AllocationPlanRef);}
        if(input.SelectedPlans.Select(x=>x.LineKey).Distinct().Count()!=input.SelectedPlans.Length)throw Error("REP_SELECTION_DUPLICATED");
        if(SelectionDigest(input.SelectedPlans)!=input.SelectionDigest)throw Error("REP_SELECTION_DIGEST_INVALID");
        var c=Store.Decode<RepBatchInputV1>(Bytes(input));
        return Command(c.Command,"generate",new{kind="batch",input=c},async(a,s,t)=>
        {
            var doc=await s.Read(Id(c.SuggestionRef),"suggestion",t);await Visible(a,s,doc,t);var suggestion=Store.Decode<RepSuggestionV1>(doc.Body);Exact(c.SuggestionRef,suggestion,suggestion.Ref);
            var canonical=Hash(new{c.SuggestionRef,c.SelectionDigest});var existing=await db.Database.SqlQuery<Guid?>($"""
                SELECT id AS "Value" FROM cp6_replenish_v1.batches WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND canonical={canonical}
                """).SingleOrDefaultAsync(t);
            if(existing is {} prior){Replayed=true;var d=await s.Read(prior,"batch",t);await Visible(a,s,d,t);var saved=Store.Decode<RepBatchV1>(d.Body);
                return(prior,saved with{Items=saved.Items.Select(x=>x with{Outcome="EXISTING",Splits=x.Splits.Select(y=>y with{Outcome="EXISTING"}).ToArray()}).ToArray()});}
            var observation=await s.Observation(doc.ScopeKey,t);var (intents,active)=await Active(s,t);
            var selected=new List<(RepPlanV1 Plan,RepRowV1 Row)>();
            var existingItems=new List<RepBatchItemV1>();
            foreach(var choice in c.SelectedPlans.OrderBy(x=>x.LineKey,StringComparer.Ordinal))
            {
                var row=suggestion.Rows.SingleOrDefault(x=>x.LineKey==choice.LineKey&&x.AllocationPlanRef==choice.AllocationPlanRef)??throw Error("REP_PLAN_NOT_SELECTED",422);
                var plan=Store.Decode<RepPlanV1>((await s.Read(Id(choice.AllocationPlanRef),"plan",t)).Body);Exact(choice.AllocationPlanRef,plan,plan.Ref);
                var priorOrders=await db.Database.SqlQuery<Guid>($"""
                    SELECT order_id AS "Value" FROM cp6_replenish_v1.split_orders WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND plan_id={Id(plan.Ref)}
                    """).ToArrayAsync(t);
                if(priorOrders.Length!=0)
                {
                    var priorSplits=new List<RepBatchSplitV1>();
                    foreach(var id in priorOrders)
                    {var old=await s.Read(id,"order",t);await Visible(a,s,old,t);var order=Store.Decode<RepOrderV1>(old.Body);
                     var split=plan.Splits.Single(x=>x.SourceLocationRef==order.Input.Source.SourceLocationRef);priorSplits.Add(new(split.SplitKey,order.Ref,split.Quantity,"EXISTING"));}
                    if(priorSplits.Count!=plan.Splits.Length)throw Error("REP_ORIGINAL_REGISTRATION_INCOMPLETE",503);
                    existingItems.Add(new(row.LineKey,plan.Ref,plan.TotalQuantity,priorSplits.OrderBy(x=>x.SplitKey,StringComparer.Ordinal).ToArray(),"EXISTING",null));continue;
                }
                var rule=observation.Rules.SingleOrDefault(x=>x.Ref==row.RuleRef)??throw Error("REP_RULE_STALE",412);
                var cut=Store.Decode<RepCutV1>((await s.Read(Id(plan.CutRef),"cut",t)).Body);
                var before=await s.ObservationAt(doc.ScopeKey,cut.UpstreamCutRef,t);
                var current=ReplenishPlanningCalculatorV1.Calculate(observation,rule,row.TargetLocationRef,intents,active,DateTimeOffset.UtcNow);
                if(current.Row.Coverage!="COMPLETE"||current.Row.Proposed==null||Number(current.Row.Proposed.Value)<Number(plan.TotalQuantity.Value))throw Error("REP_DEMAND_CHANGED",409);
                var currentSpans=current.Splits.SelectMany(x=>x.Source.Spans).ToArray();
                foreach(var split in plan.Splits)foreach(var span in split.Source.Spans)
                {
                    if(ReplenishPlanningCalculatorV1.Subtract(span,currentSpans.Where(x=>x.RootRef==span.RootRef)).Any())throw Error("REP_SOURCE_CHANGED",412);
                    var originalSource=before.Roots.Single(x=>x.Span.RootRef==span.RootRef&&Number(x.Span.From)<=Number(span.From)&&Number(x.Span.To)>=Number(span.To));
                    var currentSources=observation.Roots.Where(x=>x.Span.RootRef==span.RootRef&&Number(x.Span.From)<Number(span.To)&&Number(x.Span.To)>Number(span.From));
                    if(currentSources.Any(x=>x.LocationRef!=split.SourceLocationRef||originalSource.StockObservationRef!=x.StockObservationRef||originalSource.MasterConditionRef!=x.MasterConditionRef))
                        throw Error("REP_SOURCE_CHANGED",412);
                }
                // Selected lines also share the source budget: a batch cannot freeze one range twice.
                foreach(var previous in selected)foreach(var span in plan.Splits.SelectMany(x=>x.Source.Spans))
                    if(previous.Plan.Splits.SelectMany(x=>x.Source.Spans).Any(x=>x.RootRef==span.RootRef&&Number(x.From)<Number(span.To)&&Number(span.From)<Number(x.To)))throw Error("REP_SOURCE_OVERLAP",409);
                selected.Add((plan,row));
            }
            if(selected.Count!=0)await Visible(a,observation,doc.CreatorId,doc.DepartmentId,t,current:true);
            var batchId=Guid.NewGuid();var items=new List<RepBatchItemV1>(existingItems);
            foreach(var (plan,row) in selected)
            {
                var orders=new List<RepBatchSplitV1>();var intentSplits=new List<RepIntentSplitV1>();
                foreach(var split in plan.Splits)
                {
                    var id=Guid.NewGuid();var orderInput=new RepOrderInputV1(observation.Scope,row.ProductRef,row.TechnicalRef,row.OwnerRef,row.BaseUomRef,row.TargetLocationRef,
                        split.Source,split.Quantity,2,"BATCH",suggestion.Ref,c.Command.Reason??"批量补货登记",null);
                    var reference=Ref("order",id,orderInput);var order=new RepOrderV1(reference,orderInput,plan.Ref,"REGISTERED","KNOWN",split.Quantity,Q(0,row.BaseUomRef),split.Quantity,null,[],["READ"],suggestion.CutRef,"1");
                    await s.SaveOrderVersion(id,id,order,a.ActorId,doc.DepartmentId,t);
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_replenish_v1.split_orders VALUES({db.CurrentTenantId},{scope.EnvironmentId},{Id(plan.Ref)},{split.SplitKey},{id})",t);
                    orders.Add(new(split.SplitKey,reference,split.Quantity,"CREATED"));intentSplits.Add(new(split.SplitKey,reference,split.Quantity,"REGISTERED"));
                }
                var intentId=Guid.NewGuid();var intentRef=Ref("active-intent",intentId,new{plan.Ref,plan.DemandSlotKey,plan.TotalQuantity});
                var intent=new RepIntentV1(intentRef,plan.DemandSlotKey,plan.Ref,"REGISTERED_COUNTED",plan.TotalQuantity,plan.TotalQuantity,Q(0,row.BaseUomRef),Q(0,row.BaseUomRef),intentSplits.ToArray(),suggestion.CutRef,"COMPLETE");
                await s.Save(intentId,"intent",a.ActorId,doc.DepartmentId,doc.ScopeKey,intent,t);
                items.Add(new(row.LineKey,plan.Ref,plan.TotalQuantity,orders.ToArray(),"CREATED",null));
            }
            var result=new RepBatchV1(Ref("batch",batchId,new{c.SuggestionRef,c.SelectionDigest,items}),items.ToArray(),"COMMITTED","NOT_REQUESTED");
            await s.Save(batchId,"batch",a.ActorId,doc.DepartmentId,doc.ScopeKey,result,t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_replenish_v1.batches VALUES({db.CurrentTenantId},{scope.EnvironmentId},{canonical},{batchId});
                INSERT INTO cp6_replenish_v1.events VALUES({db.CurrentTenantId},{scope.EnvironmentId},{batchId},'REGISTERED',{Bytes(result)});
                """,t);return(batchId,result);
        },ct);
    }
    public Task<RepOperationV1> OperationAsync(string key,CancellationToken ct=default)
    {
        Key(key);return Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"query",false,async(a,t)=>
        {await Lease(a,t);var s=Persistence;await s.Fence(false,t);var command=await s.Command(a.ActorId,key,t);
         if(command==null)return new RepOperationV1(key,"UNKNOWN",null);var doc=await s.Read(command.ResultId,null,t);await Visible(a,s,doc,t);
         object result=doc.Kind is "order" or "order-revision"?a.ProjectReplenishResult(Store.Decode<RepOrderV1>(command.Body)):JsonSerializer.Deserialize<JsonElement>(command.Body);
         return new RepOperationV1(key,"COMMITTED",result);},ct);
    }
}

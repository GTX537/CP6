using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms.Replenish;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record RepFixture(ReplenishPlanningOwnerV1 Owner,RepObservationV1 Observation,OperationsOwnerV1 Authority);
    private static RepRefV1 RepReference(string owner,string id)=>new(owner,id,"1","REP-ACCEPTANCE-INPUT","1",Hash(new{owner,id}));
    private static RepCommandV1 RepCommand()=>new(Guid.NewGuid().ToString("D"),null,"模拟资料生产业务验收");
    private static RepSuggestInputV1 SuggestInput(RepObservationV1 o)=>new(RepCommand(),o.Scope,o.Rules.Select(x=>x.Ref).ToArray(),o.Rules.SelectMany(x=>x.TargetLocationRefs).Distinct().ToArray());
    private static RepBatchInputV1 BatchInput(RepSuggestionV1 s)
    {var selected=s.Rows.Where(x=>x.AllocationPlanRef!=null).Select(x=>new RepSelectionV1(x.LineKey,x.AllocationPlanRef!)).ToArray();return new(RepCommand(),s.Ref,selected,SelectionDigest(selected));}
    private static ReplenishPlanningOwnerV1 RepOwner(Harness h,OperationsOwnerV1 a,bool simulate=true)
        =>new(h.Db,a,ScopeFence(h),Options.Create(new ReplenishSimulationOptionsV1{Enabled=simulate}));
    private static OperationsOwnerV1 RepAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterScope("wms-replenish","Replenishment",[1,2,3,4,5],1);
        catalog.RegisterFields("wms-replenish",[new("OperatorCd","OperatorCd"),new("Remarks","Remarks")]);catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private async Task<RepFixture> RepSetup(Harness h,string onHand="0",string min="10",string max="10",string? lot=null)
    {
        var authority=await GrantReplenish(h);await RegisterScope(h);await EngineeringAction(h,"wms-replenish","simulate-source");
        h.Db.Warehouses.Add(new(){WarehouseCd="W1",WarehouseName="模拟正式仓"});
        var locations=new[]{new Location{WarehouseCd="W1",LocationCd="SRC-A",AreaCd="SOURCE"},new Location{WarehouseCd="W1",LocationCd="SRC-B",AreaCd="SOURCE"},new Location{WarehouseCd="W1",LocationCd="PIK-BLOCK-CHECK",AreaCd="TARGET"}};
        h.Db.Locations.AddRange(locations);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var map=locations.Select(x=>new RepLocationMapV1(RepReference("MASTER",x.Id.ToString("D")),x.Id)).ToArray();
        var product=RepReference("PLM","product");var technical=RepReference("PLM","technical");var own=RepReference("MASTER","owner");var unit=RepReference("MASTER","EA");
        var sourceRule=RepReference("MASTER","reserve-policy");var domain=RepReference("MASTER","target-domain");var metric=RepReference("MASTER","pieces");
        var rule=new RepRuleV1(RepReference("MASTER","replenish-rule"),[map[2].Ref],product,technical,own,unit,new(min,unit),new(max,unit),lot==null?null:new(lot,unit),sourceRule,DateTimeOffset.UtcNow.AddDays(-1),null,"COMPLETE");
        var cut=RepReference("STOCK-CUT",Guid.NewGuid().ToString("D"));
        RepRootObservationV1 Root(int index,string quantity)=>new(map[index].Ref,product,technical,own,unit,new(RepReference("STOCK","root-"+index),"0",quantity),cut,RepReference("MASTER","condition-"+index),DateTimeOffset.UtcNow.AddDays(index+1),DateTimeOffset.UtcNow.AddDays(-2),[sourceRule],[new(domain,metric,unit,"1","1")],true,[]);
        var observation=new RepObservationV1(cut,new(RepReference("MASTER","site"),RepReference("MASTER","warehouse")),null,DateTimeOffset.UtcNow,"SIMULATED","COMPLETE",map,[rule],
            [new(map[2].Ref,product,technical,own,unit,new(onHand,unit),true,"COMPLETE",true,[new(domain,metric,unit,"LIMITED","100")])],[Root(0,"3"),Root(1,"4")],[]);
        var owner=RepOwner(h,authority);await owner.PublishSimulationAsync(new(observation));return new(owner,observation,authority);
    }
    private static RepObservationV1 NewCut(RepObservationV1 o)=>o with{CutRef=RepReference("STOCK-CUT",Guid.NewGuid().ToString("D")),ObservedAt=DateTimeOffset.UtcNow};

    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_same_cut_3_plus_2_targets_10_proposes_5_and_batch_counts_once()
    {
        await using var h=await Seed();var f=await RepSetup(h,"3");var o=f.Observation;var incomingRoot=RepReference("STOCK","incoming");
        var incoming=new RepIncomingObservationV1("external-intent",Slot(o.Rules[0],o.Locations[2].Ref),[new(incomingRoot,"0","2")],"COMPLETE");
        o=NewCut(o) with{Incoming=[incoming,incoming with{IntentKey="transport-alias"}]};await f.Owner.PublishSimulationAsync(new(o));
        var suggestion=await f.Owner.SuggestAsync(SuggestInput(o));var row=Assert.Single(suggestion.Rows);
        var cut=await f.Owner.CutAsync(Guid.Parse(suggestion.CutRef.Id));Assert.Equal(o.CutRef,cut.UpstreamCutRef);Assert.Equal("SIMULATED",cut.DataKind);
        Assert.Equal("3.00000000",row.OnHand!.Value);Assert.Equal("2.00000000",row.OpenIncoming!.Value);Assert.Equal("5.00000000",row.Proposed!.Value);
        var plan=await f.Owner.PlanAsync(Guid.Parse(row.AllocationPlanRef!.Id));Assert.Equal(new[]{"3.00000000","2.00000000"},plan.Splits.Select(x=>x.Quantity.Value));
        var input=BatchInput(suggestion);var batch=await f.Owner.GenerateAsync(input);Assert.Equal("COMMITTED",batch.Registration);Assert.Equal("NOT_REQUESTED",batch.DispatchSummary);
        Assert.Equal(TransferJson(batch),TransferJson(await f.Owner.GenerateAsync(input)));
        var existing=await f.Owner.GenerateAsync(input with{Command=RepCommand()});Assert.Equal(batch.Ref,existing.Ref);Assert.All(existing.Items,x=>Assert.Equal("EXISTING",x.Outcome));
        var after=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(o))).Rows);Assert.Equal("7.00000000",after.OpenIncoming!.Value);Assert.Equal("10.00000000",after.EffectivePosition!.Value);
        Assert.Contains("NO_NEED",after.Reasons);Assert.Null(after.AllocationPlanRef);
        await h.Db.Locations.Where(x=>x.AreaCd=="TARGET").ExecuteUpdateAsync(x=>x.SetProperty(l=>l.IsBlocked,true));
        Assert.Equal(batch.Ref,(await f.Owner.BatchAsync(Guid.Parse(batch.Ref.Id))).Ref);
        Assert.Equal(batch.Ref,(await f.Owner.GenerateAsync(input)).Ref);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='order'").SingleAsync());
        Assert.False(await h.Db.StockTransactions.AnyAsync());Assert.False(await h.Db.MobileTasks.AnyAsync());
        foreach(var split in batch.Items[0].Splits){var order=await f.Owner.OrderAsync(Guid.Parse(split.OrderRef.Id));Assert.Equal("REGISTERED",order.Lifecycle);Assert.Equal("0.00000000",order.Moved!.Value);Assert.Null(order.ExecutionLink);}
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_lot_rounds_total_once_unknown_stays_null_and_formal_blocked_target_rejects()
    {
        await using var h=await Seed();var f=await RepSetup(h,lot:"5");var row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows);
        Assert.Equal("5.00000000",row.Proposed!.Value);Assert.Equal(new[]{"3.00000000","2.00000000"},row.Sources.Select(x=>x.Quantity.Value));
        var unknown=NewCut(f.Observation) with{Targets=[f.Observation.Targets[0] with{Coverage="UNKNOWN",Initialized=false,OnHand=null}]};await f.Owner.PublishSimulationAsync(new(unknown));
        row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(unknown))).Rows);Assert.Equal("UNKNOWN",row.Coverage);Assert.Null(row.OnHand);Assert.Null(row.Proposed);Assert.Null(row.AllocationPlanRef);
        await h.Db.Locations.Where(x=>x.LocationCd=="PIK-BLOCK-CHECK").ExecuteUpdateAsync(x=>x.SetProperty(l=>l.IsBlocked,true));
        Assert.Equal("REP_LOCATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SuggestAsync(SuggestInput(unknown)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_concurrent_distinct_suggestions_have_one_registration_and_source_budget()
    {
        await using var h=await Seed();var f=await RepSetup(h);var first=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));var second=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peerHarness=h with{Db=db};var peer=RepOwner(peerHarness,RepAuthority(peerHarness));
        async Task<bool> Send(ReplenishPlanningOwnerV1 owner,RepBatchInputV1 c)
        {try{await owner.GenerateAsync(c);return true;}catch(OrderPriceCorrectionException e)when(e.Message=="REP_DEMAND_CHANGED"){return false;}}
        Assert.Single(await Task.WhenAll(Send(f.Owner,first),Send(peer,second)),x=>x);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='intent'").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_second_order_failure_rolls_back_batch_intent_orders_and_command()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RepSetup(h);var input=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));var writes=0;
        hook.Before=command=>{if(command.CommandText.Contains("INSERT INTO cp6_replenish_v1.split_orders",StringComparison.Ordinal)&&++writes==2)throw new InvalidOperationException("simulated-second-order-failure");return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Owner.GenerateAsync(input));hook.Before=null;Assert.Equal(2,writes);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind IN ('order','intent','batch')").SingleAsync());
        Assert.Equal("UNKNOWN",(await f.Owner.OperationAsync(input.Command.CommandKey)).State);Assert.Equal("COMMITTED",(await f.Owner.GenerateAsync(input)).Registration);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_original_commit_recovers_after_write_revocation_and_current_scope_protects_reads()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await RepSetup(h);var input=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.GenerateAsync(input))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==423&&x.ActionCode=="generate").ExecuteDeleteAsync();
        var result=await f.Owner.GenerateAsync(input);Assert.Equal("COMMITTED",(await f.Owner.OperationAsync(input.Command.CommandKey)).State);
        Assert.Equal("COMMITTED",result.Registration);
        await h.Db.WmsRoleScopes.Where(x=>x.WarehouseCd=="W1").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.AreaCd,"TARGET"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BatchAsync(Guid.Parse(result.Ref.Id)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_exact_eight_decimals_wide_totals_and_typed_zero_capacity()
    {
        await using var h=await Seed();var f=await RepSetup(h,min:"0.00000003",max:"0.00000003");var o=NewCut(f.Observation);
        o=o with{Roots=[o.Roots[0] with{Span=o.Roots[0].Span with{To="0.00000001"}},o.Roots[1] with{Span=o.Roots[1].Span with{To="0.00000002"}}]};await f.Owner.PublishSimulationAsync(new(o));
        var row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(o))).Rows);Assert.Equal("0.00000003",row.Proposed!.Value);
        var zero=NewCut(o) with{Targets=[o.Targets[0] with{CapacityDomains=[o.Targets[0].CapacityDomains[0] with{FreeValue="0"}]}]};await f.Owner.PublishSimulationAsync(new(zero));
        row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(zero))).Rows);Assert.Contains("CAPACITY_INSUFFICIENT",row.Reasons);Assert.Null(row.AllocationPlanRef);
        var wide=NewCut(o) with{Targets=[o.Targets[0] with{OnHand=new("18999999999999.12345679",o.Rules[0].BaseUomRef)}]};await f.Owner.PublishSimulationAsync(new(wide));
        row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(wide))).Rows);Assert.Equal("18999999999999.12345679",row.EffectivePosition!.Value);Assert.Contains("NO_NEED",row.Reasons);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_changed_root_version_rejects_original_plan_and_input_conflicts_do_not_overwrite()
    {
        await using var h=await Seed();var f=await RepSetup(h);var c=SuggestInput(f.Observation);var result=await f.Owner.SuggestAsync(c);var input=BatchInput(result);
        var observationChanged=NewCut(f.Observation) with{CutRef=f.Observation.CutRef with{Version="2"},Roots=[f.Observation.Roots[0] with{StockObservationRef=RepReference("STOCK","changed-observation")},f.Observation.Roots[1]]};
        await f.Owner.PublishSimulationAsync(new(observationChanged));Assert.Equal("REP_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.GenerateAsync(input))).Message);
        var relocated=NewCut(f.Observation) with{Roots=[f.Observation.Roots[0] with{LocationRef=f.Observation.Locations[1].Ref},f.Observation.Roots[1]]};
        await f.Owner.PublishSimulationAsync(new(relocated));Assert.Equal("REP_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.GenerateAsync(input))).Message);
        var changed=NewCut(f.Observation) with{Roots=[f.Observation.Roots[0] with{Span=f.Observation.Roots[0].Span with{RootRef=f.Observation.Roots[0].Span.RootRef with{Version="2"}}},f.Observation.Roots[1]]};await f.Owner.PublishSimulationAsync(new(changed));
        Assert.Equal("REP_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.GenerateAsync(input))).Message);
        Assert.Equal("REP_COMMAND_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SuggestAsync(c with{Command=c.Command with{Reason="另一输入"}}))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RepOwner(h,f.Authority,false).PublishSimulationAsync(new(changed)));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_actual_HTTP_simulated_source_suggestion_registration_and_closed_input_contract()
    {
        await using var h=await Seed();var f=await RepSetup(h);var settings=await DocumentHostSettings(new());settings["ReplenishSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);var root="/api/wms/replenish/v1";
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/simulation/observations",new RepSimulationInputV1(NewCut(f.Observation)))))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var malformed=JsonSerializer.SerializeToNode(new RepSimulationInputV1(NewCut(f.Observation)),RepJson)!.AsObject();malformed["observation"]!["fakePermission"]=true;
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/simulation/observations",malformed)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var missing=JsonSerializer.SerializeToNode(new RepSimulationInputV1(NewCut(f.Observation)),RepJson)!.AsObject();missing["observation"]!["roots"]![0]!.AsObject().Remove("movable");
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/simulation/observations",missing)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var c=SuggestInput(f.Observation);var forged=JsonSerializer.SerializeToNode(c,RepJson)!.AsObject();forged["tenantId"]=Guid.NewGuid();
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/suggestions",forged)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        RepSuggestionV1 suggestion;
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/suggestions",c)))
        {Assert.Equal(HttpStatusCode.Created,response.StatusCode);suggestion=JsonSerializer.Deserialize<RepSuggestionV1>(await response.Content.ReadAsStringAsync(),RepJson)!;}
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/batches",BatchInput(suggestion))))
        {Assert.Equal(HttpStatusCode.Created,response.StatusCode);var batch=JsonSerializer.Deserialize<RepBatchV1>(await response.Content.ReadAsStringAsync(),RepJson)!;Assert.Equal(2,Assert.Single(batch.Items).Splits.Length);}
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_overlapping_batch_selection_reuses_original_split_orders_and_counts_new_dimension_once()
    {
        await using var h=await Seed();var f=await RepSetup(h);var o=NewCut(f.Observation);var otherProduct=RepReference("PLM","other-product");
        var second=o.Rules[0] with{Ref=RepReference("MASTER","rule-two"),ProductRef=otherProduct};
        o=o with{Rules=[o.Rules[0],second],Targets=[o.Targets[0],o.Targets[0] with{ProductRef=otherProduct}],
            Roots=[..o.Roots,..o.Roots.Select(x=>x with{ProductRef=otherProduct,Span=x.Span with{RootRef=RepReference("STOCK","other-"+x.Span.RootRef.Id)}})]};
        await f.Owner.PublishSimulationAsync(new(o));var suggestion=await f.Owner.SuggestAsync(SuggestInput(o));var all=BatchInput(suggestion);
        var one=new[]{all.SelectedPlans[0]};var first=await f.Owner.GenerateAsync(all with{SelectedPlans=one,SelectionDigest=SelectionDigest(one)});
        var combined=await f.Owner.GenerateAsync(all with{Command=RepCommand()});Assert.Equal(2,combined.Items.Length);
        var old=Assert.Single(combined.Items,x=>x.Outcome=="EXISTING");Assert.Equal(first.Items[0].Splits.Select(x=>x.OrderRef.Id).Order(),old.Splits.Select(x=>x.OrderRef.Id).Order());
        Assert.Single(combined.Items,x=>x.Outcome=="CREATED");Assert.Equal(4,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='order'").SingleAsync());
        Assert.All((await f.Owner.SuggestAsync(SuggestInput(o))).Rows,x=>Assert.Equal("7.00000000",x.OpenIncoming!.Value));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_shared_capacity_domain_uses_net_delta_and_unrepresentable_conversion_fails_fast()
    {
        await using var h=await Seed();var f=await RepSetup(h);var o=NewCut(f.Observation);var parent=RepReference("MASTER","shared-parent");
        var target=o.Targets[0];var domain=target.CapacityDomains[0] with{DomainRef=parent,FreeValue="0"};
        o=o with{Targets=[target with{CapacityDomains=[..target.CapacityDomains,domain]}],Roots=o.Roots.Select(x=>x with{CapacityDeltas=[..x.CapacityDeltas,
            new(parent,domain.MetricRef,domain.UomRef,"-1","1"),new(parent,domain.MetricRef,domain.UomRef,"1","1")]}).ToArray()};
        await f.Owner.PublishSimulationAsync(new(o));Assert.NotNull(Assert.Single((await f.Owner.SuggestAsync(SuggestInput(o))).Rows).AllocationPlanRef);
        var bad=NewCut(o) with{Roots=o.Roots.Select(x=>x with{CapacityDeltas=[x.CapacityDeltas[0] with{Numerator="1",Denominator="3"},..x.CapacityDeltas.Skip(1)]}).ToArray()};
        await f.Owner.PublishSimulationAsync(new(bad));Assert.Equal("REP_CAPACITY_PRECISION_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.SuggestAsync(SuggestInput(bad)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepPlanning_equivalent_adjacent_root_fragments_keep_frozen_plan_and_foreign_tenant_cannot_read()
    {
        await using var h=await Seed();var f=await RepSetup(h);var input=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));var root=f.Observation.Roots[0];
        var next=NewCut(f.Observation) with{Roots=[root with{Span=root.Span with{To="1"}},root with{Span=root.Span with{From="1"}},f.Observation.Roots[1]]};
        await f.Owner.PublishSimulationAsync(new(next));var batch=await f.Owner.GenerateAsync(input);Assert.Equal("COMMITTED",batch.Registration);
        await using var foreign=await Seed();var peer=await RepSetup(foreign);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>peer.Owner.BatchAsync(Guid.Parse(batch.Ref.Id)))).Status);
        Assert.Equal("UNKNOWN",(await peer.Owner.OperationAsync(input.Command.CommandKey)).State);
    }
}

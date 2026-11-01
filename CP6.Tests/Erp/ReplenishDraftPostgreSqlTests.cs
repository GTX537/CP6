using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms.Replenish;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static RepDraftInputV1 Manual(RepObservationV1 o,int source=0,string? amount=null,string from="0")
    {
        var r=o.Roots[source];var rule=o.Rules[0];var quantity=amount==null?Q(SpanAmount(r.Span),rule.BaseUomRef):new RepQuantityV1(amount,rule.BaseUomRef);
        var span=r.Span with{From=from,To=Text(Number(from)+Number(quantity.Value))};
        return new(RepCommand(),new(o.Scope,rule.ProductRef,rule.TechnicalRef,rule.OwnerRef,rule.BaseUomRef,o.Locations[2].Ref,
            new(r.LocationRef,[span],quantity,o.CutRef,r.ExpiryAt,r.ReceivedAt,r.MasterConditionRef),quantity,2,"MANUAL",null,"模拟人工补货",null));
    }
    private static async Task DraftActions(Harness h)
    {await EngineeringAction(h,"wms-replenish","edit");await EngineeringAction(h,"wms-replenish","prepare");}
    private static RepCommandV1 CurrentCommand(RepOrderV1 order)=>RepCommand() with{ExpectedRevision=order.Revision};
    private static Task<int> IntentCount(Harness h)=>h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='intent'").SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_edit_history_original_replay_and_prepare_counts_once_without_moving_stock()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var create=Manual(f.Observation);
        var original=await f.Owner.CreateDraftAsync(create);var id=Guid.Parse(original.Ref.Id);Assert.Equal("DRAFT",original.Lifecycle);Assert.Null(original.SelectionRef);Assert.Null(original.CurrentCutRef);
        Assert.Equal(0,await IntentCount(h));Assert.Equal("7.00000000",(await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows[0].Proposed!.Value);
        var edit=Manual(f.Observation,amount:"2",from:"1") with{Command=CurrentCommand(original)};
        var changed=await f.Owner.EditDraftAsync(id,edit);Assert.Equal("2",changed.Revision);Assert.Equal("2",changed.Planned.Value);
        Assert.Equal(TransferJson(original),TransferJson(await f.Owner.CreateDraftAsync(create)));
        Assert.Equal("2",(await f.Owner.OrderAsync(id)).Revision);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.EditDraftAsync(id,edit with{Command=edit.Command with{CommandKey=Guid.NewGuid().ToString("D")}}))).Status);
        var history=JsonSerializer.SerializeToElement(await f.Owner.OrderHistoryAsync(id,limit:1),RepJson);Assert.Equal("1",history.GetProperty("items")[0].GetProperty("revision").GetString());Assert.Equal("1",history.GetProperty("nextRevision").GetString());
        var prepare=await f.Owner.PrepareAsync(id,CurrentCommand(changed));Assert.False(prepare.CanDispatch);Assert.Null(prepare.AdoptionRef);Assert.Contains("WAITING_TRANSFER_ADOPTION",prepare.Blockers);
        var current=await f.Owner.OrderAsync(id);Assert.Equal("REGISTERED",current.Lifecycle);Assert.Equal("3",current.Revision);Assert.Equal(1,await IntentCount(h));
        Assert.Equal(prepare.Ref,(await f.Owner.PrepareAsync(id,CurrentCommand(current))).Ref);Assert.Equal(1,await IntentCount(h));
        var row=(await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows[0];Assert.Equal("2.00000000",row.OpenIncoming!.Value);Assert.Equal("5.00000000",row.Proposed!.Value);Assert.Equal("1.00000000",row.Sources[0].Quantity.Value);
        Assert.Equal("REP_DRAFT_FROZEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.EditDraftAsync(id,edit with{Command=CurrentCommand(current)}))).Message);
        Assert.False(await h.Db.StockTransactions.AnyAsync());Assert.False(await h.Db.MobileTasks.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_manual_non_FEFO_selection_and_typed_capacity_use_exact_selected_ranges()
    {
        await using var h=await Seed();var f=await RepSetup(h,min:"2",max:"2");await DraftActions(h);
        var o=NewCut(f.Observation) with{Roots=[f.Observation.Roots[0] with{CapacityDeltas=[f.Observation.Roots[0].CapacityDeltas[0] with{Numerator="100"}]},f.Observation.Roots[1]],Targets=[f.Observation.Targets[0] with{CapacityDomains=[f.Observation.Targets[0].CapacityDomains[0] with{FreeValue="2"}]}]};
        await f.Owner.PublishSimulationAsync(new(o));Assert.Null((await f.Owner.SuggestAsync(SuggestInput(o))).Rows[0].AllocationPlanRef);
        var draft=await f.Owner.CreateDraftAsync(Manual(o,1,"2"));var p=await f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),CurrentCommand(draft));
        Assert.Equal(o.Roots[1].Span.RootRef,Assert.Single(p.SourceSpans).RootRef);Assert.Equal(1,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_concurrent_manual_and_batch_share_target_and_source_budget()
    {
        await using var h=await Seed();var f=await RepSetup(h,min:"7",max:"7");await DraftActions(h);
        var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));var batch=BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation)));
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peerHarness=h with{Db=db};var peer=RepOwner(peerHarness,RepAuthority(peerHarness));
        async Task<bool> Send(Func<Task> run){try{await run();return true;}catch(OrderPriceCorrectionException e)when(e.Message is "REP_DEMAND_CHANGED" or "REP_SOURCE_ALREADY_REGISTERED"){return false;}}
        Assert.Single(await Task.WhenAll(Send(async()=>await f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),CurrentCommand(draft))),Send(async()=>await peer.GenerateAsync(batch))),x=>x);
        Assert.Equal(1,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_prepare_write_failure_rolls_back_registration_version_intent_and_command()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));var command=CurrentCommand(draft);var hit=false;
        hook.Before=x=>{if(x.CommandText.Contains("INSERT INTO cp6_replenish_v1.prepare_registrations",StringComparison.Ordinal)){hit=true;throw new InvalidOperationException("simulated-prepare-registration-failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),command));hook.Before=null;Assert.True(hit);
        Assert.Equal(0,await IntentCount(h));Assert.Equal("DRAFT",(await f.Owner.OrderAsync(Guid.Parse(draft.Ref.Id))).Lifecycle);Assert.Equal("UNKNOWN",(await f.Owner.OperationAsync(command.CommandKey)).State);
        await f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),command);Assert.Equal(1,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_prepare_lost_ack_recovers_after_revoke_and_current_source_scope_protects_history()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));var c=CurrentCommand(draft);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),c))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==423&&x.ActionCode=="prepare").ExecuteDeleteAsync();
        var original=await f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),c);Assert.False(original.CanDispatch);Assert.Equal(1,await IntentCount(h));Assert.Equal("COMMITTED",(await f.Owner.OperationAsync(c.CommandKey)).State);
        await h.Db.WmsRoleScopes.Where(x=>x.WarehouseCd=="W1").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.AreaCd,"TARGET"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.OrderHistoryAsync(Guid.Parse(draft.Ref.Id)))).Status);
        await using var foreign=await Seed();var other=await RepSetup(foreign);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>other.Owner.OrderAsync(Guid.Parse(draft.Ref.Id)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_current_source_capacity_and_version_checks_prevent_registration()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));var id=Guid.Parse(draft.Ref.Id);
        var zero=NewCut(f.Observation) with{Targets=[f.Observation.Targets[0] with{CapacityDomains=[f.Observation.Targets[0].CapacityDomains[0] with{FreeValue="0"}]}]};await f.Owner.PublishSimulationAsync(new(zero));
        Assert.Equal("REP_CAPACITY_INSUFFICIENT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(id,CurrentCommand(draft)))).Message);
        var changed=NewCut(f.Observation) with{Roots=[f.Observation.Roots[0] with{StockObservationRef=RepReference("STOCK","new-version")},f.Observation.Roots[1]]};await f.Owner.PublishSimulationAsync(new(changed));
        Assert.Equal("REP_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(id,CurrentCommand(draft)))).Message);Assert.Equal(0,await IntentCount(h));
        await f.Owner.PublishSimulationAsync(new(NewCut(f.Observation)));await h.Db.Locations.Where(x=>x.AreaCd=="TARGET").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.IsBlocked,true));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(id,CurrentCommand(draft)))).Status);Assert.Equal(0,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_expedite_and_remarks_use_current_independent_authority()
    {
        await using var h=await Seed();var f=await RepSetup(h);var normal=Manual(f.Observation);var urgent=normal with{Input=normal.Input with{Priority=1}};
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.CreateDraftAsync(urgent))).Status);
        await EngineeringAction(h,"wms-replenish","expedite");Assert.Equal(1,(await f.Owner.CreateDraftAsync(urgent)).Input.Priority);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-replenish",FieldName="Remarks",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var note=normal with{Command=RepCommand(),Input=normal.Input with{Remarks="受控备注"}};Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.CreateDraftAsync(note))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==72&&x.ResourceKey=="wms-replenish"&&x.FieldName=="Remarks").ExecuteDeleteAsync();var draft=await f.Owner.CreateDraftAsync(note);Assert.Equal("受控备注",draft.Input.Remarks);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-replenish",FieldName="Remarks",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();Assert.Null((await f.Owner.OrderAsync(Guid.Parse(draft.Ref.Id))).Input.Remarks);Assert.Null((await f.Owner.CreateDraftAsync(note)).Input.Remarks);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_root_version_does_not_release_registered_source_budget()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));await f.Owner.PrepareAsync(Guid.Parse(draft.Ref.Id),CurrentCommand(draft));
        var root=f.Observation.Roots[0];var next=NewCut(f.Observation) with{Roots=[root with{Span=root.Span with{RootRef=root.Span.RootRef with{Version="2"}}},f.Observation.Roots[1]]};
        await f.Owner.PublishSimulationAsync(new(next));var row=(await f.Owner.SuggestAsync(SuggestInput(next))).Rows[0];Assert.Equal("4.00000000",row.Proposed!.Value);Assert.Equal(next.Roots[1].Span.RootRef,Assert.Single(Assert.Single(row.Sources).Spans).RootRef);
        var duplicate=await f.Owner.CreateDraftAsync(Manual(next));Assert.Equal("REP_SOURCE_ALREADY_REGISTERED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(Guid.Parse(duplicate.Ref.Id),CurrentCommand(duplicate)))).Message);Assert.Equal(1,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_batch_prepare_preserves_one_intent_and_rejects_changed_frozen_rule()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var batch=await f.Owner.GenerateAsync(BatchInput(await f.Owner.SuggestAsync(SuggestInput(f.Observation))));
        var first=await f.Owner.OrderAsync(Guid.Parse(batch.Items[0].Splits[0].OrderRef.Id));var p=await f.Owner.PrepareAsync(Guid.Parse(first.Ref.Id),CurrentCommand(first));Assert.False(p.CanDispatch);Assert.Equal(1,await IntentCount(h));
        Assert.Equal(p.Ref,(await f.Owner.PrepareAsync(Guid.Parse(first.Ref.Id),CurrentCommand(first))).Ref);
        var second=await f.Owner.OrderAsync(Guid.Parse(batch.Items[0].Splits[1].OrderRef.Id));var changed=NewCut(f.Observation) with{Rules=[f.Observation.Rules[0] with{Ref=f.Observation.Rules[0].Ref with{Version="2"}}]};await f.Owner.PublishSimulationAsync(new(changed));
        Assert.Equal("REP_RULE_STALE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareAsync(Guid.Parse(second.Ref.Id),CurrentCommand(second)))).Message);Assert.Equal(1,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_concurrent_full_edits_accept_exactly_one_current_revision()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation));var id=Guid.Parse(draft.Ref.Id);
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peerHarness=h with{Db=db};var peer=RepOwner(peerHarness,RepAuthority(peerHarness));
        async Task<bool> Edit(ReplenishPlanningOwnerV1 owner,string reason){try{await owner.EditDraftAsync(id,new(CurrentCommand(draft),draft.Input with{Reason=reason}));return true;}catch(OrderPriceCorrectionException e)when(e.Message=="REP_REVISION_STALE"){return false;}}
        Assert.Single(await Task.WhenAll(Edit(f.Owner,"修改甲"),Edit(peer,"修改乙")),x=>x);Assert.Equal("2",(await f.Owner.OrderAsync(id)).Revision);Assert.Equal(0,await IntentCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_actual_HTTP_create_patch_prepare_and_original_operation()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var settings=await DocumentHostSettings(new());
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);var root="/api/wms/replenish/v1";var c=Manual(f.Observation);RepOrderV1 draft;
        var missing=JsonSerializer.SerializeToNode(c,RepJson)!.AsObject();missing["input"]!["source"]!.AsObject().Remove("expiryAt");
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/orders",missing)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/orders",c))){Assert.Equal(HttpStatusCode.Created,response.StatusCode);draft=JsonSerializer.Deserialize<RepOrderV1>(await response.Content.ReadAsStringAsync(),RepJson)!;}
        var edit=Manual(f.Observation,amount:"0.00000001") with{Command=CurrentCommand(draft)};
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Patch,root+"/orders/"+draft.Ref.Id,edit))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);draft=JsonSerializer.Deserialize<RepOrderV1>(await response.Content.ReadAsStringAsync(),RepJson)!;}
        var prepare=CurrentCommand(draft);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/orders/"+draft.Ref.Id+"/prepare",prepare))){Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var result=JsonSerializer.Deserialize<RepPrepareV1>(await response.Content.ReadAsStringAsync(),RepJson)!;Assert.False(result.CanDispatch);}
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/operations/"+prepare.CommandKey,null))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("COMMITTED",await response.Content.ReadAsStringAsync());}
        Assert.Equal("0.00000001",(await f.Owner.OrderAsync(Guid.Parse(draft.Ref.Id))).Planned.Value);
    }
}

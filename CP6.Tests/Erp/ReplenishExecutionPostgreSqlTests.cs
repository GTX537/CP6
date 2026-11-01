using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms.Replenish;
using CP6.Core.Services.Wms.StockKernel;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 RepExecutionAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-replenish","Replenishment",[1,2,3,4,5],1);
        catalog.RegisterFields("wms-replenish",[new("OperatorCd","OperatorCd"),new("Remarks","Remarks")]);
        catalog.RegisterScope("wms-transfer-request","调拨请求",[1,2,3,4,5],5);catalog.RegisterFields("wms-transfer-request",[]);catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private sealed record RepExecutionFixture(ReplenishPlanningOwnerV1 Owner,ReplenishPlanningOwnerV1 Reviewer,Harness ReviewerHarness,RepObservationV1 Observation,StockOpeningV1 Opening);
    private async Task<RepExecutionFixture> RepExecutionSetup(Harness h)
    {
        var planning=await RepSetup(h);await DraftActions(h);
        foreach(var action in new[]{"query","approve","prepare","execute","close-stage"})await EngineeringAction(h,"wms-transfer-request",action);
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-transfer-request",ScopeType=5});
        var user=new Sys_User{UserName="rep-reviewer",Password=new BCryptPasswordHasher(4).Hash("Price1!Password"),RoleId=72};h.Db.Sys_Users.Add(user);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var reviewer=h with{User=user.Id,Http=new DefaultHttpContext{User=CurrentPermissionContextTests.Principal(user.Id,h.Tenant)}};
        var owner=RepOwner(h,RepExecutionAuthority(h));var opening=StockOpening(planning.Observation);
        await owner.OpenSimulatedStockAsync(new(planning.Observation.Scope,opening,"SIM-DEVICE"));
        await using var tx=await h.Db.Database.BeginTransactionAsync();var current=await new ReplenishPlanningStoreV1(h.Db,ScopeFence(h).EnvironmentId).Observation(Hash(planning.Observation.Scope),default);await tx.CommitAsync();
        return new(owner,RepOwner(reviewer,RepExecutionAuthority(reviewer)),reviewer,current,opening);
    }
    private static RepExecutionCommandV1 Exec(RepOrderV1 order,bool notOccurred=true)=>new(CurrentCommand(order),"SIM-DEVICE",Guid.NewGuid().ToString("D"),notOccurred);
    private static async Task<RepOrderV1> RegisterExecution(RepExecutionFixture f,string amount="2",string from="0")
    {
        var order=await f.Owner.CreateDraftAsync(Manual(f.Observation,amount:amount,from:from));var id=Guid.Parse(order.Ref.Id);
        await f.Owner.PrepareAsync(id,CurrentCommand(order));order=await f.Owner.OrderAsync(id);return await f.Owner.PrepareTransferAsync(id,CurrentCommand(order));
    }
    private static async Task<RepOrderV1> IssueExecution(RepExecutionFixture f)
    {
        var order=await RegisterExecution(f);var id=Guid.Parse(order.Ref.Id);
        order=await f.Reviewer.ExecutionAsync(id,"approve",Exec(order));return await f.Owner.ExecutionAsync(id,"dispatch",Exec(order));
    }
    [CurrentPermissionPostgresFact]
    public async Task RepDraft_authorized_source_target_pair_does_not_require_unrelated_location()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);
        await h.Db.Locations.Where(x=>x.LocationCd=="SRC-B").ExecuteUpdateAsync(x=>x.SetProperty(l=>l.AreaCd,"UNRELATED"));
        await h.Db.WmsRoleScopes.Where(x=>x.WarehouseCd=="W1").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.AreaCd,"SOURCE"));
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="W1",AreaCd="TARGET"});await h.Db.SaveChangesAsync();
        var created=await f.Owner.CreateDraftAsync(Manual(f.Observation,amount:"2"));Assert.Equal("DRAFT",created.Lifecycle);
        var context=JsonSerializer.SerializeToElement(await f.Owner.ContextAsync(),RepJson);
        var options=context.GetProperty("scopes")[0].GetProperty("draftOptions");Assert.Single(options.EnumerateArray());
        var prepared=await f.Owner.PrepareAsync(Guid.Parse(created.Ref.Id),CurrentCommand(created));Assert.NotNull(prepared);
        var intentId=await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='intent'").SingleAsync();
        Assert.Equal("2.00000000",(await f.Owner.IntentAsync(intentId)).OpenIncoming!.Value);
        await h.Db.WmsRoleScopes.Where(x=>x.AreaCd=="SOURCE").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.OrderAsync(Guid.Parse(created.Ref.Id)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_real_transfer_stock_claim_and_same_cut_arrival_use_simulated_business_inputs()
    {
        await using var h=await Seed();var f=await RepExecutionSetup(h);var order=await RegisterExecution(f);var id=Guid.Parse(order.Ref.Id);
        Assert.NotNull(order.ExecutionLink);Assert.Contains("APPROVE_TRANSFER",order.Capabilities);
        Assert.Equal("TRANSFER_INDEPENDENT_APPROVER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ExecutionAsync(id,"approve",Exec(order)))).Message);
        order=await f.Reviewer.ExecutionAsync(id,"approve",Exec(order));Assert.Equal("PREPARED",order.Lifecycle);
        order=await f.Owner.ExecutionAsync(id,"dispatch",Exec(order));Assert.Equal("TASK_ISSUED",order.Lifecycle);Assert.Equal("0.00000000",order.Moved!.Value);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
        var command=Exec(order);order=await f.Owner.ExecutionAsync(id,"complete",command);Assert.Equal("MOVED",order.Lifecycle);Assert.Equal("2.00000000",order.Moved!.Value);Assert.Single(order.StockReceiptRefs);
        Assert.Equal(Hash(order),Hash(await f.Owner.ExecutionAsync(id,"complete",command)));
        var suggestion=await f.Owner.SuggestAsync(SuggestInput(f.Observation));var row=Assert.Single(suggestion.Rows);
        Assert.Equal("2.00000000",row.OnHand!.Value);Assert.Equal("0.00000000",row.OpenIncoming!.Value);Assert.Equal("2.00000000",row.EffectivePosition!.Value);
        var link=await f.Owner.ExecutionLinkAsync(id);Assert.Equal("POSTED",link!.State);Assert.Single(link.StageRefs);Assert.Single(link.ReceiptRefs);
        var intentId=await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_replenish_v1.documents WHERE tenant_id={h.Tenant} AND kind='intent'").SingleAsync();
        var intent=await f.Owner.IntentAsync(intentId);Assert.Equal("SETTLED",intent.State);Assert.Equal(order.CurrentCutRef,intent.CutRef);Assert.Equal(order.Ref,Assert.Single(intent.Splits).OrderRef);
        var claim=await h.Db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.claim_bindings WHERE tenant_id={h.Tenant}").SingleAsync();Assert.Equal(Hash(f.Opening.Claims[0]),Hash(StockDirectMoveValuesV1.Decode<StockClaimBindingV1>(claim)));
        Assert.False(await h.Db.MobileTasks.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_stop_before_send_seals_original_and_after_move_keeps_physical_facts()
    {
        await using var h=await Seed();var f=await RepExecutionSetup(h);var order=await RegisterExecution(f);var id=Guid.Parse(order.Ref.Id);
        order=await f.Owner.ExecutionAsync(id,"stop",Exec(order));Assert.Equal("CANCELLED_NO_EFFECT",order.Lifecycle);Assert.Empty(order.StockReceiptRefs);
        Assert.Equal("CLOSED_NO_EFFECT",(await f.Owner.ExecutionLinkAsync(id))!.State);
        var row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows);Assert.Equal("0.00000000",row.OpenIncoming!.Value);Assert.Equal("0.00000000",row.OnHand!.Value);
        var next=await IssueExecution(f);var nextId=Guid.Parse(next.Ref.Id);next=await f.Owner.ExecutionAsync(nextId,"complete",Exec(next));
        var stopped=await f.Owner.ExecutionAsync(nextId,"stop",Exec(next));Assert.Equal("MOVED",stopped.Lifecycle);Assert.Equal(next.StockReceiptRefs,stopped.StockReceiptRefs);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_unknown_physical_stop_preserves_source_and_target_protection()
    {
        await using var h=await Seed();var f=await RepExecutionSetup(h);var order=await IssueExecution(f);var id=Guid.Parse(order.Ref.Id);
        order=await f.Owner.ExecutionAsync(id,"stop",Exec(order,notOccurred:false));Assert.Equal("RECONCILIATION",order.Lifecycle);Assert.Equal("UNKNOWN",order.Knowledge);Assert.Null(order.Moved);Assert.Null(order.Remaining);
        var row=Assert.Single((await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows);Assert.Equal("UNKNOWN",row.Coverage);Assert.Null(row.Proposed);
        Assert.Equal("TRANSFER_ACTUAL_DISPOSITION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ExecutionAsync(id,"stop",Exec(order)))).Message);
        Assert.Equal("TRANSFER_EXECUTION_STATE_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ExecutionAsync(id,"complete",Exec(order)))).Message);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_lost_commit_ack_recovers_original_after_execute_revocation()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await RepExecutionSetup(h);var order=await IssueExecution(f);var id=Guid.Parse(order.Ref.Id);var c=Exec(order);fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ExecutionAsync(id,"complete",c))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==423&&x.ActionCode=="execute").ExecuteDeleteAsync();
        var recovered=await f.Owner.ExecutionAsync(id,"complete",c);Assert.Equal("MOVED",recovered.Lifecycle);Assert.Equal("COMMITTED",(await f.Owner.OperationAsync(c.Command.CommandKey)).State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_final_parent_write_failure_rolls_back_stock_and_transfer()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RepExecutionSetup(h);var order=await IssueExecution(f);var id=Guid.Parse(order.Ref.Id);var c=Exec(order);var hit=false;
        hook.Before=x=>{if(x.CommandText.Contains("INSERT INTO cp6_replenish_v1.order_versions",StringComparison.Ordinal)){hit=true;throw new InvalidOperationException("simulated-parent-final-write-failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Owner.ExecutionAsync(id,"complete",c));hook.Before=null;Assert.True(hit);
        Assert.Equal("TASK_ISSUED",(await f.Owner.OrderAsync(id)).Lifecycle);Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("MOVED",(await f.Owner.ExecutionAsync(id,"complete",c)).Lifecycle);
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_HTTP_independent_approval_dispatch_move_and_original_receipt()
    {
        await using var h=await Seed();var f=await RepExecutionSetup(h);var order=await RegisterExecution(f);var id=Guid.Parse(order.Ref.Id);
        var settings=await DocumentHostSettings(new());await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);
        var creatorCookies=await Login(host,h);var approverCookies=await DocumentReviewerLogin(host,h,f.ReviewerHarness.User);var path="/api/wms/replenish/v1/orders/"+id;
        using(var response=await host.Send(OperationsRequest(approverCookies,HttpMethod.Post,path+"/transfer-approve",Exec(order))))
        {var body=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,body);order=JsonSerializer.Deserialize<RepOrderV1>(body,RepJson)!;}
        using(var response=await host.Send(OperationsRequest(creatorCookies,HttpMethod.Post,path+"/dispatch",Exec(order))))
        {var body=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.OK,body);order=JsonSerializer.Deserialize<RepOrderV1>(body,RepJson)!;}
        var command=Exec(order);
        using(var response=await host.Send(OperationsRequest(creatorCookies,HttpMethod.Post,path+"/complete",command)))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);order=JsonSerializer.Deserialize<RepOrderV1>(await response.Content.ReadAsStringAsync(),RepJson)!;Assert.Equal("MOVED",order.Lifecycle);Assert.Single(order.StockReceiptRefs);}
        using(var response=await host.Send(OperationsRequest(creatorCookies,HttpMethod.Get,"/api/wms/replenish/v1/operations/"+command.Command.CommandKey,null)))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal("MOVED",result.RootElement.GetProperty("result").GetProperty("lifecycle").GetString());}
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_stop_registered_not_sent_closes_source_gate_without_stock_or_transfer_adoption()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var draft=await f.Owner.CreateDraftAsync(Manual(f.Observation,amount:"2"));var id=Guid.Parse(draft.Ref.Id);
        await f.Owner.PrepareAsync(id,CurrentCommand(draft));var registered=await f.Owner.OrderAsync(id);
        var closed=await f.Owner.ExecutionAsync(id,"stop",Exec(registered));Assert.Equal("CANCELLED_NO_EFFECT",closed.Lifecycle);Assert.Null(closed.ExecutionLink);
        Assert.Equal("0.00000000",Assert.Single((await f.Owner.SuggestAsync(SuggestInput(f.Observation))).Rows).OpenIncoming!.Value);
        Assert.Equal("REP_REVISION_STALE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareTransferAsync(id,CurrentCommand(registered)))).Message);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_transfer_root_v1.requests WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RepExecution_current_independent_approver_revocation_blocks_final_stock_effect()
    {
        await using var h=await Seed();var f=await RepExecutionSetup(h);var order=await IssueExecution(f);var id=Guid.Parse(order.Ref.Id);
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="wms-transfer-request").Select(x=>x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu&&x.RoleId==72&&x.ActionCode=="approve").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ExecutionAsync(id,"complete",Exec(order)))).Status);
        Assert.Equal("TASK_ISSUED",(await f.Owner.OrderAsync(id)).Lifecycle);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}

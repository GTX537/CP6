using System.Net;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes.Scheduling;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SchedFlow_original_work_lookup_and_reconcile_never_apply_and_keep_first_command()
    {
        await using var h=await Seed();var f=await SchedSetup(h);await EngineeringAction(h,"mes-planning-board","work.read");await EngineeringAction(h,"mes-planning-board","work.reconcile");var plan=await Plan(f);
        var before=await f.Owner.ReconcileWorkAsync(plan.RootApplyGroup.GroupId);Assert.Equal("NOT_OBSERVED",before.State);Assert.False(before.CanInferNoEffect);Assert.Null(before.Result);Assert.Empty(await f.Owner.HistoryAsync(f.Opening.RootId));
        var command=Apply(plan);var applied=await f.Owner.ApplyAsync(command);await f.Owner.ApplyAsync(command with{CommandId=Guid.NewGuid()});
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&(x.ActionCode=="change.apply"||x.ActionCode=="query"||x.ActionCode=="command.read")).ExecuteDeleteAsync();
        var read=await f.Owner.WorkAsync(applied.WorkId);var reconciled=await f.Owner.ReconcileWorkAsync(applied.WorkId);
        Assert.Equal("COMMITTED",read.State);Assert.Equal(command.CommandId,read.OriginalCommandId);Assert.Equal(Digest(command),read.PayloadDigest);Assert.Equal(Digest(applied.Result),Digest(read.Result));Assert.Equal(read.OriginalCommandId,reconciled.OriginalCommandId);
        Assert.Equal("SCHED_NOT_VISIBLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.WorkAsync(Guid.NewGuid()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task SchedFlow_capabilities_exact_links_and_current_work_scope_are_server_owned()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,2,foreignFrom:1);await BoardSources(f);var query=BoardQuery(f);var full=await f.Owner.BoardAsync(query);
        Assert.All(full.Bars,b=>Assert.Contains("plan.prepare",b.Capabilities));var operation=full.Bars[0];Assert.Contains(operation.WoId.ToString(),operation.OwnerLinks["workOrder"]);Assert.EndsWith(operation.OperationId.ToString(),operation.OwnerLinks["operation"]);
        // Use the actual WO menu identity rather than a client capability claim.
        var woMenu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="mes-work-order").Select(x=>x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==woMenu&&x.ActionCode=="change.schedule").ExecuteDeleteAsync();
        var limited=await f.Owner.BoardAsync(query);Assert.All(limited.Bars,b=>Assert.DoesNotContain("change.apply",b.Capabilities));
        var capture=await f.Owner.CaptureAsync(f.Openings[1].RootId);var plan=await f.Owner.PrepareAsync(Change(capture,capture.Root.Operations[0].OperationId,40,42));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="mes-planning-board").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.WorkAsync(plan.RootApplyGroup.GroupId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task SchedFlow_actual_HTTP_work_read_uses_independent_permission_and_reconcile_is_read_only()
    {
        await using var h=await Seed();var f=await SchedSetup(h);await EngineeringAction(h,"mes-planning-board","work.read");await EngineeringAction(h,"mes-planning-board","work.reconcile");var plan=await Plan(f);var reply=await f.Owner.ApplyAsync(Apply(plan));
        var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);var route="/api/mes/scheduling/works/"+reply.WorkId;
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="query").ExecuteDeleteAsync();
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/mes/scheduling/work-context")))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await EngineeringAction(h,"mes-planning-board","preview.read");
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/mes/scheduling/preview-context")))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(reply.CommandId,Decode<SchedWorkV1>(await response.Content.ReadAsByteArrayAsync()).OriginalCommandId);Assert.True(response.Headers.CacheControl?.NoStore);}
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="work.read").ExecuteDeleteAsync();
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route)))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+"/reconcile"))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("COMMITTED",Decode<SchedWorkV1>(await response.Content.ReadAsByteArrayAsync()).State);}
        Assert.Single(await f.Owner.HistoryAsync(f.Opening.RootId));await EngineeringAction(h,"mes-planning-board","query");Assert.Equal("11",(await f.Owner.CaptureAsync(f.Opening.RootId)).Root.Expected.Revision);
    }
}

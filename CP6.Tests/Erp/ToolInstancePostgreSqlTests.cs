using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] ToolFields=["CustomerCd","CustomerName","MadeCost","ProductCd","ProductName","Remarks"];
    private static OperationsOwnerV1 ToolAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();c.RegisterScope("wms-plate-mold","Physical tools",[1,2,3,4,5],1);
        c.RegisterFields("wms-plate-mold",ToolFields.Select(f=>new FieldRegistry.Field(f,f)));c.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static ToolInstanceOwnerV1 ToolInstances(Harness h,CP6Context? db=null)=>new(db??h.Db,ToolAuthority(h,db));
    private static async Task GrantToolInstance(Harness h)
    {
        await GrantOperations(h);var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuId==443);
        if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=443,MenuKey="wms-plate-mold",MenuName="Physical tools"});else menu.MenuKey="wms-plate-mold";
        foreach(var action in new[]{"query","add","edit","use","maintenance","dispose","del"})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==443&&x.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=443,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=443,ActionCode=action});
        }
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-plate-mold",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static async Task<Guid> SeedToolInstance(Harness h,int count=1)
    {
        var rows=Enumerable.Range(0,count).Select(i=>new PlateMoldStock{PlateNo="PLT2-"+i,PlateType="PLATE",CustomerCd="CUSTOMER ",CustomerName="private customer",
            ProductCd="PRODUCT ",ProductName="private product",MadeCost=9999999999999999.99m,UsedShots=500,MaxShots=1000,Status=0,
            Remarks="private note",Creator=i==0?"price-owner":"other",CreateDate=new(2026,1,1)}).ToArray();
        h.Db.PlateMoldStocks.AddRange(rows);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return rows[0].Id;
    }
    private static ToolInstanceCommandV1 ToolEdit(ToolInstanceViewV1 view,params ToolInstancePatchV1[] patches)=>new(Guid.NewGuid(),view.Reference,patches);
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_HTTP_requires_current_independent_query()
    {
        await using var h = await Seed(); await GrantOperations(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/wms/specials/v1/tools/scope"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_native_original_paging_precision_and_unknown_cycle_do_not_invent_qualification()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h,503);var svc=ToolInstances(h);
        var first=await svc.ListAsync(new());var last=await svc.ListAsync(new(){Page=11});Assert.Equal(503,first.Total);Assert.Equal(50,first.Items.Length);Assert.Equal(3,last.Items.Length);
        Assert.Equal(first.Items.Select(x=>x.Reference.Id),(await svc.ListAsync(new())).Items.Select(x=>x.Reference.Id));
        var value=await svc.ReadAsync(id);Assert.Equal("Main.Wms.ToolInstance.v1",value.Reference.Owner);Assert.Equal(500,value.Source.GetProperty("usedShots").GetInt32());
        Assert.Equal("9999999999999999.99",value.Source.GetProperty("madeCost").GetString());Assert.Equal("PRODUCT ",value.Source.GetProperty("productCd").GetString());
        Assert.Null(value.LifetimeShots);Assert.Equal("UNKNOWN",value.LifetimeKnowledge);Assert.Null(value.CycleNo);Assert.Null(value.SiteId);Assert.Equal("UNKNOWN",value.CurrentEligibility);
        Assert.Equal("LEGACY_UNMAPPED",value.EngineeringLinkKnowledge);Assert.Equal(0,await h.Db.Stocks.CountAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(id,"2"))).Status);
        await using var foreign=await Seed();await GrantToolInstance(foreign);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ToolInstances(foreign).ReadAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_profile_has_exact_if_match_original_receipt_and_immutable_history()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);var before=await svc.ReadAsync(id);
        var input=ToolEdit(before,new ToolInstancePatchV1("sizeNote","A"),new ToolInstancePatchV1("madeCost","9999999999999999.98"),new ToolInstancePatchV1("colorCount","12"));
        Assert.Equal(428,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,null,input))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,"*",input))).Status);
        var saved=await svc.SaveAsync(id,before.Etag,input);Assert.Equal("2",saved.Version);Assert.True(saved.Changed);Assert.Equal(saved,await svc.SaveAsync(id,before.Etag,input));
        var after=await svc.ReadAsync(id);Assert.Equal("9999999999999999.98",after.Source.GetProperty("madeCost").GetString());Assert.NotEqual(before.Etag,after.Etag);
        var old=await svc.ReadAsync(id,"1");Assert.False(old.IsCurrent);Assert.Equal(before.Source.GetRawText(),old.Source.GetRawText());
        Assert.Equal(500,after.Source.GetProperty("usedShots").GetInt32());Assert.Equal(0,after.Source.GetProperty("status").GetInt32());Assert.Null(after.CycleNo);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,before.Etag,input with{Patches=[new ToolInstancePatchV1("sizeNote","B")]}))).Message);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,before.Etag,input with{OperationId=Guid.NewGuid()}))).Status);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_instance_v1.versions"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE cp6_tool_instance_v1.heads SET revision=revision+2"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"T_PlateMoldStock\""));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_patch_canonical_decimal_null_and_forbidden_state_are_distinct()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);var view=await svc.ReadAsync(id);
        var input=ToolEdit(view,new ToolInstancePatchV1("madeCost","1.2"),new ToolInstancePatchV1("sizeNote",""));var saved=await svc.SaveAsync(id,view.Etag,input);
        Assert.Equal(saved,await svc.SaveAsync(id,view.Etag,input with{Patches=[new ToolInstancePatchV1("sizeNote",""),new ToolInstancePatchV1("madeCost","1.20")]}));
        var next=await svc.ReadAsync(id);Assert.Equal("",next.Source.GetProperty("sizeNote").GetString());
        await svc.SaveAsync(id,next.Etag,ToolEdit(next,new ToolInstancePatchV1("sizeNote",null)));Assert.Equal(JsonValueKind.Null,(await svc.ReadAsync(id)).Source.GetProperty("sizeNote").ValueKind);
        foreach(var patch in new ToolInstancePatchV1[]{new ToolInstancePatchV1("usedShots","1"),new ToolInstancePatchV1("status","0"),new ToolInstancePatchV1("warehouseCd","W1"),new ToolInstancePatchV1("siteId",Guid.NewGuid().ToString()),new ToolInstancePatchV1("madeCost","10000000000000000"),new ToolInstancePatchV1("madeCost","1.001"),new ToolInstancePatchV1("colorCount","13"),new ToolInstancePatchV1("maxShots","2147483648"),new ToolInstancePatchV1("madeDate","2025-02-29")})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,next.Etag,ToolEdit(next,patch)))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Tool_instance_department_mapping_is_native_physical_identity_and_revocation_applies_to_history(int scope)
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var svc=ToolInstances(h);
        Assert.Empty((await svc.ListAsync(new())).Items);Assert.Equal(id,Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("tool-instance","PLT2-0",_=>Task.FromResult(h.User),default)).Id);
        var binding=await BindStandalone(h,admin,"tool-instance",id);Assert.Equal(id,Assert.Single((await svc.ListAsync(new())).Items).Reference.Id);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"tool-instance",id,binding.EntityId)) with{Enabled=false});
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_current_and_historical_warehouse_area_and_self_scope_fail_closed()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h,2);var svc=ToolInstances(h);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-plate-mold").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Single((await svc.ListAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W1"));Assert.Empty((await svc.ListAsync(new())).Items);
        await h.Db.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W1"));Assert.Single((await svc.ListAsync(new())).Items);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(id,"1"))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A1"));Assert.Empty((await svc.ListAsync(new())).Items);
        h.Db.Warehouses.Add(new(){WarehouseCd="W1",WarehouseName="Recorded"});h.Db.Locations.Add(new(){WarehouseCd="W1",LocationCd="L1",AreaCd="A1"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LocationCd,"L1"));Assert.Single((await svc.ListAsync(new())).Items);
        await h.Db.Locations.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.ListAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_cost_and_private_fields_are_masked_in_list_history_and_filter_with_write_denial()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);
        foreach(var f in ToolFields)h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-plate-mold",FieldName=f,Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var view=await svc.ReadAsync(id);Assert.Empty(view.ReadableFields);Assert.DoesNotContain("private",view.Source.GetRawText());Assert.DoesNotContain("999999",view.Source.GetRawText());
        Assert.Equal(view.Source.GetRawText(),(await svc.ReadAsync(id,"1")).Source.GetRawText());Assert.Single((await svc.ListAsync(new())).Items);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ListAsync(new(){CustomerCd=""}))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,view.Etag,ToolEdit(view,new ToolInstancePatchV1("madeCost","0"))))).Status);
        Assert.Equal(9999999999999999.99m,await h.Db.PlateMoldStocks.Select(x=>x.MadeCost).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_unknown_close_and_lost_ack_recover_original_after_write_revoked()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);var view=await svc.ReadAsync(id);var input=ToolEdit(view,new ToolInstancePatchV1("sizeNote","changed"));
        var result=await svc.SaveAsync(id,view.Etag,input);await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="edit").ExecuteDeleteAsync();
        Assert.Equal(result,(await svc.OperationAsync(input.OperationId,false)).Result);Assert.Equal(result,await svc.SaveAsync(id,view.Etag,input));
        var key=Guid.NewGuid();Assert.Equal("UNKNOWN",(await svc.OperationAsync(key,false)).State);Assert.Equal("CLOSED",(await svc.OperationAsync(key,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,view.Etag,input with{OperationId=key}))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(input.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_concurrent_profiles_accept_one_predecessor_and_final_failure_rolls_back_original_and_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);var view=await svc.ReadAsync(id);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=ToolInstances(h,peer);
        async Task<bool> Save(ToolInstanceOwnerV1 target,string text){try{await target.SaveAsync(id,view.Etag,ToolEdit(view,new ToolInstancePatchV1("sizeNote",text)));return true;}catch(OrderPriceCorrectionException e){Assert.Equal(412,e.Status);return false;}}
        Assert.Single(await Task.WhenAll(Save(svc,"A"),Save(other,"B")),x=>x);var current=await svc.ReadAsync(id);var input=ToolEdit(current,new ToolInstancePatchV1("sizeNote","must roll back"));
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==3)throw new IOException("final authority failure");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SaveAsync(id,current.Etag,input))).Status);hook.Before=null;
        Assert.Equal(current.Reference,(await svc.ReadAsync(id)).Reference);Assert.Equal("UNKNOWN",(await svc.OperationAsync(input.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_HoldExact_protects_current_head_until_commit_and_detects_same_transaction_changes()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var authority=ToolAuthority(h);var current=await ToolInstances(h).ReadAsync(id);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new ToolInstanceStoreV1(h.Db).Fence(default));
        Task<int>? writer=null;var started=new TaskCompletionSource();
        await authority.ExecuteAsync("wms-plate-mold","query",false,async(a,t)=>
        {
            var held=await new ToolInstanceStoreV1(h.Db).HoldExactAsync(a,current.Reference,t);Assert.Equal("UNKNOWN",held.CurrentEligibility);
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});started.SetResult();return await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=501 WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");});
            await started.Task;await Task.Delay(100);Assert.False(writer.IsCompleted);return true;
        });Assert.Equal(1,await writer!);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>authority.ExecuteAsync("wms-plate-mold","query",false,(a,t)=>new ToolInstanceStoreV1(h.Db).HoldExactAsync(a,current.Reference,t)))).Status);
        var next=await ToolInstances(h).ReadAsync(id);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>authority.ExecuteAsync("wms-plate-mold","edit",true,async(a,t)=>
        {
            var query=await a.SourceAsync("wms-plate-mold",t);await new ToolInstanceStoreV1(h.Db).HoldExactAsync(query,next.Reference,t);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=502 WHERE \"Id\"={id}",t);return true;
        }))).Status);Assert.Equal(next.Reference,(await ToolInstances(h).ReadAsync(id)).Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_legacy_writers_fail_closed_without_side_effects_and_missing_capture_is_unavailable()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var svc=ToolInstances(h);var legacy=new CP6.Core.Services.Wms.PlateMoldService(h.Db,new WmsSequenceService(h.Db),svc);
        foreach(var call in new Func<Task>[]{()=>legacy.CreateAsync(new(),"forged"),()=>legacy.UpdateAsync("PLT2-0",new(),"forged"),()=>legacy.RecordUsageAsync("PLT2-0",1,"forged"),()=>legacy.StartMaintenanceAsync("PLT2-0",null,null),()=>legacy.CompleteMaintenanceAsync("PLT2-0",null),()=>legacy.DiscardAsync("PLT2-0",null),()=>legacy.DeleteAsync("PLT2-0",null)})await Assert.ThrowsAsync<OrderPriceCorrectionException>(call);
        Assert.Equal(500,await h.Db.PlateMoldStocks.Select(x=>x.UsedShots).SingleAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new CP6.Core.Services.Wms.PlateMoldService(h.Db,new WmsSequenceService(h.Db)).DeleteAsync("PLT2-0",null));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_PlateMoldStock\" DISABLE TRIGGER tool_instance_source_capture");
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(id))).Status);
        await h.Db.PlateMoldStocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UsedShots,700));await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_PlateMoldStock\" ENABLE TRIGGER tool_instance_source_capture");
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_instance_actual_HTTP_profile_requires_csrf_if_match_and_rejects_unknown_fields()
    {
        await using var h=await Seed();await GrantToolInstance(h);var id=await SeedToolInstance(h);var view=await ToolInstances(h).ReadAsync(id);var command=ToolEdit(view,new ToolInstancePatchV1("sizeNote","HTTP"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        async Task<HttpResponseMessage> Save(object body,string? etag,bool csrf=true)
        {var request=OperationsRequest(cookies,HttpMethod.Put,$"/api/wms/specials/v1/tools/{id}/profile");request.Content=JsonContent.Create(body);if(etag!=null)request.Headers.TryAddWithoutValidation("If-Match",etag);if(!csrf)request.Headers.Remove("X-CSRF-TOKEN");return await host.Send(request);}
        using var csrf=await Save(command,view.Etag,false);Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using var missing=await Save(command,null);Assert.Equal((HttpStatusCode)428,missing.StatusCode);
        using var extra=await Save(new{command.OperationId,command.Expected,command.Patches,status=0},view.Etag);Assert.Equal(HttpStatusCode.BadRequest,extra.StatusCode);
        using var saved=await Save(command,view.Etag);Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.True(saved.Headers.CacheControl?.NoStore);
        using var history=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{id}?version=1"));Assert.Equal(HttpStatusCode.OK,history.StatusCode);
        using var legacy=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"/api/wms/plate-mold/PLT2-0/use",new{shots=1}));Assert.Equal(HttpStatusCode.ServiceUnavailable,legacy.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/plate-mold"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}

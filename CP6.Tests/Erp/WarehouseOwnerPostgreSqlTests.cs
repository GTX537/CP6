using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.WebApi.Controllers.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using System.Text.Json;
using System.Net;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_missing_authority_rejects_even_empty_search()
    {
        await using var h=await Seed();var controller=new WarehouseController(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>controller.Search(null,null,null));
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_missing_authority_cannot_read_location_tree()
    {
        await using var h=await Seed();h.Db.Locations.Add(new(){WarehouseCd="W",LocationCd="PRIVATE",Barcode="hidden"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var controller=new WarehouseController(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>controller.GetTree("W"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_missing_authority_cannot_create_unisolated_location()
    {
        await using var h=await Seed();var controller=new WarehouseController(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>controller.CreateLocation(new Location{WarehouseCd="W",LocationCd="NEW"}));
    }
    private static AuthorizationCatalogStoreV1 MasterCatalog()
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-warehouse",new[]{"AddressText","ManagerCd","Remarks","WarehouseName"}),
            ("wms-location",new[]{"Barcode","LocationName"})})
        {catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}
        catalog.Seal();return catalog;
    }
    private static async Task<OperationsOwnerV1> GrantMaster(Harness h)
    {
        foreach(var(key,id) in new[]{("wms-warehouse",401),("wms-location",402)})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in new[]{"query","edit","add","del","abandon"})
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id&&a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture);
    }
    private static async Task<(Guid warehouse,Guid location,string code)> SeedMaster(Harness h,int locations=2)
    {
        var code="M"+h.Tenant.ToString("N")[..8];var w=new Warehouse{WarehouseCd=code,WarehouseName="Original",Creator="price-owner",ManagerCd="private-manager",AddressText="private-address",Remarks="private-note",AllowNegative=true};
        h.Db.Warehouses.Add(w);var rows=new List<Location>();
        for(var i=0;i<locations;i++)rows.Add(new(){WarehouseCd=code,LocationCd="L"+i.ToString("D4"),LocationName="Original",Creator=i==0?"price-owner":"other",AreaCd=i==0?"A":"B",ParentLocationCd=i==0?"L0001":null,
            Barcode="private-barcode",CapacityQty=i==0?0:9999999999999.99999999m,ReservedCapacityQty=9999999999999.99999999m,AllowedProductType="legacy,unknown"});
        h.Db.Locations.AddRange(rows);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return(w.Id,rows[0].Id,code);
    }
    private static JsonElement MasterPatch(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    private static JsonElement SetMasterName(string value)=>JsonSerializer.SerializeToElement(new{name=new{op="SET",value}});
    private static Task<int> MasterOperations(Harness h)=>h.Db.Database.SqlQuery<int>($"""
        SELECT COUNT(*)::integer AS "Value" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND owner LIKE 'Main.Wms.MasterName.%'
        """).SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_scoped_pages_keep_original_precision_unknown_and_real_parent_disclosure()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h,203);var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        var wh=Assert.Single((await svc.WarehousesAsync(new())).Items);Assert.Null(wh.SiteId);Assert.Equal("NOT_RECORDED",wh.SiteState);
        var first=await svc.LocationsAsync(ids.warehouse,new());Assert.Equal(203,first.Total);Assert.Equal(50,first.Items.Count);
        var row=first.Items[0];Assert.Equal("0",row.RawCapacityQty);Assert.Equal("9999999999999.99999999",row.RawReservedCapacityQty);
        Assert.Equal("UNKNOWN",row.CapacityState);Assert.Null(row.TypedCapacity);Assert.Null(row.ConfirmedReservedQty);Assert.Equal("AUTHORIZED_ORIGINAL_REFERENCE",row.ParentState);
        Assert.Equal(3,(await svc.LocationsAsync(ids.warehouse,new(){Page=5})).Items.Count);
        await using var foreign=await Seed();var other=await SeedMaster(foreign);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.WarehouseAsync(other.warehouse))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));
        var filtered=await svc.LocationsAsync(ids.warehouse,new());row=Assert.Single(filtered.Items);Assert.Null(row.ParentId);Assert.Null(row.ParentLocationCd);Assert.Equal("SOURCE_DISCLOSURE_UNAVAILABLE",row.ParentState);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-location").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Single((await svc.LocationsAsync(ids.warehouse,new())).Items);
        await h.Db.Warehouses.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-warehouse").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Empty((await svc.WarehousesAsync(new())).Items);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LocationsAsync(ids.warehouse,new()))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Warehouse_owner_real_department_bindings_require_both_parent_and_location(int scope)
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        Assert.Empty((await svc.WarehousesAsync(new())).Items);
        Assert.Equal(ids.warehouse,Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("warehouse",ids.code,_=>Task.FromResult(h.User),default)).Id);
        await BindStandalone(h,admin,"location",ids.location);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LocationAsync(ids.location))).Status);
        var binding=await BindStandalone(h,admin,"warehouse",ids.warehouse);Assert.Single((await svc.LocationsAsync(ids.warehouse,new())).Items);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"warehouse",ids.warehouse,binding.EntityId)) with{Enabled=false});
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LocationAsync(ids.location))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_fields_are_current_and_name_write_cannot_change_inventory_controls()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        h.Db.Sys_RoleFieldPerms.AddRange(new(){RoleId=72,ResourceKey="wms-warehouse",FieldName="AddressText",Access=3},new(){RoleId=72,ResourceKey="wms-location",FieldName="Barcode",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();Assert.Null((await svc.WarehouseAsync(ids.warehouse)).AddressText);Assert.Null((await svc.LocationAsync(ids.location)).Barcode);
        var before=await svc.LocationAsync(ids.location);var op=Guid.NewGuid();
        var changed=await svc.RenameAsync("locations",ids.location,op,before.Etag,SetMasterName("New"));Assert.Equal("CHANGED",changed.Effect);Assert.NotEqual(before.Etag,changed.Etag);
        var after=await svc.LocationAsync(ids.location);Assert.Equal("New",after.LocationName);Assert.Equal(before.RawCapacityQty,after.RawCapacityQty);Assert.Equal(before.RawReservedCapacityQty,after.RawReservedCapacityQty);
        Assert.Equal(before.LegacyIsBlocked,after.LegacyIsBlocked);Assert.Equal(before.LegacyIsPickable,after.LegacyIsPickable);Assert.Equal(before.ParentId,after.ParentId);
        var noChange=await svc.RenameAsync("locations",ids.location,Guid.NewGuid(),after.Etag,SetMasterName("New"));Assert.Equal("NO_CHANGE",noChange.Effect);Assert.Equal(after.Etag,noChange.Etag);
        var keep=await svc.RenameAsync("locations",ids.location,Guid.NewGuid(),after.Etag,MasterPatch("{}"));Assert.Equal("NO_CHANGE",keep.Effect);Assert.Equal(after.Etag,keep.Etag);
        var cleared=await svc.RenameAsync("locations",ids.location,Guid.NewGuid(),after.Etag,MasterPatch("{\"name\":{\"op\":\"CLEAR\"}}"));Assert.Equal("CHANGED",cleared.Effect);Assert.Null((await svc.LocationAsync(ids.location)).LocationName);
        Assert.Equal(4,await MasterOperations(h));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location",FieldName="LocationName",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("locations",ids.location,Guid.NewGuid(),cleared.Etag,SetMasterName("Denied")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_original_replay_reads_current_scope_without_repeating_write_or_private_names()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);var before=await svc.WarehouseAsync(ids.warehouse);var op=Guid.NewGuid();
        var result=await svc.RenameAsync("warehouses",ids.warehouse,op,before.Etag,SetMasterName("First"));
        await svc.RenameAsync("warehouses",ids.warehouse,Guid.NewGuid(),result.Etag,SetMasterName("Later"));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==401&&x.ActionCode=="edit").ExecuteDeleteAsync();
        Assert.Equal(result,await svc.RenameAsync("warehouses",ids.warehouse,op,before.Etag,SetMasterName("First")));
        Assert.Equal("Later",(await svc.WarehouseAsync(ids.warehouse)).WarehouseName);Assert.Equal(2,await MasterOperations(h));
        var original=await svc.OperationAsync("warehouses",ids.warehouse,op);Assert.Equal(result,original.Result);Assert.DoesNotContain("First",JsonSerializer.Serialize(original));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("warehouses",ids.warehouse,op,before.Etag,SetMasterName("Different")))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==401&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync("warehouses",ids.warehouse,op))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_unknown_query_is_not_no_effect_and_independent_close_blocks_late_retry()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);var row=await svc.WarehouseAsync(ids.warehouse);var op=Guid.NewGuid();
        Assert.Equal("UNKNOWN",(await svc.OperationAsync("warehouses",ids.warehouse,op)).State);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==401&&x.ActionCode=="abandon").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync("warehouses",ids.warehouse,op,true))).Status);
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=401,ActionCode="abandon"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("CLOSED",(await svc.OperationAsync("warehouses",ids.warehouse,op,true)).State);
        Assert.Equal("CLOSED",(await svc.OperationAsync("warehouses",ids.warehouse,op,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("warehouses",ids.warehouse,op,row.Etag,SetMasterName("Late")))).Message);
        Assert.Equal(row.Etag,(await svc.WarehouseAsync(ids.warehouse)).Etag);Assert.Equal(1,await MasterOperations(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_stale_no_change_invalid_patch_and_legacy_control_writes_have_zero_effects()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);var row=await svc.WarehouseAsync(ids.warehouse);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("warehouses",ids.warehouse,Guid.NewGuid(),"\"old\"",SetMasterName("Original")));
        Assert.Equal(428,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("warehouses",ids.warehouse,Guid.NewGuid(),null,MasterPatch("{}")))).Status);
        foreach(var patch in new[]{"{\"name\":null}","{\"name\":{\"op\":\"CLEAR\"}}","{\"name\":{\"op\":\"SET\",\"value\":null}}","{\"tenantId\":\"other\"}","{\"name\":{\"op\":\"SET\",\"value\":\"N\"},\"allowNegative\":false}","{\"name\":{},\"name\":{}}"})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("warehouses",ids.warehouse,Guid.NewGuid(),row.Etag,MasterPatch(patch)))).Status);
        var controller=new WarehouseController(h.Db,svc);
        foreach(var call in new Func<Task>[] {()=>controller.Create(new(){WarehouseCd="NEW",WarehouseName="N"}),()=>controller.Update(ids.code,new(){AllowNegative=false}),()=>controller.Delete(ids.code),
            ()=>controller.CreateLocation(new(){WarehouseCd=ids.code,LocationCd="NEW"}),()=>controller.UpdateLocation("L0000",new(){CapacityQty=1,IsBlocked=true}),()=>controller.DeleteLocation("L0000")})
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(call);
        Assert.Equal(0,await MasterOperations(h));Assert.Equal(row.Etag,(await svc.WarehouseAsync(ids.warehouse)).Etag);Assert.Equal(2,await h.Db.Locations.CountAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Warehouse_owner_final_failure_rolls_back_name_and_journal_or_recovers_lost_commit(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        var row=await svc.LocationAsync(ids.location);var reads=0;var op=Guid.NewGuid();
        if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==4)throw new IOException("final source authority failed");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RenameAsync("locations",ids.location,op,row.Etag,SetMasterName("Committed")));hook.Before=null;
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?"Committed":"Original",(await svc.LocationAsync(ids.location)).LocationName);Assert.Equal(lostAck?1:0,await MasterOperations(h));
        var recovered=await svc.OperationAsync("locations",ids.location,op);Assert.Equal(lostAck?"COMMITTED":"UNKNOWN",recovered.State);
        if(lostAck)Assert.Equal(recovered.Result,await svc.RenameAsync("locations",ids.location,op,row.Etag,SetMasterName("Committed")));
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_native_concurrent_same_key_applies_once_and_stale_other_intent_refuses()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);var row=await svc.WarehouseAsync(ids.warehouse);var op=Guid.NewGuid();
        async Task<MasterNameResultV1> Write(Guid operation,string name)
        {
            await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var current=new OperationsOwnerV1(db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture);
            return await new WarehouseOwnerServiceV1(db,current).RenameAsync("warehouses",ids.warehouse,operation,row.Etag,SetMasterName(name));
        }
        var results=await Task.WhenAll(Task.Run(()=>Write(op,"One")),Task.Run(()=>Write(op,"One")));Assert.Equal(results[0],results[1]);Assert.Equal(1,await MasterOperations(h));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Write(Guid.NewGuid(),"Two"))).Status);
        Assert.Equal("One",(await svc.WarehouseAsync(ids.warehouse)).WarehouseName);
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_close_and_late_write_serialize_to_one_terminal_original()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        var row=await svc.WarehouseAsync(ids.warehouse);var op=Guid.NewGuid();
        async Task<(MasterNameResultV1? result,OrderPriceCorrectionException? error)> Write()
        {
            await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            var service=new WarehouseOwnerServiceV1(db,new OperationsOwnerV1(db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture));
            try{return(await service.RenameAsync("warehouses",ids.warehouse,op,row.Etag,SetMasterName("Race")),null);}
            catch(OrderPriceCorrectionException error){return(null,error);}
        }
        async Task<MasterOperationV1> Close()
        {
            await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            var service=new WarehouseOwnerServiceV1(db,new OperationsOwnerV1(db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture));
            return await service.OperationAsync("warehouses",ids.warehouse,op,true);
        }
        var write=Task.Run(Write);var close=Task.Run(Close);await Task.WhenAll(write,close);
        var original=await svc.OperationAsync("warehouses",ids.warehouse,op);Assert.Equal(await close,original);Assert.Equal(1,await MasterOperations(h));
        if(original.State=="CLOSED")
        {Assert.Null((await write).result);Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await write).error!.Message);Assert.Equal(row.Etag,(await svc.WarehouseAsync(ids.warehouse)).Etag);}
        else
        {Assert.Equal("COMMITTED",original.State);Assert.Null((await write).error);Assert.Equal(original.Result,(await write).result);Assert.Equal("Race",(await svc.WarehouseAsync(ids.warehouse)).WarehouseName);}
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_native_raw_writer_waits_while_current_read_is_held()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantMaster(h);var ids=await SeedMaster(h);Task<int>? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await db.Warehouses.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseName,"Raw changed"));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_Warehouse%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        WarehouseObservationV1? result=null;try{result=await new WarehouseOwnerServiceV1(h.Db,owner).WarehouseAsync(ids.warehouse);}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal("Original",result!.WarehouseName);Assert.Equal("Raw changed",await h.Db.Warehouses.Select(x=>x.WarehouseName).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_actual_http_reads_etag_names_recovery_and_legacy_controls()
    {
        await using var h=await Seed();await GrantMaster(h);var ids=await SeedMaster(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/wms/warehouse/owner-v1/warehouses/{ids.warehouse}"));Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);var etag=read.Headers.ETag!.ToString();
        var op=Guid.NewGuid();var path=$"api/wms/warehouse/owner-v1/warehouses/{ids.warehouse}";
        using var request=OperationsRequest(cookies,HttpMethod.Patch,path+"/name",new{name=new{op="SET",value="Http name"}});request.Headers.Add("Idempotency-Key",op.ToString());request.Headers.Add("If-Match",etag);
        using var response=await host.Send(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.NotEqual(etag,response.Headers.ETag!.ToString());
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path+"/operations/"+op));Assert.Contains("COMMITTED",await original.Content.ReadAsStringAsync());
        using var scoped=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/warehouse/owner-v1/warehouses/operation-scope"));Assert.Equal(HttpStatusCode.OK,scoped.StatusCode);
        using var scopedJson=JsonDocument.Parse(await scoped.Content.ReadAsStringAsync());Assert.Equal(h.Tenant,scopedJson.RootElement.GetProperty("data").GetProperty("tenantId").GetGuid());Assert.Equal(h.User,scopedJson.RootElement.GetProperty("data").GetProperty("actorId").GetGuid());
        var closedOp=Guid.NewGuid();using var closed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,path+"/operations/"+closedOp+"/close"));Assert.Equal(HttpStatusCode.OK,closed.StatusCode);
        using var closedJson=JsonDocument.Parse(await closed.Content.ReadAsStringAsync());Assert.Equal("CLOSED",closedJson.RootElement.GetProperty("data").GetProperty("state").GetString());Assert.Equal(JsonValueKind.Null,closedJson.RootElement.GetProperty("data").GetProperty("result").ValueKind);
        using var oldRead=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/warehouse"));Assert.Equal(HttpStatusCode.Conflict,oldRead.StatusCode);
        using var oldCreate=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/warehouse/location",new{warehouseCd=ids.code,locationCd="UNSAFE"}));Assert.Equal(HttpStatusCode.UnprocessableEntity,oldCreate.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==401&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path+"/operations/"+op));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Warehouse_owner_native_mapping_integrity_catalog_and_unsafe_context_refuse()
    {
        await using var h=await Seed();var owner=await GrantMaster(h);var ids=await SeedMaster(h);var admin=await MappingAdmin(h);await using var foreign=await Seed();var other=await SeedMaster(foreign);
        foreach(var(kind,id) in new[]{("warehouse",other.warehouse),("location",other.location)})
        {
            var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({Guid.NewGuid()},{h.Tenant},{kind},{id},{admin.Child},true)
                """));Assert.Equal("23514",error.SqlState);
        }
        var svc=new WarehouseOwnerServiceV1(h.Db,owner);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.WarehousesAsync(new()));
        await h.Db.Locations.FirstAsync();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.WarehousesAsync(new()));h.Db.ChangeTracker.Clear();
        foreach(var q in new[]{new WarehouseQueryV1{Page=0},new(){PageSize=201},new(){Page=int.MaxValue},new(){WarehouseCd=new string('x',11)}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.WarehousesAsync(q))).Status);
        await h.Db.Sys_RoleActions.Where(x=>(x.MenuId==401||x.MenuId==402)&&(x.ActionCode=="query"||x.ActionCode=="abandon")).ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(x=>(x.MenuId==401||x.MenuId==402)&&(x.ActionCode=="query"||x.ActionCode=="abandon")).ExecuteDeleteAsync();
        foreach(var operation in new CP6.Persistence.PostgreSql.Migrations.Core.WarehouseDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);}
        Assert.Equal(4,await h.Db.Sys_MenuActions.CountAsync(x=>(x.MenuId==401||x.MenuId==402)&&(x.ActionCode=="query"||x.ActionCode=="abandon")));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>(x.MenuId==401||x.MenuId==402)&&(x.ActionCode=="query"||x.ActionCode=="abandon")));
    }
}

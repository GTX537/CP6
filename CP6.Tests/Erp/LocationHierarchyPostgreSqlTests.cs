using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static LocationHierarchyOwnerV1 Hierarchy(Harness h)=>new(h.Db,new OperationsOwnerV1(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture));
    private static async Task<(Guid Warehouse,Guid Parent)> HierarchySetup(Harness h,bool grant=true)
    {
        await GrantMaster(h);if(grant)await EngineeringAction(h,"wms-warehouse","hierarchy-policy");
        var w=new Warehouse{WarehouseCd="TREE",WarehouseName="Hierarchy",Creator="price-owner"};
        var p=new Location{WarehouseCd="TREE",LocationCd="ZONE",LocationLevel=1,Creator="price-owner",AreaCd="A"};
        h.Db.Warehouses.Add(w);h.Db.Locations.Add(p);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return(w.Id,p.Id);
    }
    private static LocationHierarchyCommandV1 Policy(string version="absent",string hierarchy="0")=>new(Guid.NewGuid(),version,hierarchy,true,[1],[new(1,2),new(2,3),new(1,4)],"Explicit approved hierarchy, no inferred cross-level edges");
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_policy_CAS_noop_history_original_recovery_and_disabled_precheck()
    {
        await using var h=await Seed();var f=await HierarchySetup(h);var o=Hierarchy(h);var c=Policy();
        Assert.Equal("POLICY_UNAVAILABLE",(await o.ContextAsync(f.Warehouse)).Compatibility);
        var r=await o.SaveAsync(f.Warehouse,c);Assert.True(r.Changed);Assert.Equal(r,await o.SaveAsync(f.Warehouse,c));
        var original=await o.HistoryAsync(f.Warehouse,"1");Assert.Equal(h.User,original.Original.ActorId);
        var noop=await o.SaveAsync(f.Warehouse,Policy("1","1") with{RootLevels=[1],Edges=[new(1,4),new(2,3),new(1,2)]});Assert.False(noop.Changed);Assert.Equal(r.Reference,noop.Reference);
        var cross=await o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,4));Assert.True(cross.StructureValid);Assert.False(cross.CreationGrant);Assert.Equal("UNAVAILABLE",cross.InitializationFence);
        Assert.Equal("HIERARCHY_EDGE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,3)))).Message);
        Assert.Equal("HIERARCHY_ROOT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",null,2)))).Message);
        var disabled=await o.SaveAsync(f.Warehouse,Policy("1","1") with{Enabled=false});Assert.Equal("2",disabled.Reference.Version);
        Assert.Equal("HIERARCHY_POLICY_DISABLED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("2","1",null,1)))).Message);
        Assert.Equal(original.OriginalJson,(await o.HistoryAsync(f.Warehouse,"1")).OriginalJson);
        Assert.Equal(r,(await o.OperationAsync(f.Warehouse,c.OperationId)).Result);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy("1","1")))).Status);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,c with{Reason="Different intent"}))).Message);
        await RevokeTransferAction(h,"wms-warehouse","hierarchy-policy");Assert.Equal(r,await o.SaveAsync(f.Warehouse,c));
        var id=Guid.NewGuid();Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Warehouse,id)).State);Assert.Equal("CLOSED",(await o.OperationAsync(f.Warehouse,id,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy("2","1") with{OperationId=id}))).Message);
        Assert.Single(await h.Db.Locations.ToArrayAsync());Assert.False(await h.Db.Stocks.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_native_structure_revision_ignores_name_but_rejects_stale_and_invalid_tree()
    {
        await using var h=await Seed();var f=await HierarchySetup(h);var o=Hierarchy(h);
        await h.Db.Locations.Where(x=>x.Id==f.Parent).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"tree"));
        await o.SaveAsync(f.Warehouse,Policy());
        Assert.True((await o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2))).StructureValid);
        await h.Db.Locations.Where(x=>x.Id==f.Parent).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LocationName,"Renamed"));Assert.Equal("1",(await o.ContextAsync(f.Warehouse)).HierarchyRevision);
        var child=new Location{WarehouseCd="TREE",LocationCd="AISLE",ParentLocationCd="zone",LocationLevel=2};h.Db.Locations.Add(child);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var context=await o.ContextAsync(f.Warehouse);Assert.Equal("2",context.HierarchyRevision);Assert.Equal("COMPATIBLE",context.Compatibility);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2)))).Status);
        Assert.Equal("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy("1","2") with{Edges=[new(1,4)]}))).Message);
        await h.Db.Locations.Where(x=>x.Id==f.Parent).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ParentLocationCd,"AISLE"));
        Assert.Equal("REQUIRES_REPAIR",(await o.ContextAsync(f.Warehouse)).Compatibility);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","3",child.Id,3)));
        await h.Db.Locations.Where(x=>x.Id==f.Parent).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ParentLocationCd,(string?)null));
        await h.Db.Locations.Where(x=>x.Id==child.Id).ExecuteDeleteAsync();Assert.Equal("5",(await o.ContextAsync(f.Warehouse)).HierarchyRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_no_default_or_implicit_grant_and_partial_area_cannot_control_whole_tree()
    {
        await using var h=await Seed();var f=await HierarchySetup(h,false);var o=Hierarchy(h);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy()))).Status);
        await EngineeringAction(h,"wms-warehouse","hierarchy-policy");
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));
        Assert.Equal("HIERARCHY_WAREHOUSE_SCOPE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy()))).Message);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"*"));await o.SaveAsync(f.Warehouse,Policy());
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"B"));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2)))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"*"));
        await RevokeTransferAction(h,"wms-warehouse","query");await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(f.Warehouse));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,Policy("1","1")));
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task LocationHierarchy_current_warehouse_and_parent_department_scopes_are_independent(int type)
    {
        await using var h=await Seed();var f=await HierarchySetup(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,type);var o=Hierarchy(h);
        if(type is 2 or 3 or 4)
        {
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(f.Warehouse));
            await BindStandalone(h,admin,"warehouse",f.Warehouse);
        }
        await o.SaveAsync(f.Warehouse,Policy());
        if(type is 2 or 3 or 4)
        {
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2)));
            await BindStandalone(h,admin,"location",f.Parent);
        }
        Assert.True((await o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2))).StructureValid);
        await using var foreign=await Seed();var other=await HierarchySetup(foreign);await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(other.Warehouse));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",other.Parent,2)));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_invalid_rules_and_database_tamper_cannot_create_trusted_originals()
    {
        await using var h=await Seed();var f=await HierarchySetup(h);var o=Hierarchy(h);
        foreach(var c in new[]{Policy() with{RootLevels=[]},Policy() with{RootLevels=[1,1]},Policy() with{Edges=[new(2,3)]},Policy() with{Edges=[new(2,1)]},Policy() with{Edges=[new(1,2),new(1,2)]}})
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,c));
        await o.SaveAsync(f.Warehouse,Policy());
        foreach(var sql in new[]{"UPDATE cp6_location_hierarchy_v1.versions SET body=body WHERE tenant_id={0}","DELETE FROM cp6_location_hierarchy_v1.heads WHERE tenant_id={0}","UPDATE cp6_location_hierarchy_v1.heads SET hierarchy_revision=hierarchy_revision+1 WHERE tenant_id={0}","UPDATE cp6_location_hierarchy_v1.heads SET revision=revision+1 WHERE tenant_id={0}"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql,h.Tenant));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Warehouses.Where(x=>x.Id==f.Warehouse).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"OTHER")));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Location\" DISABLE TRIGGER hierarchy_native_change");
        try{await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PrecheckAsync(f.Warehouse,new("1","1",f.Parent,2)));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Location\" ENABLE TRIGGER hierarchy_native_change");}
        Assert.Equal("1",(await o.ContextAsync(f.Warehouse)).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_final_permission_and_native_structure_changes_rollback_policy_and_journal()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await HierarchySetup(h);var o=Hierarchy(h);var injected=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;injected=true;
            await using var sql=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"MenuId\"=401 AND \"RoleId\"=72 AND \"ActionCode\"='hierarchy-policy'",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);
            sql.Parameters.AddWithValue("tenant",h.Tenant);await sql.ExecuteNonQueryAsync();
        };
        var c=Policy();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,c));Assert.True(injected);Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Warehouse,c.OperationId)).State);
        Assert.Equal("absent",(await o.ContextAsync(f.Warehouse)).Version);injected=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;injected=true;
            await using var sql=new NpgsqlCommand("UPDATE public.\"T_Location\" SET \"LocationLevel\"=5 WHERE \"Id\"=@id",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);sql.Parameters.AddWithValue("id",f.Parent);await sql.ExecuteNonQueryAsync();
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.SaveAsync(f.Warehouse,c));Assert.True(injected);Assert.Equal("absent",(await o.ContextAsync(f.Warehouse)).Version);
        Assert.Equal(1,await h.Db.Locations.Where(x=>x.Id==f.Parent).Select(x=>x.LocationLevel).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_concurrent_CAS_and_close_serialize_to_one_result()
    {
        await using var h=await Seed();var f=await HierarchySetup(h);var o=Hierarchy(h);await o.SaveAsync(f.Warehouse,Policy());
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Hierarchy(h with{Db=db});
        async Task<bool> Save(LocationHierarchyOwnerV1 service,LocationHierarchyCommandV1 c){try{await service.SaveAsync(f.Warehouse,c);return true;}catch(OrderPriceCorrectionException e){Assert.True(e.Status is 409 or 412);return false;}}
        Assert.Single((await Task.WhenAll(Save(o,Policy("1","1") with{Enabled=false}),Save(peer,Policy("1","1") with{Enabled=false}))).Where(x=>x));
        var c=Policy("2","1");await Task.WhenAll(Save(o,c),peer.OperationAsync(f.Warehouse,c.OperationId,true));
        var result=await o.OperationAsync(f.Warehouse,c.OperationId);Assert.True(result.State is "COMMITTED" or "CLOSED");Assert.Equal(result.State=="COMMITTED"?"3":"2",(await o.ContextAsync(f.Warehouse)).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationHierarchy_actual_HTTP_auth_CSRF_closed_fields_and_explicit_null_parent()
    {
        await using var h=await Seed();var f=await HierarchySetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        var root=$"/api/wms/warehouses/{f.Warehouse}/hierarchy-v1";
        using(var r=await host.Send(new HttpRequestMessage(HttpMethod.Get,root)))Assert.Equal(HttpStatusCode.Unauthorized,r.StatusCode);
        using(var req=OperationsRequest(cookies,HttpMethod.Post,root+"/commands",Policy())){req.Headers.Remove("X-CSRF-Token");using var r=await host.Send(req);Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);}
        var c=Policy();using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",c))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(r.Headers.CacheControl?.NoStore);}
        var body=JsonSerializer.SerializeToNode(new LocationHierarchyPrecheckV1("1","1",null,1),new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/precheck",body))){var checkedNode=await DocumentHttpData<LocationHierarchyCheckV1>(r);Assert.False(checkedNode.CreationGrant);}
        body.Remove("parentLocationId");using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/precheck",body)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        body["parentLocationId"]=null;body["tenantId"]=h.Tenant;using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/precheck",body)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        await RevokeTransferAction(h,"wms-warehouse","hierarchy-policy");using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/operations/"+c.OperationId)))Assert.Equal(HttpStatusCode.OK,r.StatusCode);
        Assert.Single(await h.Db.Locations.ToArrayAsync());
    }
}

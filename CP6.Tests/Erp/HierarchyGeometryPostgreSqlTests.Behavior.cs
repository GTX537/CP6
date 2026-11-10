using System.Net;
using System.Text.Json;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("zone")][InlineData("aisle")][InlineData("rack")]
    public async Task Hierarchy_creates_require_actual_current_parent(string child)
    {
        await using var h=await HierarchySeed();await using var other=await HierarchySeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        var foreignFloor=await other.Db.Space_Floors.Where(x=>x.FloorCode=="F1").Select(x=>x.Id).SingleAsync();var foreignZone=await other.Db.Space_Zones.Where(x=>x.ZoneCode=="A").Select(x=>x.Id).SingleAsync();
        object input=child switch{"zone"=>HierarchyZone(foreignFloor,"DENIED"),"aisle"=>HierarchyAisle(foreignZone,"DENIED"),_=>HierarchyNewRack(Guid.NewGuid(),"DENIED")};
        var before=await HierarchyRows(h);var otherBefore=await HierarchyRows(other);var stock=await PalletStockRows(h);
        using var response=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/"+child,input));
        var after=await HierarchyRows(h);var otherAfter=await HierarchyRows(other);var stockAfter=await PalletStockRows(h);
        var emptyRackFloor=await h.Db.Space_Racks.AnyAsync(x=>x.RackCode=="DENIED"&&x.FloorId==Guid.Empty);
        await DeviceEvidence("hierarchy-parent-"+child+".json",new{Status=(int)response.StatusCode,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter,EmptyRackFloor=emptyRackFloor});
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Equal(before,after);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
    }

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("edit")]
    public async Task Hierarchy_rack_cannot_reference_an_aisle_in_another_zone(string operation)
    {
        await using var h=await HierarchySeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");var aisle=await h.Db.Space_Aisles.Where(x=>x.AisleCode=="OTHER").Select(x=>x.Id).SingleAsync();
        var before=await HierarchyRows(h);var payload=operation=="create"?HierarchyNewRack(rack.ZoneId,"DENIED",aisle):HierarchyRackEdit(rack,110);payload.AisleId=aisle;
        using var response=await host.Send(OperationsRequest(actor,operation=="create"?HttpMethod.Post:HttpMethod.Put,SpaceCodePath+"/rack"+(operation=="create"?"":"/"+rack.Id),payload));var after=await HierarchyRows(h);
        await DeviceEvidence("hierarchy-aisle-"+operation+".json",new{Status=(int)response.StatusCode,Before=before,After=after});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(before,after);
    }

    [CurrentPermissionPostgresFact]
    public async Task Hierarchy_floor_delete_first_cannot_leave_a_zone()
    {
        await using var h=await HierarchySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var floor=await h.Db.Space_Floors.Where(x=>x.FloorCode=="FREE").Select(x=>x.Id).SingleAsync();var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> deleting,creating;FenceWait deleteWait;FenceWait? createWait=null;
        await using(var held=await control.BeginTransactionAsync())
        {
            await using var gate=new NpgsqlCommand("SELECT 1 FROM public.\"Space_Floor\" WHERE \"Id\"=@floor FOR UPDATE",control,held);gate.Parameters.AddWithValue("floor",floor);Assert.Equal(1,await gate.ExecuteScalarAsync());
            try
            {
                deleting=host.Send(OperationsRequest(actor,HttpMethod.Delete,SpaceCodePath+"/floor/"+floor));deleteWait=await SerialNativeWait(observer,control.ProcessID,control.ProcessID,"hierarchy-floor-delete",deleting);
                creating=host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/zone",HierarchyZone(floor,"RACE")));
                try{createWait=await SerialNativeWait(observer,deleteWait.Backend,control.ProcessID,"hierarchy-zone-after-delete",creating);}catch(Xunit.Sdk.XunitException) when(creating.IsCompletedSuccessfully){ }
            }
            finally{await held.RollbackAsync();}
        }
        using var deleted=await deleting;using var created=await creating;var parentExists=await h.Db.Space_Floors.AnyAsync(x=>x.Id==floor);var childCount=await h.Db.Space_Zones.CountAsync(x=>x.FloorId==floor);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("hierarchy-delete-first.json",new{DeleteStatus=(int)deleted.StatusCode,CreateStatus=(int)created.StatusCode,ParentWait=createWait?.Blocker==deleteWait.Backend,deleteWait,createWait,ParentExists=parentExists,ChildCount=childCount,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.OK,deleted.StatusCode);Assert.Equal(HttpStatusCode.NotFound,created.StatusCode);Assert.Equal(deleteWait.Backend,createWait?.Blocker);Assert.False(parentExists);Assert.Equal(0,childCount);Assert.Equal(stock,stockAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task Hierarchy_late_coordinate_save_failure_rolls_back_rack_and_locations()
    {
        await using var h=await HierarchySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");var loc=await h.Db.Space_Locations.AsNoTracking().SingleAsync(x=>x.Col==1);var input=await HierarchyInput(h,"edit-rack");var before=await HierarchyRows(h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_hierarchy_geometry_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated late location coordinate failure'; END $fault$;
            CREATE TRIGGER cp6_test_hierarchy_geometry_failure BEFORE UPDATE OF "AbsX" ON public."Space_Location" FOR EACH ROW
            WHEN (NEW."Id"='{{loc.Id}}' AND NEW."AbsX" IS DISTINCT FROM OLD."AbsX") EXECUTE FUNCTION public.cp6_test_hierarchy_geometry_failure();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await Send();var after=await HierarchyRows(h);var rackX=await h.Db.Space_Racks.Where(x=>x.Id==rack.Id).Select(x=>x.X).SingleAsync();var absX=await h.Db.Space_Locations.Where(x=>x.Id==loc.Id).Select(x=>x.AbsX).SingleAsync();var stockAfter=await PalletStockRows(h);
            await DeviceEvidence("hierarchy-coordinate-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=after,RackX=rackX,AbsX=absX,StockBefore=stock,StockAfter=stockAfter});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,after);Assert.Equal(10,rackX);Assert.Equal(510,absX);Assert.Equal(stock,stockAfter);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_hierarchy_geometry_failure ON public.\"Space_Location\"; DROP FUNCTION public.cp6_test_hierarchy_geometry_failure()",control);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal(110,await h.Db.Space_Racks.Where(x=>x.Id==rack.Id).Select(x=>x.X).SingleAsync());Assert.Equal(610,await h.Db.Space_Locations.Where(x=>x.Id==loc.Id).Select(x=>x.AbsX).SingleAsync());
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task Hierarchy_normal_seven_writes_preserve_code_status_and_existing_row_version_contract()
    {
        await using var h=await HierarchySeed();await using var other=await HierarchySeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var otherBefore=await HierarchyRows(other);var stock=await PalletStockRows(h);
        var mainZone=await h.Db.Space_Zones.AsNoTracking().SingleAsync(x=>x.ZoneCode=="A");var otherZone=await h.Db.Space_Zones.AsNoTracking().SingleAsync(x=>x.ZoneCode=="OTHER");var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");var freeFloor=await h.Db.Space_Floors.Where(x=>x.FloorCode=="FREE").Select(x=>x.Id).SingleAsync();
        using var createZone=await Send(HttpMethod.Post,"/zone",HierarchyZone(mainZone.FloorId,"NEW"));var zone=(await SpaceCodeHttpData<JsonElement>(createZone)).GetProperty("id").GetGuid();
        using var editZone=await Send(HttpMethod.Put,"/zone/"+zone,HierarchyZone(freeFloor,"EDITED"));Assert.Equal(HttpStatusCode.OK,editZone.StatusCode);Assert.Equal(mainZone.FloorId,await h.Db.Space_Zones.Where(x=>x.Id==zone).Select(x=>x.FloorId).SingleAsync());
        using var createAisle=await Send(HttpMethod.Post,"/aisle",HierarchyAisle(mainZone.Id,"NEW"));var aisle=(await SpaceCodeHttpData<JsonElement>(createAisle)).GetProperty("id").GetGuid();
        using var editAisle=await Send(HttpMethod.Put,"/aisle/"+aisle,HierarchyAisle(otherZone.Id,"EDITED"));Assert.Equal(HttpStatusCode.OK,editAisle.StatusCode);Assert.Equal(mainZone.Id,await h.Db.Space_Aisles.Where(x=>x.Id==aisle).Select(x=>x.ZoneId).SingleAsync());
        using var createRack=await Send(HttpMethod.Post,"/rack",HierarchyNewRack(mainZone.Id,"NEW"));var newRack=(await SpaceCodeHttpData<JsonElement>(createRack)).GetProperty("id").GetGuid();var saved=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.Id==newRack);Assert.Equal(mainZone.FloorId,saved.FloorId);Assert.Null(saved.AisleId);
        var codes=await LocationIdentity();var payload=HierarchyRackEdit(rack,110);payload.ZoneId=otherZone.Id;using var editRack=await Send(HttpMethod.Put,"/rack/"+rack.Id,payload);Assert.Equal(HttpStatusCode.OK,editRack.StatusCode);
        var placed=await h.Db.Space_Locations.AsNoTracking().SingleAsync(x=>x.Col==1);Assert.Equal((610,420,1030),(placed.AbsX,placed.AbsY,placed.AbsZ));Assert.Equal(mainZone.Id,await h.Db.Space_Racks.Where(x=>x.Id==rack.Id).Select(x=>x.ZoneId).SingleAsync());Assert.Equal(codes,await LocationIdentity());
        payload.X=220;using var stale=await Send(HttpMethod.Put,"/rack/"+rack.Id,payload);Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);Assert.Equal(110,await h.Db.Space_Racks.Where(x=>x.Id==rack.Id).Select(x=>x.X).SingleAsync());
        using var blocked=await Send(HttpMethod.Delete,"/zone/"+mainZone.Id,null);Assert.Equal(HttpStatusCode.BadRequest,blocked.StatusCode);
        using var deleteZone=await Send(HttpMethod.Delete,"/zone/"+zone,null);Assert.Equal(HttpStatusCode.OK,deleteZone.StatusCode);Assert.False(await h.Db.Space_Zones.AnyAsync(x=>x.Id==zone));
        var otherAfter=await HierarchyRows(other);var stockAfter=await PalletStockRows(h);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
        await DeviceEvidence("hierarchy-normal.json",new{Status=200,StaleStatus=(int)stale.StatusCode,ChildGuard=(int)blocked.StatusCode,NoAisle=saved.AisleId==null,AbsX=placed.AbsX,AbsY=placed.AbsY,AbsZ=placed.AbsZ,LocationIdentityPreserved=codes==await LocationIdentity(),OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body)=>host.Send(OperationsRequest(actor,method,SpaceCodePath+path,body));
        async Task<string> LocationIdentity()=>JsonSerializer.Serialize(await h.Db.Space_Locations.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.LocationCode,x.Status,x.Version}).ToArrayAsync());
    }
}

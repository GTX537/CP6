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
    [InlineData("missing")][InlineData("other-tenant")]
    public async Task SceneIngress_import_requires_actual_current_site(string source)
    {
        await using var h=await SceneIngressSeed();await using var other=await SceneIngressSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var site=source=="missing"?Guid.NewGuid():await SceneIngressSite(other);var before=await SceneIngressRows(h);var otherBefore=await SceneIngressRows(other);var stock=await PalletStockRows(h);
        using var response=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/site/"+site+"/import",SceneIngressBlueprint()));var after=await SceneIngressRows(h);var otherAfter=await SceneIngressRows(other);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("scene-ingress-site-"+source+".json",new{Status=(int)response.StatusCode,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Equal(before,after);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneIngress_late_import_location_failure_rolls_back_the_skeleton()
    {
        await using var h=await SceneIngressSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SceneIngressInput(h,"import");var site=await SceneIngressSite(h);var before=await SceneIngressRows(h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_scene_location_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated late scene location insert failure'; END $fault$;
            CREATE TRIGGER cp6_test_scene_location_failure BEFORE INSERT ON public."Space_Location" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}') EXECUTE FUNCTION public.cp6_test_scene_location_failure();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await Send();var after=await SceneIngressRows(h);var floors=h.Db.Space_Floors.Where(x=>x.SiteId==site&&x.FloorCode=="IMPORT").Select(x=>x.Id);var zones=h.Db.Space_Zones.Where(x=>floors.Contains(x.FloorId)).Select(x=>x.Id);var stockAfter=await PalletStockRows(h);
            await DeviceEvidence("scene-ingress-import-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=after,Floors=await floors.CountAsync(),Zones=await zones.CountAsync(),Aisles=await h.Db.Space_Aisles.CountAsync(x=>zones.Contains(x.ZoneId)),Racks=await h.Db.Space_Racks.CountAsync(x=>floors.Contains(x.FloorId)),Locations=await h.Db.Space_Locations.CountAsync(x=>x.FloorId!=null&&floors.Contains(x.FloorId.Value)),StockBefore=stock,StockAfter=stockAfter});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,after);Assert.Equal(stock,stockAfter);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_scene_location_failure ON public.\"Space_Location\"; DROP FUNCTION public.cp6_test_scene_location_failure()",control);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();var floor=(await SpaceCodeHttpData<JsonElement>(recovered)).GetProperty("floorId").GetGuid();Assert.Equal(4,await h.Db.Space_Locations.CountAsync(x=>x.FloorId==floor));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneIngress_competing_bind_observes_the_committed_placement()
    {
        await using var h=await SceneIngressSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var rack=await SceneIngressRack(h);var id=await SceneIngressLocation(h,"BIND-A");var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
        await using(var held=await control.BeginTransactionAsync())
        {
            await using var gate=new NpgsqlCommand("SELECT 1 FROM public.\"Space_Location\" WHERE \"Id\"=@id FOR UPDATE",control,held);gate.Parameters.AddWithValue("id",id);Assert.Equal(1,await gate.ExecuteScalarAsync());
            try{first=Send(4);firstWait=await SerialNativeWait(observer,control.ProcessID,control.ProcessID,"scene-ingress-bind-first",first);second=Send(5);secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"scene-ingress-bind-second",second);}
            finally{await held.RollbackAsync();}
        }
        using var a=await first;using var b=await second;var row=await h.Db.Space_Locations.AsNoTracking().SingleAsync(x=>x.Id==id);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("scene-ingress-bind-order.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,FirstWait=firstWait,SecondWait=secondWait,WaitedForFirst=secondWait.Blocker==firstWait.Backend,row.Placed,row.Col,row.AbsX,row.LocationCode,row.Status,row.Version,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.BadRequest,b.StatusCode);Assert.Equal(firstWait.Backend,secondWait.Blocker);Assert.True(row.Placed);Assert.Equal(rack,row.RackId);Assert.Equal(4,row.Col);Assert.Equal(3510,row.AbsX);Assert.Equal("BIND-A",row.LocationCode);Assert.Equal(1,row.Status);Assert.Equal(17,row.Version);Assert.Equal(stock,stockAfter);
        Task<HttpResponseMessage> Send(int col)=>host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/rack/"+rack+"/bind-codes",SceneIngressBind((id,col))));
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneIngress_invalid_later_pair_keeps_the_whole_bind_batch_unwritten()
    {
        await using var h=await SceneIngressSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var rack=await SceneIngressRack(h);var first=await SceneIngressLocation(h,"BIND-A");var invalid=await SceneIngressLocation(h,"FROZEN");var before=await SceneIngressRows(h);
        using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/rack/"+rack+"/bind-codes",SceneIngressBind((first,4),(invalid,5))));var after=await SceneIngressRows(h);
        await DeviceEvidence("scene-ingress-bind-invalid.json",new{Status=(int)result.StatusCode,Before=before,After=after});Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);Assert.Equal(before,after);
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneIngress_normal_export_import_and_bind_preserve_geometry_and_identity()
    {
        await using var h=await SceneIngressSeed();await using var other=await SceneIngressSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var otherBefore=await SceneIngressRows(other);var stock=await PalletStockRows(h);var source=await h.Db.Space_Floors.Where(x=>x.FloorCode=="F1").Select(x=>x.Id).SingleAsync();var target=await SceneIngressSite(h);var oldRack=await SceneIngressRack(h);
        using var exported=await host.Send(OperationsRequest(actor,HttpMethod.Get,SpaceCodePath+"/floor/"+source+"/export"));var raw=await exported.Content.ReadAsStringAsync();Assert.DoesNotContain("\"tenantId\"",raw,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("\"locationCode\"",raw,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("\"rowVersion\"",raw,StringComparison.OrdinalIgnoreCase);
        var blueprint=await SpaceCodeHttpData<SceneExportDto>(exported);Assert.Single(blueprint.Racks);
        using var imported=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/site/"+target+"/import",blueprint));var floor=(await SpaceCodeHttpData<JsonElement>(imported)).GetProperty("floorId").GetGuid();Assert.NotEqual(source,floor);Assert.Equal(target,await h.Db.Space_Floors.Where(x=>x.Id==floor).Select(x=>x.SiteId).SingleAsync());
        var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.FloorId==floor);Assert.NotEqual(oldRack,rack.Id);var zones=await h.Db.Space_Zones.AsNoTracking().Where(x=>x.FloorId==floor).ToArrayAsync();Assert.Equal(blueprint.Zones.Count,zones.Length);Assert.Empty(blueprint.Zones.Select(x=>x.Id).Intersect(zones.Select(x=>x.Id)));
        var aisle=await h.Db.Space_Aisles.AsNoTracking().SingleAsync(x=>x.Id==rack.AisleId);Assert.Equal(rack.ZoneId,aisle.ZoneId);Assert.Contains(zones,x=>x.Id==aisle.ZoneId);
        var generated=await h.Db.Space_Locations.AsNoTracking().Where(x=>x.FloorId==floor).ToArrayAsync();Assert.Equal(15,generated.Length);Assert.All(generated,x=>{Assert.Equal(rack.Id,x.RackId);Assert.True(x.Placed);Assert.Equal(0,x.Status);Assert.Equal(1,x.CodeOrigin);Assert.Null(x.LocationCode);});
        var firstCell=generated.Single(x=>x.Col==1&&x.Level==1&&x.Depth==1);Assert.Equal((510,420,1030),(firstCell.AbsX,firstCell.AbsY,firstCell.AbsZ));
        var a=await SceneIngressLocation(h,"BIND-A");var b=await SceneIngressLocation(h,"BIND-B");using var bound=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/rack/"+oldRack+"/bind-codes",SceneIngressBind((a,4),(b,5))));Assert.Equal(HttpStatusCode.OK,bound.StatusCode);
        var adopted=await h.Db.Space_Locations.AsNoTracking().Where(x=>x.Id==a||x.Id==b).OrderBy(x=>x.LocationCode).ToArrayAsync();Assert.Equal(new[]{a,b},adopted.Select(x=>x.Id));Assert.Equal(new[]{"BIND-A","BIND-B"},adopted.Select(x=>x.LocationCode));Assert.Equal(new long[]{17,19},adopted.Select(x=>x.Version));Assert.Equal(new int?[]{3510,4510},adopted.Select(x=>x.AbsX));Assert.All(adopted,x=>{Assert.Equal(oldRack,x.RackId);Assert.Equal(source,x.FloorId);Assert.True(x.Placed);Assert.Equal(1,x.Status);Assert.Equal(2,x.CodeOrigin);});
        var otherAfter=await SceneIngressRows(other);var stockAfter=await PalletStockRows(h);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
        await DeviceEvidence("scene-ingress-normal.json",new{Status=200,Zones=zones.Length,Generated=generated.Length,Bound=adopted.Length,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
    }
}

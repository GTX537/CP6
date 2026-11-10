using System.Net;
using System.Text.Json;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task SiteFloor_parent_delete_first_cannot_leave_a_child_floor()
    {
        await using var h=await SiteFloorSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var site=await h.Db.Space_Sites.Where(x=>x.SiteCode=="EMPTY").Select(x=>x.Id).SingleAsync();var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> deleting,creating;FenceWait deleteWait;FenceWait? createWait=null;
        await using(var held=await control.BeginTransactionAsync())
        {
            await using var gate=new NpgsqlCommand("SELECT 1 FROM public.\"Space_Site\" WHERE \"Id\"=@site FOR UPDATE",control,held);gate.Parameters.AddWithValue("site",site);Assert.Equal(1,await gate.ExecuteScalarAsync());
            try
            {
                deleting=host.Send(OperationsRequest(actor,HttpMethod.Delete,SpaceCodePath+"/site/"+site));deleteWait=await SerialNativeWait(observer,control.ProcessID,control.ProcessID,"site-floor-parent-delete",deleting);
                creating=host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/floor",FloorPayload(site,"RACE","SIM Race")));
                try{createWait=await SerialNativeWait(observer,deleteWait.Backend,control.ProcessID,"site-floor-create-after-delete",creating);}catch(Xunit.Sdk.XunitException) when(creating.IsCompletedSuccessfully){ }
            }
            finally{await held.RollbackAsync();}
        }
        using var deleted=await deleting;using var created=await creating;var siteExists=await h.Db.Space_Sites.AnyAsync(x=>x.Id==site);var childCount=await h.Db.Space_Floors.CountAsync(x=>x.SiteId==site);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("site-floor-delete-first.json",new{DeleteStatus=(int)deleted.StatusCode,CreateStatus=(int)created.StatusCode,AuthorityWait=createWait?.Blocker==deleteWait.Backend,deleteWait,createWait,SiteExists=siteExists,ChildCount=childCount,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.OK,deleted.StatusCode);Assert.Equal(HttpStatusCode.NotFound,created.StatusCode);Assert.NotNull(createWait);Assert.Equal(deleteWait.Backend,createWait!.Blocker);Assert.False(siteExists);Assert.Equal(0,childCount);Assert.Equal(stock,stockAfter);
    }

    [TenantManagementPostgresTheory]
    [InlineData("missing")][InlineData("other-tenant")]
    public async Task SiteFloor_creating_floor_requires_a_current_tenant_parent(string source)
    {
        await using var h=await SiteFloorSeed();await using var other=await SiteFloorSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var parent=source=="missing"?Guid.NewGuid():await other.Db.Space_Sites.Where(x=>x.SiteCode=="EMPTY").Select(x=>x.Id).SingleAsync();
        var before=await SiteFloorRows(h);var otherBefore=await SiteFloorRows(other);var stock=await PalletStockRows(h);
        using var response=await host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/floor",FloorPayload(parent,"DENIED","SIM Invalid Parent")));var after=await SiteFloorRows(h);var otherAfter=await SiteFloorRows(other);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("site-floor-parent-"+source+".json",new{Status=(int)response.StatusCode,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Equal(before,after);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task SiteFloor_normal_crud_preserves_geometry_values_existing_parent_and_guarded_children()
    {
        await using var h=await SiteFloorSeed();await using var other=await SiteFloorSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await SiteFloorRows(h);var otherBefore=await SiteFloorRows(other);var stock=await PalletStockRows(h);
        var payload=new SiteDto{SiteCode="NORMAL",SiteName="SIM Normal",Address="SIM Address",Lng=116.123456789,Lat=-8.765432101,WarehouseCd="SIMW",Enable=false};
        using var createSite=await Send(HttpMethod.Post,"/site",payload);var site=(await SpaceCodeHttpData<JsonElement>(createSite)).GetProperty("id").GetGuid();
        payload.SiteName="SIM Renamed";using var editSite=await Send(HttpMethod.Put,"/site/"+site,payload);Assert.Equal(HttpStatusCode.OK,editSite.StatusCode);
        using var sites=await Send(HttpMethod.Get,"/site",null);var savedSite=Assert.Single(await SpaceCodeHttpData<SiteDto[]>(sites),x=>x.Id==site);Assert.Equal(payload.SiteName,savedSite.SiteName);Assert.Equal(payload.Lng,savedSite.Lng);Assert.Equal(payload.Lat,savedSite.Lat);Assert.Equal("SIMW",savedSite.WarehouseCd);Assert.False(savedSite.Enable);
        var floorPayload=new FloorDto{SiteId=site,FloorCode="NORMAL",FloorName="SIM Floor",Level=-2,Height=7788,UnderlayImage="sim-floor.png",UnderlayScale=0.123456789,UnderlayOffsetX=-123,UnderlayOffsetY=456,OriginX=-789,OriginY=1024};
        using var createFloor=await Send(HttpMethod.Post,"/floor",floorPayload);var floor=(await SpaceCodeHttpData<JsonElement>(createFloor)).GetProperty("id").GetGuid();
        floorPayload.SiteId=await h.Db.Space_Sites.Where(x=>x.SiteCode=="EMPTY").Select(x=>x.Id).SingleAsync();floorPayload.FloorName="SIM Floor Renamed";
        using var editFloor=await Send(HttpMethod.Put,"/floor/"+floor,floorPayload);Assert.Equal(HttpStatusCode.OK,editFloor.StatusCode);
        using var floors=await Send(HttpMethod.Get,"/floor?siteId="+site,null);var savedFloor=Assert.Single(await SpaceCodeHttpData<FloorDto[]>(floors));Assert.Equal(floor,savedFloor.Id);Assert.Equal(site,savedFloor.SiteId);Assert.Equal(floorPayload.FloorName,savedFloor.FloorName);
        Assert.Equal((floorPayload.Level,floorPayload.Height,floorPayload.UnderlayImage,floorPayload.UnderlayScale,floorPayload.UnderlayOffsetX,floorPayload.UnderlayOffsetY,floorPayload.OriginX,floorPayload.OriginY),(savedFloor.Level,savedFloor.Height,savedFloor.UnderlayImage,savedFloor.UnderlayScale,savedFloor.UnderlayOffsetX,savedFloor.UnderlayOffsetY,savedFloor.OriginX,savedFloor.OriginY));
        using var childGuard=await Send(HttpMethod.Delete,"/site/"+site,null);Assert.Equal(HttpStatusCode.BadRequest,childGuard.StatusCode);Assert.True(await h.Db.Space_Sites.AnyAsync(x=>x.Id==site));
        using var deleteFloor=await Send(HttpMethod.Delete,"/floor/"+floor,null);Assert.Equal(HttpStatusCode.OK,deleteFloor.StatusCode);using var deleteSite=await Send(HttpMethod.Delete,"/site/"+site,null);Assert.Equal(HttpStatusCode.OK,deleteSite.StatusCode);
        var protectedSite=await h.Db.Space_Sites.Where(x=>x.SiteCode=="SIM").Select(x=>x.Id).SingleAsync();var protectedFloor=await h.Db.Space_Floors.Where(x=>x.FloorCode=="F1").Select(x=>x.Id).SingleAsync();
        using var anchor=await Send(HttpMethod.Put,"/site/"+protectedSite,SitePayload("CHANGED","SIM Anchor Change"));Assert.Equal(HttpStatusCode.BadRequest,anchor.StatusCode);
        using var zoneGuard=await Send(HttpMethod.Delete,"/floor/"+protectedFloor,null);Assert.Equal(HttpStatusCode.BadRequest,zoneGuard.StatusCode);
        var after=await SiteFloorRows(h);var otherAfter=await SiteFloorRows(other);var stockAfter=await PalletStockRows(h);Assert.Equal(before,after);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
        await DeviceEvidence("site-floor-normal.json",new{Status=200,ChildGuard=(int)childGuard.StatusCode,AnchorGuard=(int)anchor.StatusCode,ZoneGuard=(int)zoneGuard.StatusCode,ParentPreserved=savedFloor.SiteId==site,SiteEnabled=savedSite.Enable,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body)=>host.Send(OperationsRequest(actor,method,SpaceCodePath+path,body));
    }
}

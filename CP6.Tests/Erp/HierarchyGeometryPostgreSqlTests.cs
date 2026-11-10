using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string HierarchyBarrier="LOCK TABLE public.\"Space_Zone\", public.\"Space_Aisle\", public.\"Space_Rack\", public.\"Space_Location\" IN ACCESS EXCLUSIVE MODE";
    private const string HierarchyPolygon="[[0,0],[1200,0],[1200,900],[0,900]]";
    private static ZoneDto HierarchyZone(Guid floor,string code)=>new(){FloorId=floor,ZoneCode=code,ZoneName="SIM "+code,Polygon=HierarchyPolygon};
    private static AisleDto HierarchyAisle(Guid zone,string code)=>new(){ZoneId=zone,AisleCode=code,Polygon=HierarchyPolygon,Centerline="[[0,0],[1200,0]]"};
    private static RackDto HierarchyNewRack(Guid zone,string code,Guid? aisle=null)=>new(){ZoneId=zone,AisleId=aisle,RackCode=code,Cols=5,Levels=3,DepthCount=1,CellW=1000,CellH=2000,CellD=800};
    private static RackDto HierarchyRackEdit(Space_Rack r,int x)=>new(){Id=r.Id,ZoneId=r.ZoneId,AisleId=r.AisleId,TemplateId=r.TemplateId,RackCode=r.RackCode,X=x,Y=r.Y,Z=r.Z,RotationZ=r.RotationZ,Cols=r.Cols,Levels=r.Levels,DepthCount=r.DepthCount,CellW=r.CellW,CellH=r.CellH,CellD=r.CellD,Enable=r.Enable,RowVersion=r.RowVersion};

    private async Task<Harness> HierarchySeed()
    {
        var h=await SiteFloorSeed();var db=h.Db;var zone=await db.Space_Zones.AsNoTracking().SingleAsync(x=>x.ZoneCode=="A");var other=Guid.NewGuid();var aisle=Guid.NewGuid();
        db.Space_Zones.AddRange(new(){Id=other,FloorId=zone.FloorId,ZoneCode="OTHER",ZoneName="SIM Other",Polygon=HierarchyPolygon},new(){Id=Guid.NewGuid(),FloorId=zone.FloorId,ZoneCode="EMPTY",ZoneName="SIM Empty",Polygon=HierarchyPolygon});
        db.Space_Aisles.AddRange(new(){Id=aisle,ZoneId=zone.Id,AisleCode="A1",Polygon=HierarchyPolygon,Centerline="[[0,0],[1200,0]]"},new(){Id=Guid.NewGuid(),ZoneId=other,AisleCode="OTHER",Polygon=HierarchyPolygon,Centerline="[[0,0],[1200,0]]"});
        var rack=await db.Space_Racks.SingleAsync(x=>x.RackCode=="R01");rack.AisleId=aisle;rack.X=10;rack.Y=20;rack.Z=30;rack.CellW=1000;rack.CellH=2000;rack.CellD=800;
        foreach(var loc in await db.Space_Locations.Where(x=>x.RackId==rack.Id).ToArrayAsync()){loc.AbsX=10+loc.Col!.Value*1000-500;loc.AbsY=420;loc.AbsZ=1030;}
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private sealed record HierarchyHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<HierarchyHttpInput> HierarchyInput(Harness h,string operation)
    {
        var zone=await h.Db.Space_Zones.AsNoTracking().SingleAsync(x=>x.ZoneCode=="A");var empty=await h.Db.Space_Zones.AsNoTracking().SingleAsync(x=>x.ZoneCode=="EMPTY");var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");
        return operation switch
        {
            "create-zone"=>new(HttpMethod.Post,SpaceCodePath+"/zone",HierarchyZone(zone.FloorId,"NEW")),
            "edit-zone"=>new(HttpMethod.Put,SpaceCodePath+"/zone/"+zone.Id,HierarchyZone(zone.FloorId,"CHANGED")),
            "delete-zone"=>new(HttpMethod.Delete,SpaceCodePath+"/zone/"+empty.Id,new{}),
            "create-aisle"=>new(HttpMethod.Post,SpaceCodePath+"/aisle",HierarchyAisle(zone.Id,"NEW")),
            "edit-aisle"=>new(HttpMethod.Put,SpaceCodePath+"/aisle/"+rack.AisleId,HierarchyAisle(zone.Id,"CHANGED")),
            "create-rack"=>new(HttpMethod.Post,SpaceCodePath+"/rack",HierarchyNewRack(zone.Id,"NEW",rack.AisleId)),
            "edit-rack"=>new(HttpMethod.Put,SpaceCodePath+"/rack/"+rack.Id,HierarchyRackEdit(rack,110)),
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
    private static async Task<string> HierarchyRows(Harness h)
        => JsonSerializer.Serialize(new{Floors=await h.Db.Space_Floors.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Zones=await h.Db.Space_Zones.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Aisles=await h.Db.Space_Aisles.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Racks=await h.Db.Space_Racks.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Locations=await h.Db.Space_Locations.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()});


    [TenantManagementPostgresTheory]
    [InlineData("create-zone")][InlineData("edit-zone")][InlineData("delete-zone")]
    [InlineData("create-aisle")][InlineData("edit-aisle")][InlineData("create-rack")][InlineData("edit-rack")]
    public async Task Hierarchy_existing_seven_writes_decide_current_tenant_and_session(string operation)
    {
        await using var h=await HierarchySeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await HierarchyInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await HierarchyRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(HierarchyBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("hierarchy-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"hierarchy-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("hierarchy-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await HierarchyRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await HierarchyRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("hierarchy-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task Hierarchy_current_action_revocation_wins_before_the_write()
    {
        const string operation="edit-rack",resource="space-floor",action="edit";
        await using var h=await HierarchySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await HierarchyInput(h,operation);var before=await HierarchyRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==resource).Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(HierarchyBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"hierarchy-revoke-"+resource+"-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("hierarchy-revoke-"+resource+"-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await HierarchyRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await HierarchyRows(h));
    }

}

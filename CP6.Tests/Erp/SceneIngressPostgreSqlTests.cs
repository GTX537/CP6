using System.Net;
using CP6.Core.Services.Platform;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SceneIngressBarrier="LOCK TABLE public.\"Space_Site\", public.\"Space_Floor\", public.\"Space_Zone\", public.\"Space_Aisle\", public.\"Space_Rack\", public.\"Space_Location\" IN ACCESS EXCLUSIVE MODE";
    private async Task<Harness> SceneIngressSeed()
    {
        var h=await HierarchySeed();
        h.Db.Space_Locations.AddRange(new(){Id=Guid.NewGuid(),LocationCode="BIND-A",Status=1,CodeOrigin=2,Placed=false,Version=17},new(){Id=Guid.NewGuid(),LocationCode="BIND-B",Status=1,CodeOrigin=2,Placed=false,Version=19});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return h;
    }
    private static SceneExportDto SceneIngressBlueprint()
    {
        var zone=Guid.NewGuid();var aisle=Guid.NewGuid();
        return new(){Meta=new(){FloorCode="IMPORT",FloorName="SIM Imported",Level=-1,Height=7788,UnderlayScale=0.123456789,OriginX=-123,OriginY=456},
            Zones=[new(){Id=zone,ZoneCode="SIM",ZoneName="SIM Import",Polygon=HierarchyPolygon}],
            Aisles=[new(){Id=aisle,ZoneId=zone,AisleCode="SIM",Polygon=HierarchyPolygon,Centerline="[[0,0],[1200,0]]"}],
            Racks=[new(){Id=Guid.NewGuid(),ZoneId=zone,AisleId=aisle,RackCode="SIM",Cols=2,Levels=2,DepthCount=1,CellW=1000,CellH=2000,CellD=800}]};
    }
    private static BindCodesDto SceneIngressBind(params (Guid id,int col)[] items)
        => new(){Pairs=items.Select(x=>new BindCodePairDto{LocationId=x.id,Col=x.col,Level=1,Depth=1}).ToList()};
    private static Task<Guid> SceneIngressSite(Harness h)=>h.Db.Space_Sites.Where(x=>x.SiteCode=="EMPTY").Select(x=>x.Id).SingleAsync();
    private static Task<Guid> SceneIngressRack(Harness h)=>h.Db.Space_Racks.Where(x=>x.RackCode=="R01").Select(x=>x.Id).SingleAsync();
    private static Task<Guid> SceneIngressLocation(Harness h,string code)=>h.Db.Space_Locations.Where(x=>x.LocationCode==code).Select(x=>x.Id).SingleAsync();
    private sealed record SceneIngressHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<SceneIngressHttpInput> SceneIngressInput(Harness h,string operation)
        => operation switch
        {
            "import"=>new(HttpMethod.Post,SpaceCodePath+"/site/"+await SceneIngressSite(h)+"/import",SceneIngressBlueprint()),
            "bind"=>new(HttpMethod.Post,SpaceCodePath+"/rack/"+await SceneIngressRack(h)+"/bind-codes",SceneIngressBind((await SceneIngressLocation(h,"BIND-A"),4))),
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
    private static Task<string> SceneIngressRows(Harness h)=>HierarchyRows(h);


    [TenantManagementPostgresTheory]
    [InlineData("import")][InlineData("bind")]
    public async Task SceneIngress_existing_two_writes_decide_current_tenant_and_session(string operation)
    {
        await using var h=await SceneIngressSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await SceneIngressInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await SceneIngressRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SceneIngressBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("scene-ingress-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"scene-ingress-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("scene-ingress-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await SceneIngressRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await SceneIngressRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("scene-ingress-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneIngress_current_action_revocation_wins_before_the_write()
    {
        const string operation="import",resource="space-floor",action="edit";
        await using var h=await SceneIngressSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SceneIngressInput(h,operation);var before=await SceneIngressRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==resource).Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SceneIngressBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"scene-ingress-revoke-"+resource+"-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("scene-ingress-revoke-"+resource+"-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await SceneIngressRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await SceneIngressRows(h));
    }

}

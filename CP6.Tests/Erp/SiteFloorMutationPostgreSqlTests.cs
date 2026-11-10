using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SiteFloorBarrier="LOCK TABLE public.\"Space_Site\", public.\"Space_Floor\" IN ACCESS EXCLUSIVE MODE";
    private static SiteDto SitePayload(string code,string name)=>new(){SiteCode=code,SiteName=name};
    private static FloorDto FloorPayload(Guid site,string code,string name)=>new(){SiteId=site,FloorCode=code,FloorName=name,Level=2};
    private async Task<Harness> SiteFloorSeed()
    {
        var h=await SpaceCodeSeed();var db=h.Db;
        foreach(var (id,key,route) in new[]{(902,"space-site","/space/site"),(903,"space-floor","/space/floor")})
        {
            var menu=await db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuKey==key||m.RoutePath==route);
            if(menu==null){menu=new(){MenuId=id,MenuKey=key,MenuName="SIM "+key,RoutePath=route};db.Sys_Menus.Add(menu);await db.SaveChangesAsync();}
            foreach(var action in new[]{"add","edit","delete"})
            {
                if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu.MenuId&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode=action,ActionName=action});
                db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode=action});
            }
        }
        var existing=await db.Space_Sites.AsNoTracking().SingleAsync(x=>x.SiteCode=="SIM");
        db.Space_Sites.Add(new(){Id=Guid.NewGuid(),SiteCode="EMPTY",SiteName="SIM Empty"});
        db.Space_Floors.Add(new(){Id=Guid.NewGuid(),SiteId=existing.Id,FloorCode="FREE",FloorName="SIM Free",Level=2});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }

    private sealed record SiteFloorHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<SiteFloorHttpInput> SiteFloorInput(Harness h,string operation)
    {
        var site=await h.Db.Space_Sites.AsNoTracking().SingleAsync(x=>x.SiteCode=="EMPTY");
        var floor=await h.Db.Space_Floors.AsNoTracking().SingleAsync(x=>x.FloorCode=="FREE");
        return operation switch
        {
            "create-site"=>new(HttpMethod.Post,SpaceCodePath+"/site",SitePayload("NEW","SIM New")),
            "edit-site"=>new(HttpMethod.Put,SpaceCodePath+"/site/"+site.Id,SitePayload("EMPTY","SIM Edited")),
            "delete-site"=>new(HttpMethod.Delete,SpaceCodePath+"/site/"+site.Id,new{}),
            "create-floor"=>new(HttpMethod.Post,SpaceCodePath+"/floor",FloorPayload(site.Id,"NEW","SIM New")),
            "edit-floor"=>new(HttpMethod.Put,SpaceCodePath+"/floor/"+floor.Id,FloorPayload(floor.SiteId,"FREE","SIM Edited")),
            "delete-floor"=>new(HttpMethod.Delete,SpaceCodePath+"/floor/"+floor.Id,new{}),
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
    private static async Task<string> SiteFloorRows(Harness h)
        => JsonSerializer.Serialize(new{Sites=await h.Db.Space_Sites.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Floors=await h.Db.Space_Floors.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Locations=await h.Db.Space_Locations.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()});


    [TenantManagementPostgresTheory]
    [InlineData("create-site")][InlineData("edit-site")][InlineData("delete-site")]
    [InlineData("create-floor")][InlineData("edit-floor")][InlineData("delete-floor")]
    public async Task SiteFloor_existing_six_writes_decide_current_tenant_and_session(string operation)
    {
        await using var h=await SiteFloorSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await SiteFloorInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await SiteFloorRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SiteFloorBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("site-floor-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"site-floor-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("site-floor-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await SiteFloorRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await SiteFloorRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("site-floor-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData("create-site","space-site","add")][InlineData("edit-site","space-site","edit")][InlineData("delete-site","space-site","delete")]
    [InlineData("create-floor","space-floor","add")][InlineData("edit-floor","space-floor","edit")][InlineData("delete-floor","space-floor","delete")]
    public async Task SiteFloor_current_action_revocation_wins_before_the_write(string operation,string resource,string action)
    {
        await using var h=await SiteFloorSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SiteFloorInput(h,operation);var before=await SiteFloorRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==resource).Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SiteFloorBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"site-floor-revoke-"+resource+"-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("site-floor-revoke-"+resource+"-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await SiteFloorRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await SiteFloorRows(h));
    }

}

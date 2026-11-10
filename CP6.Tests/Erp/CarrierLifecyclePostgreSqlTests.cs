using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> CarrierSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-carrier"))db.Sys_Menus.Add(new(){MenuId=463,MenuKey="wms-carrier",MenuName="Manual carrier"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-carrier").Select(m=>m.MenuId).SingleAsync();
        foreach(var action in new[]{"view","add","event"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation!="create")foreach(var suffix in new[]{"A","B"})db.CarrierShipments.Add(new(){ShipmentNo=CarrierNo(h,suffix),PackageNo=CarrierNo(h,"PKG"),
            CarrierCd="YAMATO",TrackingNo="SIMULATED-"+suffix,CustomerCd="SIM-CUSTOMER",WeightKg=1.234m,CarrierFee=12.34m,EventsJson="[]",
            Status=operation=="in-transit"?CarrierShipmentStatus.PickedUp:operation=="delivered"?CarrierShipmentStatus.InTransit:CarrierShipmentStatus.Created,Creator="price-owner"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string CarrierNo(Harness h,string suffix)=>"CS"+h.Tenant.ToString("N")[..12]+suffix;
    private static CarrierShipmentDto CarrierInput()=>new(){PackageNo="SIM-PACKAGE",CarrierCd="YAMATO",TrackingNo="SIM-TRACKING",ServiceType="STANDARD",
        CustomerCd="SIM-CUSTOMER",ShipToAddress="SIMULATED ADDRESS",ShipToName="SIMULATED RECIPIENT",ShipToTel="000-0000",WeightKg=3.456m,CarrierFee=98.76m,Remarks="manual simulated metadata"};

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("event")][InlineData("pickup")][InlineData("in-transit")][InlineData("delivered")][InlineData("fail")][InlineData("list")][InlineData("detail")]
    public async Task Carrier_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await CarrierSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await CarrierWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_CarrierShipment\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("carrier-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"carrier-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("carrier-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await CarrierWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await CarrierWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await CarrierWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("carrier-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));

        Task<HttpResponseMessage> Send(string suffix)
        {
            const string path="api/wms/carrier";
            if(operation=="list")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?carrierCd=YAMATO"));
            if(operation=="detail")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+CarrierNo(h,suffix)));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,path,CarrierInput()));
            object? input=operation=="event"?new CarrierEventDto{Status="MANUAL",Message="SIMULATED-OBSERVATION"}:operation=="fail"?new{Reason="SIMULATED-FAILURE"}:null;
            return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+CarrierNo(h,suffix)+"/"+operation,input));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("list")][InlineData("detail")]
    public async Task Carrier_reads_require_the_current_view_action(string kind)
    {
        await using var h=await CarrierSeed("list");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-carrier").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72&&a.MenuId==menu&&a.ActionCode=="view").ExecuteDeleteAsync();
        var actor=await Login(host,h);var before=await CarrierWriteSet(h);
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/carrier"+(kind=="list"?"":"/"+CarrierNo(h,"A"))));
        await DeviceEvidence("carrier-"+kind+"-action.json",new{Status=(int)denied.StatusCode,Before=before,After=await CarrierWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await CarrierWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Carrier_current_action_revocation_wins_before_create()
    {
        await using var h=await CarrierSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-carrier").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await CarrierWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_CarrierShipment\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='add'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/carrier",CarrierInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"carrier-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("carrier-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await CarrierWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await CarrierWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Carrier_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await CarrierSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_CarrierShipment\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/carrier",CarrierInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"carrier-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("carrier-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"carrier-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("carrier-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.CarrierShipments.ToListAsync());
    }

}

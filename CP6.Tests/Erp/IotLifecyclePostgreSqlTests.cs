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
    private async Task<Harness> IotSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-iot"))db.Sys_Menus.Add(new(){MenuId=464,MenuKey="wms-iot",MenuName="Legacy IoT"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-iot").Select(m=>m.MenuId).SingleAsync();
        foreach(var action in new[]{"view","add","edit","del","ingest","simulate"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        var at=IotAt();
        if(operation!="create")foreach(var suffix in new[]{"A","B"})
        {
            db.IotSensors.Add(new(){SensorId=IotNo(h,suffix),SensorType="TEMP",SensorName="SIMULATED",WarehouseCd="W1",LocationCd="SRC",Unit="C",IsEnabled=true,MinThreshold=10,MaxThreshold=30,LastValue=35.1234m,LastReadAt=at,Creator="price-owner"});
            db.IotSensorReadings.Add(new(){SensorId=IotNo(h,suffix),ReadAt=at,Value=35.1234m,IsAlert=true,AlertMessage="SIMULATED OBSERVATION",Creator="price-owner"});
        }
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string IotNo(Harness h,string suffix)=>"IO"+h.Tenant.ToString("N")[..12]+suffix;
    private static DateTime IotAt()=>new(DateTime.Now.AddMinutes(-20).Ticks/TimeSpan.TicksPerSecond*TimeSpan.TicksPerSecond,DateTimeKind.Unspecified);
    private static IotSensorDto IotInput()=>new(){SensorType="TEMP",SensorName="SIMULATED",WarehouseCd="W1",LocationCd="SRC",Unit="C",IsEnabled=true,MinThreshold=10.1234m,MaxThreshold=30.5678m,Remarks="manual simulated metadata"};
    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("update")][InlineData("delete")][InlineData("ingest")][InlineData("simulate")][InlineData("list")][InlineData("detail")][InlineData("readings")][InlineData("alerts")]
    public async Task Iot_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await IotSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await IotWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\""+(operation=="readings"?"T_IotSensorReading":"T_IotSensor")+"\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("iot-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"iot-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("iot-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await IotWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await IotWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await IotWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("iot-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));

        Task<HttpResponseMessage> Send(string suffix)
        {
            const string path="api/wms/iot";
            if(operation=="list")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/sensors?warehouseCd=W1"));
            if(operation=="detail")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/sensors/"+IotNo(h,suffix)));
            if(operation=="readings")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/sensors/"+IotNo(h,suffix)+"/readings"));
            if(operation=="alerts")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/alerts"));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/sensors",IotInput()));
            if(operation=="update")return host.Send(OperationsRequest(actor,HttpMethod.Put,path+"/sensors/"+IotNo(h,suffix),IotInput()));
            if(operation=="delete")return host.Send(OperationsRequest(actor,HttpMethod.Delete,path+"/sensors/"+IotNo(h,suffix)));
            if(operation=="simulate")return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/simulate?count=1"));
            return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/sensors/"+IotNo(h,suffix)+"/readings",new{Value=18.2345m,ReadAt=IotAt().AddMinutes(5)}));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Iot_current_action_revocation_wins_before_create()
    {
        await using var h=await IotSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-iot").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await IotWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_IotSensor\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='add'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors",IotInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"iot-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("iot-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await IotWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await IotWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Iot_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await IotSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_IotSensor\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors",IotInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"iot-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("iot-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"iot-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("iot-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.IotSensors.ToListAsync());
    }
}

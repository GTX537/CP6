using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FeatureQueryPath="api/v2/admin/wms-features";
    private async Task<Harness> FeatureQuerySeed()
    {
        var h=await FeatureRequestSeed();
        h.Db.Warehouses.AddRange(new(){WarehouseCd="W2",WarehouseName="SIM second"},new(){WarehouseCd="DELETED",WarehouseName="SIM deleted configuration"});
        h.Db.WmsFeatureFlags.AddRange(new(){WarehouseCd="W1",ProductionMoveEnabled=false,SerialLpnEnabled=false,ScanRetentionDays=365},
            new(){WarehouseCd="W2",ProductionMoveEnabled=true,SerialLpnEnabled=false,ScanRetentionDays=180},
            new(){WarehouseCd="DELETED",ProductionMoveEnabled=true,SerialLpnEnabled=false,ScanRetentionDays=99,IsDeleted=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return h;
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureQuery_pause_first_denies_the_current_effective_configuration_read()
    {
        await using var h=await FeatureQuerySeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var normal=await Send();Assert.Equal(HttpStatusCode.OK,normal.StatusCode);using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await FeatureRequestWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlag\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-query-pause",active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"feature-query-paused",pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("feature-query-pause.json",new{Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await FeatureRequestWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-query-reactivate",suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureQueryPath));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureQuery_current_action_revocation_wins_before_the_configuration_read()
    {
        await using var h=await FeatureQuerySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-mobile").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await FeatureRequestWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlag\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"='device-manage'",control,revoked);
                change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();pending=host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureQueryPath));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"feature-query-revoked",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("feature-query-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await FeatureRequestWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureQuery_read_first_completes_before_the_later_pause()
    {
        await using var h=await FeatureQuerySeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await FeatureRequestWriteSet(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> reading,pause;FenceWait readWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlag\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                reading=host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureQueryPath));readWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"feature-query-first",reading);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-query-read-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,readWait.Backend,business.ProcessID,"feature-query-pause-after-read",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var result=await reading;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("feature-query-read-first.json",new{Status=(int)result.StatusCode,AuthorityWait=pauseWait?.Blocker==readWait.Backend,readWait,pauseWait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(readWait.Backend,pauseWait!.Blocker);Assert.Equal(before,await FeatureRequestWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureQuery_actual_configuration_list_preserves_tenant_deletion_version_and_retired_write()
    {
        await using var h=await FeatureQuerySeed();await using var other=await FeatureRequestSeed();other.Db.WmsFeatureFlags.Add(new(){WarehouseCd="W1",ProductionMoveEnabled=true,SerialLpnEnabled=false,ScanRetentionDays=999});await other.Db.SaveChangesAsync();other.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await FeatureRequestWriteSet(h);var stock=await PalletStockRows(h);
        using var result=await host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureQueryPath));Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        var values=JsonSerializer.Deserialize<List<WmsFeatureFlagDto>>(await result.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(new[]{"W1","W2"},values.Select(x=>x.WarehouseCd).ToArray());Assert.False(values[0].ProductionMoveEnabled);Assert.True(values[1].ProductionMoveEnabled);Assert.All(values,x=>Assert.False(x.SerialLpnEnabled));
        Assert.Equal(365,values[0].ScanRetentionDays);Assert.Equal(180,values[1].ScanRetentionDays);
        var rows=await h.Db.WmsFeatureFlags.AsNoTracking().Where(x=>!x.IsDeleted).OrderBy(x=>x.WarehouseCd).ToArrayAsync();
        for(var i=0;i<rows.Length;i++)Assert.Equal(rows[i].RowVersion==null?string.Empty:Convert.ToBase64String(rows[i].RowVersion!),values[i].RowVersion);
        using var retired=await host.Send(OperationsRequest(actor,HttpMethod.Put,FeatureQueryPath+"/W1",new UpdateWmsFeatureFlagRequest{ProductionMoveEnabled=true,SerialLpnEnabled=false,ScanRetentionDays=365}));
        Assert.Equal(HttpStatusCode.Gone,retired.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));Assert.Equal(stock,await PalletStockRows(h));
        await DeviceEvidence("feature-query-normal.json",new{Status=(int)result.StatusCode,Warehouses=values.Select(x=>x.WarehouseCd),RetiredWriteStatus=(int)retired.StatusCode,Before=before,After=await FeatureRequestWriteSet(h)});
    }
}

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
    private async Task<Harness> WcsSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-wcs-task"))db.Sys_Menus.Add(new(){MenuId=1465,MenuKey="wms-wcs-task",MenuName="Manual WCS"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-wcs-task").Select(m=>m.MenuId).SingleAsync();
        foreach(var action in new[]{"view","add","dispatch","start","complete","fail","del"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation!="create")foreach(var suffix in new[]{"A","B"})db.WcsTasks.Add(new(){TaskNo=WcsNo(h,suffix),TaskType="MOVE",Qty=1.23456789m,
            FromWarehouseCd="W1",FromLocationCd="SRC",ToWarehouseCd="W2",ToLocationCd="DEST",ProductCd="SIM-PRODUCT",RelatedNo="SIM-SOURCE",RelatedType="MOVE",
            Status=operation=="start"?WcsTaskStatus.Dispatched:operation=="complete"?WcsTaskStatus.Executing:WcsTaskStatus.Created,DeviceCd=operation is "start" or "complete"?"SIM-GW":null,Creator="price-owner"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string WcsNo(Harness h,string suffix)=>"WC"+h.Tenant.ToString("N")[..12]+suffix;
    private static WcsTaskDto WcsInput()=>new(){TaskType="MOVE",FromWarehouseCd="W1",FromLocationCd="SRC",ToWarehouseCd="W2",ToLocationCd="DEST",
        ProductCd="SIM-PRODUCT",RelatedNo="SIM-SOURCE",RelatedType="MOVE",Qty=1.23456789m,UnitCd="EA",Remarks="manual simulated metadata"};

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("dispatch")][InlineData("start")][InlineData("complete")][InlineData("fail")][InlineData("delete")][InlineData("list")][InlineData("detail")]
    public async Task Wcs_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await WcsSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await WcsWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WcsTask\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("wcs-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"wcs-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("wcs-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await WcsWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await WcsWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await WcsWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("wcs-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));

        Task<HttpResponseMessage> Send(string suffix)
        {
            const string path="api/wms/wcs-task";
            if(operation=="list")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?taskType=MOVE"));
            if(operation=="detail")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+WcsNo(h,suffix)));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,path,WcsInput()));
            if(operation=="delete")return host.Send(OperationsRequest(actor,HttpMethod.Delete,path+"/"+WcsNo(h,suffix)));
            object? input=operation=="dispatch"?new{DeviceCd="SIM-GW"}:operation=="fail"?new{ErrorMessage="SIMULATED-OBSERVATION"}:null;
            return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+WcsNo(h,suffix)+"/"+operation,input));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("list")][InlineData("detail")]
    public async Task Wcs_reads_require_the_current_view_action(string kind)
    {
        await using var h=await WcsSeed("list");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-wcs-task").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72&&a.MenuId==menu&&a.ActionCode=="view").ExecuteDeleteAsync();
        var actor=await Login(host,h);var before=await WcsWriteSet(h);
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/wcs-task"+(kind=="list"?"":"/"+WcsNo(h,"A"))));
        await DeviceEvidence("wcs-"+kind+"-action.json",new{Status=(int)denied.StatusCode,Before=before,After=await WcsWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await WcsWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Wcs_late_native_insert_failure_rolls_back_number_and_failed_task()
    {
        await using var h=await WcsSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_wcs_once;
            CREATE FUNCTION public.cp6_test_wcs_insert() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_wcs_once')=1 THEN RAISE EXCEPTION 'simulated WCS late insert failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_wcs_insert BEFORE INSERT ON public."T_WcsTask" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_wcs_insert();
            """,fault);await install.ExecuteNonQueryAsync();var before=await WcsWriteSet(h);
        try
        {
            using var failed=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/wcs-task",WcsInput()));
            await DeviceEvidence("wcs-insert-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await WcsWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await WcsWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_wcs_insert ON public.\"T_WcsTask\"; DROP FUNCTION public.cp6_test_wcs_insert(); DROP SEQUENCE public.cp6_test_wcs_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/wcs-task",WcsInput()));Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        Assert.Single(await h.Db.WcsTasks.ToListAsync());Assert.Equal(1,await h.Db.WmsSequences.Where(s=>s.Prefix=="WCS").Select(s=>s.NextNo).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wcs_current_action_revocation_wins_before_create()
    {
        await using var h=await WcsSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-wcs-task").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await WcsWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WcsTask\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='add'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/wcs-task",WcsInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"wcs-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("wcs-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await WcsWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await WcsWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Wcs_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await WcsSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WcsTask\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/wcs-task",WcsInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"wcs-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("wcs-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"wcs-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("wcs-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.WcsTasks.ToListAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wcs_manual_lifecycle_preserves_existing_errors_precision_and_no_stock_effect()
    {
        await using var h=await WcsSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        const string path="api/wms/wcs-task";
        using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,WcsInput()));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());var no=json.RootElement.GetProperty("data").GetProperty("taskNo").GetString()!;
        async Task Expect(string suffix,HttpStatusCode status,object? input=null,HttpMethod? method=null)
        {using var result=await host.Send(OperationsRequest(actor,method??HttpMethod.Post,path+"/"+no+suffix,input));Assert.True(result.StatusCode==status,await OperationsFailure(result));}
        await Expect("/start",HttpStatusCode.BadRequest);await Expect("/dispatch",HttpStatusCode.BadRequest,new{DeviceCd=""});
        await Expect("/dispatch",HttpStatusCode.OK,new{DeviceCd="SIM-GW"});await Expect("/start",HttpStatusCode.OK);
        await Expect("",HttpStatusCode.BadRequest,method:HttpMethod.Delete);await Expect("/complete",HttpStatusCode.OK);await Expect("/complete",HttpStatusCode.BadRequest);
        var row=await h.Db.WcsTasks.AsNoTracking().SingleAsync();Assert.Equal(WcsTaskStatus.Completed,row.Status);Assert.Equal(1.23456789m,row.Qty);Assert.Equal("price-owner",row.Modifier);Assert.Equal("price-owner",row.Creator);
        await Expect("",HttpStatusCode.OK,method:HttpMethod.Delete);using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+no));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        using var second=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,WcsInput()));Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        no=(await h.Db.WcsTasks.AsNoTracking().SingleAsync(t=>!t.IsDeleted)).TaskNo;
        await Expect("/fail",HttpStatusCode.OK,new{ErrorMessage="SIMULATED-OBSERVATION"});await Expect("/fail",HttpStatusCode.BadRequest,new{ErrorMessage="again"});
        var failed=await h.Db.WcsTasks.AsNoTracking().SingleAsync(t=>!t.IsDeleted);Assert.Equal(WcsTaskStatus.Failed,failed.Status);Assert.Equal("SIMULATED-OBSERVATION",failed.ErrorMessage);
        Assert.Equal(stock,await PalletStockRows(h));Assert.Empty(await h.Db.StockTransactions.ToListAsync());
    }

    private static Task<string> WcsWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Tasks',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_WcsTask" t WHERE t."TenantId"={h.Tenant}),
          'Numbers',(SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n."Id"),'[]') FROM public."T_WmsSequence" n WHERE n."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}

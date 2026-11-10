using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string OperationLogPath="api/OperLog";
    private const string OperationLogMarker="SIM-LOG-907";
    private async Task<Harness> OperationLogSeed()
    {
        var h=await PalletSeed("create");var db=h.Db;
        var menu=await db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuKey=="operlog"||m.RoutePath=="/operlog");
        if(menu==null){menu=new(){MenuId=9076,MenuKey="operlog",MenuName="SIM operation logs"};db.Sys_Menus.Add(menu);await db.SaveChangesAsync();}
        foreach(var action in new[]{"query","delete"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu.MenuId&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode=action});
        }
        for(var i=0;i<3;i++)db.Sys_OperLogs.Add(new(){TenantId=h.Tenant,UserName="SIM operator",Controller=OperationLogMarker,Action=i<2?"match":"other",RequestUrl=i<2?"/SIM-LOG-907/"+i:"/unmatched",HttpMethod="GET",StatusCode=200,CreateDate=new DateTime(2026,11,10,10,i,0),ElapsedMs=10+i});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static Task<int[]> OperationLogIds(Harness h)=>h.Db.Sys_OperLogs.Where(x=>x.Controller==OperationLogMarker).OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync();
    private static async Task<string> OperationLogRows(Harness h,int[] ids)=>JsonSerializer.Serialize(await h.Db.Sys_OperLogs.AsNoTracking().Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.Id).ToArrayAsync());

    private static async Task<FenceWait?> OperationLogControlWait(NpgsqlConnection observer,int blocker,Task control)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command=new NpgsqlCommand("""
            SELECT pid,usename,wait_event,clock_timestamp() FROM pg_stat_activity
            WHERE datname=current_database() AND wait_event_type='Lock' AND @blocker=ANY(pg_blocking_pids(pid))
              AND query LIKE '%cp6_authentication_v1.acquire%' ORDER BY pid LIMIT 1
            """,observer);command.Parameters.AddWithValue("blocker",blocker);
        while(true)
        {
            await using(var rows=await command.ExecuteReaderAsync(deadline.Token))
                if(await rows.ReadAsync(deadline.Token))return new(rows.GetInt32(0),blocker,rows.GetString(1),rows.GetString(2),rows.GetFieldValue<DateTimeOffset>(3));
            if(control.IsCompleted)return null;
            await Task.Delay(35,deadline.Token);
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("get","disable")][InlineData("clear","disable")][InlineData("get","revoke")][InlineData("clear","revoke")]
    public async Task OperationLog_existing_entries_decide_current_user_and_action(string operation,string kind)
    {
        await using var h=await OperationLogSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var ids=await OperationLogIds(h);var before=await OperationLogRows(h,ids);var stock=await PalletStockRows(h);var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="operlog"||m.RoutePath=="/operlog").Select(m=>m.MenuId).SingleAsync();
        var action=operation=="get"?"query":"delete";var users=h.Db.Model.FindEntityType(typeof(Sys_User))!.GetTableName();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_OperLogs\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();var sql=kind=="disable"?
                    "UPDATE public.\""+users+"\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"Id\"=@user":
                    "DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action";
                await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); "+sql,control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("user",h.User);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=Send();wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"operation-log-"+operation+"-"+kind,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("operation-log-"+operation+"-"+kind+".json",new{Operation=operation,Kind=kind,Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await OperationLogRows(h,ids),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(kind=="disable"?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await OperationLogRows(h,ids));Assert.Equal(stock,await PalletStockRows(h));
        if(kind=="disable")await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,true));
        else{h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});await h.Db.SaveChangesAsync();}
        actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        if(operation=="clear")Assert.Equal("[]",await OperationLogRows(h,ids));else Assert.Equal(before,await OperationLogRows(h,ids));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,operation=="get"?HttpMethod.Get:HttpMethod.Delete,OperationLogPath));
    }

    [CurrentPermissionPostgresFact]
    public async Task OperationLog_query_first_completes_before_the_current_permission_revocation()
    {
        await using var h=await OperationLogSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var ids=await OperationLogIds(h);var before=await OperationLogRows(h,ids);var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="operlog"||m.RoutePath=="/operlog").Select(m=>m.MenuId).SingleAsync();
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> reading;Task<int> revoke;FenceWait readWait;FenceWait? revokeWait=null;var released=false;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_OperLogs\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                reading=host.Send(OperationsRequest(actor,HttpMethod.Get,OperationLogPath));readWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"operation-log-query-first",reading);
                await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"='query'",control);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);revoke=change.ExecuteNonQueryAsync();
                revokeWait=await OperationLogControlWait(observer,readWait.Backend,revoke);
                await locked.RollbackAsync();released=true;using var result=await reading;await revoke;
                await DeviceEvidence("operation-log-query-first.json",new{Status=(int)result.StatusCode,AuthorityWait=revokeWait?.Blocker==readWait.Backend,readWait,revokeWait,Before=before,After=await OperationLogRows(h,ids)});
                Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.NotNull(revokeWait);Assert.Equal(readWait.Backend,revokeWait!.Blocker);Assert.Equal(before,await OperationLogRows(h,ids));
            }
            finally{if(!released)await locked.RollbackAsync();}
        }
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,OperationLogPath));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task OperationLog_late_delete_fault_keeps_original_rows_and_recovers()
    {
        await using var h=await OperationLogSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var ids=await OperationLogIds(h);var before=await OperationLogRows(h,ids);var stock=await PalletStockRows(h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_operlog_once;
            CREATE FUNCTION public.cp6_test_operlog_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_operlog_once')=1 THEN RAISE EXCEPTION 'simulated operation log delete failure'; END IF; RETURN OLD; END $fault$;
            CREATE TRIGGER cp6_test_operlog_failure BEFORE DELETE ON public."Sys_OperLogs" FOR EACH ROW
            WHEN (OLD."TenantId"='{{h.Tenant}}'::uuid AND OLD."Controller"='SIM-LOG-907') EXECUTE FUNCTION public.cp6_test_operlog_failure();
            """,fault);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await host.Send(OperationsRequest(actor,HttpMethod.Delete,OperationLogPath));await DeviceEvidence("operation-log-delete-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await OperationLogRows(h,ids),StockBefore=stock,StockAfter=await PalletStockRows(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await OperationLogRows(h,ids));Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_operlog_failure ON public.\"Sys_OperLogs\"; DROP FUNCTION public.cp6_test_operlog_failure(); DROP SEQUENCE public.cp6_test_operlog_once",fault);await remove.ExecuteNonQueryAsync();}
        using var recovered=await host.Send(OperationsRequest(actor,HttpMethod.Delete,OperationLogPath));Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal("[]",await OperationLogRows(h,ids));
    }

    [CurrentPermissionPostgresFact]
    public async Task OperationLog_normal_keyword_page_and_clear_preserve_the_other_tenant()
    {
        await using var h=await OperationLogSeed();await using var other=await OperationLogSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        var ids=await OperationLogIds(h);var otherIds=await OperationLogIds(other);var otherBefore=await OperationLogRows(other,otherIds);var stock=await PalletStockRows(h);
        using var query=await host.Send(OperationsRequest(actor,HttpMethod.Get,OperationLogPath+"?page=1&pageSize=1&keyword=SIM-LOG-907"));Assert.Equal(HttpStatusCode.OK,query.StatusCode);using var doc=JsonDocument.Parse(await query.Content.ReadAsStringAsync());
        Assert.Equal(3,doc.RootElement.GetProperty("total").GetInt32());var rows=doc.RootElement.GetProperty("rows");Assert.Equal(1,rows.GetArrayLength());Assert.Equal(ids.Last(),rows[0].GetProperty("id").GetInt32());Assert.Equal(h.Tenant,rows[0].GetProperty("tenantId").GetGuid());
        using var cleared=await host.Send(OperationsRequest(actor,HttpMethod.Delete,OperationLogPath));Assert.Equal(HttpStatusCode.OK,cleared.StatusCode);using var result=JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());Assert.True(result.RootElement.GetProperty("count").GetInt32()>=3);
        Assert.Equal("[]",await OperationLogRows(h,ids));Assert.Equal(otherBefore,await OperationLogRows(other,otherIds));Assert.Equal(stock,await PalletStockRows(h));
        await DeviceEvidence("operation-log-normal.json",new{QueryStatus=(int)query.StatusCode,ClearStatus=(int)cleared.StatusCode,Total=doc.RootElement.GetProperty("total").GetInt32(),OtherBefore=otherBefore,OtherAfter=await OperationLogRows(other,otherIds),StockBefore=stock,StockAfter=await PalletStockRows(h)});
    }
}

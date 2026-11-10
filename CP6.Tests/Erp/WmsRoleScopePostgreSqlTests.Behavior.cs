using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("role")][InlineData("warehouse")][InlineData("area")]
    public async Task RoleScope_current_reference_change_wins_before_replacement(string kind)
    {
        await using var h=await RoleScopeSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await RoleScopeWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsRoleScope\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();
                var roleTable=h.Db.Model.FindEntityType(typeof(Sys_Role))!.GetTableName();
                var sql=kind=="role"?"UPDATE public.\""+roleTable+"\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"RoleId\"=73":kind=="warehouse"?
                    "UPDATE public.\"T_Warehouse\" SET \"IsDeleted\"=true WHERE \"TenantId\"=@tenant AND \"WarehouseCd\"='W2'":
                    "UPDATE public.\"T_Location\" SET \"AreaCd\"='CHANGED' WHERE \"TenantId\"=@tenant AND \"WarehouseCd\"='W2' AND \"LocationCd\"='W2-SRC'";
                await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); "+sql,control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);await change.ExecuteNonQueryAsync();
                var input=kind=="area"?new ReplaceWmsRoleScopesRequest{Scopes=new[]{new WmsRoleScopeItem{WarehouseCd="W2",AreaCd="SECOND"}}}:RoleScopeInput();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Put,"api/v2/admin/wms-role-scopes/73",input));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"role-scope-reference-"+kind,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("role-scope-reference-"+kind+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await RoleScopeWriteSet(h)});
        Assert.Equal(kind=="role"?HttpStatusCode.NotFound:HttpStatusCode.BadRequest,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await RoleScopeWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task RoleScope_late_insert_fault_keeps_the_original_permission_set_and_recovers()
    {
        await using var h=await RoleScopeSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_scope_once;
            CREATE FUNCTION public.cp6_test_scope_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_scope_once')=1 THEN RAISE EXCEPTION 'simulated warehouse scope insert failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_scope_failure BEFORE INSERT ON public."T_WmsRoleScope" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid AND NEW."RoleId"=73) EXECUTE FUNCTION public.cp6_test_scope_failure();
            """,fault);await install.ExecuteNonQueryAsync();var before=await RoleScopeWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("role-scope-insert-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await RoleScopeWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await RoleScopeWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_scope_failure ON public.\"T_WmsRoleScope\"; DROP FUNCTION public.cp6_test_scope_failure(); DROP SEQUENCE public.cp6_test_scope_once",fault);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Assert.Equal("W2",await h.Db.WmsRoleScopes.Where(s=>s.RoleId==73).Select(s=>s.WarehouseCd).SingleAsync());
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Put,"api/v2/admin/wms-role-scopes/73",RoleScopeInput()));
    }

    [CurrentPermissionPostgresFact]
    public async Task RoleScope_competing_replacements_leave_one_complete_final_set()
    {
        await using var h=await RoleScopeSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_scope_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726450001); RETURN OLD; END $gate$;
            CREATE TRIGGER cp6_test_scope_gate BEFORE DELETE ON public."T_WmsRoleScope" FOR EACH ROW
            WHEN (OLD."TenantId"='{{h.Tenant}}'::uuid AND OLD."RoleId"=73) EXECUTE FUNCTION public.cp6_test_scope_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726450001)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    first=Send(RoleScopeInput());firstWait=await WaitForFence(observer,control.ProcessID,"%DELETE%T_WmsRoleScope%",null,first);
                    second=Send(new(){Scopes=new[]{new WmsRoleScopeItem{WarehouseCd="W1"}}});secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"role-scope-second-replace",second);
                }
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;var rows=await h.Db.WmsRoleScopes.AsNoTracking().Where(s=>s.RoleId==73).ToArrayAsync();
            await DeviceEvidence("role-scope-two-replacements.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,firstWait,secondWait,Rows=rows.Select(s=>new{s.WarehouseCd,s.AreaCd})});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.OK,b.StatusCode);Assert.Equal(firstWait.Backend,secondWait.Blocker);Assert.Single(rows);Assert.Equal("W1",rows[0].WarehouseCd);Assert.Equal("*",rows[0].AreaCd);Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_scope_gate ON public.\"T_WmsRoleScope\"; DROP FUNCTION public.cp6_test_scope_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send(ReplaceWmsRoleScopesRequest input)=>host.Send(OperationsRequest(actor,HttpMethod.Put,"api/v2/admin/wms-role-scopes/73",input));
    }

    [CurrentPermissionPostgresFact]
    public async Task RoleScope_committed_grant_change_is_consumed_by_the_actual_pallet_http_owner()
    {
        await using var h=await RoleScopeSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        using var before=await Pallet("W1");Assert.True(before.StatusCode==HttpStatusCode.OK,await OperationsFailure(before));
        using var changed=await host.Send(OperationsRequest(actor,HttpMethod.Put,"api/v2/admin/wms-role-scopes/72",RoleScopeInput()));Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        using var old=await Pallet("W1");using var next=await Pallet("W2");
        await DeviceEvidence("role-scope-pallet-consumer.json",new{BeforeStatus=(int)before.StatusCode,OldWarehouseStatus=(int)old.StatusCode,NewWarehouseStatus=(int)next.StatusCode,StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Forbidden,old.StatusCode);Assert.True(next.StatusCode==HttpStatusCode.OK,await OperationsFailure(next));Assert.Equal(2,await h.Db.Pallets.CountAsync());Assert.Equal(stock,await PalletStockRows(h));
        Task<HttpResponseMessage> Pallet(string warehouse){var input=PalletInput();input.WarehouseCd=warehouse;input.LocationCd=warehouse=="W2"?"W2-SRC":"SRC";return host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/pallet",input));}
    }

    [CurrentPermissionPostgresFact]
    public async Task RoleScope_admin_cycle_preserves_normalization_wildcards_limits_and_errors()
    {
        await using var h=await RoleScopeSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);const string path="api/v2/admin/wms-role-scopes/";
        using var admin=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"1"));Assert.Equal("*",(await RoleScopeData(admin)).Single().WarehouseCd);
        using var immutable=await host.Send(OperationsRequest(actor,HttpMethod.Put,path+"1",RoleScopeInput()));Assert.Equal(HttpStatusCode.Conflict,immutable.StatusCode);
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"99999"));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        var normalized=new ReplaceWmsRoleScopesRequest{Scopes=new[]{new WmsRoleScopeItem{WarehouseCd=" w1 ",AreaCd=" source "},new WmsRoleScopeItem{WarehouseCd="W1",AreaCd="SOURCE"},new WmsRoleScopeItem{WarehouseCd="w2"}}};
        using var saved=await Replace(normalized);var result=await RoleScopeData(saved);Assert.Equal(2,result.Count);Assert.Contains(result,s=>s.WarehouseCd=="W1"&&s.AreaCd=="SOURCE");Assert.Contains(result,s=>s.WarehouseCd=="W2"&&s.AreaCd==null);
        foreach(var item in new[]{new WmsRoleScopeItem{WarehouseCd="*",AreaCd="SOURCE"},new(){WarehouseCd="MISSING"},new(){WarehouseCd="W2",AreaCd="MISSING"}})
        {var before=await RoleScopeWriteSet(h);using var invalid=await Replace(new(){Scopes=new[]{item}});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal(before,await RoleScopeWriteSet(h));}
        for(var i=0;i<200;i++)h.Db.Locations.Add(new(){WarehouseCd="W2",LocationCd="CAP"+i.ToString("D3"),AreaCd="CAP"+i.ToString("D3")});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var boundary=new ReplaceWmsRoleScopesRequest{Scopes=Enumerable.Range(0,200).Select(i=>new WmsRoleScopeItem{WarehouseCd="W2",AreaCd="CAP"+i.ToString("D3")}).ToArray()};
        using var maximum=await Replace(boundary);Assert.Equal(200,(await RoleScopeData(maximum)).Count);
        var prior=await RoleScopeWriteSet(h);using var tooMany=await Replace(new(){Scopes=boundary.Scopes.Append(new WmsRoleScopeItem{WarehouseCd="W2",AreaCd="CAP200"}).ToArray()});Assert.Equal(HttpStatusCode.BadRequest,tooMany.StatusCode);Assert.Equal(prior,await RoleScopeWriteSet(h));
        using var cleared=await Replace(new());Assert.Empty(await RoleScopeData(cleared));
        using var wildcard=await Replace(new(){Scopes=new[]{new WmsRoleScopeItem{WarehouseCd="*"}}});Assert.Equal("*",(await RoleScopeData(wildcard)).Single().WarehouseCd);
        using var read=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"73"));Assert.Equal("*",(await RoleScopeData(read)).Single().WarehouseCd);Assert.Equal(stock,await PalletStockRows(h));
        Task<HttpResponseMessage> Replace(ReplaceWmsRoleScopesRequest input)=>host.Send(OperationsRequest(actor,HttpMethod.Put,path+"73",input));
    }

    private static async Task<List<WmsRoleScopeDto>> RoleScopeData(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));return JsonSerializer.Deserialize<List<WmsRoleScopeDto>>(await response.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static Task<string> RoleScopeWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Scopes',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsRoleScope" s WHERE s."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockTransaction" s WHERE s."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_OutboundOrder" s WHERE s."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}

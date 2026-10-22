using System.Net;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using Npgsql;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CP6.Core.EFDbContext;
using CP6.WebApi.BackgroundServices;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantOee(Harness h)
    {
        await GrantMachine(h);
        foreach(var (key,id,actions) in new[]{("mes-oee",311,new[]{"query","recalculate"}),("mes-production-result",304,new[]{"query"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"mes-oee","mes-machine","mes-production-result","mes-work-order"})
        {catalog.RegisterFields(key,[]);catalog.RegisterScope(key,key,[1,2,3,4,5],1);}
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static async Task SeedOeeInputs(Harness h)
    {
        await SeedMachine(h);await h.Db.Machines.ExecuteUpdateAsync(s=>s.SetProperty(m=>m.CapacityPerHour,60m));
        var day=DateTime.Today;var number="OW"+h.Tenant.ToString("N")[..18];
        h.Db.WorkOrders.Add(new(){WorkOrderNo=number,ProductCd="P1",Creator="price-owner"});
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo=number,ProcessCd="P",TaskCd="T",MachineCd="MC1",Creator="price-owner"});
        h.Db.MachineDowntimes.Add(new(){MachineCd="MC1",DowntimeNo="OD"+h.Tenant.ToString("N")[..18],WorkOrderNo=number,StartTime=day.AddHours(8),EndTime=day.AddHours(9),Creator="price-owner"});
        h.Db.ProductionResults.Add(new(){MachineCd="MC1",ResultNo="OP"+h.Tenant.ToString("N")[..18],WorkOrderNo=number,ProcessCd="P",TaskCd="T",OperatorCd="OP",GoodQty=120.12345678m,DefectQty=10.87654322m,CreateDate=day.AddHours(10),Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static Task<int> OeeReceipts(Harness h)=>h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_oee_legacy_v1.calculations WHERE tenant_id={h.Tenant}").SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_missing_service_authority_rejects_independent_read()
    {
        await using var h=await Seed();await SeedMachine(h);
        var service=new OeeService(h.Db);
        await Assert.ThrowsAnyAsync<Exception>(()=>service.SearchAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_missing_service_authority_rejects_live_aggregate()
    {
        await using var h=await Seed();await SeedMachine(h);
        var service=new OeeService(h.Db);
        await Assert.ThrowsAnyAsync<Exception>(()=>service.CalculateTodayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_string_worker_name_is_not_authority_to_recalculate()
    {
        await using var h=await Seed();await SeedMachine(h);
        var service=new OeeService(h.Db);
        await Assert.ThrowsAnyAsync<Exception>(()=>service.RecalculateAsync(new(){TargetDate=DateTime.Today},"OeeWorker"));
        Assert.False(await h.Db.OeeDailies.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_actual_HTTP_requires_current_query_action()
    {
        await using var h=await Seed();await SeedMachine(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/oee/today"));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_actual_HTTP_has_current_sources_no_store_and_actual_calculation_actor()
    {
        await using var h=await Seed();await GrantOee(h);await SeedOeeInputs(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var live=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/oee/today"));
        Assert.Equal(HttpStatusCode.OK,live.StatusCode);Assert.True(live.Headers.CacheControl?.NoStore);
        Assert.Contains("LEGACY_CALC_V1",await live.Content.ReadAsStringAsync());
        using var recalc=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/oee/recalculate",new{targetDate=DateTime.Today,machineCd="MC1"}));
        Assert.Equal(HttpStatusCode.OK,recalc.StatusCode);
        Assert.True(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_oee_legacy_v1.calculations WHERE tenant_id={h.Tenant} AND actor_id={h.User} AND actor_kind='HUMAN' AND actor_name='price-owner') AS \"Value\"").SingleAsync());
        using var history=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/oee"));
        Assert.Equal(HttpStatusCode.OK,history.StatusCode);Assert.Contains("120.12345678",await history.Content.ReadAsStringAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==304 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/oee/trend"));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using var deniedWrite=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/oee/recalculate",new{targetDate=DateTime.Today}));
        Assert.Equal(HttpStatusCode.Forbidden,deniedWrite.StatusCode);Assert.Equal(1,await OeeReceipts(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_preserves_legacy_formula_precision_and_binds_saved_result_to_current_inputs()
    {
        await using var h=await Seed();var owner=await GrantOee(h);await SeedOeeInputs(h);var service=new OeeService(h.Db,owner);
        var live=Assert.Single(await service.CalculateTodayAsync());
        Assert.Equal("LEGACY_CALC_V1",live.CalculationProfile);Assert.Equal(120.12345678m,live.GoodQty);Assert.Equal(10.87654322m,live.DefectQty);
        Assert.Equal(60,live.DowntimeMinutes);Assert.Equal(420,live.ActualRunMinutes);Assert.Equal(87.5m,live.Availability);
        Assert.Equal(1,await service.RecalculateAsync(new(){TargetDate=DateTime.Today},"forged"));
        var stored=Assert.Single(await service.SearchAsync(new()));Assert.Equal(live.Oee,stored.Oee);Assert.Equal(live.GoodQty,stored.GoodQty);
        Assert.Equal("Main.Oee.InputEvidence.v1",stored.SourceEvidenceVersion);
        Assert.Equal("price-owner",await h.Db.OeeDailies.Select(o=>o.Creator).SingleAsync());Assert.Equal(1,await OeeReceipts(h));
        Assert.Single((await service.GetTrendAsync(7,null))["MC1"]);
        await h.Db.ProductionResults.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.GoodQty,121.12345678m));
        Assert.Empty(await service.SearchAsync(new()));
        await h.Db.ProductionResults.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.GoodQty,120.12345678m));
        Assert.Empty(await service.SearchAsync(new())); // ABA does not restore the old source version.
        await service.RecalculateAsync(new(){TargetDate=DateTime.Today},null);Assert.Single(await service.SearchAsync(new()));
        await h.Db.OeeDailies.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.GoodQty,999m));Assert.Empty(await service.SearchAsync(new()));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_oee_legacy_v1.calculations WHERE tenant_id={h.Tenant}"));
        Assert.Equal("23514",error.SqlState);Assert.Equal(2,await OeeReceipts(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)] [InlineData(false)]
    public async Task Oee_owner_hidden_source_excludes_whole_machine_and_never_persists_a_partial_sum(bool production)
    {
        await using var h=await Seed();var owner=await GrantOee(h);await SeedOeeInputs(h);var service=new OeeService(h.Db,owner);
        await service.RecalculateAsync(new(){TargetDate=DateTime.Today},null);
        if(production)
        {
            await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-production-result").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
            await h.Db.ProductionResults.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Creator,"other"));
        }
        else
        {
            await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-machine").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
            await h.Db.MachineDowntimes.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Creator,"other"));
        }
        Assert.Empty(await service.CalculateTodayAsync());Assert.Empty(await service.SearchAsync(new()));
        Assert.Equal("OEE_SOURCE_INCOMPLETE_OR_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CalculateTodayAsync("MC1"))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RecalculateAsync(new(){TargetDate=DateTime.Today},null));
        Assert.Equal(1,await OeeReceipts(h));Assert.Equal(120.12345678m,await h.Db.OeeDailies.Select(o=>o.GoodQty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_department_object_scope_and_each_source_query_action_are_independent()
    {
        await using var h=await Seed();var owner=await GrantOee(h);await SeedOeeInputs(h);var admin=await MappingAdmin(h);
        var service=new OeeService(h.Db,owner);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-oee").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,4).SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
        Assert.Empty(await service.CalculateTodayAsync());
        await BindStandalone(h,admin,"machine",await h.Db.Machines.Select(m=>m.Id).SingleAsync());Assert.Single(await service.CalculateTodayAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==304 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CalculateTodayAsync())).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RecalculateAsync(new(){TargetDate=DateTime.Today},null));Assert.Equal(0,await OeeReceipts(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Oee_owner_final_failure_rolls_back_all_daily_results_and_evidence_or_reports_unknown_commit(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);
        var owner=await GrantOee(h);await SeedOeeInputs(h);await SeedMachine(h,"MC2");var service=new OeeService(h.Db,owner);
        var reads=0;var receiptsAtFault=0;
        if(lostAck)commit.Armed=true;
        else hook.Before=async c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==6){receiptsAtFault=await OeeReceipts(h);throw new IOException("final OEE authority unavailable");}};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RecalculateAsync(new(){TargetDate=DateTime.Today},null));hook.Before=null;
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        if(!lostAck)Assert.Equal(2,receiptsAtFault);
        Assert.Equal(lostAck?2:0,await h.Db.OeeDailies.CountAsync());Assert.Equal(lostAck?2:0,await OeeReceipts(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_trusted_worker_rechecks_scope_tenant_and_source_without_human_action_bypass()
    {
        await using var h=await Seed();var owner=await GrantOee(h);await SeedOeeInputs(h);var service=new OeeService(h.Db,owner);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==311).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RecalculateAsync(new(){TargetDate=DateTime.Today},"OeeWorker"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RecalculateWorkerAsync(new(){TargetDate=DateTime.Today},new(h.Db,Guid.NewGuid()),default));
        Assert.Equal(1,await ((IOeeWorkerV1)service).RecalculateAsync(new(){TargetDate=DateTime.Today},h.Tenant,default));
        Assert.Equal("Main.Oee.DailyWorker.v1",await h.Db.OeeDailies.Select(o=>o.Creator).SingleAsync());
        await h.Db.Sys_Tenants.Where(t=>t.Id==h.Tenant).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.ExpireDate,DateTime.Now.AddSeconds(-1)));
        Assert.Equal("OEE_WORKER_TENANT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>((IOeeWorkerV1)service).RecalculateAsync(new(){TargetDate=DateTime.Today},h.Tenant,default))).Message);
        Assert.Equal(1,await OeeReceipts(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_unverified_legacy_rows_are_withheld_and_native_receipts_reject_foreign_objects()
    {
        await using var h=await Seed();var owner=await GrantOee(h);await SeedOeeInputs(h);var service=new OeeService(h.Db,owner);
        h.Db.OeeDailies.Add(new(){MachineCd="MC1",OeeDate=DateTime.Today,Oee=99m});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty(await service.SearchAsync(new()));await service.RecalculateAsync(new(){TargetDate=DateTime.Today},null);
        Assert.Single(await service.SearchAsync(new()));
        await using var foreign=await Seed();await SeedMachine(foreign);var foreignMachine=await foreign.Db.Machines.Select(m=>m.Id).SingleAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oee_legacy_v1.calculations
              (id,tenant_id,daily_id,machine_id,profile,evidence_version,input_bytes,input_hash,result_bytes,result_hash,daily_version,observed_at,actor_kind,actor_id,actor_name)
            SELECT {Guid.NewGuid()},tenant_id,daily_id,{foreignMachine},profile,evidence_version,input_bytes,input_hash,result_bytes,result_hash,daily_version,observed_at,actor_kind,actor_id,actor_name
            FROM cp6_oee_legacy_v1.calculations WHERE tenant_id={h.Tenant}
            """));
        Assert.Equal("23514",error.SqlState);Assert.Equal(1,await OeeReceipts(h));Assert.Equal(0,await OeeReceipts(foreign));
    }

    [TenantManagementPostgresTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Oee_owner_worker_final_expiry_cancellation_and_lost_commit_ack_keep_atomic_evidence(int fault)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);
        var owner=await GrantOee(h);await SeedOeeInputs(h);var service=new OeeService(h.Db,owner);
        using var cancellation=new CancellationTokenSource();var tenantReads=0;var receiptsAtFault=0;
        if(fault==2)commit.Armed=true;
        else hook.Before=async command=>
        {
            if(!command.CommandText.Contains("AuthenticationPolicyRevision") || !command.CommandText.Contains("Sys_Tenants") || ++tenantReads!=2)return;
            hook.Before=null;receiptsAtFault=await OeeReceipts(h);
            if(fault==1)cancellation.Cancel();
            else await h.Db.Sys_Tenants.Where(t=>t.Id==h.Tenant).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.ExpireDate,DateTime.Now.AddSeconds(-1)));
        };
        Func<Task> action=async()=>await ((IOeeWorkerV1)service).RecalculateAsync(new(){TargetDate=DateTime.Today},h.Tenant,cancellation.Token);
        if(fault==1)await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        else Assert.Equal(fault==2?"OPERATIONS_OUTCOME_UNKNOWN":"OEE_WORKER_TENANT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(action)).Message);
        hook.Before=null;if(fault!=2)Assert.Equal(1,receiptsAtFault);
        Assert.Equal(fault==2?1:0,await OeeReceipts(h));Assert.Equal(fault==2?1:0,await h.Db.OeeDailies.CountAsync());
        Assert.False(h.Db.ChangeTracker.HasChanges());
    }

    [CurrentPermissionPostgresFact]
    public async Task Oee_owner_host_tenant_loop_resolves_fresh_context_before_worker_without_a_browser_actor()
    {
        await using var first=await Seed();await SeedOeeInputs(first);
        await using var second=await Seed();await SeedOeeInputs(second);
        var services=new ServiceCollection();services.AddScoped<ITenantContext,TenantContext>();
        services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<ITenantEnumerator,TenantEnumerator>();
        services.AddScoped(sp=>new OperationsOwnerV1(sp.GetRequiredService<CP6Context>(),new OperationsHttp(),_=>throw new InvalidOperationException("worker must not borrow human identity")));
        services.AddScoped<OeeService>();services.AddScoped<IOeeWorkerV1>(sp=>sp.GetRequiredService<OeeService>());
        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
        var contexts=new HashSet<Guid>();var tenants=new HashSet<Guid>();
        await TenantScopeRunner.ForEachTenantAsync(provider.GetRequiredService<IServiceScopeFactory>(),async(sp,tenant,ct)=>
        {
            if(tenant!=first.Tenant && tenant!=second.Tenant)return;
            var db=sp.GetRequiredService<CP6Context>();Assert.Equal(tenant,db.CurrentTenantId);contexts.Add(db.ContextId.InstanceId);
            Assert.Equal(1,await sp.GetRequiredService<IOeeWorkerV1>().RecalculateAsync(new(){TargetDate=DateTime.Today},tenant,ct));tenants.Add(tenant);
        });
        Assert.Equal(2,contexts.Count);Assert.Equal(new[]{first.Tenant,second.Tenant}.ToHashSet(),tenants);
        Assert.Equal(1,await OeeReceipts(first));Assert.Equal(1,await OeeReceipts(second));
        Assert.Equal("Main.Oee.DailyWorker.v1",await first.Db.OeeDailies.Select(x=>x.Creator).SingleAsync());
        Assert.Equal(120.12345678m,await second.Db.OeeDailies.Select(x=>x.GoodQty).SingleAsync());
    }
}

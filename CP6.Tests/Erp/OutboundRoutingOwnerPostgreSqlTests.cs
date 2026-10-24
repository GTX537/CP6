using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static AuthorizationCatalogStoreV1 RoutingCatalog()
    {
        var c=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-warehouse",new[]{"AddressText","ManagerCd","Remarks","WarehouseName"}),
            ("wms-outbound-routing",new[]{"CustomerCd","ProductCdPrefix","Remarks","RuleName"})})
        {c.RegisterScope(key,key,[1,2,3,4,5],1);c.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}
        c.Seal();return c;
    }
    private static async Task<OperationsOwnerV1> GrantRouting(Harness h)
    {
        await GrantMaster(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==419);
        if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=419,MenuKey="wms-outbound-routing",MenuName="routing"});else menu.MenuKey="wms-outbound-routing";
        foreach(var action in new[]{"query","preview","add","edit","del"})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==419&&a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=419,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=419,ActionCode=action});
        }
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-outbound-routing",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),RoutingCatalog().Capture);
    }
    private static async Task<(Guid rule,Guid warehouse,string code)> SeedRouting(Harness h,int count=1)
    {
        var wh=await SeedMaster(h,1);var rows=Enumerable.Range(0,count).Select(i=>new OutboundRoutingRule{
            RuleName="Rule"+i,TargetWarehouseCd=wh.code,SortOrder=i,Enabled=i%2==0,CustomerCd=i==0?"":null,
            ProductCdPrefix=i==0?"":null,Creator=i==0?"price-owner":"other",Remarks="Private"}).ToArray();
        h.Db.OutboundRoutingRules.AddRange(rows);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return(rows[0].Id,wh.warehouse,wh.code);
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_missing_authority_cannot_read_even_empty_rules()
    {
        await using var h=await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new OutboundRoutingService(h.Db).ListRulesAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_missing_authority_cannot_preview_all_warehouses()
    {
        await using var h=await Seed();h.Db.Warehouses.Add(new(){WarehouseCd="PRIVATE",WarehouseName="Hidden"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new OutboundRoutingService(h.Db).ResolveCandidateWarehousesAsync(null,"P",1,"FORGED"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_missing_authority_cannot_install_active_rule()
    {
        await using var h=await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new OutboundRoutingService(h.Db).CreateRuleAsync(new OutboundRoutingRule{RuleName="Unsafe",TargetWarehouseCd="HIDDEN",Enabled=true},"forged"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_current_pages_preserve_legacy_unknown_and_scoped_original_warehouse()
    {
        await using var h=await Seed();var owner=await GrantRouting(h);var ids=await SeedRouting(h,203);var svc=new OutboundRoutingOwnerV1(h.Db,owner);
        var page=await svc.SearchAsync(new());Assert.Equal(203,page.Total);Assert.Equal(50,page.Items.Count);Assert.False(page.LegacyResolverEnabled);Assert.False(page.FormalPreviewAvailable);
        var row=page.Items[0];Assert.Equal(ids.warehouse,row.TargetWarehouseId);Assert.Equal("",row.CustomerCd);Assert.Equal("LEGACY_EXACT",row.CustomerCondition);Assert.Equal("LEGACY_ANY",row.ProductCondition);
        Assert.Equal("UNPROVEN",row.FormalPolicyState);Assert.Equal("NOT_RECORDED",row.DomainState);Assert.Equal("NOT_ADOPTED",row.SelectorState);Assert.Equal(8,Convert.FromBase64String(row.Etag.Trim('"')).Length);
        Assert.Equal(3,(await svc.SearchAsync(new(){Page=5})).Items.Count);Assert.Equal(102,(await svc.SearchAsync(new(){Enabled=true})).Total);
        await using var other=await Seed();var foreign=await SeedRouting(other);
        Assert.Empty((await svc.SearchAsync(new(){TargetWarehouseId=foreign.warehouse})).Items);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(foreign.rule))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"OTHER"));Assert.Empty((await svc.SearchAsync(new())).Items);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(ids.rule))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Routing_owner_department_mapping_requires_both_actual_rule_and_current_target(int scope)
    {
        await using var h=await Seed();var owner=await GrantRouting(h);var ids=await SeedRouting(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);
        var svc=new OutboundRoutingOwnerV1(h.Db,owner);Assert.Empty((await svc.SearchAsync(new())).Items);
        Assert.Equal(ids.rule,Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("routing-rule","Rule",_=>Task.FromResult(h.User),default)).Id);
        var binding=await BindStandalone(h,admin,"routing-rule",ids.rule);Assert.Empty((await svc.SearchAsync(new())).Items);
        await BindStandalone(h,admin,"warehouse",ids.warehouse);Assert.Single((await svc.SearchAsync(new())).Items);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"routing-rule",ids.rule,binding.EntityId)) with{Enabled=false});
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(ids.rule))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_current_fields_and_self_scope_do_not_invent_any_conditions()
    {
        await using var h=await Seed();var owner=await GrantRouting(h);var ids=await SeedRouting(h,2);var svc=new OutboundRoutingOwnerV1(h.Db,owner);
        foreach(var field in new[]{"RuleName","CustomerCd","ProductCdPrefix","Remarks"})h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-outbound-routing",FieldName=field,Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var row=await svc.GetAsync(ids.rule);Assert.Null(row.RuleName);Assert.Null(row.CustomerCd);Assert.Null(row.ProductCdPrefix);Assert.Null(row.Remarks);
        Assert.False(row.CustomerReadable);Assert.Equal("HIDDEN",row.CustomerCondition);Assert.Equal("HIDDEN",row.ProductCondition);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-outbound-routing").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.Warehouses.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-warehouse").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Empty((await svc.SearchAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Routing_owner_legacy_paths_require_current_actions_before_formal_adoption_gate(bool disabled)
    {
        await using var h=await Seed();var owner=await GrantRouting(h);var ids=await SeedRouting(h);var current=new OutboundRoutingOwnerV1(h.Db,owner,!disabled);
        IOutboundRoutingService svc=disabled?new NoOpOutboundRoutingService(h.Db,current):new OutboundRoutingService(h.Db,current);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ListRulesAsync())).Status);
        Assert.Equal("ROUTING_SOURCE_POLICY_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ResolveCandidateWarehousesAsync("fake","P",1,"ALL"))).Message);
        foreach(var call in new Func<Task>[]{()=>svc.CreateRuleAsync(new(),"forged"),()=>svc.UpdateRuleAsync(ids.rule,new(),"forged"),()=>svc.DeleteRuleAsync(ids.rule,"forged")})
            Assert.Equal("ROUTING_POLICY_WRITE_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(call)).Message);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.DeleteRuleAsync(Guid.NewGuid(),null))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==419&&x.ActionCode!="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ResolveCandidateWarehousesAsync(null,"P",1,ids.code))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CreateRuleAsync(new(),null))).Status);
        Assert.Single((await current.SearchAsync(new())).Items);Assert.Single(await h.Db.OutboundRoutingRules.ToArrayAsync());h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==401&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>current.SearchAsync(new()))).Status);Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_final_source_failure_frozen_input_and_unavailable_context_do_not_disclose()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantRouting(h);var ids=await SeedRouting(h,2);var svc=new OutboundRoutingOwnerV1(h.Db,owner);var q=new RoutingQueryV1{Enabled=true};
        hook.After=c=>{if(c.CommandText.Contains("WITH field_subject")){q.Enabled=false;hook.After=null;}return Task.CompletedTask;};
        Assert.True(Assert.Single((await svc.SearchAsync(q)).Items).LegacyEnabled);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==3)throw new IOException("final source fault");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(ids.rule))).Status);hook.Before=null;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);
        await h.Db.OutboundRoutingRules.FirstAsync();Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);h.Db.ChangeTracker.Clear();
        foreach(var invalid in new[]{new RoutingQueryV1{Page=0},new(){PageSize=201},new(){Page=int.MaxValue},new(){TargetWarehouseId=Guid.Empty}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(invalid))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_raw_sql_change_waits_for_current_read_transaction()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantRouting(h);var ids=await SeedRouting(h);Task<int>? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await db.OutboundRoutingRules.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RuleName,"Changed"));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_OutboundRoutingRule%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        RoutingObservationV1? result=null;try{result=await new OutboundRoutingOwnerV1(h.Db,owner).GetAsync(ids.rule);}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal("Rule0",result!.RuleName);Assert.Equal("Changed",await h.Db.OutboundRoutingRules.Select(x=>x.RuleName).SingleAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Routing_owner_actual_program_http_handles_feature_flag_without_bypassing_owner(bool enabled)
    {
        await using var h=await Seed();await GrantRouting(h);var ids=await SeedRouting(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{{"OutboundRouting__Enabled",enabled.ToString()}});var cookies=await Login(host,h);
        using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-routing/owner/v1/rules"));Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);
        using var body=JsonDocument.Parse(await read.Content.ReadAsStringAsync());var data=body.RootElement.GetProperty("data");Assert.Equal(enabled,data.GetProperty("legacyResolverEnabled").GetBoolean());Assert.Single(data.GetProperty("items").EnumerateArray());
        var record=data.GetProperty("items")[0];Assert.Equal(JsonValueKind.Null,record.GetProperty("outboundType").ValueKind);Assert.Equal("",record.GetProperty("customerCd").GetString());Assert.Equal(ids.warehouse,record.GetProperty("targetWarehouseId").GetGuid());
        using var single=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-routing/owner/v1/rules/"+ids.rule));Assert.Equal(HttpStatusCode.OK,single.StatusCode);
        using var singleBody=JsonDocument.Parse(await single.Content.ReadAsStringAsync());Assert.Equal(ids.rule,singleBody.RootElement.GetProperty("data").GetProperty("id").GetGuid());
        using var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-routing"));Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);
        using var preview=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-routing/preview?productCd=P&fallbackWarehouseCd=FORGED"));Assert.Equal(HttpStatusCode.ServiceUnavailable,preview.StatusCode);
        using var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-routing",new{ruleName="Unsafe",targetWarehouseCd=ids.code}));Assert.Equal(HttpStatusCode.ServiceUnavailable,create.StatusCode);
        using var update=await host.Send(OperationsRequest(cookies,HttpMethod.Put,"api/wms/outbound-routing/"+ids.rule,new{ruleName="Unsafe",targetWarehouseCd=ids.code}));Assert.Equal(HttpStatusCode.ServiceUnavailable,update.StatusCode);
        using var delete=await host.Send(OperationsRequest(cookies,HttpMethod.Delete,"api/wms/outbound-routing/"+ids.rule));Assert.Equal(HttpStatusCode.ServiceUnavailable,delete.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==419&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-routing/owner/v1/rules/"+ids.rule));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_native_mapping_and_new_action_definitions_cannot_grant_or_cross_tenants()
    {
        await using var h=await Seed();await GrantRouting(h);var ids=await SeedRouting(h);var admin=await MappingAdmin(h);await using var other=await Seed();var foreign=await SeedRouting(other);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'routing-rule',{foreign.rule},{admin.Child},true)
            """));Assert.Equal("23514",error.SqlState);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==419&&(x.ActionCode=="query"||x.ActionCode=="preview")).ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(x=>x.MenuId==419&&(x.ActionCode=="query"||x.ActionCode=="preview")).ExecuteDeleteAsync();
        foreach(var op in new CP6.Persistence.PostgreSql.Migrations.Core.RoutingDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {await h.Db.Database.ExecuteSqlRawAsync(op.Sql);await h.Db.Database.ExecuteSqlRawAsync(op.Sql);}
        Assert.Equal(2,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==419&&(x.ActionCode=="query"||x.ActionCode=="preview")));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==419&&(x.ActionCode=="query"||x.ActionCode=="preview")));
        await BindStandalone(h,admin,"routing-rule",ids.rule);
    }
    [CurrentPermissionPostgresFact]
    public async Task Routing_owner_internal_capability_is_bound_to_actual_context_transaction_parent_and_action()
    {
        await using var h=await Seed();var owner=await GrantOperations(h);var wms=OperationsWms(h,owner);var number=await wms.CreateOrderAsync(NewOutbound(),null);await wms.ConfirmOrderAsync(number,null);
        var header=await h.Db.OutboundOrders.AsNoTracking().SingleAsync();var detail=await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync();var routing=new NoOpOutboundRoutingService(h.Db);
        OperationsAccessV1? saved=null;
        await owner.ExecuteAsync(OperationsOwnerV1.Outbound,"allocate",false,async(a,t)=>
        {
            saved=a;Assert.Equal(new[]{"W01"},await routing.ResolveWithinOwnerAsync(a,header.Id,detail.Id,t));
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>routing.ResolveWithinOwnerAsync(a,header.Id,Guid.NewGuid(),t))).Status);
            await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new NoOpOutboundRoutingService(other).ResolveWithinOwnerAsync(a,header.Id,detail.Id,t))).Status);return true;
        });
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>routing.ResolveWithinOwnerAsync(saved!,header.Id,detail.Id))).Status);
        await owner.ExecuteAsync(OperationsOwnerV1.Outbound,"query",false,async(a,t)=>
        {Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>routing.ResolveWithinOwnerAsync(a,header.Id,detail.Id,t))).Status);return true;});
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Routing_owner_internal_allocation_uses_stored_sources_and_rolls_back_on_final_failure(bool disabled)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantOperations(h);
        IOutboundRoutingService routing=disabled?new NoOpOutboundRoutingService(h.Db):new OutboundRoutingService(h.Db);
        var wms=new OutboundService(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),routing:routing,owner:owner);
        h.Db.Warehouses.Add(new(){WarehouseCd="W02",WarehouseName="Legacy target"});
        h.Db.OutboundRoutingRules.Add(new(){RuleName="Existing",TargetWarehouseCd="W02",Enabled=true});
        h.Db.Stocks.AddRange(new(){WarehouseCd="W01",LocationCd="L",ProductCd="P",PhysicalQty=10m,AvailableQty=10m},new(){WarehouseCd="W02",LocationCd="L",ProductCd="P",PhysicalQty=10m,AvailableQty=10m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var no=await wms.CreateOrderAsync(NewOutbound(),null);await wms.ConfirmOrderAsync(no,null);var reads=0;
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==2)throw new IOException("final source fault");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>wms.AllocateAsync(no,"forged"))).Status);hook.Before=null;
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.All(await h.Db.Stocks.AsNoTracking().ToArrayAsync(),s=>Assert.Equal(0,s.AllocatedQty));
        await wms.AllocateAsync(no,null);var chosen=await h.Db.Stocks.AsNoTracking().SingleAsync(x=>x.AllocatedQty>0);Assert.Equal(disabled?"W01":"W02",chosen.WarehouseCd);Assert.Equal(1.12345678m,chosen.AllocatedQty);
        Assert.Equal("price-owner",(await h.Db.StockTransactions.AsNoTracking().SingleAsync()).OperatorCd);
    }
}

using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantSample(Harness h)
    {
        await GrantOperations(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuId==447);
        if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=447,MenuKey="wms-sample-stock",MenuName="Samples"});else menu.MenuKey="wms-sample-stock";
        foreach(var action in new[]{"query","add","edit","lend","return","expire","del"})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==447&&x.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=447,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=447,ActionCode=action});
        }
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-sample-stock",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var c=new AuthorizationCatalogStoreV1();c.RegisterScope("wms-sample-stock","Samples",[1,2,3,4,5],1);
        c.RegisterFields("wms-sample-stock",new[]{"CustomerCd","CustomerName","LentTo","ProductCd","ProductName","Remarks"}.Select(x=>new FieldRegistry.Field(x,x)));c.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static async Task<Guid> SeedSample(Harness h,int count=1)
    {
        var rows=Enumerable.Range(0,count).Select(i=>new SampleStock{SampleNo="SMP"+i,SampleType="PROTO",Quantity=99999999999999.9999m,UnitCd="BOX ",
            CustomerCd="Customer ",CustomerName="Private customer",ProductCd="Product ",ProductName="Private product",LentTo="Free name ",Remarks="Private note",
            Status=i%4,LentAt=i%4==0?null:new DateTime(2026,1,1),ExpectedReturnDate=new(2026,1,2),ReturnedAt=i%4==2?new DateTime(2026,1,3):null,
            RegisteredAt=new(2026,1,1),Creator=i==0?"price-owner":"other"}).ToArray();
        h.Db.SampleStocks.AddRange(rows);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return rows[0].Id;
    }
    [TenantManagementPostgresTheory]
    [InlineData("api/wms/sample-stock")]
    [InlineData("api/wms/sample-stock/SECRET")]
    [InlineData("api/wms/sample-stock/overdue")]
    public async Task Sample_owner_legacy_HTTP_cannot_disclose_without_current_query(string path)
    {
        await using var h=await Seed();await GrantOperations(h);
        h.Db.SampleStocks.Add(new(){SampleNo="SECRET",LentTo="Sensitive",Status=1,LentAt=new(2026,1,1),ExpectedReturnDate=new(2026,1,2)});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("Sensitive",await response.Content.ReadAsStringAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_pages_beyond_500_preserve_exact_legacy_facts_and_unknown_obligations()
    {
        await using var h=await Seed();var owner=await GrantSample(h);var id=await SeedSample(h,503);var svc=new SampleOwnerV1(h.Db,owner);
        h.Db.SampleStocks.Add(new(){SampleNo="DELETED",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var first=await svc.SearchAsync(new());var last=await svc.SearchAsync(new(){Page=11});Assert.Equal(503,first.Total);Assert.Equal(50,first.Items.Count);Assert.Equal(3,last.Items.Count);
        Assert.Equal(first.Items.Select(x=>x.Id),(await svc.SearchAsync(new())).Items.Select(x=>x.Id));
        var row=await svc.GetAsync(id);Assert.Equal("99999999999999.9999",row.DeclaredQuantity);Assert.Equal("BOX ",row.UnitCd);Assert.Equal("Free name ",row.LentTo);Assert.Null(row.WarehouseCd);
        Assert.Null(row.OutstandingQuantity);Assert.Equal("SOURCE_UNPROVEN",row.StockSourceState);Assert.Equal("HISTORY_INCOMPLETE",row.HistoryState);Assert.Equal("UNPROVEN",row.SiteDateState);Assert.False(first.FormalOperationsAvailable);
        Assert.Equal(8,Convert.FromBase64String(row.Etag.Trim('"')).Length);Assert.Equal("NO_LOAN_RECORDED",row.ReturnEvidenceState);
        var pending=await svc.SearchAsync(new(){LegacyStatus=3,AwaitingReturnEvidence=true});Assert.Equal(125,pending.Total);Assert.All(pending.Items,x=>Assert.Equal("OUTSTANDING_UNKNOWN",x.ReturnEvidenceState));
        Assert.All((await svc.SearchAsync(new(){LegacyStatus=2})).Items,x=>Assert.Equal("RETURN_RECORDED_UNVERIFIED",x.ReturnEvidenceState));
        Assert.Equal(503,(await svc.SearchAsync(new(){CustomerCd="Customer",ProductCd="product"})).Total); // Native comparison, raw bytes preserved above.
        await using var other=await Seed();var foreign=await SeedSample(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(foreign))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Sample_owner_department_mapping_uses_original_identity_and_current_binding(int scope)
    {
        await using var h=await Seed();var owner=await GrantSample(h);var id=await SeedSample(h,2);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var svc=new SampleOwnerV1(h.Db,owner);
        Assert.Equal(0,(await svc.SearchAsync(new())).Total);
        Assert.Equal(id,Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("sample-stock","SMP0",_=>Task.FromResult(h.User),default)).Id);
        var binding=await BindStandalone(h,admin,"sample-stock",id);Assert.Equal(id,Assert.Single((await svc.SearchAsync(new())).Items).Id);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"sample-stock",id,binding.EntityId)) with{Enabled=false});Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_self_and_current_warehouse_area_do_not_invent_unknown_locations()
    {
        await using var h=await Seed();var owner=await GrantSample(h);await SeedSample(h,3);var svc=new SampleOwnerV1(h.Db,owner);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-sample-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-sample-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W1"));Assert.Empty((await svc.SearchAsync(new())).Items);
        await h.Db.SampleStocks.Where(x=>x.SampleNo=="SMP0").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W1"));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A1"));Assert.Empty((await svc.SearchAsync(new())).Items);
        h.Db.Warehouses.Add(new(){WarehouseCd="W1",WarehouseName="Recorded"});h.Db.Locations.Add(new(){WarehouseCd="W1",LocationCd="L1",AreaCd="A1"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.SampleStocks.Where(x=>x.SampleNo=="SMP0").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LocationCd,"L1"));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.Locations.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();Assert.Empty((await svc.SearchAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_MIN_fields_and_hidden_filters_cannot_disclose_borrower_or_business_codes()
    {
        await using var h=await Seed();var owner=await GrantSample(h);var id=await SeedSample(h);var svc=new SampleOwnerV1(h.Db,owner);
        foreach(var field in new[]{"CustomerCd","CustomerName","LentTo","ProductCd","ProductName","Remarks"})h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-sample-stock",FieldName=field,Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var row=await svc.GetAsync(id);Assert.Null(row.LentTo);Assert.Null(row.Remarks);Assert.Null(row.CustomerCd);Assert.Null(row.CustomerName);Assert.Null(row.ProductCd);Assert.Null(row.ProductName);Assert.False(row.BorrowerReadable);
        foreach(var query in new[]{new SampleQueryV1{CustomerCd=""},new(){CustomerCd="Customer"},new(){ProductCd="Product"}})
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(query))).Status);
        Assert.Single((await svc.SearchAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_legacy_writers_are_fenced_after_current_actions_and_exact_object_scope()
    {
        await using var h=await Seed();var owner=await GrantSample(h);await SeedSample(h,4);var svc=new SampleStockService(h.Db,new WmsSequenceService(h.Db),new(h.Db,owner));
        foreach(var call in new Func<Task>[]{()=>svc.CreateAsync(new(),"forged"),()=>svc.UpdateAsync("SMP0",new(),"forged"),()=>svc.LendAsync("SMP0","new borrower",null,"forged"),()=>svc.ReturnAsync("SMP1","forged"),()=>svc.ExpireAsync("SMP1","forged"),()=>svc.DeleteAsync("SMP3","forged")})
            Assert.Equal("SAMPLE_WRITE_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(call)).Message);
        foreach(var call in new Func<Task>[]{()=>svc.SearchAsync(new()),()=>svc.GetAsync("SMP0"),()=>svc.OverdueAsync()})Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(call)).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReturnAsync("OTHER",null))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==447&&x.ActionCode=="lend").ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LendAsync("SMP0","forged",null,null))).Status);
        Assert.Equal(4,await h.Db.SampleStocks.CountAsync());Assert.Equal("Free name ",await h.Db.SampleStocks.Where(x=>x.SampleNo=="SMP0").Select(x=>x.LentTo).SingleAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new SampleStockService(h.Db,new WmsSequenceService(h.Db)).CreateAsync(new(),null));
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_freezes_filters_checks_final_authority_and_rejects_unavailable_contexts()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantSample(h);var id=await SeedSample(h);var svc=new SampleOwnerV1(h.Db,owner);var q=new SampleQueryV1{SampleType="PROTO"};
        hook.After=c=>{if(c.CommandText.Contains("WITH field_subject")){q.SampleType="OTHER";hook.After=null;}return Task.CompletedTask;};Assert.Single((await svc.SearchAsync(q)).Items);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==2)throw new IOException("final source fault");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(id))).Status);hook.Before=null;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);
        await h.Db.SampleStocks.FirstAsync();Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync(id))).Status);h.Db.ChangeTracker.Clear();
        foreach(var invalid in new[]{new SampleQueryV1{Page=0},new(){PageSize=101},new(){Page=int.MaxValue},new(){SampleNo=new string('x',101)}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(invalid))).Status);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new SampleOwnerV1(other,owner).GetAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_native_reader_fences_raw_SQL_history_changes()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantSample(h);var id=await SeedSample(h);Task<int>? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await db.SampleStocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LentTo,"Changed"));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_SampleStock%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        SampleObservationV1? result=null;try{result=await new SampleOwnerV1(h.Db,owner).GetAsync(id);}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal("Free name ",result!.LentTo);Assert.Equal("Changed",await h.Db.SampleStocks.Select(x=>x.LentTo).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_native_mapping_rejects_cross_tenant_objects_and_action_seeding_cannot_grant_roles()
    {
        await using var h=await Seed();await GrantSample(h);var id=await SeedSample(h);var admin=await MappingAdmin(h);await using var other=await Seed();var foreign=await SeedSample(other);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'sample-stock',{foreign},{admin.Child},true)
            """));Assert.Equal("23514",error.SqlState);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==447&&x.ActionCode=="query").ExecuteDeleteAsync();await h.Db.Sys_MenuActions.Where(x=>x.MenuId==447&&x.ActionCode=="query").ExecuteDeleteAsync();
        foreach(var op in new CP6.Persistence.PostgreSql.Migrations.Core.SampleDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {await h.Db.Database.ExecuteSqlRawAsync(op.Sql);await h.Db.Database.ExecuteSqlRawAsync(op.Sql);}
        Assert.Equal(1,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==447&&x.ActionCode=="query"));Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==447&&x.ActionCode=="query"));await BindStandalone(h,admin,"sample-stock",id);
    }
    [CurrentPermissionPostgresFact]
    public async Task Sample_owner_actual_HTTP_keeps_no_store_string_precision_and_safe_compatibility_statuses()
    {
        await using var h=await Seed();await GrantSample(h);var id=await SeedSample(h,4);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var list=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/sample-stock/owner/v1/records?legacyStatus=3&awaitingReturnEvidence=true"));Assert.Equal(HttpStatusCode.OK,list.StatusCode);Assert.True(list.Headers.CacheControl?.NoStore);
        using var body=JsonDocument.Parse(await list.Content.ReadAsStringAsync());var row=body.RootElement.GetProperty("data").GetProperty("items")[0];Assert.Equal("99999999999999.9999",row.GetProperty("declaredQuantity").GetString());Assert.Equal("OUTSTANDING_UNKNOWN",row.GetProperty("returnEvidenceState").GetString());Assert.Equal(JsonValueKind.Null,row.GetProperty("outstandingQuantity").ValueKind);
        using var single=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/sample-stock/owner/v1/records/"+id));Assert.Equal(HttpStatusCode.OK,single.StatusCode);
        using var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/sample-stock/SMP0"));Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);
        foreach(var (verb,path,payload) in new[]{(HttpMethod.Post,"",(object?)new{sampleType="PROTO",quantity=1}), (HttpMethod.Put,"/SMP0",new{sampleType="PROTO",quantity=2}), (HttpMethod.Post,"/SMP0/lend",new{lentTo="Forged"}), (HttpMethod.Post,"/SMP1/return",null),(HttpMethod.Post,"/SMP1/expire",null),(HttpMethod.Delete,"/SMP3",null)})
        {using var response=await host.Send(OperationsRequest(cookies,verb,"api/wms/sample-stock"+path,payload));Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);}
        using var hidden=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/sample-stock/owner/v1/records/"+Guid.NewGuid()));Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==447&&x.ActionCode=="query").ExecuteDeleteAsync();using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/sample-stock/owner/v1/records/"+id));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(4,await h.Db.SampleStocks.CountAsync());
    }
}

using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using CP6.Tests.Platform;
using Moq;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantVmi(Harness h)
    {
        await GrantOperations(h);
        foreach(var(key,id,actions)in new[]{("wms-vmi",446,new[]{"query","amount-read","rate-read","calculate","confirm"}),("wms-stock",403,new[]{"query"})})
        {
            if(!await h.Db.Sys_Menus.AnyAsync(x=>x.MenuId==id))h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==id&&x.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var catalog=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields)in new[]{("wms-vmi",new[]{"BillingAmount","CustomerName","DailyStorageRate","Remarks"}),("wms-stock",new[]{"UnitPrice"}),
            ("wms-outbound-order",new[]{"UnitPrice"}),("mes-work-order",Array.Empty<string>()),("order",new[]{"UnitPrice","Amount"})})
        {catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static async Task SeedVmi(Harness h,int count=1,bool distinctCustomers=false)
    {
        var wh="V"+h.Tenant.ToString("N")[..6];h.Db.Warehouses.Add(new(){WarehouseCd=wh,WarehouseName=wh});
        h.Db.Locations.Add(new(){WarehouseCd=wh,LocationCd="L1",AreaCd="A"});
        for(var i=0;i<count;i++)
        {
            var cd=distinctCustomers?"C"+i.ToString("D4"):"C1";
            h.Db.Stocks.Add(new(){WarehouseCd=wh,LocationCd="L1",ProductCd="P"+i.ToString("D4"),LotNo="L",OwnerType=StockOwnerType.Customer,OwnerCd=cd,
                PhysicalQty=9999999999999.99999999m,AllocatedQty=0.00000001m,AvailableQty=9999999999999.99999998m,UnitCd="EA",Creator="price-owner"});
            h.Db.VmiBillings.Add(new(){BillingNo=wh+i.ToString("D4"),CustomerCd=distinctCustomers||i==0?cd:"B"+i.ToString("D4"),CustomerName="customer original",YearMonth="202610",SkuCount=1,
                BeginQty=9999999999999.99999999m,EndQty=0.00000001m,AvgQty=5000000000000m,DailyStorageRate=123.4567m,BillingAmount=9999999999999999.99m,
                Confirmed=true,Creator="price-owner",Remarks="original remarks",CalculatedAt=new(2026,10,1)});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static VmiService VmiFor(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new Mock<IWmsSequenceService>(MockBehavior.Strict).Object,owner);

    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_exact_quantities_group_by_unit_without_inventing_free_or_valuation()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h,4);var service=VmiFor(h,owner);
        await h.Db.Stocks.Where(x=>x.ProductCd=="P0002").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UnitCd,"KG"));
        await h.Db.Stocks.Where(x=>x.ProductCd=="P0003").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UnitCd,(string?)null));
        var page=await service.SearchCustomersOwnerAsync(new());Assert.Equal(3,page.Total);
        var ea=Assert.Single(page.Items.Where(x=>x.UnitCd=="EA"));Assert.Equal("19999999999999.99999998",ea.PhysicalQty);
        Assert.Equal("0.00000002",ea.LegacyAllocatedQty);Assert.Equal(2,ea.StockRowCount);Assert.Equal("NOT_EVALUATED",ea.AvailabilityKnowledge);Assert.Null(ea.Free);Assert.Null(ea.EstimatedValue);
        Assert.Null(Assert.Single(page.Items.Where(x=>x.UnitCd==null)).PhysicalQty);
        var details=await service.SearchStockOwnerAsync("C1",new());Assert.Equal(4,details.Total);Assert.All(details.Items,x=>Assert.Equal("9999999999999.99999999",x.PhysicalQty));
        var billing=(await service.SearchBillingsOwnerAsync(new())).Items[0];Assert.Equal("9999999999999999.99",billing.BillingAmount);Assert.Equal("123.4567",billing.DailyStorageRate);
        Assert.Equal("POINT_IN_TIME_ESTIMATE",billing.LegacySemantics);Assert.Equal("UNKNOWN",billing.FinanceOutcome);Assert.True(billing.LegacyConfirmedFlag);
        var json=JsonSerializer.Serialize(billing);Assert.DoesNotContain("TenantId",json);Assert.DoesNotContain("Creator",json);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_pages_customers_details_and_billings_before_materialization()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h,103,true);var service=VmiFor(h,owner);
        var first=await service.SearchCustomersOwnerAsync(new(){PageSize=100});var second=await service.SearchCustomersOwnerAsync(new(){Page=2,PageSize=100});
        Assert.Equal(103,first.Total);Assert.Equal(100,first.Items.Count);Assert.Equal(3,second.Items.Count);
        Assert.Equal(103,first.Items.Concat(second.Items).Select(x=>x.CustomerCd).Distinct().Count());
        var bills=await service.SearchBillingsOwnerAsync(new(){Page=2,PageSize=100});Assert.Equal(103,bills.Total);Assert.Equal(3,bills.Items.Count);
        await h.Db.Stocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.OwnerCd,"C1"));
        var stocks=await service.SearchStockOwnerAsync("C1",new(){Page=2,PageSize=100});Assert.Equal(103,stocks.Total);Assert.Equal(3,stocks.Items.Count);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_current_stock_source_intersection_and_live_location_filter_before_totals()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h,2);var service=VmiFor(h,owner);
        await h.Db.Stocks.Where(x=>x.ProductCd=="P0001").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        foreach(var key in new[]{"wms-stock","wms-vmi"})
        {
            await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey==key).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
            Assert.Single((await service.SearchStockOwnerAsync("C1",new())).Items);
            await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey==key).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        }
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"WRONG"));Assert.Empty((await service.SearchCustomersOwnerAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"*"));
        await h.Db.Locations.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await service.SearchStockOwnerAsync("C1",new())).Items);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==403).ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchCustomersOwnerAsync(new()))).Message);
        Assert.Equal(2,(await service.SearchBillingsOwnerAsync(new())).Total);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)][InlineData(3)][InlineData(4)]
    public async Task Vmi_owner_department_scope_uses_actual_stock_and_billing_UUID_independently(int scope)
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var service=VmiFor(h,owner);
        Assert.Empty((await service.SearchCustomersOwnerAsync(new())).Items);Assert.Empty((await service.SearchBillingsOwnerAsync(new())).Items);
        var stock=await h.Db.Stocks.AsNoTracking().SingleAsync();var bill=await h.Db.VmiBillings.AsNoTracking().SingleAsync();
        await BindStandalone(h,admin,"stock",stock.Id);Assert.Single((await service.SearchCustomersOwnerAsync(new())).Items);Assert.Empty((await service.SearchBillingsOwnerAsync(new())).Items);
        Assert.Equal(bill.Id,Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("vmi-billing",bill.BillingNo,_=>Task.FromResult(h.User),default)).Id);
        var mapping=await BindStandalone(h,admin,"vmi-billing",bill.Id);Assert.Single((await service.SearchBillingsOwnerAsync(new())).Items);
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"vmi-billing",bill.Id,mapping.EntityId))with{Enabled=false});Assert.Empty((await service.SearchBillingsOwnerAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_current_action_and_field_MIN_hide_sensitive_values_without_mutating_legacy()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h);var service=VmiFor(h,owner);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==446&&x.ActionCode=="amount-read").ExecuteDeleteAsync();
        var row=Assert.Single((await service.SearchBillingsOwnerAsync(new())).Items);Assert.False(row.AmountReadable);Assert.Null(row.BillingAmount);Assert.True(row.RateReadable);
        foreach(var f in new[]{"DailyStorageRate","CustomerName","Remarks"})h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-vmi",FieldName=f,Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();row=Assert.Single((await service.SearchBillingsOwnerAsync(new())).Items);
        Assert.Null(row.DailyStorageRate);Assert.Null(row.CustomerName);Assert.Null(row.Remarks);Assert.False(row.RateReadable);
        Assert.Equal(9999999999999999.99m,await h.Db.VmiBillings.Select(x=>x.BillingAmount).SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==446&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_tenant_creator_deleted_bill_and_unadopted_writes_have_no_effects()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h,2);var service=VmiFor(h,owner);
        await using var foreign=await Seed();await SeedVmi(foreign);var foreignNo=await foreign.Db.VmiBillings.Select(x=>x.BillingNo).SingleAsync();
        Assert.Equal("OBJECT_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ConfirmBillingAsync(foreignNo,"forged"))).Message);
        var no=await h.Db.VmiBillings.Select(x=>x.BillingNo).FirstAsync();
        Assert.Equal("VMI_OWNER_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ConfirmBillingAsync(no,"forged"))).Message);
        Assert.Equal("VMI_OWNER_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CalculateMonthlyBillingAsync("broken",-1,"forged"))).Message);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchByCustomerAsync(null))).Message);
        Assert.Equal(2,await h.Db.VmiBillings.CountAsync());Assert.False(await h.Db.WmsSequences.AnyAsync());Assert.False(await h.Db.StockTransactions.AnyAsync());
        await h.Db.VmiBillings.Where(x=>x.BillingNo==no).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-vmi").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Single((await service.SearchBillingsOwnerAsync(new())).Items);
        await h.Db.VmiBillings.Where(x=>x.Creator=="price-owner").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await service.SearchBillingsOwnerAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_native_mapping_and_catalog_replay_preserve_no_implicit_grants()
    {
        await using var h=await Seed();var owner=await GrantVmi(h);await SeedVmi(h);var admin=await MappingAdmin(h);
        await using var foreign=await Seed();await SeedVmi(foreign);var id=await foreign.Db.VmiBillings.Select(x=>x.Id).SingleAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'vmi-billing',{id},{admin.Child},true)
            """));Assert.Equal("23514",error.SqlState);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==446).ExecuteDeleteAsync();await h.Db.Sys_MenuActions.Where(x=>x.MenuId==446).ExecuteDeleteAsync();
        foreach(var op in new CP6.Persistence.PostgreSql.Migrations.Core.VmiBillingDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {await h.Db.Database.ExecuteSqlRawAsync(op.Sql);await h.Db.Database.ExecuteSqlRawAsync(op.Sql);}
        Assert.Equal(3,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==446));Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==446));
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>VmiFor(h,owner).SearchBillingsOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_bounds_frozen_query_and_outer_or_pretracked_context_reject()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantVmi(h);await SeedVmi(h,2);var service=VmiFor(h,owner);
        foreach(var q in new[]{new VmiQueryV1{Page=0},new(){PageSize=101},new(){Page=int.MaxValue},new(){CustomerCd=new string('C',21)}})
            Assert.Equal("VMI_QUERY_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(q))).Message);
        Assert.Equal("INVALID_YEAR_MONTH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(new(){YearMonth="2026-10"}))).Message);
        var input=new VmiQueryV1{PageSize=1};hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;input.CustomerCd="WRONG";input.Page=2;}return Task.CompletedTask;};
        var page=await service.SearchBillingsOwnerAsync(input);Assert.Equal(1,page.Page);Assert.Equal(2,page.Total);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==4)throw new IOException("final VMI authority unavailable");return Task.CompletedTask;};
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(new()))).Message);hook.Before=null;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(new()));
        await h.Db.VmiBillings.FirstAsync();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsOwnerAsync(new()));h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_actual_program_http_denies_legacy_effects_and_returns_current_exact_read()
    {
        await using var h=await Seed();await GrantVmi(h);await SeedVmi(h);var no=await h.Db.VmiBillings.Select(x=>x.BillingNo).SingleAsync();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var path in new[]{"customers","customers/C1/details","billings"})
        {
            using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/vmi/owner-v1/"+path));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal("VMI-OWNER/1",json.RootElement.GetProperty("data").GetProperty("version").GetString());
        }
        foreach(var route in new[]{"calculate",no+"/confirm"})
        {
            using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/vmi/billings/"+route,new{yearMonth="202610",dailyStorageRate=1}));
            Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Contains("VMI_OWNER_ADOPTION_UNPROVEN",await response.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==446&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/vmi/owner-v1/billings"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.True(await h.Db.VmiBillings.Select(x=>x.Confirmed).SingleAsync());Assert.False(await h.Db.WmsSequences.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_native_fence_keeps_raw_billing_writer_out_of_read_cut()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantVmi(h);await SeedVmi(h);Task<int>? writer=null;var blocked=false;
        hook.After=async command=>
        {
            if(!command.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await other.VmiBillings.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Confirmed,false));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE %T_VmiBilling%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        VmiPageV1<VmiBillingObservationV1>? page=null;
        try{page=await VmiFor(h,owner).SearchBillingsOwnerAsync(new());}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.True(Assert.Single(page!.Items).LegacyConfirmedFlag);Assert.False(await h.Db.VmiBillings.Select(x=>x.Confirmed).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_missing_authority_cannot_read_empty_stock_or_historical_billings()
    {
        await using var h=await Seed();var service=new VmiService(h.Db,new WmsSequenceService(h.Db));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchByCustomerAsync(null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetDetailsAsync("customer"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchBillingsAsync(null,null,null));
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_missing_authority_cannot_calculate_with_forged_actor()
    {
        await using var h=await Seed();var service=new VmiService(h.Db,new WmsSequenceService(h.Db));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CalculateMonthlyBillingAsync("202610",1,"forged"));
        Assert.False(await h.Db.VmiBillings.AnyAsync());Assert.False(await h.Db.WmsSequences.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Vmi_owner_missing_authority_cannot_confirm_historical_estimate()
    {
        await using var h=await Seed();var no="V"+h.Tenant.ToString("N")[..12];
        h.Db.VmiBillings.Add(new(){BillingNo=no,CustomerCd="CUSTOMER",YearMonth="202610",BeginQty=1,EndQty=2,AvgQty=1.5m,DailyStorageRate=1,BillingAmount=46.5m,Creator="other"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var service=new VmiService(h.Db,new WmsSequenceService(h.Db));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ConfirmBillingAsync(no,"forged"));
        Assert.False(await h.Db.VmiBillings.Select(x=>x.Confirmed).SingleAsync());
    }
}

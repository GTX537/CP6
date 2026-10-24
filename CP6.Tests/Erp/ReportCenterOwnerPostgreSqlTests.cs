using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using CP6.Entity.DomainModels.Wms;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_missing_authority_rejects_empty_reports()
    {
        await using var h = await Seed(); var svc = new ReportCenterService(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.MonthlyStockReportAsync("2026-01"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.AbcAnalysisAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.DeadStockAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_missing_authority_cannot_read_original_history()
    {
        await using var h = await Seed();
        h.Db.StockTransactions.Add(new() { TxnNo = "R" + h.Tenant.ToString("N")[..15], TxnType = "IN", WarehouseCd = "W", LocationCd = "L", ProductCd = "P", LotNo = "LOT", Qty = 1, TxnDateTime = new(2026,1,1), OperatorCd = "private" });
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear(); var svc = new ReportCenterService(h.Db);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.InboundHistoryAsync(new(2026,1,1),new(2026,1,31)));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => svc.OutboundHistoryAsync(new(2026,1,1),new(2026,1,31)));
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_missing_authority_cannot_export_detached_rows()
    {
        await using var h = await Seed(); var svc = new ReportCenterService(h.Db);
        Assert.Throws<OrderPriceCorrectionException>(() => svc.ExportCsv(new[]{new InboundHistoryRow{TxnNo="private",Qty=99}}));
    }
    private static async Task<OperationsOwnerV1> GrantReportCenter(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,fields,actions) in new[]{("wms-report-center",481,new[]{"OperatorCd","OwnerCd","Remark"},new[]{"query","export"}),
            ("wms-stock",403,new[]{"UnitPrice"},new[]{"query"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id&&a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
            catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static async Task<string> SeedReportCenter(Harness h,int stockCount=2,int transactionCount=2)
    {
        var key="R"+h.Tenant.ToString("N")[..8];h.Db.Warehouses.Add(new(){WarehouseCd=key,WarehouseName=key});
        h.Db.Locations.AddRange(new Location{WarehouseCd=key,LocationCd="A",LocationName="A",AreaCd="A"},new Location{WarehouseCd=key,LocationCd="B",LocationName="B",AreaCd="B"});
        for(var i=0;i<stockCount;i++)h.Db.Stocks.Add(new(){WarehouseCd=key,LocationCd=i%2==0?"A":"B",ProductCd="P"+i.ToString("D5"),LotNo=i==0?"":"L",UnitCd=i==0?null:"EA",
            PhysicalQty=9999999999999.99999999m,AllocatedQty=0,AvailableQty=9999999999999.99999999m,OwnerType=i==0?"LEGACY":"CUSTOMER",OwnerCd="private-owner",Creator=i==0?"price-owner":"other"});
        for(var i=0;i<transactionCount;i++)h.Db.StockTransactions.Add(new(){TxnNo=key+i.ToString("D5"),TxnType=i==0?"ADJ":"OUT",TxnDateTime=new(2026,1,1),
            WarehouseCd=key,LocationCd=i%2==0?"A":"B",ProductCd="P"+i.ToString("D5"),LotNo=i==0?"":"L",Qty=i==0?-9999999999999.99999999m:9999999999999.99999999m,
            UnitCd=i==0?null:"KG",OwnerType=null,OwnerCd="private-owner",OperatorCd="private-operator",Remark="private-note",RelatedNo="hidden-source",RelatedType="OUTBOUND",Creator=i==0?"price-owner":"other"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return key;
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_original_precision_unknown_units_owners_and_meanings_are_preserved()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h);var svc=new ReportCenterService(h.Db,owner);
        var stocks=await svc.SearchStockOwnerAsync(new(){LotNo=""});var s=Assert.Single(stocks.Items);
        Assert.Equal("9999999999999.99999999",s.PhysicalQty);Assert.Null(s.UnitCd);Assert.Equal("NOT_RECORDED",s.UnitState);Assert.Equal("LEGACY",s.OwnerType);Assert.Equal("UNKNOWN",s.OwnerState);
        Assert.Null(s.FreeQty);Assert.Null(s.Valuation);Assert.Equal("NOT_EVALUATED",stocks.FreeState);Assert.Equal("NOT_CAPTURED",stocks.SnapshotState);Assert.False(stocks.ExportAvailable);
        var txns=await svc.SearchTransactionsOwnerAsync(new(){TxnType="ADJ"});var t=Assert.Single(txns.Items);Assert.Equal("-9999999999999.99999999",t.Qty);
        Assert.Null(t.OwnerType);Assert.Equal("UNKNOWN",t.OwnerState);Assert.Null(t.RelatedNo);Assert.Null(t.RelatedType);Assert.Equal("NOT_RECONSTRUCTED",t.OccurrenceState);
        Assert.All(await h.Db.StockTransactions.Select(x=>x.RelatedNo).ToArrayAsync(),v=>Assert.Equal("hidden-source",v));
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_pages_beyond_old_5000_limit_with_real_total_and_stable_ties()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h,203,5001);var svc=new ReportCenterService(h.Db,owner);
        var lastStock=await svc.SearchStockOwnerAsync(new(){Page=5});Assert.Equal(203,lastStock.Total);Assert.Equal(3,lastStock.Items.Count);
        var first=await svc.SearchTransactionsOwnerAsync(new(){PageSize=200});var second=await svc.SearchTransactionsOwnerAsync(new(){Page=2,PageSize=200});
        var last=await svc.SearchTransactionsOwnerAsync(new(){Page=26,PageSize=200});Assert.Equal(5001,last.Total);Assert.Single(last.Items);
        Assert.Empty(first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)));Assert.DoesNotContain(last.Items[0].Id,first.Items.Select(x=>x.Id));
        Assert.Equal(first.Items.Select(x=>x.Id),(await svc.SearchTransactionsOwnerAsync(new(){PageSize=200})).Items.Select(x=>x.Id));
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_report_and_source_current_scope_intersect_before_total()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);var key=await SeedReportCenter(h);var svc=new ReportCenterService(h.Db,owner);
        await using var foreign=await Seed();var foreignKey=await SeedReportCenter(foreign);
        Assert.Equal(2,(await svc.SearchStockOwnerAsync(new())).Total);Assert.Empty((await svc.SearchTransactionsOwnerAsync(new(){WarehouseCd=foreignKey})).Items);
        foreach(var resource in new[]{"wms-report-center","wms-stock"})
        {
            await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey==resource).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
            Assert.Single((await svc.SearchStockOwnerAsync(new())).Items);Assert.Single((await svc.SearchTransactionsOwnerAsync(new())).Items);
            await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey==resource).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        }
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));Assert.Single((await svc.SearchStockOwnerAsync(new())).Items);Assert.Single((await svc.SearchTransactionsOwnerAsync(new())).Items);
        await h.Db.Locations.Where(x=>x.LocationCd=="A").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchStockOwnerAsync(new())).Items);Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"*"));Assert.Single((await svc.SearchStockOwnerAsync(new())).Items);
        await h.Db.StockTransactions.Where(x=>x.LocationCd=="B").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);
        await h.Db.Warehouses.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchStockOwnerAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Report_center_owner_real_stock_and_transaction_department_bindings_remain_independent(int scope)
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var svc=new ReportCenterService(h.Db,owner);
        Assert.Empty((await svc.SearchStockOwnerAsync(new())).Items);Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);
        var stockId=await h.Db.Stocks.Where(x=>x.LocationCd=="B").Select(x=>x.Id).SingleAsync();var txnId=await h.Db.StockTransactions.Where(x=>x.LocationCd=="B").Select(x=>x.Id).SingleAsync();
        await BindStandalone(h,admin,"stock",stockId);Assert.Single((await svc.SearchStockOwnerAsync(new())).Items);Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);
        var mapping=await BindStandalone(h,admin,"stock-transaction",txnId);Assert.Single((await svc.SearchTransactionsOwnerAsync(new())).Items);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Empty((await svc.SearchStockOwnerAsync(new())).Items);Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-stock").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,scope));
        await SaveStandalone(h,admin,(await StandaloneCommand(h,admin,"stock-transaction",txnId,mapping.EntityId)) with {Enabled=false});
        Assert.Empty((await svc.SearchTransactionsOwnerAsync(new())).Items);Assert.Single((await svc.SearchStockOwnerAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_fields_never_expose_cross_owner_references_or_hidden_values()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h);var svc=new ReportCenterService(h.Db,owner);
        foreach(var field in new[]{"OwnerCd","OperatorCd","Remark"})h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-report-center",FieldName=field,Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.All((await svc.SearchStockOwnerAsync(new())).Items,x=>{Assert.Null(x.OwnerCd);Assert.False(x.OwnerCdReadable);});
        Assert.All((await svc.SearchTransactionsOwnerAsync(new())).Items,x=>{Assert.Null(x.OwnerCd);Assert.Null(x.OperatorCd);Assert.Null(x.Remark);Assert.False(x.RemarkReadable);Assert.Null(x.RelatedNo);});
        Assert.All(await h.Db.StockTransactions.Select(x=>x.Remark).ToArrayAsync(),x=>Assert.Equal("private-note",x));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==403&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchTransactionsOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_raw_datetime_filter_keeps_last_microsecond_and_original_sign()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h,2,3);var svc=new ReportCenterService(h.Db,owner);
        await h.Db.StockTransactions.Where(x=>x.ProductCd=="P00000").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TxnDateTime,new DateTime(2026,1,31,23,59,59).AddTicks(9999990)));
        await h.Db.StockTransactions.Where(x=>x.ProductCd=="P00001").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TxnDateTime,new DateTime(2026,2,1)));
        await h.Db.StockTransactions.Where(x=>x.ProductCd=="P00002").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TxnDateTime,new DateTime(2025,12,31)));
        var rows=await svc.SearchTransactionsOwnerAsync(new(){FromDate="2026-01-01",ToDate="2026-01-31"});Assert.Single(rows.Items);Assert.StartsWith("-",rows.Items[0].Qty);
        Assert.Equal(3,(await svc.SearchTransactionsOwnerAsync(new(){FromDate="0001-01-01",ToDate="9999-12-31"})).Total);
        foreach(var q in new[]{new ReportQueryV1{Page=0},new(){PageSize=201},new(){Page=int.MaxValue},new(){FromDate="2026-01-01"},new(){FromDate="2026-02-01",ToDate="2026-01-01"},new(){FromDate="2026-01-01T00:00:00Z",ToDate="2026-01-01"}})
            Assert.Equal("REPORT_QUERY_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchTransactionsOwnerAsync(q))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchStockOwnerAsync(new(){TxnType="OUT"}));
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_frozen_query_final_authority_fault_and_unsafe_context_reject()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantReportCenter(h);await SeedReportCenter(h);var svc=new ReportCenterService(h.Db,owner);var q=new ReportQueryV1{PageSize=1};
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;q.Page=99;q.ProductCd="wrong";}return Task.CompletedTask;};
        Assert.Equal(1,(await svc.SearchStockOwnerAsync(q)).Page);
        foreach(var finalRead in new[]{3,4})
        {
            var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==finalRead)throw new IOException("final report or source authority unavailable");return Task.CompletedTask;};
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchStockOwnerAsync(new()))).Message);hook.Before=null;
        }
        await using(var tx=await h.Db.Database.BeginTransactionAsync())await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchTransactionsOwnerAsync(new()));
        await h.Db.StockTransactions.FirstAsync();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchTransactionsOwnerAsync(new()));h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_legacy_read_and_export_actions_are_independent_and_emit_no_data()
    {
        await using var h=await Seed();var owner=await GrantReportCenter(h);await SeedReportCenter(h);var svc=new ReportCenterService(h.Db,owner);
        foreach(var read in new Func<Task>[] {()=>svc.MonthlyStockReportAsync("bad"),()=>svc.AbcAnalysisAsync(),()=>svc.DeadStockAsync(),()=>svc.InboundHistoryAsync(default,default),()=>svc.OutboundHistoryAsync(default,default)})
            Assert.Equal("OWNER_READ_VERSION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(read)).Message);
        Assert.Equal("REPORT_SNAPSHOT_ADOPTION_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RequireExportOwnerAsync())).Message);
        // A lazy sequence must not be consumed by the denied generic PostgreSQL exporter.
        var enumerated=false;IEnumerable<InboundHistoryRow> Rows(){enumerated=true;yield return new(){TxnNo="secret"};}
        Assert.Equal("REPORT_EXPORT_CONTEXT_REQUIRED",Assert.Throws<OrderPriceCorrectionException>(()=>svc.ExportCsv(Rows())).Message);Assert.False(enumerated);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==481&&x.ActionCode=="export").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RequireExportOwnerAsync())).Message);Assert.Equal(2,(await svc.SearchStockOwnerAsync(new())).Total);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==481&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchStockOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_native_catalog_replay_defines_actions_without_grants()
    {
        await using var h=await Seed();await GrantReportCenter(h);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==481).ExecuteDeleteAsync();await h.Db.Sys_MenuActions.Where(x=>x.MenuId==481).ExecuteDeleteAsync();
        foreach(var op in new CP6.Persistence.PostgreSql.Migrations.Core.ReportCenterOwnerCatalogV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {await h.Db.Database.ExecuteSqlRawAsync(op.Sql);await h.Db.Database.ExecuteSqlRawAsync(op.Sql);}
        Assert.Equal(2,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==481));Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==481));
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_actual_program_http_queries_and_all_csv_routes_enforce_current_authority()
    {
        await using var h=await Seed();await GrantReportCenter(h);await SeedReportCenter(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var mode in new[]{"stocks","transactions"})
        {using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/owner-v1/"+mode));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);}
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/owner-v1/transactions?txnType=ADJ")))
        {using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal("-9999999999999.99999999",json.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("qty").GetString());}
        var query="?yearMonth=2026-01&fromDate=2026-01-01&toDate=2026-01-31";
        foreach(var route in new[]{"monthly-stock","abc-analysis","dead-stock","inbound-history","outbound-history"})
        {
            using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/"+route+query));Assert.Equal(HttpStatusCode.Conflict,read.StatusCode);
            using var csv=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/"+route+"/csv"+query));Assert.Equal(HttpStatusCode.ServiceUnavailable,csv.StatusCode);Assert.Contains("REPORT_SNAPSHOT_ADOPTION_UNPROVEN",await csv.Content.ReadAsStringAsync());Assert.True(csv.Headers.CacheControl?.NoStore);
        }
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==481&&x.ActionCode=="export").ExecuteDeleteAsync();
        using(var csv=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/monthly-stock/csv"+query)))Assert.Equal(HttpStatusCode.Forbidden,csv.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==403&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/report-center/owner-v1/transactions"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Report_center_owner_native_fence_blocks_raw_stock_writer_until_read_finishes()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantReportCenter(h);await SeedReportCenter(h);Task<int>? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await db.Stocks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PhysicalQty,2));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE %T_Stock%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        ReportPageV1<ReportStockRowV1>? result=null;try{result=await new ReportCenterService(h.Db,owner).SearchStockOwnerAsync(new());}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.All(result!.Items,x=>Assert.Equal("9999999999999.99999999",x.PhysicalQty));Assert.All(await h.Db.Stocks.Select(x=>x.PhysicalQty).ToArrayAsync(),x=>Assert.Equal(2,x));
    }
}

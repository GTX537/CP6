using System.Net;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantPlanning(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,actions) in new[]{("mes-planning-board",301,new[]{"query","reschedule","arrange"}),
            ("mes-plan-achievement",313,new[]{"query","export"}),("mes-work-order",302,new[]{"query"}),("mes-production-result",304,new[]{"query"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});catalog.RegisterFields(key,[]);catalog.RegisterScope(key,key,[1,2,3,4,5],1);
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static async Task<Guid> SeedPlanning(Harness h)
    {
        var number="PL"+h.Tenant.ToString("N")[..18];
        h.Db.WorkOrders.Add(new(){WorkOrderNo=number,ProductCd="P1",ProductName="private plan",Creator="price-owner",Status=WorkOrderStatus.Completed,
            ProductionQty=200.12345678m,CompletedQty=100.12345678m,DefectQty=1.12345678m,PlanEndDate=DateTime.Today.AddDays(-2),ActualEndDate=DateTime.Today});
        var process=new WorkOrderProcess{WorkOrderNo=number,ProcessCd="P",TaskCd="T",Creator="price-owner",ProcessStatus=0,
            PlanStartTime=DateTime.Today.AddHours(8),PlanEndTime=DateTime.Today.AddHours(10),PlanQty=200.12345678m,GoodQty=100.12345678m};
        h.Db.WorkOrderProcesses.Add(process);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return process.Id;
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_missing_authority_rejects_board_read()
    {
        await using var h=await Seed();await SeedPlanning(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanningBoardService(h.Db).GetBarsAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_missing_authority_rejects_achievement_read()
    {
        await using var h=await Seed();await SeedPlanning(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanAchievementService(h.Db).GetSummaryAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_missing_authority_rejects_export()
    {
        await using var h=await Seed();await SeedPlanning(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanAchievementService(h.Db).ExportCsvAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_legacy_process_status_and_name_do_not_authorize_schedule_effect()
    {
        await using var h=await Seed();var id=await SeedPlanning(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanningBoardService(h.Db).RescheduleAsync(new(){Id=id,PlanStartTime=DateTime.Today.AddHours(11),PlanEndTime=DateTime.Today.AddHours(12),MachineCd="forged"},"planner"));
        Assert.Null(await h.Db.WorkOrderProcesses.Select(p=>p.MachineCd).SingleAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("api/mes/planning-board",false)]
    [InlineData("api/mes/plan-achievement/summary",true)]
    [InlineData("api/mes/plan-achievement/export-csv",true)]
    public async Task Planning_owner_actual_HTTP_requires_independent_current_action(string route,bool post)
    {
        await using var h=await Seed();await SeedPlanning(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,post?HttpMethod.Post:HttpMethod.Get,route,post?new{}:null));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_pg_capture_applies_same_process_status_date_and_parent_filter_to_bars_and_kpi()
    {
        await using var h=await Seed();var owner=await GrantPlanning(h);await SeedPlanning(h);
        var number="PX"+h.Tenant.ToString("N")[..18];
        h.Db.WorkOrders.Add(new(){WorkOrderNo=number,ProductCd="P2",Creator="price-owner",Status=WorkOrderStatus.InProgress,PlanEndDate=DateTime.Today.AddDays(-2),BaseCd="B2"});
        h.Db.WorkOrderProcesses.Add(new(){WorkOrderNo=number,ProcessCd="OTHER",TaskCd="T",Creator="price-owner",ProcessStatus=2,WgCd="WG2",MachineCd="MC2",PlanStartTime=DateTime.Today.AddHours(11),PlanEndTime=DateTime.Today.AddHours(12)});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var service=new PlanningBoardService(h.Db,owner);
        var query=new CP6.Entity.DTOs.Mes.PlanningBoardQuery{ProcessCd="OTHER",Statuses=[2],WgCd="WG2",MachineCd="MC2",BaseCd="B2",DateFrom=DateTime.Today,DateTo=DateTime.Today.AddDays(1)};
        var result=await service.CaptureAsync(query);Assert.Equal(number,Assert.Single(result.Bars).WorkOrderNo);
        Assert.Equal(1,result.Kpi.TotalOrders);Assert.Equal(1,result.Kpi.InProgressOrders);Assert.Equal(1,result.Kpi.DelayedOrders);Assert.Equal(2m,result.Kpi.AvgDelayDays);Assert.Equal(100m,result.Kpi.CompletionRate);
        Assert.Equal("LEGACY_READ_ONLY",result.Mode);Assert.False(result.CanApplySchedule);
        Assert.Single(await service.GetBarsAsync(query));Assert.Equal(1,(await service.GetKpiAsync(query)).TotalOrders);
        query.DateFrom=DateTime.Today.AddDays(2);query.DateTo=DateTime.Today.AddDays(3);result=await service.CaptureAsync(query);
        Assert.Empty(result.Bars);Assert.Equal(0,result.Kpi.TotalOrders);Assert.Equal(0m,result.Kpi.CompletionRate);
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_achievement_exports_lossless_quantity_and_never_spreadsheet_formulas_with_own_query_and_export()
    {
        await using var h=await Seed();var owner=await GrantPlanning(h);await SeedPlanning(h);
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.ProductName,"=1+1"));
        var service=new PlanAchievementService(h.Db,owner);var summary=await service.GetSummaryAsync(new());
        Assert.Equal(100.12345678m,summary.TotalGoodQty);Assert.Equal(200.12345678m,summary.TotalPlannedQty);
        var csv=System.Text.Encoding.UTF8.GetString(await service.ExportCsvAsync(new()));
        Assert.Contains("200.12345678,100.12345678,1.12345678",csv);Assert.Contains("'=1+1",csv);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==313 && a.ActionCode=="export").ExecuteDeleteAsync();
        Assert.Equal(1,(await service.GetSummaryAsync(new())).TotalWorkOrders);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ExportCsvAsync(new()))).Message);
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=313,ActionCode="export"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==313 && a.ActionCode=="query").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ExportCsvAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_requires_actual_department_and_complete_independent_source_before_total_or_export()
    {
        await using var h=await Seed();var owner=await GrantPlanning(h);await SeedPlanning(h);var admin=await MappingAdmin(h);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-planning-board" || s.ResourceKey=="mes-plan-achievement")
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,4).SetProperty(x=>x.CustomDeptIds,admin.Child.ToString()));
        var board=new PlanningBoardService(h.Db,owner);var report=new PlanAchievementService(h.Db,owner);
        Assert.Empty(await board.GetBarsAsync(new()));Assert.Equal(0,(await report.GetSummaryAsync(new())).TotalWorkOrders);
        await BindStandalone(h,admin,"work-order",await h.Db.WorkOrders.Select(w=>w.Id).SingleAsync());
        Assert.Single(await board.GetBarsAsync(new()));Assert.Equal(1,(await report.GetSummaryAsync(new())).TotalWorkOrders);
        var number=await h.Db.WorkOrders.Select(w=>w.WorkOrderNo).SingleAsync();
        h.Db.ProductionResults.Add(new(){WorkOrderNo=number,ResultNo="PR"+h.Tenant.ToString("N")[..18],ProcessCd="P",TaskCd="T",OperatorCd="O",Creator="hidden",GoodQty=3m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-production-result").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Empty(await board.GetBarsAsync(new()));Assert.Equal(0,(await report.GetSummaryAsync(new())).TotalWorkOrders);
        Assert.DoesNotContain("100.12345678",System.Text.Encoding.UTF8.GetString(await report.ExportCsvAsync(new())));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==304 && a.ActionCode=="query").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>board.CaptureAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>report.ExportCsvAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_valid_legacy_actions_cannot_substitute_for_root_and_resource_adoption()
    {
        await using var h=await Seed();var owner=await GrantPlanning(h);var id=await SeedPlanning(h);
        var before=await h.Db.WorkOrderProcesses.AsNoTracking().SingleAsync();var audits=await h.Db.Sys_FieldAuditLogs.CountAsync();
        var service=new PlanningBoardService(h.Db,owner);
        Assert.Equal("SCHED_OWNER_PROTOCOL_NOT_READY",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.RescheduleAsync(new(){Id=id,PlanStartTime=DateTime.Today.AddDays(1),PlanEndTime=DateTime.Today.AddDays(2),MachineCd="MC2"},"forged"))).Message);
        Assert.Equal("SCHED_OWNER_PROTOCOL_NOT_READY",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.AutoArrangeAsync(new(){BaseDate=DateTime.Today.AddDays(1)},"forged"))).Message);
        var after=await h.Db.WorkOrderProcesses.AsNoTracking().SingleAsync();Assert.Equal(before.RowVersion,after.RowVersion);Assert.Equal(before.PlanStartTime,after.PlanStartTime);Assert.Null(after.MachineCd);
        Assert.Equal(audits,await h.Db.Sys_FieldAuditLogs.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_actual_HTTP_captures_exports_and_rechecks_sources_without_caching()
    {
        await using var h=await Seed();await GrantPlanning(h);var id=await SeedPlanning(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var capture=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/planning-board/capture"));
        Assert.Equal(HttpStatusCode.OK,capture.StatusCode);Assert.True(capture.Headers.CacheControl?.NoStore);
        Assert.Contains("LEGACY_READ_ONLY",await capture.Content.ReadAsStringAsync());
        using var csv=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/plan-achievement/export-csv",new{}));
        Assert.Equal(HttpStatusCode.OK,csv.StatusCode);Assert.True(csv.Headers.CacheControl?.NoStore);
        Assert.Contains("200.12345678",await csv.Content.ReadAsStringAsync());
        using var change=await host.Send(OperationsRequest(cookies,HttpMethod.Put,"api/mes/planning-board/reschedule",new{id,planStartTime=DateTime.Today.AddDays(1),planEndTime=DateTime.Today.AddDays(2)}));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,change.StatusCode);Assert.Contains("SCHED_OWNER_PROTOCOL_NOT_READY",await change.Content.ReadAsStringAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==304 && a.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/planning-board/capture"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using var deniedExport=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/plan-achievement/export-csv",new{}));Assert.Equal(HttpStatusCode.Forbidden,deniedExport.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Planning_owner_forward_catalog_keeps_existing_grants_and_does_not_create_authority()
    {
        await using var h=await Seed();await GrantPlanning(h);await using var other=await Seed();
        var existing=await h.Db.Sys_MenuActions.Where(a=>a.MenuId==313 && a.ActionCode=="export").Select(a=>a.Id).SingleAsync();
        var migration=new CP6.Persistence.PostgreSql.Migrations.Core.PlanningOwnerCatalogV1();
        for(var pass=0;pass<2;pass++)
        foreach(var operation in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await h.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        Assert.Equal(existing,await h.Db.Sys_MenuActions.Where(a=>a.MenuId==313 && a.ActionCode=="export").Select(a=>a.Id).SingleAsync());
        foreach(var (menu,action) in new[]{(301,"query"),(313,"query"),(313,"export")})
        {
            Assert.Single(await other.Db.Sys_MenuActions.Where(a=>a.MenuId==menu && a.ActionCode==action).ToArrayAsync());
            Assert.False(await other.Db.Sys_RoleActions.AnyAsync(a=>a.MenuId==menu && a.ActionCode==action));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planning_owner_never_returns_prepared_data_after_final_authority_failure(bool export)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantPlanning(h);await SeedPlanning(h);
        var reads=0;var failed=false;
        hook.Before=c=>
        {
            // All source reads have completed before the repeated Owner authority read.
            if(c.CommandText.Contains("WITH field_subject") && ++reads==(export?5:4))
            {failed=true;throw new IOException("final planning authority unavailable");}
            return Task.CompletedTask;
        };
        var error=export
            ? await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanAchievementService(h.Db,owner).ExportCsvAsync(new()))
            : await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlanningBoardService(h.Db,owner).CaptureAsync(new()));
        Assert.True(failed);Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",error.Message);Assert.Null(h.Db.Database.CurrentTransaction);
    }
}

using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using System.Net;
using System.Text.Json;
using Moq;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantDefect(Harness h)
    {
        var owner=await GrantStocktakeDashboard(h);
        foreach(var action in new[]{"add","edit","del"})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==308 && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=308,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=308,ActionCode=action});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return owner;
    }
    private static DefectRecordService Defects(Harness h,OperationsOwnerV1 owner,IMesNotifier? notifier=null)=>new(h.Db,new MesSequenceService(h.Db),notifier??new NoOpMesNotifier(),owner);
    private static DefectRecordDto DefectInput(string no)=>new(){WorkOrderNo=no,ProcessCd="P",CategoryCd="D07",DetailCd="D1",DefectDescription="observation",DefectQty=1234567890123.12345678m};
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_missing_owner_rejects_reads()
    {
        await using var h=await Seed();
        var service=new DefectRecordService(h.Db,new MesSequenceService(h.Db),new NoOpMesNotifier());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetByNoAsync("missing"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetAllCategoriesAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_missing_owner_rejects_write_before_sequence()
    {
        await using var h=await Seed();var no=await SeedProduction(h);
        var service=new DefectRecordService(h.Db,new MesSequenceService(h.Db),new NoOpMesNotifier());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(new(){WorkOrderNo=no,CategoryCd="D07",DefectDescription="observation"},"forged"));
        Assert.Empty(await h.Db.DefectRecords.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_native_keys_CRUD_precision_creator_and_postcommit_notification()
    {
        await using var h=await Seed();var owner=await GrantDefect(h);var wo=await SeedProduction(h);
        h.Db.DefectCategories.Add(new(){CategoryCd="D07",DetailCd="D1",CategoryName="Category",DetailName="Detail",ActiveFlg=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var notified=0;var notifier=new Mock<IMesNotifier>();notifier.Setup(n=>n.NotifyDefectIssuedAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>())).Returns(async()=>
        {Assert.Null(h.Db.Database.CurrentTransaction);Assert.Single(await h.Db.DefectRecords.AsNoTracking().ToArrayAsync());notified++;throw new IOException("notification unavailable after confirmed commit");});
        var service=Defects(h,owner,notifier.Object);var input=DefectInput(wo.ToLowerInvariant()+" ");input.ProcessCd="p ";input.CategoryCd="d07 ";input.DetailCd="d1 ";
        var no=await service.CreateAsync(input,"forged");Assert.Equal(1,notified);
        var row=(await service.GetByNoAsync(no.ToLowerInvariant()+" "))!;Assert.Equal(input.DefectQty,row.DefectQty);Assert.Equal("FG",row.ProductCd);Assert.Equal("Category",row.CategoryName);Assert.Equal("Detail",row.DetailName);
        Assert.Single(await service.GetAllCategoriesAsync());row.Status=3;await service.UpdateAsync(no.ToLowerInvariant(),row,"forged");
        var saved=await h.Db.DefectRecords.AsNoTracking().SingleAsync();Assert.Equal("price-owner",saved.Creator);Assert.Equal("price-owner",saved.Modifier);Assert.NotNull(saved.CompletedDate);
        await service.DeleteAsync(no,"forged");Assert.Null(await service.GetByNoAsync(no));Assert.Equal(0,(await service.SearchAsync(new())).Total);
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_self_whole_optional_inspection_and_old_new_links_before_paging()
    {
        await using var h=await Seed();var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);
        var qi="QI"+h.Tenant.ToString("N")[..16];
        h.Db.QualityInspections.Add(new(){InspectionNo=qi,WorkOrderNo=wo,ProcessCd="P",InspectorCd="OP",Creator="price-owner"});
        h.Db.QualityInspectionItems.Add(new(){InspectionNo=qi,ItemSeqNo=1,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var input=DefectInput(wo);input.InspectionNo=qi.ToLowerInvariant()+" ";var no=await service.CreateAsync(input,null);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-defect" || s.ResourceKey=="mes-work-order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Equal(1,(await service.SearchAsync(new(){PageSize=1})).Total);
        h.Db.QualityInspectionItems.Add(new(){InspectionNo=qi,ItemSeqNo=2,Creator="other",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(0,(await service.SearchAsync(new(){PageSize=1})).Total);Assert.Null(await service.GetByNoAsync(no));
        Assert.Empty(await new MesDashboardService(h.Db,owner).GetDefectTop5Async());
        input.InspectionNo=null;await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.UpdateAsync(no,input,null));
        await h.Db.QualityInspectionItems.Where(i=>i.ItemSeqNo==2).ExecuteDeleteAsync();
        h.Db.WorkOrders.Add(new(){WorkOrderNo="OTHER",ProductCd="FG",Creator="other"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        input.WorkOrderNo="OTHER";input.ProcessCd=null;await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.UpdateAsync(no,input,null));
        Assert.Equal(wo,await h.Db.DefectRecords.Select(d=>d.WorkOrderNo).SingleAsync());
        input.WorkOrderNo=wo;input.ProcessCd="P";input.InspectionNo="missing";await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.UpdateAsync(no,input,null));
        await h.Db.WorkOrderProcesses.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Creator,"other").SetProperty(p=>p.IsDeleted,true));
        Assert.Null(await service.GetByNoAsync(no));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.DeleteAsync(no,null));
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_current_actions_fields_tenant_and_dirty_context()
    {
        await using var h=await Seed();var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);var no=await service.CreateAsync(DefectInput(wo),null);
        await using var other=await Seed();var otherOwner=await GrantDefect(other);Assert.Null(await Defects(other,otherOwner).GetByNoAsync(no));
        await using(var tx=await h.Db.Database.BeginTransactionAsync())await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetByNoAsync(no));
        await h.Db.QualityInspections.AddAsync(new(){InspectionNo="UNSAVED",WorkOrderNo=wo,InspectorCd="OP"});await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetByNoAsync(no));h.Db.ChangeTracker.Clear();
        await h.Db.DefectRecords.SingleAsync();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.DeleteAsync(no,null));h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchAsync(new()))).Message);
        Assert.Single(await h.Db.DefectRecords.ToArrayAsync());h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==308 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetAllCategoriesAsync())).Message);
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=308,ActionCode="query"});h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="mes-defect",FieldName="unknown",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetAllCategoriesAsync());await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
    }
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Defect_owner_final_revoke_or_lost_ack_is_atomic_and_never_publishes(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);var owner=await GrantDefect(h);var wo=await SeedProduction(h);
        var notifier=new Mock<IMesNotifier>();var service=Defects(h,owner,notifier.Object);var input=DefectInput(wo);var reads=0;
        var priorSequence=await h.Db.MesSequences.Where(s=>s.SeqKey=="DF").Select(s=>(int?)s.CurrentValue).SingleOrDefaultAsync()??0;
        if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++reads==3)throw new IOException("final defect authority unavailable");return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(input,"forged"));hook.Before=null;
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?1:0,await h.Db.DefectRecords.CountAsync());Assert.Equal(priorSequence+(lostAck?1:0),await h.Db.MesSequences.Where(s=>s.SeqKey=="DF").Select(s=>(int?)s.CurrentValue).SingleOrDefaultAsync()??0);
        notifier.Verify(n=>n.NotifyDefectIssuedAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>()),Times.Never);
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_freezes_input_and_query_lists_before_first_authority_await()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);var input=DefectInput(wo);
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;input.WorkOrderNo="forged";input.DefectQty=0;input.CategoryCd="changed";}return Task.CompletedTask;};
        var no=await service.CreateAsync(input,null);var saved=(await service.GetByNoAsync(no))!;Assert.Equal(wo,saved.WorkOrderNo);Assert.Equal(1234567890123.12345678m,saved.DefectQty);Assert.Equal("D07",saved.CategoryCd);
        var query=new DefectRecordSearchQuery{Statuses=[0]};
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;query.Statuses[0]=3;query.PageIndex=99;}return Task.CompletedTask;};
        Assert.Single((await service.SearchAsync(query)).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_department_uses_trusted_order_and_source_scopes_independently()
    {
        await using var h=await Seed();var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);var no=await service.CreateAsync(DefectInput(wo),null);
        var mapping=await MappingAdmin(h);await SaveMapping(h,mapping,await MappingCommand(h,mapping));
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"sales "));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,mapping.Root));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-defect" || s.ResourceKey=="mes-work-order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,3));
        Assert.Single((await service.SearchAsync(new())).Items);await service.UpdateAsync(no,DefectInput(wo),null);
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.WebOrderNo,(string?)null));
        Assert.Empty((await service.SearchAsync(new())).Items);await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.DeleteAsync(no,null));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-defect").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,5));
        Assert.Null(await service.GetByNoAsync(no)); // The source scope still denies unmapped work orders.
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_actual_final_action_revoke_rolls_back_update_and_preserves_creator()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);var no=await service.CreateAsync(DefectInput(wo),null);
        var input=DefectInput(wo);input.DefectDescription="must roll back";var reads=0;
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject") || ++reads!=3)return;hook.Before=null;
            await h.Db.Sys_RoleActions.Where(a=>a.MenuId==308 && a.ActionCode=="edit").ExecuteDeleteAsync();
        };
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.UpdateAsync(no,input,"forged"))).Message);
        Assert.Equal(3,reads);var saved=await h.Db.DefectRecords.AsNoTracking().SingleAsync();Assert.Equal("observation",saved.DefectDescription);Assert.Equal("price-owner",saved.Creator);
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync(a=>a.MenuId==308 && a.ActionCode=="edit"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_serializes_raw_inspection_graph_changes_and_denies_next_intent()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantDefect(h);var wo=await SeedProduction(h);var service=Defects(h,owner);
        var qi="QI"+h.Tenant.ToString("N")[..16];
        h.Db.QualityInspections.Add(new(){InspectionNo=qi,WorkOrderNo=wo,ProcessCd="P",InspectorCd="OP",Creator="price-owner"});
        h.Db.QualityInspectionItems.Add(new(){InspectionNo=qi,ItemSeqNo=1,Creator="price-owner"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var input=DefectInput(wo);input.ProcessCd=null;input.InspectionNo=qi.ToLowerInvariant()+" ";var no=await service.CreateAsync(input,null); // Optional defect process remains optional.
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-defect").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Task? writerTask=null;var observed=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writerTask=Task.Run(async()=>
            {
                await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                await writer.Database.ExecuteSqlInterpolatedAsync($"/* W40 quality graph */ UPDATE public.\"T_QualityInspectionItem\" SET \"Creator\"='other',\"IsDeleted\"=true WHERE \"TenantId\"={h.Tenant} AND \"InspectionNo\"={qi}");
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                    AND wait_event_type='Lock' AND query LIKE '/* W40 quality graph */ UPDATE%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            observed=true;Assert.False(writerTask.IsCompleted);
        };
        try{Assert.NotNull(await service.GetByNoAsync(no));}finally{if(writerTask!=null)await writerTask;}
        Assert.True(observed);Assert.Null(await service.GetByNoAsync(no));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.DeleteAsync(no,null));
    }
    [CurrentPermissionPostgresFact]
    public async Task Defect_owner_actual_Program_GETs_query_gate_CSRF_CRUD_and_current_session()
    {
        await using var h=await Seed();var wo=await SeedProduction(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var path in new[]{"api/mes/defects","api/mes/defects/missing","api/mes/defects/categories"})
        {using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        await GrantDefect(h);
        using(var csrf=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/defects",DefectInput(wo),csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        string no;
        using(var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/defects",DefectInput(wo))))
        {Assert.True(create.StatusCode==HttpStatusCode.OK,await OperationsFailure(create));no=JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("defectNo").GetString()!;}
        foreach(var path in new[]{"api/mes/defects","api/mes/defects/"+no,"api/mes/defects/categories"})
        {using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.True(read.StatusCode==HttpStatusCode.OK,await OperationsFailure(read));Assert.True(read.Headers.CacheControl?.NoStore);}
        using(var edit=await host.Send(OperationsRequest(cookies,HttpMethod.Put,"api/mes/defects/"+no,DefectInput(wo))))Assert.True(edit.StatusCode==HttpStatusCode.OK,await OperationsFailure(edit));
        using(var remove=await host.Send(OperationsRequest(cookies,HttpMethod.Delete,"api/mes/defects/"+no)))Assert.True(remove.StatusCode==HttpStatusCode.OK,await OperationsFailure(remove));
        using(var absent=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/defects/"+no)))Assert.Equal(HttpStatusCode.NotFound,absent.StatusCode);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Put,"api/mes/defects/"+no,DefectInput(wo))))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/defects/categories"));Assert.Equal(HttpStatusCode.Unauthorized,old.StatusCode);
    }
}

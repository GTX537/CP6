using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Entity.DomainModels.Mes;
using System.Net;
using System.Text.Json;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantQuality(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var (key,id,actions,fields) in new[]{
            ("wms-stock-qc",429,new[]{"set"},Array.Empty<string>()),
            ("wms-qc-inspection",421,new[]{"query","add","edit","judge","cancel"},Array.Empty<string>()),
            ("mes-quality-inspection",306,new[]{"query","add","edit"},Array.Empty<string>()),
            ("mes-work-order",302,new[]{"query"},Array.Empty<string>()),
            ("wms-stock",403,new[]{"query"},new[]{"UnitPrice"}),
            ("wms-inbound-order",405,new[]{"query","add","confirm"},new[]{"UnitPrice"}),
            ("wms-inbound-receipt",406,new[]{"query","post"},new[]{"UnitPrice"})})
        {
            var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuId==id);
            if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=id,MenuKey=key,MenuName=key});else menu.MenuKey=key;
            foreach(var action in actions)
            {
                if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==id && a.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=id,ActionCode=action,ActionName=action});
                h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=id,ActionCode=action});
            }
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
            catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));
        }
        h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="*",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static InboundService QualityInbound(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),owner:owner);
    private static QcInspectionService InboundQuality(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new WmsSequenceService(h.Db),QualityInbound(h,owner),owner);
    private static QualityInspectionService MesQuality(Harness h,OperationsOwnerV1 owner)=>new(h.Db,new MesSequenceService(h.Db),new StockQcService(h.Db,NullLogger<StockQcService>.Instance,owner),owner:owner);

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_missing_stock_QC_owner_rejects_write()
    {
        await using var h=await Seed();await SeedInventory(h);
        var service=new StockQcService(h.Db,NullLogger<StockQcService>.Instance);
        var id=await h.Db.Stocks.Select(s=>s.Id).FirstAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SetStockQcStatusAsync(id,StockQcStatus.Failed,"reason","forged"));
        Assert.False(await h.Db.Stocks.AnyAsync(s=>s.QcStatus==StockQcStatus.Failed));
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_missing_inbound_QC_owner_rejects_read()
    {
        await using var h=await Seed();var seq=new WmsSequenceService(h.Db);
        var service=new QcInspectionService(h.Db,seq,new InboundService(h.Db,seq,new StockMovementService(h.Db,seq)));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetAsync("missing"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_missing_MES_quality_owner_rejects_read()
    {
        await using var h=await Seed();var service=new QualityInspectionService(h.Db,new MesSequenceService(h.Db));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetByNoAsync("missing"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.GetAllTemplateCodesAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_direct_inspection_judge_receipt_native_precision_and_duplicate_denial()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var service=InboundQuality(h,owner);
        var quantity=1.12345678m;
        var no=await service.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=quantity,ExpectedQty=quantity}]} ,"forged");
        Assert.Single(await service.SearchAsync(new()));Assert.NotNull(await service.GetAsync(no.ToLowerInvariant()+" "));
        await service.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=quantity,AcceptedQty=quantity}],"forged");
        var result=await service.JudgeAsync(no.ToLowerInvariant(),new(){FinalJudgement="PASS",AcceptWarehouseCd="W01",AcceptLocations=["L1"]},"forged");
        Assert.NotNull(result.GeneratedReceiptNo);var stock=await h.Db.Stocks.AsNoTracking().SingleAsync();Assert.Equal(quantity,stock.PhysicalQty);Assert.Equal("price-owner",stock.Creator);
        Assert.Equal(result.GeneratedReceiptNo,(await service.GetAsync(no))!.GeneratedReceiptNo);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.JudgeAsync(no,new(){AcceptWarehouseCd="W01"},null));
        Assert.Single(await h.Db.InboundReceipts.ToArrayAsync());h.Db.ChangeTracker.Clear();
        var single=new StockQcService(h.Db,NullLogger<StockQcService>.Instance,owner);
        await single.SetStockQcStatusAsync(stock.Id,StockQcStatus.Hold,null,"forged");Assert.Equal("price-owner",await h.Db.Stocks.Select(s=>s.Modifier).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_MES_failure_creates_defect_and_marks_only_exact_linked_stock()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var wo=await SeedProduction(h);
        var receipt="R"+h.Tenant.ToString("N")[..16];
        h.Db.InboundReceipts.Add(new(){ReceiptNo=receipt,WorkOrderNo=wo,WarehouseCd="W01",Status=InboundReceiptStatus.Confirmed,Creator="price-owner"});
        h.Db.InboundReceiptDetails.Add(new(){ReceiptNo=receipt,LineNo=1,ProductCd="FG",LotNo="LOT",LocationCd="L1",ReceivedQty=3m,Creator="price-owner"});
        h.Db.Stocks.AddRange(new Stock{WarehouseCd="W01",LocationCd="L1",ProductCd="FG",LotNo="LOT",PhysicalQty=3m,Creator="price-owner"},
            new Stock{WarehouseCd="W02",LocationCd="L1",ProductCd="FG",LotNo="LOT",PhysicalQty=3m,Creator="price-owner"},
            new Stock{WarehouseCd="W01",LocationCd="L2",ProductCd="FG",LotNo="LOT",PhysicalQty=3m,Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var service=MesQuality(h,owner);
        var input=new QualityInspectionDto{WorkOrderNo=wo.ToLowerInvariant()+" ",ProcessCd="p ",InspectorCd="OP",OverallResult=2,DispositionAction=1,InspectionQty=3.12345678m,Items=[new(){ItemSeqNo=1,MeasuredValue=1.1234m,Result=2}]};
        var no=await service.CreateAsync(input,"forged");var defect=await h.Db.DefectRecords.AsNoTracking().SingleAsync();Assert.Equal(no,defect.InspectionNo);Assert.Equal(input.InspectionQty,defect.DefectQty);Assert.Equal("price-owner",defect.Creator);
        Assert.Equal(1,await h.Db.Stocks.CountAsync(s=>s.QcStatus==StockQcStatus.Failed));
        Assert.True(await h.Db.Stocks.AnyAsync(s=>s.WarehouseCd=="W01" && s.LocationCd=="L1" && s.QcStatus==StockQcStatus.Failed));
        var read=await service.GetByNoAsync(no);Assert.Equal("FG",read!.ProductCd);Assert.Single(read.Items);
        Assert.Equal(1,(await service.SearchAsync(new())).Total);Assert.Equal(1,(await service.SearchAsync(new())).Items[0].ItemCount);
        await service.UpdateAsync(no,read,"forged");Assert.Equal("price-owner",await h.Db.QualityInspections.Select(q=>q.Creator).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_receipt_fault_rolls_back_judgement_stock_and_original_state()
    {
        var fault=new FailSecondInbound();await using var h=await Seed(fault);var owner=await GrantQuality(h);var service=InboundQuality(h,owner);
        var no=await service.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=2m},new(){ProductCd="P2",ReceivedQty=3m}]},null);
        await service.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=2m,AcceptedQty=2m},new(){LineNo=2,ReceivedQty=3m,AcceptedQty=3m}],null);
        fault.Enabled=true;
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.JudgeAsync(no,new(){AcceptWarehouseCd="W01",AcceptLocations=["L1","L2"]},null));
        Assert.False(fault.Enabled);var header=(await service.GetAsync(no))!;
        Assert.Equal(QcInspectionStatus.Inspecting,header.Status);Assert.Null(header.FinalJudgement);Assert.Null(header.GeneratedReceiptNo);
        Assert.Empty(await h.Db.Stocks.ToArrayAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_self_scope_checks_soft_deleted_children_and_source_permissions()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var inbound=InboundQuality(h,owner);var no=await inbound.CreateDirectAsync(new(){Items=[new(){ProductCd="P"}]},null);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="wms-qc-inspection").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        h.Db.QcInspectionItems.Add(new(){InspectionNo=no,LineNo=2,ProductCd="HIDDEN",Creator="other",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty(await inbound.SearchAsync(new(){PageSize=1}));Assert.Null(await inbound.GetAsync(no));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>inbound.CancelAsync(no,null));
        var wo=await SeedProduction(h);var mes=MesQuality(h,owner);var qi=await mes.CreateAsync(new(){WorkOrderNo=wo,InspectorCd="OP",OverallResult=1},null);
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-quality-inspection").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        h.Db.QualityInspectionItems.Add(new(){InspectionNo=qi,ItemSeqNo=9,Creator="other",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(0,(await mes.SearchAsync(new(){PageSize=1})).Total);Assert.Null(await mes.GetByNoAsync(qi));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==302 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>mes.SearchAsync(new()))).Message);
        await using var foreign=await Seed();var other=await GrantQuality(foreign);Assert.Null(await InboundQuality(foreign,other).GetAsync(no));Assert.Null(await MesQuality(foreign,other).GetByNoAsync(qi));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quality_owners_final_failure_and_lost_ack_preserve_actual_outcome(bool lostAck)
    {
        var hook=new CommandHook();var commit=new CommitFault();await using var h=await Seed(hook,commit);var owner=await GrantQuality(h);var service=InboundQuality(h,owner);
        var no=await service.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=1m}]},null);
        await service.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=1m,AcceptedQty=1m}],null);
        if(lostAck)commit.Armed=true;
        else hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && h.Db.ChangeTracker.Entries<InboundReceipt>().Any()){hook.Before=null;throw new IOException("final authority read failed");}return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.JudgeAsync(no,new(){AcceptWarehouseCd="W01",AcceptLocations=["L1"]},null));
        Assert.Equal(lostAck?"OPERATIONS_OUTCOME_UNKNOWN":"AUTHORITY_FENCE_UNAVAILABLE",error.Message);
        Assert.Equal(lostAck?1:0,await h.Db.InboundReceipts.CountAsync());Assert.Equal(lostAck?1:0,await h.Db.Stocks.CountAsync());
        Assert.Equal(lostAck?QcInspectionStatus.Judged:QcInspectionStatus.Inspecting,await h.Db.QcInspections.Select(q=>q.Status).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_actual_HTTP_permissions_CSRF_read_cache_and_old_session()
    {
        await using var h=await Seed();var wo=await SeedProduction(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var path in new[]{"api/wms/qc-inspection","api/wms/qc-inspection/missing","api/mes/inspections","api/mes/inspections/missing","api/mes/inspections/templates","api/mes/inspections/templates/T"})
        {using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        await GrantQuality(h);var input=new QcInspectionDto{Items=[new(){ProductCd="P1",ReceivedQty=1m}]};
        using(var csrf=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/qc-inspection",input,csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        string number;
        using(var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/qc-inspection",input)))
        {Assert.True(create.StatusCode==HttpStatusCode.OK,await OperationsFailure(create));number=JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("inspectionNo").GetString()!;}
        foreach(var path in new[]{"api/wms/qc-inspection","api/wms/qc-inspection/"+number,"api/mes/inspections","api/mes/inspections/templates"})
        {using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.True(read.StatusCode==HttpStatusCode.OK,await OperationsFailure(read));Assert.True(read.Headers.CacheControl?.NoStore);}
        using(var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/mes/inspections",new QualityInspectionDto{WorkOrderNo=wo,InspectorCd="OP",OverallResult=1})))Assert.True(create.StatusCode==HttpStatusCode.OK,await OperationsFailure(create));
        await SeedInventory(h);var stock=await h.Db.Stocks.Select(s=>s.Id).FirstAsync();
        using(var set=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"api/wms/stock-qc/{stock}/set",new{newStatus="HOLD"})))Assert.True(set.StatusCode==HttpStatusCode.OK,await OperationsFailure(set));
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==429).ExecuteDeleteAsync();
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,$"api/wms/stock-qc/{stock}/set",new{newStatus="FAILED"})))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/inspections/templates"));Assert.Equal(HttpStatusCode.Unauthorized,old.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_MES_missing_shared_stock_writer_rolls_back_inspection_defect_and_sequence()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var wo=await SeedProduction(h);
        var before=await h.Db.MesSequences.Where(s=>s.SeqKey=="QC" || s.SeqKey=="DF").SumAsync(s=>(int?)s.CurrentValue)??0;
        var service=new QualityInspectionService(h.Db,new MesSequenceService(h.Db),owner:owner);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(new(){WorkOrderNo=wo,InspectorCd="OP",OverallResult=2,DispositionAction=1},null));
        Assert.Empty(await h.Db.QualityInspections.ToArrayAsync());Assert.Empty(await h.Db.DefectRecords.ToArrayAsync());
        Assert.Equal(before,await h.Db.MesSequences.Where(s=>s.SeqKey=="QC" || s.SeqKey=="DF").SumAsync(s=>(int?)s.CurrentValue)??0);
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_final_revoke_is_atomic_and_parent_child_authorities_are_rechecked()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantQuality(h);var service=InboundQuality(h,owner);
        var no=await service.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=1m}]},null);
        await service.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=1m,AcceptedQty=1m}],null);
        var fired=false;
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject") || !h.Db.ChangeTracker.Entries<InboundReceipt>().Any())return;
            hook.Before=null;fired=true;await h.Db.Sys_RoleActions.Where(a=>a.MenuId==406 && a.ActionCode=="post").ExecuteDeleteAsync();
        };
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.JudgeAsync(no,new(){AcceptWarehouseCd="W01",AcceptLocations=["L1"]},null))).Status);
        Assert.True(fired);Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());Assert.Empty(await h.Db.Stocks.ToArrayAsync());
        Assert.True(await h.Db.Sys_RoleActions.AnyAsync(a=>a.MenuId==406 && a.ActionCode=="post"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_freezes_request_and_only_publishes_after_confirmed_commit()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantQuality(h);var notifications=0;
        var notifier=new Mock<IWmsNotifier>();notifier.Setup(n=>n.NotifyInboundReceivedAsync(It.IsAny<string>(),It.IsAny<string>())).Returns(()=>
        {Assert.Null(h.Db.Database.CurrentTransaction);notifications++;throw new IOException("best effort notification failed");});
        var inbound=new InboundService(h.Db,new WmsSequenceService(h.Db),new StockMovementService(h.Db,new WmsSequenceService(h.Db)),notifier.Object,owner);
        var service=new QcInspectionService(h.Db,new WmsSequenceService(h.Db),inbound,owner);
        var input=new QcInspectionDto{Items=[new(){ProductCd="P1",ReceivedQty=1.12345678m}]};
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;input.Items[0].ProductCd="forged";input.Items.Clear();}return Task.CompletedTask;};
        var no=await service.CreateDirectAsync(input,null);Assert.Single((await service.GetAsync(no))!.Items);
        await service.SaveItemsAsync(no,[new(){LineNo=1,ReceivedQty=1.12345678m,AcceptedQty=1.12345678m}],null);
        var request=new JudgeRequest{AcceptWarehouseCd="W01",AcceptLocations=["L1"]};
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")){hook.Before=null;request.AcceptWarehouseCd="OTHER";request.AcceptLocations[0]="OTHER";}return Task.CompletedTask;};
        var result=await service.JudgeAsync(no,request,null);Assert.NotNull(result.GeneratedReceiptNo);Assert.Equal(1,notifications);
        Assert.Equal("W01",await h.Db.Stocks.Select(s=>s.WarehouseCd).SingleAsync());Assert.Equal("L1",await h.Db.Stocks.Select(s=>s.LocationCd).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_linked_QC_needs_source_but_direct_QC_does_not()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var inbound=QualityInbound(h,owner);var qc=InboundQuality(h,owner);
        var order=await inbound.CreateOrderAsync(Plan(),null);await inbound.ConfirmOrderAsync(order,null);
        var linked=await qc.CreateFromInboundAsync(order.ToLowerInvariant()+" ","forged");
        Assert.Equal(5.12345678m,(await qc.GetAsync(linked))!.Items.Single().ReceivedQty);
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==405 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Null(await qc.GetAsync(linked));Assert.Empty(await qc.SearchAsync(new()));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>qc.CancelAsync(linked,null));
        var direct=await qc.CreateDirectAsync(new(){Items=[new(){ProductCd="P1",ReceivedQty=1m}]},null);
        await qc.SaveItemsAsync(direct,[new(){LineNo=1,ReceivedQty=1m,AcceptedQty=1m}],null);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="W02",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>qc.JudgeAsync(direct,new(){AcceptWarehouseCd="W01",AcceptLocations=["L1"]},null));
        Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());Assert.Equal(QcInspectionStatus.Inspecting,(await qc.GetAsync(direct))!.Status);
        await qc.CancelAsync(direct,null);Assert.Equal(QcInspectionStatus.Cancelled,(await qc.GetAsync(direct))!.Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_update_preserves_existing_child_owner_and_related_defect_graph()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var wo=await SeedProduction(h);var service=MesQuality(h,owner);
        var no=await service.CreateAsync(new(){WorkOrderNo=wo,InspectorCd="OP",OverallResult=2,DispositionAction=1,Items=[new(){ItemSeqNo=1,Result=2}]},null);
        await h.Db.QualityInspectionItems.Where(i=>i.InspectionNo==no).ExecuteUpdateAsync(s=>s.SetProperty(i=>i.Creator,(string?)null));
        var dto=(await service.GetByNoAsync(no))!;dto.Items[0].Remarks="edited";await service.UpdateAsync(no,dto,"forged");
        Assert.Null(await h.Db.QualityInspectionItems.Select(i=>i.Creator).SingleAsync());
        var alternate=wo+"B";h.Db.WorkOrders.Add(new(){WorkOrderNo=alternate,ProductCd="FG",WebOrderNo=h.Number,Creator="price-owner"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        dto.WorkOrderNo=alternate;
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.UpdateAsync(no,dto,null));
        Assert.Equal(wo,await h.Db.QualityInspections.Select(i=>i.WorkOrderNo).SingleAsync());
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey=="mes-quality-inspection").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        Assert.Null(await service.GetByNoAsync(no));
    }

    [CurrentPermissionPostgresFact]
    public async Task Quality_owners_manual_stock_QC_requires_all_linked_objects_and_inventory_source()
    {
        await using var h=await Seed();var owner=await GrantQuality(h);var wo=await SeedProduction(h);
        h.Db.InboundReceipts.Add(new(){ReceiptNo="R",WorkOrderNo=wo,WarehouseCd="W01",Status=InboundReceiptStatus.Confirmed,Creator="price-owner"});
        h.Db.InboundReceiptDetails.Add(new(){ReceiptNo="R",LineNo=1,ProductCd="P1",LotNo="LOT",LocationCd="L1",Creator="price-owner"});
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L1",ProductCd="P1",LotNo="LOT",Creator="price-owner"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var stock=new StockQcService(h.Db,NullLogger<StockQcService>.Instance,owner);
        Assert.Equal(1,await stock.MarkLinkedStockByWorkOrderAsync(wo.ToLowerInvariant()+" ",StockQcStatus.Hold,null,"forged"));
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();h.Db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="W02",AreaCd="*"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>stock.MarkLinkedStockByWorkOrderAsync(wo,StockQcStatus.Failed,null,null));
        Assert.Equal(StockQcStatus.Hold,await h.Db.Stocks.Select(s=>s.QcStatus).SingleAsync());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==403 && a.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>stock.MarkLinkedStockByWorkOrderAsync(wo,StockQcStatus.Passed,null,null))).Message);
    }
}

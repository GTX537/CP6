using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.Http;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(OperationsOwnerV1 Authority,ToolOrderLinkOwnerV1 Service)> GrantToolLink(Harness h)
    {
        var authority=await GrantPlateSource(h); await GrantOrderWrites(h);
        if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==215 && a.ActionCode=="edit"))
            h.Db.Sys_MenuActions.Add(new(){MenuId=215,ActionCode="edit",ActionName="edit"});
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=215,ActionCode="edit"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var http=new OperationsHttp{HttpContext=h.Http};
        var reader=new OrderReadOwnerV1(h.Db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var writer=new OrderWriteOwnerV1(h.Db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var orders=new OrderService(h.Db,h.Workflow,new NoOpWmsBridgeHook(),readOwner:reader,writeOwner:writer);
        return (authority,new(h.Db,authority,orders,reader));
    }
    private static async Task<ToolOrderLinkCommandV1> ToolCommand(Harness h,OperationsOwnerV1 authority,Guid id)
    {
        var source=await new PlateMoldSourceOwnerV1(h.Db,authority).ReadAsync(id);
        return new(Guid.NewGuid(),source.Reference,source.CurrentNativeRowVersion);
    }
    private static Task<int> ToolLinkCount(Harness h)=>h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_tool_order_link_v1.links WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_creates_actual_order_with_full_source_identity_and_exact_money_once()
    {
        await using var h=await Seed();var (a,service)=await GrantToolLink(h);var id=await SeedPlate(h);
        var command=await ToolCommand(h,a,id);var result=await service.CreateOrLinkAsync(command);
        Assert.Equal("ORDER_CREATED",result.Effect);Assert.Equal("NOT_EXECUTED",result.PeState);Assert.Equal("NATIVE_ORDER_LINK_ONLY",result.SourceKind);
        Assert.Equal(command.Tool,result.Tool);Assert.NotEqual(command.ExpectedNativeRowVersion,result.NativeToolRowVersion);
        var line=await h.Db.OrderDetails.AsNoTracking().SingleAsync(d=>d.Id==result.Order.LineId);
        var order=await h.Db.Orders.AsNoTracking().SingleAsync(o=>o.Id==result.Order.OrderId);
        Assert.Equal("30",order.OrderType);Assert.Equal("61",line.ProductCatBig);Assert.Equal(NativePlate().DecisionAmount,line.IndividualUnitPrice);
        Assert.Equal(NativePlate().DecisionAmount,line.Amount);Assert.Equal(1m,line.Quantity);Assert.Equal("price-owner",line.Creator);
        Assert.Equal(h.Tenant.ToString("N")[..15],line.ProductCd.TrimEnd());Assert.Equal(0,line.ApprovalStatus);Assert.False(line.McTransferFlg);
        Assert.Equal(result,await service.CreateOrLinkAsync(command));Assert.Equal(2,await h.Db.Orders.CountAsync());Assert.Equal(1,await ToolLinkCount(h));
        Assert.True((await service.ReadAsync(id)).CurrentApplicable);Assert.Equal(0,h.Workflow.Calls);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateOrLinkAsync(command with{OrderDepartment="X"}))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_same_truncated_code_never_merges_distinct_native_tools()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var first=await SeedPlate(h);var second=await SeedPlate(h,"B");
        var x=await s.CreateOrLinkAsync(await ToolCommand(h,a,first));var y=await s.CreateOrLinkAsync(await ToolCommand(h,a,second));
        Assert.NotEqual(x.Order.OrderId,y.Order.OrderId);Assert.NotEqual(x.Order.LineId,y.Order.LineId);Assert.Equal(2,await ToolLinkCount(h));
        Assert.Equal(1,await h.Db.OrderDetails.Where(d=>d.Id==x.Order.LineId || d.Id==y.Order.LineId).Select(d=>d.ProductCd).Distinct().CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_missing_ambiguous_and_shared_native_links_never_recreate()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);
        await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.ArrangeNo,"missing"));
        Assert.Equal("NATIVE_ORDER_LINK_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(async()=>await s.CreateOrLinkAsync(await ToolCommand(h,a,id)))).Message);
        await h.Db.OrderDetails.ExecuteUpdateAsync(v=>v.SetProperty(d=>d.HaibaiNo1,"missing"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(async()=>await s.CreateOrLinkAsync(await ToolCommand(h,a,id)));
        await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==2).ExecuteUpdateAsync(v=>v.SetProperty(d=>d.HaibaiNo1,"other"));
        var shared=await SeedPlate(h,"B");await h.Db.PlateMolds.Where(p=>p.Id==shared).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.ArrangeNo,"missing"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(async()=>await s.CreateOrLinkAsync(await ToolCommand(h,a,id)));
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_records_existing_native_order_without_add_permission_or_rewriting_it()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);
        await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==1).ExecuteUpdateAsync(v=>v.SetProperty(d=>d.HaibaiNo1,"existing-native"));
        await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.ArrangeNo,"existing-native"));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==32622 && x.ActionCode=="add").ExecuteDeleteAsync();
        var before=await h.Db.OrderDetails.AsNoTracking().SingleAsync(d=>d.WebOrderDetailNo==1);
        var result=await s.CreateOrLinkAsync(await ToolCommand(h,a,id));Assert.Equal("NATIVE_LINK_RECORDED",result.Effect);
        Assert.Equal(before.Id,result.Order.LineId);Assert.Equal(Convert.ToBase64String(before.RowVersion),result.Order.LineRowVersion);
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(1,await ToolLinkCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_requires_original_create_fields_and_read_and_current_tool_edit()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="UnitPrice",Access=2});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Message);
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==32622 && x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Status);
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==215 && x.ActionCode=="edit").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_native_and_technical_version_conflicts_do_not_create_orders()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.DecisionAmount,9m));
        Assert.Equal("TOOL_NATIVE_VERSION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Message);
        var fresh=await ToolCommand(h,a,id);await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.SheetWidth,4m));
        Assert.Equal("TOOL_SOURCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(fresh))).Message);
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_lost_commit_response_recovers_exact_original_and_close_blocks_late_submit()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Message);
        var operation=await s.OperationAsync(command.OperationId,false);Assert.Equal("COMMITTED",operation.State);Assert.NotNull(operation.Result);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==215 && x.ActionCode=="edit" || x.MenuId==32622 && x.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal(operation.Result,await s.CreateOrLinkAsync(command));Assert.Equal(2,await h.Db.Orders.CountAsync());
        var unknown=Guid.NewGuid();Assert.Equal("UNKNOWN",(await s.OperationAsync(unknown,false)).State);Assert.Equal("CLOSED",(await s.OperationAsync(unknown,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command with{OperationId=unknown}))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_recovery_rechecks_current_order_scope_and_hides_all_amounts()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);var result=await s.CreateOrLinkAsync(command);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="Amount",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var json=JsonSerializer.Serialize(await s.OperationAsync(command.OperationId,false));Assert.DoesNotContain("1234.5678",json);Assert.DoesNotContain("private-customer",json);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="order").ExecuteUpdateAsync(v=>v.SetProperty(x=>x.ScopeType,1));
        await h.Db.OrderDetails.Where(d=>d.Id==result.Order.LineId).ExecuteUpdateAsync(v=>v.SetProperty(d=>d.Creator,"someone-else"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.OperationAsync(command.OperationId,false))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.ReadAsync(id))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_changed_native_pointer_never_recreates_and_keeps_original_receipt()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);var result=await s.CreateOrLinkAsync(command);
        await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.ArrangeNo,(string?)null));
        Assert.False((await s.ReadAsync(id)).CurrentApplicable);Assert.Equal(result,(await s.OperationAsync(command.OperationId,false)).Result);
        Assert.Equal("TOOL_ORDER_LINK_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(async()=>await s.CreateOrLinkAsync(await ToolCommand(h,a,id)))).Message);
        Assert.Equal(2,await h.Db.Orders.CountAsync());Assert.Equal(1,await ToolLinkCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_wrong_tenant_and_unmapped_order_department_roll_back_all_effects()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        await using var other=await Seed();var(_,foreign)=await GrantToolLink(other);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>foreign.CreateOrLinkAsync(command))).Status);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="order").ExecuteUpdateAsync(v=>v.SetProperty(x=>x.ScopeType,4));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command with{OrderDepartment="UNMAPPED"}))).Status);
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_final_permission_failure_rolls_back_order_native_pointer_and_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        var reached=false;
        hook.Before=async c=>{if(!c.CommandText.Contains("WITH field_subject"))return;
            // Only fail after the real association and journal have been inserted in this transaction.
            await using var probe=c.Connection!.CreateCommand();probe.Transaction=c.Transaction;probe.CommandText=$"SELECT count(*) FROM cp6_tool_order_link_v1.links WHERE tenant_id='{h.Tenant}'";
            if(Convert.ToInt32(await probe.ExecuteScalarAsync())>0){reached=true;throw new IOException("final original Order authority unavailable");}};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateOrLinkAsync(command))).Status);hook.Before=null;
        Assert.True(reached);Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
        Assert.Null(await h.Db.PlateMolds.Where(p=>p.Id==id).Select(p=>p.ArrangeNo).SingleAsync());Assert.Equal("UNKNOWN",(await s.OperationAsync(command.OperationId,false)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_participants_reject_foreign_context_and_query_only_creation()
    {
        await using var h=await Seed();var(a,_)=await GrantToolLink(h);await using var other=await Seed();await GrantToolLink(other);
        var dto=NewOrder();dto.OrderType="30";
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>a.ExecuteAsync(OperationsOwnerV1.PlateMold,"edit",true,
            (root,ct)=>other.Service.CreateToolLinkedAsync(root,dto,ct)))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>a.ExecuteAsync(OperationsOwnerV1.PlateMold,"query",false,
            (root,ct)=>h.Service.CreateToolLinkedAsync(root,dto,ct)))).Status);
        Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(1,await other.Db.Orders.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_HTTP_requires_current_original_authority()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host,h);
        using var read = await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/plate-molds/order-link-owner-v1/scope"));
        Assert.Equal(HttpStatusCode.Forbidden,read.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_actual_HTTP_creates_and_recovers_with_csrf_and_revocation()
    {
        await using var h=await Seed();var(a,_)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        HttpRequestMessage Request(HttpMethod method,string path,bool csrf=true)
        {
            var req=new HttpRequestMessage(method,path);if(method==HttpMethod.Post)req.Content=JsonContent.Create(command);
            req.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));
            if(csrf)req.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);return req;
        }
        const string path="/api/plate-molds/order-link-owner-v1/commands";
        using(var denied=await host.Send(Request(HttpMethod.Post,path,false)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var response=await host.Send(Request(HttpMethod.Post,path)))
        {Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl?.NoStore);}
        using(var response=await host.Send(Request(HttpMethod.Get,$"/api/plate-molds/order-link-owner-v1/operations/{command.OperationId}")))
        {Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.Contains("COMMITTED",await response.Content.ReadAsStringAsync());}
        Assert.Equal(2,await h.Db.Orders.CountAsync());
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(v=>v.SetProperty(u=>u.Enable,false));
        using(var response=await host.Send(Request(HttpMethod.Get,$"/api/plate-molds/order-link-owner-v1/{id}")))Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_order_link_concurrent_same_key_creates_one_order_and_database_original_is_immutable()
    {
        await using var h=await Seed();var(a,s)=await GrantToolLink(h);var id=await SeedPlate(h);var command=await ToolCommand(h,a,id);
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});
        var http=new OperationsHttp{HttpContext=h.Http};var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterFields(OperationsOwnerV1.PlateMold,[]);catalog.RegisterScope(OperationsOwnerV1.PlateMold,"Tool",[1,2,3,4,5],5);catalog.Seal();
        var root=new OperationsOwnerV1(db,http,_=>Task.FromResult(h.User),catalog.Capture);
        var reader=new OrderReadOwnerV1(db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var writer=new OrderWriteOwnerV1(db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var second=new ToolOrderLinkOwnerV1(db,root,new OrderService(db,h.Workflow,new NoOpWmsBridgeHook(),readOwner:reader,writeOwner:writer),reader);
        var results=await Task.WhenAll(s.CreateOrLinkAsync(command),second.CreateOrLinkAsync(command));Assert.Equal(results[0],results[1]);
        Assert.Equal(2,await h.Db.Orders.CountAsync());Assert.Equal(1,await ToolLinkCount(h));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_order_link_v1.links WHERE tenant_id={h.Tenant}"));
        Assert.Equal(results[0],(await s.OperationAsync(command.OperationId,false)).Result);
    }
}

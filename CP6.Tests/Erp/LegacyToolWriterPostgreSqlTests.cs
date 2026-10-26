using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class NoLegacyPe : IPlateMoldPeApiService
    {
        public Task<PlateMoldPeApiResultDto> SendAsync(PlateMoldDto dto,CancellationToken ct=default)=>throw new Exception("PE must remain NOT_EXECUTED");
        public Task<PlateMoldPeApiResultDto> SendDeleteAsync(PlateMoldDto dto,CancellationToken ct=default)=>throw new Exception("PE must remain NOT_EXECUTED");
    }
    private static async Task<(PlateMoldWriteOwnerV1 Owner,PlateMoldService Service)> GrantLegacyTool(Harness h)
    {
        var(a,links)=await GrantToolLink(h);
        foreach(var(menu,action) in new[]{(215,"add"),(215,"del"),(32622,"cancel")})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==menu && x.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var http=new OperationsHttp{HttpContext=h.Http};
        var reader=new OrderReadOwnerV1(h.Db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var writer=new OrderWriteOwnerV1(h.Db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var orders=new OrderService(h.Db,h.Workflow,new NoOpWmsBridgeHook(),readOwner:reader,writeOwner:writer);
        var owner=new PlateMoldWriteOwnerV1(h.Db,a,orders,reader,links);return(owner,new(h.Db,new NoLegacyPe(),owner));
    }
    private static async Task<PlateMoldDto> NewLegacyTool(PlateMoldWriteOwnerV1 owner)
    {
        var dto=PlateMoldService.NativeDto(NativePlate());dto.WdPtnNo=null;dto.WdRev=1;dto.RowVersion=null;
        dto.OperationId=Guid.NewGuid();dto.OperationReason="Original legacy tool maintenance";
        dto.ExpectedNativeRowVersion="absent";dto.ExpectedTechnicalControl=(await owner.ScopeAsync()).NewTechnicalControl;return dto;
    }
    private static PlateMoldDto Command(PlateMoldDto dto)
    {dto.OperationId=Guid.NewGuid();dto.OperationReason="Explicit original request";return dto;}

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_create_and_same_key_replay_preserve_original_precision_and_no_pe()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);
        var(no,rev,link,pe)=await s.CreateAsync(input,"untrusted-actor");Assert.False(pe.Ok);Assert.Equal(1,rev);
        var operation=await o.OperationAsync(input.OperationId!.Value,false);var r=Assert.IsType<PlateMoldWriteResultV1>(operation.Result);
        Assert.Equal("COMMITTED",operation.State);Assert.Equal("OWNER_APPLIED",r.CommercialState);Assert.Equal("TECHNICAL_CONTROL_UNPROVEN",r.TechnicalControl.Knowledge);
        Assert.Equal(no,r.Number);Assert.Equal(link.WebOrderNo,r.Order.Number);Assert.Equal("NOT_EXECUTED",r.PeState);
        var native=await h.Db.PlateMolds.AsNoTracking().SingleAsync();Assert.Equal("price-owner",native.Creator);Assert.Equal(NativePlate().SheetWidth,native.SheetWidth);
        Assert.Equal(0,native.Status);Assert.False(native.McTransferFlg);Assert.Equal(1234.5678m,(await h.Db.OrderDetails.AsNoTracking().SingleAsync(d=>d.Id==r.Order.LineId)).Amount);
        Assert.Equal(r,await o.ExecuteAsync("CREATE",null,1,input));Assert.Equal(1,await h.Db.PlateMolds.CountAsync());Assert.Equal(2,await h.Db.Orders.CountAsync());
        input.Memo="different input";Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("CREATE",null,1,input))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_all_direct_writers_require_versions_and_real_authority()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);input.ExpectedTechnicalControl=null;
        Assert.Equal("VERSION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateAsync(input,null))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.ReviseAsync("no",input,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.UpdateAsync("no",1,input,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.DeleteVersionedAsync("no",1,input,null));
        Assert.Equal("VERSION_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.DeleteAsync("no",1,null,null))).Message);
        var unowned=new PlateMoldService(h.Db,new NoLegacyPe());
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>unowned.CreateAsync(input,null))).Status);
        input=await NewLegacyTool(o);await h.Db.Sys_RoleActions.Where(x=>x.MenuId==215 && x.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>s.CreateAsync(input,null))).Status);Assert.Empty(await h.Db.PlateMolds.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_change_and_cancel_are_durable_requests_without_erasing_confirmed_order()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);var first=await o.ExecuteAsync("CREATE",null,1,input);
        var order=await h.Db.Orders.AsNoTracking().SingleAsync(x=>x.Id==first.Order.OrderId);var line=await h.Db.OrderDetails.AsNoTracking().SingleAsync(x=>x.Id==first.Order.LineId);
        var update=Command((await s.GetByNoAsync(first.Number,1))!);update.DecisionAmount=9876.5432m;
        await s.UpdateAsync(first.Number,1,update,null);var changed=(await o.OperationAsync(update.OperationId!.Value,false)).Result!;
        Assert.Equal("REQUESTED",changed.CommercialState);Assert.Equal("CHANGE_REQUEST",changed.Intent!.Kind);Assert.Equal("CONFIRMED",changed.Intent.OriginalLifecycle);
        var delete=Command((await s.GetByNoAsync(first.Number,1))!);await s.DeleteVersionedAsync(first.Number,1,delete,null);
        var cancelled=(await o.OperationAsync(delete.OperationId!.Value,false)).Result!;Assert.Equal("CANCEL_REQUEST",cancelled.Intent!.Kind);Assert.Equal("REQUESTED",cancelled.CommercialState);
        Assert.True((await h.Db.PlateMolds.AsNoTracking().SingleAsync()).IsDeleted);
        Assert.Equal(order.RowVersion,(await h.Db.Orders.AsNoTracking().SingleAsync(x=>x.Id==order.Id)).RowVersion);
        var after=await h.Db.OrderDetails.AsNoTracking().SingleAsync(x=>x.Id==line.Id);Assert.Equal(line.RowVersion,after.RowVersion);Assert.Equal(line.Amount,after.Amount);Assert.False(after.IsDeleted);
        Assert.Equal(2,await h.Db.Orders.CountAsync());Assert.Equal(changed,(await o.OperationAsync(update.OperationId.Value,false)).Result);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_revision_uses_exact_predecessor_preserves_history_and_rejects_stale_retry()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var first=await o.ExecuteAsync("CREATE",null,1,await NewLegacyTool(o));
        var old=Command((await s.GetByNoAsync(first.Number,1))!);old.StDate=new DateTime(2026,6,1);
        var next=await o.ExecuteAsync("REVISE",first.Number,1,old);Assert.Equal(2,next.Revision);Assert.NotEqual(first.ToolId,next.ToolId);
        Assert.Equal(new DateTime(2026,5,31),await h.Db.PlateMolds.Where(p=>p.Id==first.ToolId).Select(p=>p.EndDate).SingleAsync());
        Assert.Equal(first,(await o.OperationAsync(first.OperationId,false)).Result);Assert.Equal(next,await o.ExecuteAsync("REVISE",first.Number,1,old));
        old.OperationId=Guid.NewGuid();await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("REVISE",first.Number,1,old));
        Assert.Equal(2,await h.Db.PlateMolds.CountAsync());Assert.Equal(3,await h.Db.Orders.CountAsync());
        var exact=(await s.GetByNoAsync(first.Number,1))!;Assert.Equal(1,exact.WdRev);Assert.Equal(first.Number,exact.WdPtnNo);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_separates_native_and_observed_control_conflicts_and_missing_order()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var first=await o.ExecuteAsync("CREATE",null,1,await NewLegacyTool(o));
        var update=Command((await s.GetByNoAsync(first.Number,1))!);
        await h.Db.PlateMolds.ExecuteUpdateAsync(v=>v.SetProperty(p=>p.DecisionAmount,9m));
        Assert.Equal("TOOL_NATIVE_VERSION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("UPDATE",first.Number,1,update))).Message);
        update=Command((await s.GetByNoAsync(first.Number,1))!);await h.Db.PlateMolds.ExecuteUpdateAsync(v=>v.SetProperty(p=>p.SheetWidth,4m));
        Assert.Equal("TECHNICAL_CONTROL_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("UPDATE",first.Number,1,update))).Message);
        await h.Db.PlateMolds.ExecuteUpdateAsync(v=>v.SetProperty(p=>p.ArrangeNo,(string?)null));update=Command((await s.GetByNoAsync(first.Number,1))!);
        Assert.Equal("NATIVE_ORDER_LINK_UNPROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("UPDATE",first.Number,1,update))).Message);
        Assert.Equal(2,await h.Db.Orders.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_original_order_version_permissions_and_whole_graph_scope_are_required()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var first=await o.ExecuteAsync("CREATE",null,1,await NewLegacyTool(o));
        var update=Command((await s.GetByNoAsync(first.Number,1))!);
        await h.Db.OrderDetails.Where(d=>d.Id==first.Order.LineId).ExecuteUpdateAsync(v=>v.SetProperty(d=>d.Amount,8m));
        Assert.Equal("ORIGINAL_ORDER_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("UPDATE",first.Number,1,update))).Message);
        update=Command((await s.GetByNoAsync(first.Number,1))!);await h.Db.Sys_RoleActions.Where(x=>x.MenuId==32622 && x.ActionCode=="cancel").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("DELETE",first.Number,1,update))).Status);
        Assert.False(await h.Db.PlateMolds.Select(x=>x.IsDeleted).SingleAsync());
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="Amount",Access=2});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("UPDATE",first.Number,1,update))).Message);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="order").ExecuteUpdateAsync(v=>v.SetProperty(x=>x.ScopeType,1));
        await h.Db.OrderDetails.Where(x=>x.Id==first.Order.LineId).ExecuteUpdateAsync(v=>v.SetProperty(x=>x.Creator,"someone-else"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(first.OperationId,false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_lost_response_recovers_without_write_grant_and_close_blocks_late_create()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(o,_)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("CREATE",null,1,input))).Message);
        var result=(await o.OperationAsync(input.OperationId!.Value,false)).Result!;Assert.NotNull(result);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==215 && x.ActionCode=="add" || x.MenuId==32622 && x.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal(result,await o.ExecuteAsync("CREATE",null,1,input));
        var unknown=Guid.NewGuid();Assert.Equal("UNKNOWN",(await o.OperationAsync(unknown,false)).State);Assert.Equal("CLOSED",(await o.OperationAsync(unknown,true)).State);
        input.OperationId=unknown;Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("CREATE",null,1,input))).Message);
        Assert.Equal(1,await h.Db.PlateMolds.CountAsync());Assert.Equal(2,await h.Db.Orders.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_final_authority_failure_rolls_back_native_source_order_and_receipts()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var(o,_)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);var reached=false;
        hook.Before=async c=>{if(!c.CommandText.Contains("WITH field_subject"))return;
            await using var probe=c.Connection!.CreateCommand();probe.Transaction=c.Transaction;probe.CommandText=$"SELECT count(*) FROM cp6_tool_order_link_v1.links WHERE tenant_id='{h.Tenant}'";
            if(Convert.ToInt32(await probe.ExecuteScalarAsync())>0){reached=true;throw new IOException("final authority unavailable");}};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("CREATE",null,1,input))).Status);hook.Before=null;
        Assert.True(reached);Assert.Empty(await h.Db.PlateMolds.ToArrayAsync());Assert.Equal(1,await h.Db.Orders.CountAsync());Assert.Equal(0,await ToolLinkCount(h));
        Assert.Equal("UNKNOWN",(await o.OperationAsync(input.OperationId!.Value,false)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_rejects_precision_loss_and_invalid_effectivity_without_partial_rows()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);input.DecisionAmount=1.00001m;
        Assert.Equal("TOOL_DECIMAL_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("CREATE",null,1,input))).Message);Assert.Empty(await h.Db.PlateMolds.ToArrayAsync());
        input.DecisionAmount=1m;var first=await o.ExecuteAsync("CREATE",null,1,input);var next=Command((await s.GetByNoAsync(first.Number,1))!);
        Assert.Equal("TOOL_EFFECTIVITY_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("REVISE",first.Number,1,next))).Message);
        next.StDate=DateTime.Today.AddDays(10);next.EndDate=DateTime.Today.AddYears(1);var future=await o.ExecuteAsync("REVISE",first.Number,1,next);
        var again=Command((await s.GetByNoAsync(first.Number,2))!);again.StDate=DateTime.Today.AddDays(20);
        Assert.Equal("TOOL_FUTURE_REVISION_EXISTS",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ExecuteAsync("REVISE",first.Number,2,again))).Message);Assert.Equal(2,await h.Db.PlateMolds.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_HTTP_requires_current_original_query()
    {
        await using var h=await Seed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/plate-molds/write-owner-v1/scope"));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_current_scoped_list_and_history_hide_unowned_rows()
    {
        await using var h=await Seed();var(_,s)=await GrantLegacyTool(h);var own=await SeedPlate(h);var hidden=await SeedPlate(h,"B");
        await h.Db.PlateMolds.Where(p=>p.Id==hidden).ExecuteUpdateAsync(v=>v.SetProperty(p=>p.Creator,"someone-else"));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="erp-plate-mold").ExecuteUpdateAsync(v=>v.SetProperty(x=>x.ScopeType,1));
        var result=await s.SearchAsync(new PlateMoldQueryDto{Page=1,PageSize=100});var row=Assert.Single(result.Item1);Assert.Equal(1,result.Item2);
        Assert.Equal(await h.Db.PlateMolds.Where(x=>x.Id==own).Select(x=>x.WdPtnNo).SingleAsync(),row.WdPtnNo);
        var number=await h.Db.PlateMolds.Where(x=>x.Id==hidden).Select(x=>x.WdPtnNo).SingleAsync();Assert.Empty(await s.GetHistoryAsync(number));Assert.Null(await s.GetByNoAsync(number,7));
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_concurrent_revisions_accept_one_predecessor_without_max_retry()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var first=await o.ExecuteAsync("CREATE",null,1,await NewLegacyTool(o));
        var one=Command((await s.GetByNoAsync(first.Number,1))!);one.StDate=new DateTime(2026,6,1);
        var two=OperationsOwnerV1.Freeze(one);two.OperationId=Guid.NewGuid();
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});
        var http=new OperationsHttp{HttpContext=h.Http};var catalog=new CP6.Core.Services.Sys.AuthorizationCatalogStoreV1();
        catalog.RegisterFields(OperationsOwnerV1.PlateMold,[]);catalog.RegisterScope(OperationsOwnerV1.PlateMold,"Tool",[1,2,3,4,5],5);catalog.Seal();
        var root=new OperationsOwnerV1(db,http,_=>Task.FromResult(h.User),catalog.Capture);
        var reader=new OrderReadOwnerV1(db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);var writer=new OrderWriteOwnerV1(db,http,_=>Task.FromResult(h.User),h.Catalog.Capture);
        var orders=new OrderService(db,h.Workflow,new NoOpWmsBridgeHook(),readOwner:reader,writeOwner:writer);
        var other=new PlateMoldWriteOwnerV1(db,root,orders,reader,new ToolOrderLinkOwnerV1(db,root,orders,reader));
        async Task<bool> Apply(PlateMoldWriteOwnerV1 owner,PlateMoldDto input)
        {try{await owner.ExecuteAsync("REVISE",first.Number,1,input);return true;}catch(OrderPriceCorrectionException e){Assert.Equal(412,e.Status);return false;}}
        var outcomes=await Task.WhenAll(Apply(o,one),Apply(other,two));Assert.Single(outcomes,x=>x);Assert.Equal(2,await h.Db.PlateMolds.CountAsync());Assert.Equal(3,await h.Db.Orders.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_tool_writer_actual_HTTP_all_four_old_routes_require_versions_csrf_and_recover_real_results()
    {
        await using var h=await Seed();var(o,s)=await GrantLegacyTool(h);var input=await NewLegacyTool(o);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        async Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body=null,bool csrf=true)
        {
            var req=new HttpRequestMessage(method,path);if(body!=null)req.Content=JsonContent.Create(body);
            req.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));if(csrf)req.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);
            return await host.Send(req);
        }
        using(var denied=await Send(HttpMethod.Post,"/api/plate-molds",input,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var created=await Send(HttpMethod.Post,"/api/plate-molds",input))Assert.True(created.IsSuccessStatusCode,await created.Content.ReadAsStringAsync());
        var first=(await o.OperationAsync(input.OperationId!.Value,false)).Result!;
        var original=Command((await s.GetByNoAsync(first.Number,1))!);original.ExpectedNativeRowVersion=null;
        foreach(var(method,path) in new[]{(HttpMethod.Put,$"/api/plate-molds/{first.Number}/revise"),(HttpMethod.Put,$"/api/plate-molds/{first.Number}/1"),(HttpMethod.Delete,$"/api/plate-molds/{first.Number}/1")})
        {using var missing=await Send(method,path,original);Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);Assert.Contains("VERSION_REQUIRED",await missing.Content.ReadAsStringAsync());}
        original=Command((await s.GetByNoAsync(first.Number,1))!);original.DecisionAmount=5m;
        using(var changed=await Send(HttpMethod.Put,$"/api/plate-molds/{first.Number}/1",original))Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        using(var receipt=await Send(HttpMethod.Get,$"/api/plate-molds/write-owner-v1/operations/{original.OperationId}"))
        {Assert.True(receipt.IsSuccessStatusCode,await receipt.Content.ReadAsStringAsync());Assert.True(receipt.Headers.CacheControl?.NoStore);Assert.Contains("REQUESTED",await receipt.Content.ReadAsStringAsync());}
        var stale=Command(OperationsOwnerV1.Freeze(original));
        using(var conflict=await Send(HttpMethod.Put,$"/api/plate-molds/{first.Number}/1",stale))Assert.Equal(HttpStatusCode.PreconditionFailed,conflict.StatusCode);
        var revise=Command((await s.GetByNoAsync(first.Number,1))!);revise.StDate=new DateTime(2026,6,1);
        using(var revised=await Send(HttpMethod.Put,$"/api/plate-molds/{first.Number}/revise",revise))Assert.True(revised.IsSuccessStatusCode,await revised.Content.ReadAsStringAsync());
        var delete=Command((await s.GetByNoAsync(first.Number,2))!);
        using(var deleted=await Send(HttpMethod.Delete,$"/api/plate-molds/{first.Number}/2",delete))Assert.True(deleted.IsSuccessStatusCode,await deleted.Content.ReadAsStringAsync());
        Assert.Equal("CANCEL_REQUEST",(await o.OperationAsync(delete.OperationId!.Value,false)).Result!.Intent!.Kind);
        Assert.Equal(3,await h.Db.Orders.CountAsync());Assert.All(await h.Db.Orders.AsNoTracking().ToArrayAsync(),x=>Assert.False(x.IsDeleted));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==215 && x.ActionCode=="query").ExecuteDeleteAsync();
        using(var denied=await Send(HttpMethod.Get,$"/api/plate-molds/{first.Number}?rev=1"))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Send(HttpMethod.Get,$"/api/plate-molds/write-owner-v1/operations/{original.OperationId}"))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}

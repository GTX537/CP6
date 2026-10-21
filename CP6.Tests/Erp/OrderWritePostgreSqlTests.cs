using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_delete_rechecks_current_action_inside_service()
    {
        await using var h = await Seed();
        var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.DeleteAsync(h.Number, null, "forged"));
        Assert.Equal(403, denied.Status);
        Assert.False(await h.Db.Orders.AsNoTracking().Select(o => o.IsDeleted).SingleAsync());
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.False(d.IsDeleted));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_delete_stale_header_rolls_back_all_children()
    {
        await using var h = await Seed(); await GrantOrderWrites(h);
        var stale = await h.Db.Orders.AsNoTracking().Select(o => o.RowVersion).SingleAsync();
        stale = stale!.ToArray(); stale[0] ^= 1;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Service.DeleteAsync(h.Number, stale, "forged"));
        h.Db.ChangeTracker.Clear();
        Assert.False(await h.Db.Orders.AsNoTracking().Select(o => o.IsDeleted).SingleAsync());
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.False(d.IsDeleted));
    }

    private static async Task GrantOrderWrites(Harness h)
    {
        await GrantOrderReads(h);
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "erp-order");
        foreach (var action in new[] { "add", "edit", "del" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId=menu.MenuId, ActionCode=action, ActionName=action });
            h.Db.Sys_RoleActions.Add(new() { RoleId=72, MenuId=menu.MenuId, ActionCode=action });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static OrderDto NewOrder(string? department=null) => new()
    { CustomerCd="C",OrderType="1",OrderDepartment=department,Details=[new() { ProductCd="P1",Quantity=2,
        IndividualUnitPrice=1234567890123.12345678m,Amount=1234567890123.12345678m }] };

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_requires_complete_write_fields_and_real_action_definition()
    {
        await using var h=await Seed(); await GrantOrderWrites(h);
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number))!;
        h.Db.Sys_RoleFieldPerms.Add(new() {RoleId=72,ResourceKey="order",FieldName="UnitPrice",Access=2});
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(h.Number,dto,"forged"))).Message);
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),"forged"))).Message);
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.FieldName,"Amount"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),null));
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(a=>a.MenuId==32622 && a.ActionCode=="edit").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(h.Number,dto,null))).Message);
        var missing=new OrderService(h.Db,h.Workflow,new NoOpWmsBridgeHook());
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>missing.CreateAsync(NewOrder(),null))).Status);
        Assert.Equal(1,await h.Db.Orders.CountAsync());
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(),d=>Assert.Equal(3m,d.IndividualUnitPrice));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_checks_foreign_and_complete_old_self_scope_including_deleted_siblings()
    {
        await using var h=await Seed(); await GrantOrderWrites(h); await using var foreign=await Seed();
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number))!;
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(foreign.Number,dto,null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.DeleteAsync(foreign.Number,null,null));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s=>s.SetProperty(d=>d.ScopeType,1));
        await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==2).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Creator,"another").SetProperty(d=>d.IsDeleted,true));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(h.Number,dto,"another"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.DeleteAsync(h.Number,null,"another"));
        Assert.False(await h.Db.Orders.AsNoTracking().Select(o=>o.IsDeleted).SingleAsync());
        Assert.Equal("another",await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==2).Select(d=>d.Creator).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_validates_proposed_department_and_rolls_back_create_or_move()
    {
        await using var h=await Seed(); await GrantOrderWrites(h); var admin=await MappingAdmin(h);
        await SaveMapping(h,admin,await MappingCommand(h,admin));
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"sales "));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,admin.Child));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s=>s.SetProperty(d=>d.ScopeType,2));
        h.Db.ChangeTracker.Clear();
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number.ToLowerInvariant()+" "))!;
        dto.OrderDepartment="UNKNOWN";
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(h.Number,dto,null));
        Assert.Equal("sales",(await h.Db.Orders.AsNoTracking().Select(o=>o.OrderDepartment).SingleAsync())?.TrimEnd());
        var before=await h.Db.DocSequences.AsNoTracking().Select(s=>s.LastSeq).ToArrayAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder("UNKNOWN"),null));
        Assert.Equal(before,await h.Db.DocSequences.AsNoTracking().Select(s=>s.LastSeq).ToArrayAsync());
        var created=await h.Service.CreateAsync(NewOrder("sales "),"forged");
        var stored=await h.Db.Orders.AsNoTracking().SingleAsync(o=>o.WebOrderNo==created);
        Assert.Equal("price-owner",stored.Creator);
        Assert.Equal(1234567890123.12345678m,await h.Db.OrderDetails.Where(d=>d.WebOrderNo==created).Select(d=>d.Amount).SingleAsync());
        await h.Db.Sys_Depts.Where(d=>d.Id==admin.Root).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder("sales"),null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_replacement_preserves_line_owner_identity_shipping_and_native_child_precision()
    {
        await using var h=await Seed(); await GrantOrderWrites(h);
        const decimal exact=1234567890123.12345678m;
        await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==1).ExecuteUpdateAsync(s=>s
            .SetProperty(d=>d.Creator,"original-owner").SetProperty(d=>d.ApprovalStatus,2)
            .SetProperty(d=>d.ShippedQty,1m).SetProperty(d=>d.ShipStatus,5).SetProperty(d=>d.McOrderNo,"MC1"));
        h.Db.OrderMaterials.Add(new() {WebOrderNo=h.Number.ToLowerInvariant()+" ",WebOrderDetailNo=1,
            ProductCd="p1 ",ProcessCd="PROC",MaterialCd="M",SupplyUnitPrice=exact});
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var old=await h.Db.OrderDetails.AsNoTracking().SingleAsync(d=>d.WebOrderDetailNo==1);
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number.ToLowerInvariant()+" "))!;
        dto.Details[0].Amount=exact; dto.Details[0].ApprovalStatus=0; dto.Details[0].McOrderNo="forged";
        await h.Service.UpdateAsync(h.Number.ToLowerInvariant()+" ",dto,"forged-name");
        var row=await h.Db.OrderDetails.AsNoTracking().SingleAsync(d=>d.WebOrderDetailNo==1);
        Assert.Equal(old.Id,row.Id); Assert.Equal("original-owner",row.Creator); Assert.Equal(old.CreateDate,row.CreateDate);
        Assert.Equal("price-owner",row.Modifier); Assert.Equal(2,row.ApprovalStatus); Assert.Equal("MC1",row.McOrderNo);
        Assert.Equal(1m,row.ShippedQty); Assert.Equal(5,row.ShipStatus); Assert.Equal(exact,row.Amount);
        Assert.Equal(exact,await h.Db.OrderMaterials.Select(m=>m.SupplyUnitPrice).SingleAsync());
        Assert.Equal(2,await h.Db.OrderDetails.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_final_source_failure_rolls_back_whole_replacement_and_sequence()
    {
        var hook=new CommandHook(); await using var h=await Seed(hook); await GrantOrderWrites(h);
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number))!; dto.Details[0].IndividualUnitPrice=9m;
        var old=await h.Db.OrderDetails.AsNoTracking().OrderBy(d=>d.WebOrderDetailNo).Select(d=>d.Id).ToArrayAsync();
        var reads=0;
        hook.Before=c=> {if(c.CommandText.Contains("WITH field_subject") && ++reads==2) throw new IOException("owned final source failure"); return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.UpdateAsync(h.Number,dto,null))).Status);
        Assert.Equal(2,reads); Assert.Equal(old,await h.Db.OrderDetails.AsNoTracking().OrderBy(d=>d.WebOrderDetailNo).Select(d=>d.Id).ToArrayAsync());
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(),d=>Assert.Equal(3m,d.IndividualUnitPrice));
        reads=0; var before=await h.Db.DocSequences.AsNoTracking().Select(s=>s.LastSeq).ToArrayAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),null));
        Assert.Equal(1,await h.Db.Orders.CountAsync()); Assert.Equal(before,await h.Db.DocSequences.AsNoTracking().Select(s=>s.LastSeq).ToArrayAsync());
        Assert.False(h.Db.ChangeTracker.HasChanges());
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_refuses_old_transaction_and_uncertain_commit_without_replay()
    {
        var fault=new CommitFault(); await using var h=await Seed(fault); await GrantOrderWrites(h);
        await using(var tx=await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
            Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),null))).Status);
        fault.Armed=true;
        Assert.Equal("ORDER_WRITE_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),null))).Message);
        Assert.Equal(2,await h.Db.Orders.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_holds_raw_revocation_until_commit_then_denies_next_write()
    {
        var hook=new CommandHook(); await using var h=await Seed(hook); await GrantOrderWrites(h);
        Task? revoker=null; var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return; hook.After=null;
            revoker=Task.Run(async()=>
            { await using var writer=server.Context(new TenantContext {CurrentTenantId=h.Tenant});
              await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72"); });
            await using var observer=server.Context(new TenantContext {CurrentTenantId=h.Tenant});
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20,timeout.Token);
            blocked=true; Assert.False(revoker.IsCompleted);
        };
        try { await h.Service.CreateAsync(NewOrder(),null); } finally {if(revoker!=null)await revoker;}
        Assert.True(blocked); Assert.Equal(2,await h.Db.Orders.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Service.CreateAsync(NewOrder(),null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_actual_program_respects_csrf_fields_concurrency_and_revoked_session()
    {
        await using var h=await Seed(); await GrantOrderWrites(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        HttpRequestMessage Write(HttpMethod method,string path,object? value=null,bool csrf=true)
        {
            var req=new HttpRequestMessage(method,path); if(value!=null)req.Content=JsonContent.Create(value);
            req.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));
            if(csrf)req.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]); return req;
        }
        using(var response=await host.Send(Write(HttpMethod.Post,"api/orders",NewOrder(),false))) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() {RoleId=72,ResourceKey="order",FieldName="UnitPrice",Access=2});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var response=await host.Send(Write(HttpMethod.Post,"api/orders",NewOrder())))
        { Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); Assert.Contains("FIELD_WRITE_FORBIDDEN",await response.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        using(var response=await host.Send(Write(HttpMethod.Post,"api/orders",NewOrder())))
        { Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync()); Assert.True(response.Headers.CacheControl?.NoStore); }
        using(var response=await host.Send(Write(HttpMethod.Delete,"api/orders/"+h.Number+"?rowVersion=AAAAAAAAAAE%3D"))) Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(),d=>Assert.False(d.IsDeleted));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Enable,false));
        using(var response=await host.Send(Write(HttpMethod.Delete,"api/orders/"+h.Number))) Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
    private sealed class WriteBridge(Func<string,string?,Task> callback) : IWmsBridgeHook
    {
        public async Task<WmsBridgeResult> OnOrderCreatedAsync(string number,string? user) {await callback(number,user);return WmsBridgeResult.Skipped("owned");}
        public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string no,string? user)=>Task.FromResult(WmsBridgeResult.Skipped("owned"));
        public Task<WmsBridgeResult> OnProductionCompletedAsync(string no,decimal qty,string? user)=>Task.FromResult(WmsBridgeResult.Skipped("owned"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_write_freezes_input_and_only_calls_bridge_after_confirmed_authorized_commit()
    {
        var hook=new CommandHook(); await using var h=await Seed(hook);await GrantOrderWrites(h);
        var calls=0; var visible=false;
        var service=new OrderService(h.Db,h.Workflow,new WriteBridge(async(number,user)=>
        {
            calls++;Assert.Null(h.Db.Database.CurrentTransaction);Assert.Equal("price-owner",user);
            await using var observer=server.Context(new TenantContext {CurrentTenantId=h.Tenant});
            visible=await observer.Orders.AnyAsync(o=>o.WebOrderNo==number);
            throw new IOException("owned best-effort hook fault after confirmed commit");
        }),writeOwner:new OrderWriteOwnerV1(h.Db,new HttpContextAccessor {HttpContext=h.Http},_=>Task.FromResult(h.User),h.Catalog.Capture));
        var request=NewOrder(); var reads=0;
        hook.Before=c=> {if(c.CommandText.Contains("WITH field_subject") && ++reads==2)throw new IOException("final authority unavailable");return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.CreateAsync(request,"forged"));
        Assert.Equal(0,calls); Assert.Equal(1,await h.Db.Orders.CountAsync());
        hook.Before=c=> {if(c.CommandText.Contains("WITH field_subject")) {hook.Before=null;request.Details[0].Amount=1m;}return Task.CompletedTask;};
        var number=await service.CreateAsync(request,"forged");
        Assert.Equal(1,calls);Assert.True(visible);
        Assert.Equal(1234567890123.12345678m,await h.Db.OrderDetails.Where(d=>d.WebOrderNo==number).Select(d=>d.Amount).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_delete_failure_preserves_every_child_table_and_update_stale_token()
    {
        await using var h=await Seed(); await GrantOrderWrites(h);
        h.Db.OrderProcesses.Add(new(){WebOrderNo=h.Number,WebOrderDetailNo=1,ProductCd="P1",OperationCd="OP",ProcessCd="PR"});
        h.Db.OrderProcessNotes.Add(new(){WebOrderNo=h.Number,WebOrderDetailNo=1,ProductCd="P1",OperationCd="OP"});
        h.Db.OrderMaterials.Add(new(){WebOrderNo=h.Number,WebOrderDetailNo=1,ProductCd="P1",ProcessCd="PR",MaterialCd="M"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var dto=(await h.Service.GetByWebOrderNoAsync(h.Number))!;
        dto.RowVersion=dto.RowVersion!.ToArray();dto.RowVersion[0]^=1;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>h.Service.UpdateAsync(h.Number,dto,null));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>h.Service.DeleteAsync(h.Number,dto.RowVersion,null));
        Assert.False(await h.Db.OrderProcesses.Select(p=>p.IsDeleted).SingleAsync());
        Assert.False(await h.Db.OrderProcessNotes.Select(p=>p.IsDeleted).SingleAsync());
        Assert.False(await h.Db.OrderMaterials.Select(p=>p.IsDeleted).SingleAsync());
        await h.Service.DeleteAsync(h.Number,null,"forged");
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(),d=>Assert.True(d.IsDeleted));
        Assert.True(await h.Db.OrderProcesses.Select(p=>p.IsDeleted).SingleAsync());
        Assert.True(await h.Db.OrderProcessNotes.Select(p=>p.IsDeleted).SingleAsync());
        Assert.True(await h.Db.OrderMaterials.Select(p=>p.IsDeleted).SingleAsync());
    }

}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<string> OperationsFailure(HttpResponseMessage response)
    {
        // Development exception pages append ambient credentials. Keep the failure/stack, not fixture cookies.
        var body=await response.Content.ReadAsStringAsync();var headers=body.IndexOf("HEADERS",StringComparison.Ordinal);
        return headers<0?body:body[..headers];
    }
    private static HttpRequestMessage OperationsRequest(Dictionary<string,string> cookies,HttpMethod method,string path,object? data=null,bool csrf=true)
    {
        var r=new HttpRequestMessage(method,path);if(data!=null)r.Content=JsonContent.Create(data);
        r.Headers.Add("Cookie",string.Join("; ",cookies.Select(c=>c.Key+"="+c.Value)));
        if(csrf)r.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);return r;
    }
    [CurrentPermissionPostgresFact]
    public async Task Operations_actual_Program_login_csrf_current_owner_and_transport_contracts()
    {
        await using var h=await Seed();await SeedCancelGraph(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        var no=await h.Db.OutboundOrders.Select(o=>o.OutboundNo).SingleAsync();
        var wo=await h.Db.WorkOrders.Select(o=>o.WorkOrderNo).SingleAsync();
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(a=>a.RoleId==1 && (a.MenuId==302 || a.MenuId==408) && a.ActionCode=="query"));
        // The existing actual-API fixture deliberately skips initialization. Seed behavior is checked separately.
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/work-orders/"+wo)))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await GrantOperations(h);
        foreach(var method in new[]{HttpMethod.Get,HttpMethod.Post})
        {
            using var csrf=await host.Send(OperationsRequest(cookies,method,"api/mes/work-orders/next-seq",csrf:false));
            Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
            using var sequence=await host.Send(OperationsRequest(cookies,method,"api/mes/work-orders/next-seq"));
            Assert.True(sequence.StatusCode==HttpStatusCode.OK,await sequence.Content.ReadAsStringAsync());
        }
        using(var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/work-orders/"+wo)))
        {Assert.True(read.StatusCode==HttpStatusCode.OK,await read.Content.ReadAsStringAsync());Assert.True(read.Headers.CacheControl?.NoStore);}
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-outbound-order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-order",NewOutbound())))
        {Assert.True(denied.StatusCode==HttpStatusCode.Forbidden,await OperationsFailure(denied));Assert.Contains("FIELD_WRITE_FORBIDDEN",await denied.Content.ReadAsStringAsync());}
        using(var csrf=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-order/"+no+"/cancel",csrf:false)))
            Assert.Equal(HttpStatusCode.Forbidden,csrf.StatusCode);
        using(var cancel=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-order/"+no+"/cancel")))
            Assert.True(cancel.StatusCode==HttpStatusCode.OK,await cancel.Content.ReadAsStringAsync());
        Assert.Equal(2,await h.Db.StockTransactions.CountAsync());
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        h.Db.Stocks.Add(new(){WarehouseCd="W01",LocationCd="L",ProductCd="P",LotNo="",PhysicalQty=10m,AvailableQty=10m});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var create=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-order",NewOutbound())))
        {
            Assert.True(create.StatusCode==HttpStatusCode.OK,await OperationsFailure(create));
            var created=JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("outboundNo").GetString();
            foreach(var action in new[]{"confirm","allocate","ship"})
            {
                using var step=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/outbound-order/"+created+"/"+action,new{}));
                Assert.True(step.StatusCode==HttpStatusCode.OK,await OperationsFailure(step));
            }
        }
        Assert.Equal(4,await h.Db.StockTransactions.CountAsync());
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-outbound-order",FieldName="UnitPrice",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.FieldName,"unknown-field"));
        using(var unavailable=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/outbound-order/"+no)))
            Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
        using(var oldSession=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/mes/work-orders/"+wo)))
            Assert.Equal(HttpStatusCode.Unauthorized,oldSession.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Operations_raw_revocation_is_serialized_with_current_owner_and_next_intent_denied(bool before)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await SeedCancelGraph(h);var owner=await GrantOperations(h);
        var no=await h.Db.OutboundOrders.Select(o=>o.OutboundNo).SingleAsync();Task? revoker=null;var observed=false;
        async Task Revoke()
        {
            await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");
        }
        if(before)hook.Before=async c=>{if(!c.CommandText.Contains("cp6_authentication_v1.acquire()"))return;hook.Before=null;await Revoke();observed=true;};
        else hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;revoker=Task.Run(Revoke);
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                  AND wait_event_type='Lock' AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            observed=true;Assert.False(revoker.IsCompleted);
        };
        try
        {
            if(before)await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OperationsWms(h,owner).CancelOrderAsync(no,null));
            else await OperationsWms(h,owner).CancelOrderAsync(no,null);
        }
        finally{if(revoker!=null)await revoker;}
        Assert.True(observed);Assert.Equal(before?0:2,await h.Db.StockTransactions.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OperationsWms(h,owner).GetOrderAsync(no));
    }

    [CurrentPermissionPostgresFact]
    public async Task Operations_trusted_department_mapping_filters_linked_objects_and_never_guesses_standalone_department()
    {
        await using var h=await Seed();await SeedCancelGraph(h);var owner=await GrantOperations(h);var mapping=await MappingAdmin(h);
        await SaveMapping(h,mapping,await MappingCommand(h,mapping));
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(o=>o.OrderDepartment,"sales "));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,mapping.Root));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,3));
        var no=await h.Db.WorkOrders.Select(o=>o.WorkOrderNo).SingleAsync();
        Assert.NotNull(await OperationsMes(h,owner).GetByNoAsync(no));Assert.Single(await OperationsWms(h,owner).SearchOrdersAsync(new()));
        await h.Db.Sys_RoleDataScopes.Where(s=>s.ResourceKey!="order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,2));
        Assert.Empty(await OperationsWms(h,owner).SearchOrdersAsync(new()));
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,mapping.Child));
        Assert.NotNull(await OperationsMes(h,owner).GetByNoAsync(no));
        await h.Db.WorkOrders.ExecuteUpdateAsync(s=>s.SetProperty(w=>w.WebOrderNo,(string?)null).SetProperty(w=>w.BaseCd,"SALES"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OperationsMes(h,owner).GetByNoAsync(no));
    }
}

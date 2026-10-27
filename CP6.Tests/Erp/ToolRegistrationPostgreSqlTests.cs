using System.Net;
using System.Net.Http.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 RegistrationAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();c.RegisterScope("wms-plate-mold","Physical tools",[1,2,3,4,5],1);
        c.RegisterFields("wms-plate-mold",ToolFields.Select(f=>new FieldRegistry.Field(f,f)));
        c.RegisterScope("space-site","Sites",[5],5);c.RegisterFields("space-site",[]);c.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static ToolRegistrationOwnerV1 Registrations(Harness h,CP6Context? db=null)=>new(db??h.Db,RegistrationAuthority(h,db));
    private static async Task GrantRegistration(Harness h)
    {await GrantToolInstance(h);await EngineeringAction(h,"space-site","query");await EngineeringAction(h,"wms-plate-mold","initialize");}
    private static async Task<ToolRegistrationCommandV1> NewRegistered(Harness h,Guid? department=null)
    {
        var site=await PhysicalSite(h,"Site-"+Guid.NewGuid().ToString("N")[..12]);var svc=Registrations(h);
        return new(Guid.NewGuid(),Guid.NewGuid(),"CREATE",(await svc.SitesAsync(null)).Single(x=>x.Reference.Id==site).Reference,"New unused physical tool verified",
            CreationAuthorityVersion:(await svc.CreationContextAsync()).AuthorityVersion,DepartmentId:department,Profile:new("PLATE",MaxShots:1000,MadeCost:"9999999999999999.99"));
    }
    private static async Task<(ToolInstanceViewV1 View,ToolRegistrationCommandV1 Command)> LegacyRegistration(Harness h)
    {
        var id=await SeedToolInstance(h);var view=await ToolInstances(h).ReadAsync(id);var site=await PhysicalSite(h,"Legacy location reviewed");
        return(view,new(Guid.NewGuid(),id,"BIND_LEGACY",(await Registrations(h).SitesAsync(null)).Single(x=>x.Reference.Id==site).Reference,"Explicit physical identity inspected; history unavailable",Expected:view.Reference));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_new_original_zero_cycle_and_lifetime_are_atomic_without_stock_or_engineering()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);
        var result=await svc.CommandAsync(command,null);var value=(await svc.ReadAsync(command.ObjectId))!;var native=await ToolInstances(h).ReadAsync(command.ObjectId);
        Assert.Equal("TOOL_CREATE",result.Kind);Assert.Equal("1",value.CycleNo);Assert.Equal("0",value.CurrentCycleShots);Assert.Equal("0",value.LifetimeShots);
        Assert.Equal("KNOWN_FROM_REGISTRATION",value.LifetimeKnowledge);Assert.Equal("UNKNOWN",value.CurrentEligibility);Assert.NotEqual(Guid.Empty,value.CycleAnchorId);
        Assert.StartsWith("PLT2",native.Source.GetProperty("plateNo").GetString());Assert.Equal("9999999999999999.99",native.Source.GetProperty("madeCost").GetString());
        Assert.Null(native.Source.GetProperty("customerCd").GetString());Assert.Null(native.Source.GetProperty("warehouseCd").GetString());
        Assert.Equal(result,await svc.CommandAsync(command,null));Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(1,await h.Db.WmsSequences.Select(x=>x.NextNo).SingleAsync());
        Assert.Equal(0,await h.Db.Stocks.CountAsync());Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        var changed=command with{Profile=command.Profile! with{MadeCost="1.00"}};Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(changed,null))).Status);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_instance_v1.registrations"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_instance_v1.registrations"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_legacy_preserves_500_and_unknown_lifetime_requires_own_action_and_exact_native()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var(view,command)=await LegacyRegistration(h);
        Assert.Null(await svc.ReadAsync(command.ObjectId));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="initialize").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,view.Etag))).Status);
        await EngineeringAction(h,"wms-plate-mold","initialize");
        Assert.Equal(428,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,"*"))).Status);
        var saved=await svc.CommandAsync(command,view.Etag);var value=(await svc.ReadAsync(command.ObjectId))!;
        Assert.Equal("LEGACY_OBSERVED",value.CycleKnowledge);Assert.Equal("500",value.CurrentCycleShots);Assert.Null(value.CycleNo);Assert.Null(value.LifetimeShots);Assert.Equal("UNKNOWN",value.LifetimeKnowledge);
        Assert.Equal(view.Reference,(await ToolInstances(h).ReadAsync(command.ObjectId)).Reference);Assert.Equal(500,await h.Db.PlateMoldStocks.Select(x=>x.UsedShots).SingleAsync());
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{OperationId=Guid.NewGuid()},view.Etag))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="initialize").ExecuteDeleteAsync();
        Assert.Equal(saved,(await svc.OperationAsync(command.OperationId,false)).Result);Assert.Equal(saved,await svc.CommandAsync(command,view.Etag));
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)][InlineData(3)][InlineData(4)]
    public async Task Tool_registration_new_department_uses_trusted_mapping_and_revocation_hides_receipt(int type)
    {
        await using var h=await Seed();await GrantRegistration(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,type);var svc=Registrations(h);
        var missing=await NewRegistered(h);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(missing,null))).Status);
        var command=await NewRegistered(h,admin.Child);var result=await svc.CommandAsync(command,null);Assert.NotNull(await svc.ReadAsync(command.ObjectId));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='tool-instance' AND object_id={command.ObjectId}");
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(result.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_site_current_version_enabled_and_query_are_real_and_tenant_bound()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);
        await h.Db.Space_Sites.Where(x=>x.Id==command.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SiteName,"changed"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);Assert.Empty(await h.Db.PlateMoldStocks.ToArrayAsync());
        var current=(await svc.SitesAsync(null)).Single(x=>x.Reference.Id==command.Site.Id);command=command with{Site=current.Reference};
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==9151&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);
        await EngineeringAction(h,"space-site","query");await h.Db.Space_Sites.Where(x=>x.Id==command.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,false));
        Assert.Empty(await svc.SitesAsync(null));Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);
        await using var foreign=await Seed();await GrantRegistration(foreign);var foreignCommand=await NewRegistered(foreign);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{Site=foreignCommand.Site},null));
        Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_native_counter_change_and_reversal_never_restore_unproven_zero_lifetime()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);await svc.CommandAsync(command,null);
        var view=await ToolInstances(h).ReadAsync(command.ObjectId);await ToolInstances(h).SaveAsync(command.ObjectId,view.Etag,ToolEdit(view,new ToolInstancePatchV1("sizeNote","Profile only")));
        Assert.Equal("0",(await svc.ReadAsync(command.ObjectId))!.LifetimeShots);
        await h.Db.PlateMoldStocks.Where(x=>x.Id==command.ObjectId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UsedShots,100));
        await h.Db.PlateMoldStocks.Where(x=>x.Id==command.ObjectId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UsedShots,0));
        var value=(await svc.ReadAsync(command.ObjectId))!;Assert.Equal("RECONCILIATION_REQUIRED",value.CycleKnowledge);Assert.Null(value.CurrentCycleShots);Assert.Null(value.LifetimeShots);Assert.Null(value.CycleNo);Assert.Equal("0",value.InitialCycleShots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_unknown_close_canonical_decimal_and_write_revoked_recovery_keep_original_identity()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=(await NewRegistered(h)) with{Profile=new("MOLD",MadeCost:"1.2")};
        var result=await svc.CommandAsync(command,null);Assert.Equal(result,await svc.CommandAsync(command with{Profile=command.Profile with{MadeCost="1.20"}},null));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="add").ExecuteDeleteAsync();Assert.Equal(result,(await svc.OperationAsync(command.OperationId,false)).Result);
        Assert.Equal(result,await svc.CommandAsync(command,null));var key=Guid.NewGuid();Assert.Equal("UNKNOWN",(await svc.OperationAsync(key,false)).State);Assert.Equal("CLOSED",(await svc.OperationAsync(key,true)).State);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{OperationId=key},null))).Status);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(command.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_cost_scope_and_stale_creation_context_roll_back_number_and_every_original()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{CreationAuthorityVersion="stale"},null))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-plate-mold",FieldName="MadeCost",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        command=command with{CreationAuthorityVersion=(await svc.CreationContextAsync()).AuthorityVersion};Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W1"));command=command with{Profile=command.Profile! with{MadeCost=null}};
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command,null))).Status);
        Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal(0,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal("UNKNOWN",(await svc.OperationAsync(command.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_concurrent_legacy_binding_has_one_winner_and_final_authority_failure_rolls_back()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantRegistration(h);var(view,command)=await LegacyRegistration(h);var svc=Registrations(h);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        async Task<bool> Bind(ToolRegistrationOwnerV1 target){try{await target.CommandAsync(command with{OperationId=Guid.NewGuid()},view.Etag);return true;}catch(OrderPriceCorrectionException e){Assert.Equal(409,e.Status);return false;}}
        Assert.Single(await Task.WhenAll(Bind(svc),Bind(Registrations(h,peer))),x=>x);
        var fresh=await NewRegistered(h);var reads=0;
        // CommandHook intercepts readers; root/query/Site initial authorities are the first three reads.
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==4)throw new IOException("final authority failure");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(fresh,null))).Status);hook.Before=null;
        Assert.False(await h.Db.PlateMoldStocks.AnyAsync(x=>x.Id==fresh.ObjectId));Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal("UNKNOWN",(await svc.OperationAsync(fresh.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_HoldExact_rejects_changed_site_and_same_transaction_site_mutation()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);await svc.CommandAsync(command,null);var current=(await svc.ReadAsync(command.ObjectId))!;
        await RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",false,async(a,t)=>{await svc.HoldExactAsync(a,current.Reference,current.CurrentNative,current.CurrentSite.Reference,t);return true;});
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","edit",true,async(a,t)=>
        {var query=await a.SourceAsync("wms-plate-mold",t);await svc.HoldExactAsync(query,current.Reference,current.CurrentNative,current.CurrentSite.Reference,t);
            await h.Db.Space_Sites.Where(x=>x.Id==command.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SiteName,"must roll back"),t);return true;}))).Status);
        Assert.Equal(current.CurrentSite,(await svc.ReadAsync(command.ObjectId))!.CurrentSite);
        await h.Db.Space_Sites.Where(x=>x.Id==command.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SiteName,"changed"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",false,async(a,t)=>{await svc.HoldExactAsync(a,current.Reference,current.CurrentNative,current.CurrentSite.Reference,t);return true;}))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_HoldExact_requires_query_same_context_and_blocks_a_real_site_writer_until_commit()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);await svc.CommandAsync(command,null);var value=(await svc.ReadAsync(command.ObjectId))!;
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","edit",true,async(a,t)=>{await svc.HoldExactAsync(a,value.Reference,value.CurrentNative,value.CurrentSite.Reference,t);return true;}))).Status);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",false,async(a,t)=>{await Registrations(h,other).HoldExactAsync(a,value.Reference,value.CurrentNative,value.CurrentSite.Reference,t);return true;}))).Status);
        Task<int>? writer=null;var started=new TaskCompletionSource();
        await RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",false,async(a,t)=>
        {
            await svc.HoldExactAsync(a,value.Reference,value.CurrentNative,value.CurrentSite.Reference,t);
            writer=Task.Run(async()=>{await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});started.SetResult();return await peer.Space_Sites.Where(x=>x.Id==command.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SiteName,"After commit"));});
            await started.Task;await Task.Delay(100);Assert.False(writer.IsCompleted);return true;
        });Assert.Equal(1,await writer!);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_HTTP_new_and_legacy_use_csrf_strict_bodies_and_current_actions()
    {
        await using var h=await Seed();await GrantRegistration(h);var command=await NewRegistered(h);var(view,legacy)=await LegacyRegistration(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        async Task<HttpResponseMessage> Send(HttpMethod method,string path,object body,string? etag=null,bool csrf=true)
        {var request=OperationsRequest(cookies,method,path);request.Content=JsonContent.Create(body);if(etag!=null)request.Headers.TryAddWithoutValidation("If-Match",etag);if(!csrf)request.Headers.Remove("X-CSRF-TOKEN");return await host.Send(request);}
        using var forbidden=await Send(HttpMethod.Post,"/api/wms/specials/v1/tools",command,csrf:false);Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        // HTTP creation uses the server catalog, so obtain its actual current authority version.
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/specials/v1/tools/creation-context"));
        using var json=System.Text.Json.JsonDocument.Parse(await context.Content.ReadAsStringAsync());command=command with{CreationAuthorityVersion=json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString()};
        using var created=await Send(HttpMethod.Post,"/api/wms/specials/v1/tools",command);Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl?.NoStore);
        using var missing=await Send(HttpMethod.Put,$"/api/wms/specials/v1/tools/{legacy.ObjectId}/registration",legacy);Assert.Equal((HttpStatusCode)428,missing.StatusCode);
        using var bound=await Send(HttpMethod.Put,$"/api/wms/specials/v1/tools/{legacy.ObjectId}/registration",legacy,view.Etag);Assert.Equal(HttpStatusCode.OK,bound.StatusCode);
        using var strict=await Send(HttpMethod.Post,"/api/wms/specials/v1/tools",new{command.OperationId,command.ObjectId,command.Kind,command.Site,command.Evidence,command.Profile,command.CreationAuthorityVersion,lifetimeShots="999"});Assert.Equal(HttpStatusCode.BadRequest,strict.StatusCode);
    }
}

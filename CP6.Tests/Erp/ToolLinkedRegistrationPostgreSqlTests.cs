using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 LinkedAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-plate-mold",ToolFields),("space-site",Array.Empty<string>()),("product",Array.Empty<string>()),
            ("erp-business-partner",new[]{"BpName"}),("wms-warehouse",new[]{"AddressText","ManagerCd","Remarks","WarehouseName"}),("wms-location",new[]{"Barcode","LocationName"})})
        {c.RegisterScope(key,key,[1,2,3,4,5],key=="space-site"?5:1);c.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}
        c.Seal();return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static ToolRegistrationOwnerV1 LinkedRegistrations(Harness h,CP6Context? db=null)=>new(db??h.Db,LinkedAuthority(h,db));
    private static ToolInstanceOwnerV1 LinkedTools(Harness h)=>new(h.Db,LinkedAuthority(h));
    private static async Task<ToolLinkedRegistrationCommandV1> LinkedCommand(Harness h)
    {
        await GrantRegistration(h);await GrantBusinessPartnerIdentity(h);await GrantProductIdentity(h);await h.Db.WmsRoleScopes.ExecuteDeleteAsync();await GrantMaster(h);
        var customer=await SeedIdentityPartner(h);var product=await SeedIdentityProduct(h);var master=await SeedMaster(h);
        var old=await NewRegistered(h);await h.Db.Space_Sites.Where(x=>x.Id==old.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,master.code));
        var svc=LinkedRegistrations(h);var site=(await svc.SitesAsync(null)).Single(x=>x.Reference.Id==old.Site.Id);
        var c=(await svc.CustomersAsync(null)).Single(x=>x.Reference.Id==customer);var p=(await svc.ProductsAsync(null)).Single(x=>x.Reference.Id==product);
        var location=(await svc.LocationsAsync(site.Reference.Id,null)).Single(x=>x.Reference.Id==master.location);
        return new(old with{Site=site.Reference,CreationAuthorityVersion=(await svc.CreationContextAsync()).AuthorityVersion},
            new(new(c.Reference.Owner,customer,c.Reference.Version,c.Reference.Digest),p.Reference,new(location.Reference,location.Warehouse)));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_persists_exact_UUIDs_snapshots_and_digest_atomically_and_replays_without_write_permission()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);var result=await svc.LinkedCommandAsync(command,null);
        var native=await LinkedTools(h).ReadAsync(command.Registration.ObjectId);var view=(await svc.ReadAsync(command.Registration.ObjectId))!;
        Assert.Equal("LINKED",view.Links!.CustomerState);Assert.Equal(command.Links.Customer,view.Links.Customer);Assert.Equal(command.Links.Product,view.Links.Product);Assert.Equal(command.Links.Location,view.Links.Location);
        Assert.Equal("Original e\u0301 客户",native.Source.GetProperty("customerName").GetString());Assert.Equal("Same label",native.Source.GetProperty("productName").GetString());
        Assert.Equal("L0000",native.Source.GetProperty("locationCd").GetString());Assert.Equal("0",view.LifetimeShots);Assert.Equal(0,await h.Db.StockTransactions.CountAsync());
        using var original=JsonDocument.Parse(view.OriginalJson);Assert.Equal(64,original.RootElement.GetProperty("LinksDigest").GetString()!.Length);
        Assert.DoesNotContain("客户",view.OriginalJson);Assert.Equal(result,await svc.LinkedCommandAsync(command,null));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command.Registration,null))).Message);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command with{Links=command.Links with{Customer=null}},null))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="add").ExecuteDeleteAsync();Assert.Equal(result,await svc.LinkedCommandAsync(command,null));
        Assert.Equal(result,(await svc.OperationAsync(result.OperationId,false)).Result);Assert.Equal(1,await h.Db.WmsSequences.Select(x=>x.NextNo).SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_instance_v1.registration_links"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_instance_v1.registration_links"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_current_source_fields_actions_and_objects_filter_before_paging_and_recovery()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);await svc.LinkedCommandAsync(command,null);
        var plain=await NewRegistered(h);plain=plain with{CreationAuthorityVersion=(await svc.CreationContextAsync()).AuthorityVersion};await svc.CommandAsync(plain,null);var tools=LinkedTools(h);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="erp-business-partner",FieldName="BpName",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var masked=await tools.ReadAsync(command.Registration.ObjectId);Assert.Null(masked.Source.GetProperty("customerName").GetString());Assert.DoesNotContain("CustomerName",masked.ReadableFields);
        Assert.Null((await svc.CustomersAsync(null)).Single().Name);Assert.NotNull((await svc.ReadAsync(command.Registration.ObjectId))!.Links!.Customer);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==212&&x.ActionCode=="query").ExecuteDeleteAsync();
        var page=await tools.ListAsync(new(){PageSize=1});Assert.Equal(1,page.Total);Assert.Equal(plain.ObjectId,Assert.Single(page.Items).Reference.Id);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(command.Registration.OperationId,false))).Status);
        await h.Db.Sys_RoleActions.AddAsync(new(){RoleId=72,MenuId=212,ActionCode="query"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="erp-business-partner").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.BusinessPartners.Where(x=>x.Id==command.Links.Customer!.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"another"));
        Assert.Equal(1,(await tools.ListAsync(new())).Total);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>tools.ReadAsync(command.Registration.ObjectId,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_source_ABA_and_wrong_site_fail_before_number_and_source_authority_is_required()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);
        var id=command.Links.Location!.Location.Id;
        await h.Db.Locations.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsBlocked,true));
        await h.Db.Locations.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsBlocked,false));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Status);
        var location=(await svc.LocationsAsync(command.Registration.Site.Id,null)).Single(x=>x.Reference.Id==id);
        command=command with{Links=command.Links with{Location=new(location.Reference,location.Warehouse)}};
        await h.Db.ProductMasters.Where(x=>x.Id==command.Links.Product!.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SetProductName,"Changed display"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Status);
        command=command with{Links=command.Links with{Product=(await svc.ProductsAsync(null)).Single().Reference}};
        await h.Db.BusinessPartners.Where(x=>x.Id==command.Links.Customer!.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.BpName,"Changed customer"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Status);
        var c=(await svc.CustomersAsync(null)).Single().Reference;command=command with{Links=command.Links with{Customer=new(c.Owner,c.Id,c.Version,c.Digest)}};
        var wrongSite=await PhysicalSite(h,"Other");command=command with{Registration=command.Registration with{Site=(await svc.SitesAsync(null)).Single(x=>x.Reference.Id==wrongSite).Reference}};
        Assert.Equal("TOOL_SITE_WAREHOUSE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==212&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Status);
        Assert.Equal(0,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());Assert.Equal("UNKNOWN",(await svc.OperationAsync(command.Registration.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_legacy_is_explicit_preserves_native_snapshot_and_v2_remains_replayable()
    {
        await using var h=await Seed();var newCommand=await LinkedCommand(h);var svc=LinkedRegistrations(h);var id=await SeedToolInstance(h);
        var native=await LinkedTools(h).ReadAsync(id);var bind=new ToolRegistrationCommandV1(Guid.NewGuid(),id,"BIND_LEGACY",newCommand.Registration.Site,"Legacy inspection",Expected:native.Reference);
        var command=new ToolLinkedRegistrationCommandV1(bind,newCommand.Links);
        Assert.Equal("TOOL_LEGACY_LINK_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,native.Etag))).Message);
        await svc.LinkedCommandAsync(command with{Links=new()},native.Etag);Assert.Equal(native.Reference,(await LinkedTools(h).ReadAsync(id)).Reference);
        var view=(await svc.ReadAsync(id))!;Assert.Equal("LEGACY_UNMAPPED",view.Links!.CustomerState);Assert.Equal("500",view.CurrentCycleShots);Assert.Null(view.LifetimeShots);
        var old=await NewRegistered(h);old=old with{CreationAuthorityVersion=(await svc.CreationContextAsync()).AuthorityVersion};var result=await svc.CommandAsync(old,null);Assert.Equal("tool.registration.v2",result.InputContract);
        Assert.Equal(result,await svc.CommandAsync(old,null));Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(new(old,new()),null))).Status);
        var closed=Guid.NewGuid();await svc.OperationAsync(closed,true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(new(newCommand.Registration with{OperationId=closed},new()),null))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_area_grant_allows_selected_location_but_unlocated_and_wrong_tenant_are_denied()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);
        var loc=(await svc.LocationsAsync(command.Registration.Site.Id,null)).Single(x=>x.Reference.Id==command.Links.Location!.Location.Id);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,loc.WarehouseCd).SetProperty(x=>x.AreaCd,loc.AreaCd));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command with{Links=command.Links with{Location=null}},null))).Status);
        Assert.Equal(0,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        await svc.LinkedCommandAsync(command,null);Assert.Single((await LinkedTools(h).ListAsync(new())).Items);
        await using var foreign=await Seed();var other=await LinkedCommand(foreign);
        var bad=other with{Links=other.Links with{Customer=command.Links.Customer}};
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>LinkedRegistrations(foreign).LinkedCommandAsync(bad,null))).Status);
        Assert.Equal(0,await foreign.Db.WmsSequences.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_legacy_positive_bind_retains_names_location_count_and_original_ETag()
    {
        await using var h=await Seed();var candidate=await LinkedCommand(h);var svc=LinkedRegistrations(h);var id=await SeedToolInstance(h);
        var c=await h.Db.BusinessPartners.Select(x=>x.BpCd).SingleAsync();var p=await h.Db.ProductMasters.Select(x=>x.ProductCd).SingleAsync();
        var location=(await svc.LocationsAsync(candidate.Registration.Site.Id,null)).Single(x=>x.Reference.Id==candidate.Links.Location!.Location.Id);
        await h.Db.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CustomerCd,c).SetProperty(x=>x.ProductCd,p).SetProperty(x=>x.WarehouseCd,location.WarehouseCd).SetProperty(x=>x.LocationCd,location.LocationCd));
        var native=await LinkedTools(h).ReadAsync(id);var command=new ToolRegistrationCommandV1(Guid.NewGuid(),id,"BIND_LEGACY",candidate.Registration.Site,"Explicit native matching inspected",Expected:native.Reference);
        await svc.LinkedCommandAsync(new(command,candidate.Links),native.Etag);var after=await LinkedTools(h).ReadAsync(id);
        Assert.Equal(native.Etag,after.Etag);Assert.Equal(native.Reference,after.Reference);Assert.Equal(native.Source.GetRawText(),after.Source.GetRawText());
        var view=(await svc.ReadAsync(id))!;Assert.Equal("LINKED",view.Links!.CustomerState);Assert.Equal("LINKED",view.Links.LocationState);Assert.Equal("500",view.CurrentCycleShots);Assert.Null(view.LifetimeShots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_hidden_customer_name_never_materializes_and_each_source_query_revocation_hides_linked_rows()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="erp-business-partner",FieldName="BpName",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        command=command with{Registration=command.Registration with{CreationAuthorityVersion=(await svc.CreationContextAsync()).AuthorityVersion}};
        await svc.LinkedCommandAsync(command,null);Assert.Null(await h.Db.PlateMoldStocks.Select(x=>x.CustomerName).SingleAsync());
        foreach(var menu in new[]{9144,402,401})
        {
            await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu&&x.ActionCode=="query").ExecuteDeleteAsync();
            Assert.Equal(0,(await LinkedTools(h).ListAsync(new())).Total);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(command.Registration.OperationId,false))).Status);
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode="query"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            Assert.Equal(1,(await LinkedTools(h).ListAsync(new())).Total);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_department_source_UUID_bindings_filter_current_objects_without_rebinding_codes()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);await svc.LinkedCommandAsync(command,null);var admin=await MappingAdmin(h);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DeptId,admin.Child));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="erp-business-partner"||x.ResourceKey=="product").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,2));
        Assert.Equal(0,(await LinkedTools(h).ListAsync(new())).Total);
        await BindStandalone(h,admin,"business-partner",command.Links.Customer!.Id);Assert.Equal(0,(await LinkedTools(h).ListAsync(new())).Total);
        await BindStandalone(h,admin,"product-master",command.Links.Product!.Id);Assert.Equal(1,(await LinkedTools(h).ListAsync(new())).Total);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='business-partner' AND object_id={command.Links.Customer.Id}");
        Assert.Equal(0,(await LinkedTools(h).ListAsync(new())).Total);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.ReadAsync(command.Registration.ObjectId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_same_transaction_source_change_rolls_back_native_number_links_and_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);var reads=0;
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("FROM cp6_product_identity_v1.heads")||++reads!=2)return;
            await using var change=c.Connection!.CreateCommand();change.Transaction=c.Transaction;
            change.CommandText="UPDATE public.\"T_ProductMaster\" SET \"SetProductName\"='must rollback' WHERE \"Id\"=@id";
            change.Parameters.Add(new Npgsql.NpgsqlParameter("id",command.Links.Product!.Id));await change.ExecuteNonQueryAsync();
        };
        Assert.Equal("TOOL_PRODUCT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Message);hook.Before=null;
        Assert.Equal(2,reads);Assert.Equal(0,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(0,await h.Db.WmsSequences.CountAsync());
        Assert.Equal("Same label",await h.Db.ProductMasters.Select(x=>x.SetProductName).SingleAsync());Assert.Equal("UNKNOWN",(await svc.OperationAsync(command.Registration.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_tool_instance_v1.registration_links WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_blocks_real_source_writer_and_recovers_lost_commit_without_duplicate_effect()
    {
        var hook=new CommandHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var command=await LinkedCommand(h);var svc=LinkedRegistrations(h);
        Task<int>? writer=null;var started=new TaskCompletionSource();
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("FROM cp6_product_identity_v1.heads"))return;hook.Before=null;
            writer=Task.Run(async()=>{await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});started.SetResult();return await peer.ProductMasters.Where(x=>x.Id==command.Links.Product!.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SetProductName,"After commit"));});
            await started.Task;await Task.Delay(100);Assert.False(writer.IsCompleted);
        };
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.LinkedCommandAsync(command,null))).Message);
        Assert.Equal(1,await writer!);var recovered=await svc.OperationAsync(command.Registration.OperationId,false);Assert.Equal("COMMITTED",recovered.State);
        Assert.Equal(recovered.Result,await svc.LinkedCommandAsync(command,null));Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(1,await h.Db.WmsSequences.Select(x=>x.NextNo).SingleAsync());
        Assert.Equal("Same label",(await LinkedTools(h).ReadAsync(command.Registration.ObjectId)).Source.GetProperty("productName").GetString());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_actual_HTTP_selects_and_consumes_all_sources_and_denies_lost_source_authority()
    {
        await using var h=await Seed();var command=await LinkedCommand(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        foreach(var path in new[]{"registration-customers","registration-products","registration-locations?siteId="+command.Registration.Site.Id})
        {using var choice=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/specials/v1/tools/"+path));Assert.Equal(HttpStatusCode.OK,choice.StatusCode);Assert.True(choice.Headers.CacheControl?.NoStore);}
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/specials/v1/tools/creation-context"));using var json=JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        command=command with{Registration=command.Registration with{CreationAuthorityVersion=json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString()}};
        var request=OperationsRequest(cookies,HttpMethod.Post,"/api/wms/specials/v1/tools/linked-registration");request.Content=JsonContent.Create(command);
        using var response=await host.Send(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{command.Registration.ObjectId}/registration"));
        Assert.Equal(HttpStatusCode.OK,original.StatusCode);using var record=JsonDocument.Parse(await original.Content.ReadAsStringAsync());Assert.Equal(command.Links.Customer!.Id,record.RootElement.GetProperty("data").GetProperty("links").GetProperty("customer").GetProperty("id").GetGuid());
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==212&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{command.Registration.ObjectId}/registration"));Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_linked_registration_actual_HTTP_creates_v3_without_changing_old_request_contract()
    {
        await using var h=await Seed();await GrantRegistration(h);var command=await NewRegistered(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/specials/v1/tools/creation-context"));
        using var json=JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        command=command with{CreationAuthorityVersion=json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString()};
        var request=OperationsRequest(cookies,HttpMethod.Post,"/api/wms/specials/v1/tools/linked-registration");
        request.Content=JsonContent.Create(new{registration=command,links=new{customer=(object?)null,product=(object?)null,location=(object?)null}});
        using var response=await host.Send(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var created=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("tool.registration.v3",created.RootElement.GetProperty("data").GetProperty("inputContract").GetString());
        Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(0,await h.Db.Stocks.CountAsync());
    }
}

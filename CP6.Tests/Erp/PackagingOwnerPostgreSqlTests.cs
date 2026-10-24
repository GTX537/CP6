using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using CP6.Entity.DTOs.Wms;
using CP6.Core.Services.Common;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<OperationsOwnerV1> GrantPackaging(Harness h)
    {
        await GrantOperations(h);
        var menu=await h.Db.Sys_Menus.SingleOrDefaultAsync(x=>x.MenuId==413);
        if(menu==null)h.Db.Sys_Menus.Add(new(){MenuId=413,MenuKey="wms-packaging",MenuName="Packaging"});else menu.MenuKey="wms-packaging";
        if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==413&&x.ActionCode=="query"))h.Db.Sys_MenuActions.Add(new(){MenuId=413,ActionCode="query",ActionName="Read"});
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=413,ActionCode="query"});
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-packaging",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var c=new AuthorizationCatalogStoreV1();c.RegisterScope("wms-packaging","Packages",[1,2,3,4,5],1);
        c.RegisterFields("wms-packaging",new[]{"CarrierCd","Remarks","TrackingNo"}.Select(x=>new FieldRegistry.Field(x,x)));
        c.RegisterScope("wms-outbound-order","Outbound",[1,2,3,4,5],1);c.RegisterFields("wms-outbound-order",[new("UnitPrice","Price")]);c.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static async Task<Guid> SeedPackaging(Harness h,int count=1)
    {
        var parent=new OutboundOrder{OutboundNo="OUT1",OutboundType=2,Status=4,WebOrderNo=h.Number,WarehouseCd="W01",Creator="price-owner"};
        h.Db.OutboundOrders.Add(parent);h.Db.OutboundOrderDetails.Add(new(){OutboundNo="OUT1",LineNo=1,ProductCd="P",WarehouseCd="W01",LocationCd="L1",Creator="price-owner"});
        h.Db.ShippingPackages.AddRange(Enumerable.Range(0,count).Select(i=>new ShippingPackage{PackageNo="PKG"+i,OutboundNo="OUT1",CaseQty=3,
            TotalWeightKg=1234567.891m,TotalVolumeM3=123456.7891m,CarrierCd="Carrier ",TrackingNo="Actual ",Remarks="Private",
            DepartureTime=new DateTime(2026,10,1,9,30,0),Creator="price-owner"}));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return parent.Id;
    }
    [TenantManagementPostgresTheory]
    [InlineData("api/wms/shipping/packages")]
    [InlineData("api/wms/shipping/packages/SECRET")]
    public async Task Packaging_owner_legacy_HTTP_cannot_disclose_without_current_query(string path)
    {
        await using var h=await Seed();await GrantOperations(h);
        h.Db.ShippingPackages.Add(new(){PackageNo="SECRET",OutboundNo="ORPHAN",TrackingNo="Sensitive"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("Sensitive",await response.Content.ReadAsStringAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_original_parent_page_precision_and_tenant_are_preserved()
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);var parent=await SeedPackaging(h,63);var svc=new PackagingOwnerV1(h.Db,owner);
        h.Db.ShippingPackages.AddRange(new(){PackageNo="ORPHAN",OutboundNo="NONE"},new(){PackageNo="DELETED",OutboundNo="OUT1",IsDeleted=true});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var first=await svc.SearchAsync(new(){PageSize=50});var second=await svc.SearchAsync(new(){Page=2});
        Assert.Equal(63,first.Total);Assert.Equal(50,first.Items.Count);Assert.Equal(13,second.Items.Count);Assert.Equal(63,first.Items.Concat(second.Items).Select(x=>x.Id).Distinct().Count());
        var row=await svc.GetAsync("PKG0");Assert.Equal(parent,row.OutboundId);Assert.Equal("1234567.891",row.TotalWeightKg);Assert.Equal("123456.7891",row.TotalVolumeM3);
        Assert.Equal("Actual ",row.TrackingNo);Assert.Equal("UNPROVEN",row.FormalPackageState);Assert.Equal("NOT_RECORDED",row.DeliveryProofState);
        Assert.Equal(63,(await svc.SearchAsync(new(){TrackingNo="Actual ",From=new(2026,10,1),To=new(2026,10,2)})).Total);
        // Existing PostgreSQL business text uses bpchar PAD SPACE comparison; observation preserves raw bytes.
        Assert.Equal(63,(await svc.SearchAsync(new(){TrackingNo="Actual"})).Total);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync("ORPHAN"))).Status);
        await using var foreign=await Seed();await SeedPackaging(foreign);await foreign.Db.ShippingPackages.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PackageNo,"FOREIGN"));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync("FOREIGN"))).Status);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(413)] [InlineData(408)]
    public async Task Packaging_owner_requires_both_current_actions_even_empty_or_legacy_reads(int menu)
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);var svc=new PackagingOwnerV1(h.Db,owner);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync("GUESSED"))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.RejectLegacyAsync())).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_self_scope_requires_package_parent_and_every_line()
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);await SeedPackaging(h);var svc=new PackagingOwnerV1(h.Db,owner);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-packaging").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.ShippingPackages.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));Assert.Empty((await svc.SearchAsync(new())).Items);
        await h.Db.ShippingPackages.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"price-owner"));
        h.Db.OutboundOrderDetails.Add(new(){OutboundNo="OUT1",LineNo=2,ProductCd="P2",WarehouseCd="W01",Creator="other",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty((await svc.SearchAsync(new())).Items);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"price-owner"));
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync("PKG0"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_original_scope_and_actual_detail_warehouse_apply_before_count()
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);await SeedPackaging(h);var svc=new PackagingOwnerV1(h.Db,owner);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-outbound-order").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"other"));Assert.Equal(0,(await svc.SearchAsync(new())).Total);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"price-owner").SetProperty(x=>x.WarehouseCd,"W02"));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W01"));Assert.Empty((await svc.SearchAsync(new())).Items);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WarehouseCd,"W01"));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));Assert.Empty((await svc.SearchAsync(new())).Items);
        h.Db.Locations.Add(new(){WarehouseCd="W01",LocationCd="L1",AreaCd="A",LocationName="Allowed"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));Assert.Empty((await svc.SearchAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Packaging_owner_department_uses_only_trusted_original_source_and_no_standalone_guess(int scope)
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);await SeedPackaging(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,scope);var svc=new PackagingOwnerV1(h.Db,owner);
        await h.Db.Orders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.OrderDepartment,"sales "));Assert.Empty((await svc.SearchAsync(new())).Items);
        await SaveMapping(h,admin,await MappingCommand(h,admin));Assert.Single((await svc.SearchAsync(new())).Items);
        await h.Db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.WebOrderNo,(string?)null));Assert.Empty((await svc.SearchAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_hidden_fields_cannot_leak_through_projection_or_filter_counts()
    {
        await using var h=await Seed();var owner=await GrantPackaging(h);await SeedPackaging(h);var svc=new PackagingOwnerV1(h.Db,owner);
        foreach(var field in new[]{"CarrierCd","TrackingNo","Remarks"})h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-packaging",FieldName=field,Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var row=await svc.GetAsync("PKG0");Assert.Null(row.CarrierCd);Assert.Null(row.TrackingNo);Assert.Null(row.Remarks);Assert.False(row.TrackingReadable);
        Assert.Single((await svc.SearchAsync(new())).Items);
        foreach(var q in new[]{new PackagingQueryV1{TrackingNo=""},new(){CarrierCd=""}})Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(q))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_frozen_input_final_authority_and_native_context_are_required()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantPackaging(h);await SeedPackaging(h);var svc=new PackagingOwnerV1(h.Db,owner);var q=new PackagingQueryV1{PackageNo="PKG"};
        hook.After=c=>{if(c.CommandText.Contains("WITH field_subject")){q.PackageNo="NONE";hook.After=null;}return Task.CompletedTask;};Assert.Single((await svc.SearchAsync(q)).Items);
        var reads=0;hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject")&&++reads==4)throw new IOException("final original source failure");return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.GetAsync("PKG0"))).Status);hook.Before=null;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);
        await h.Db.ShippingPackages.FirstAsync();Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(new()))).Status);h.Db.ChangeTracker.Clear();
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PackagingOwnerV1(other,owner).SearchAsync(new()))).Status);
        foreach(var bad in new[]{new PackagingQueryV1{Page=0},new(){PageSize=201},new(){Page=int.MaxValue},new(){From=DateTime.UtcNow},new(){From=new(2026,10,2),To=new(2026,10,1)},new(){TrackingNo=new string('x',51)}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.SearchAsync(bad))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_raw_writer_cannot_change_parent_during_observation()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var owner=await GrantPackaging(h);await SeedPackaging(h);Task<int>? writer=null;var blocked=false;
        hook.After=async c=>
        {
            if(!c.CommandText.Contains("WITH field_subject"))return;hook.After=null;
            writer=Task.Run(async()=>{await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});return await db.OutboundOrders.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));});
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_OutboundOrder%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            blocked=true;Assert.False(writer.IsCompleted);
        };
        PackagingRecordV1? row=null;try{row=await new PackagingOwnerV1(h.Db,owner).GetAsync("PKG0");}finally{if(writer!=null)await writer;}
        Assert.True(blocked);Assert.Equal("PKG0",row!.PackageNo);Assert.Empty((await new PackagingOwnerV1(h.Db,owner).SearchAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Packaging_owner_actual_HTTP_version_projection_no_store_and_revocation()
    {
        await using var h=await Seed();await GrantPackaging(h);var parent=await SeedPackaging(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/owner/v1/packages"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var data=doc.RootElement.GetProperty("data");Assert.Equal("PACKAGING-OWNER/1",data.GetProperty("version").GetString());
        var row=data.GetProperty("items")[0];Assert.Equal(parent,row.GetProperty("outboundId").GetGuid());Assert.Equal("1234567.891",row.GetProperty("totalWeightKg").GetString());Assert.False(row.TryGetProperty("tenantId",out _));Assert.False(row.TryGetProperty("creator",out _));
        using var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/packages"));Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);
        using var oldDetail=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/packages/PKG0"));Assert.Equal(HttpStatusCode.Conflict,oldDetail.StatusCode);
        using var single=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/owner/v1/packages/PKG0"));Assert.Equal(HttpStatusCode.OK,single.StatusCode);
        using var invalid=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/owner/v1/packages?pageSize=201"));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==408&&x.ActionCode=="query").ExecuteDeleteAsync();
        using var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/shipping/owner/v1/packages/PKG0"));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}

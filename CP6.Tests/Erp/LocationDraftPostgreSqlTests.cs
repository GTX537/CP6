using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 LocationDraftAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();
        c.RegisterScope("wms-warehouse","warehouse",[1,2,3,4,5],1);c.RegisterFields("wms-warehouse",new[]{"WarehouseName","AddressText","ManagerCd","Remarks"}.Select(f=>new FieldRegistry.Field(f,f)));
        c.RegisterScope("wms-location","location",[1,2,3,4,5],1);c.RegisterFields("wms-location",new[]{"LocationName","Barcode"}.Select(f=>new FieldRegistry.Field(f,f)));
        c.RegisterScope("space-site","site",[5],5);c.RegisterFields("space-site",[]);c.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static LocationDraftOwnerV1 LocationDrafts(Harness h,CP6Context? db=null)=>new(db??h.Db,LocationDraftAuthority(h,db));
    private static async Task<(Guid Warehouse,WarehouseDraftCommandV1 WarehouseCommand,LocationDraftCommandV1 Command)> LocationDraftSetup(Harness h)
    {
        var c=await NewWarehouseDraft(h);var w=await WarehouseDrafts(h).CreateAsync(c);
        await EngineeringAction(h,"wms-warehouse","hierarchy-policy");await Hierarchy(h).SaveAsync(w.WarehouseId,Policy());
        var ctx=await LocationDrafts(h).ContextAsync(w.WarehouseId);
        return(w.WarehouseId,c,new(Guid.NewGuid(),ctx.Creation.AuthorityVersion,null,ctx.Policy.Reference.Version,ctx.HierarchyRevision,"ZONE","Private name",null,1,null));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_native_initializer_is_atomic_hidden_from_legacy_reads_and_name_owner_preserves_isolation()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var o=LocationDrafts(h);var r=await o.CreateAsync(f.Warehouse,f.Command);
        Assert.Equal(r,await o.CreateAsync(f.Warehouse,f.Command));var v=await o.ReadAsync(f.Warehouse,r.LocationId);
        Assert.Equal("DRAFT",v.Lifecycle);Assert.Equal("QUARANTINED",v.InitializationState);Assert.Equal("TECHNICAL_PLACEHOLDER",v.LegacyCapacityKind);
        Assert.Null(v.ReservedCapacityQty);Assert.Equal("UNKNOWN",v.ReservationKnowledge);Assert.Equal("0",v.SelectorRevision);Assert.False(v.AdmissionGrant);
        var native=await h.Db.Locations.IgnoreQueryFilters().SingleAsync(x=>x.Id==r.LocationId);
        Assert.True(native.IsBlocked);Assert.False(native.IsPickable);Assert.Equal(0,native.CapacityQty);Assert.Equal(0,native.ReservedCapacityQty);Assert.Null(native.AreaCd);Assert.Null(native.Barcode);h.Db.ChangeTracker.Clear();
        Assert.False(await h.Db.Locations.AnyAsync());Assert.Null(await h.Db.Locations.FindAsync(r.LocationId));
        Assert.Empty(await new WmsStockQuery(h.Db).GetStockByLocationsAsync([f.Command.LocationCode]));
        var names=new WarehouseOwnerServiceV1(h.Db,LocationDraftAuthority(h));Assert.Empty((await names.LocationsAsync(f.Warehouse,new())).Items);
        var rename=await names.RenameAsync("locations",r.LocationId,Guid.NewGuid(),r.Etag,SetMasterName("Later"));
        var noop=await names.RenameAsync("locations",r.LocationId,Guid.NewGuid(),rename.Etag,SetMasterName("Later"));Assert.Equal(rename.Etag,noop.Etag);
        v=await o.ReadAsync(f.Warehouse,r.LocationId);Assert.Equal("2",v.BusinessRevision);Assert.Equal("Later",v.Name);
        Assert.Equal("2",(await Hierarchy(h).ContextAsync(f.Warehouse)).HierarchyRevision);Assert.False(await h.Db.Locations.AnyAsync());
        await RevokeTransferAction(h,"wms-location","add");Assert.Equal(r,await o.CreateAsync(f.Warehouse,f.Command));Assert.Equal(r,(await o.OperationAsync(f.Warehouse,f.Command.OperationId)).Result);
        await RevokeTransferAction(h,"wms-location","query");Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(f.Warehouse,f.Command.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_draft_parent_native_mirror_policy_versions_and_authorized_tree_are_current()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var o=LocationDrafts(h);var p=await o.CreateAsync(f.Warehouse,f.Command);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),LocationCode="CHILD",ParentLocationId=p.LocationId,LocationLevel=2}))).Status);
        var c=f.Command with{OperationId=Guid.NewGuid(),LocationCode="CHILD",ParentLocationId=p.LocationId,LocationLevel=4,ExpectedHierarchyRevision="2"};
        var child=await o.CreateAsync(f.Warehouse,c);Assert.Equal(p.LocationId,(await o.ReadAsync(f.Warehouse,child.LocationId)).ParentId);
        var n=await h.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==child.LocationId);Assert.Equal("ZONE",n.ParentLocationCd);
        Assert.Equal("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Hierarchy(h).SaveAsync(f.Warehouse,Policy("1","3") with{Edges=[new(1,2)]}))).Message);
        Assert.True((await Hierarchy(h).PrecheckAsync(f.Warehouse,new("1","3",p.LocationId,2))).StructureValid);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));Assert.Empty(await o.ListAsync(f.Warehouse));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(f.Warehouse))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_code_domain_preserves_cross_warehouse_identity_and_old_namespace()
    {
        await using var h=await Seed();h.Db.Warehouses.Add(new(){WarehouseCd="OLD",WarehouseName="Old"});h.Db.Locations.Add(new(){WarehouseCd="OLD",LocationCd="é-X",CapacityQty=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var f=await LocationDraftSetup(h);var o=LocationDrafts(h);var c=f.Command with{LocationCode=" e\u0301-x "};var r=await o.CreateAsync(f.Warehouse,c);
        Assert.Equal("é-x",(await o.ReadAsync(f.Warehouse,r.LocationId)).Code);
        var second=await WarehouseDrafts(h).CreateAsync(f.WarehouseCommand with{OperationId=Guid.NewGuid(),WarehouseCode="OTHER",AuthorityVersion=(await WarehouseDrafts(h).ContextAsync()).Creation.AuthorityVersion});
        await Hierarchy(h).SaveAsync(second.WarehouseId,Policy());var context=await o.ContextAsync(second.WarehouseId);
        var r2=await o.CreateAsync(second.WarehouseId,c with{OperationId=Guid.NewGuid(),AuthorityVersion=context.Creation.AuthorityVersion});Assert.NotEqual(r.LocationId,r2.LocationId);
        Assert.Equal("DUPLICATE_CODE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,c with{OperationId=Guid.NewGuid(),LocationCode="É-X",ExpectedHierarchyRevision="2"}))).Message);
        Assert.Single(await h.Db.Locations.ToArrayAsync());Assert.Equal(3,(await new WmsStockQuery(h.Db).GetStockByLocationsAsync(["é-X"])).Single().Capacity);h.Db.ChangeTracker.Clear();
        var max=string.Concat(Enumerable.Repeat("𐐀",15));await o.CreateAsync(f.Warehouse,c with{OperationId=Guid.NewGuid(),LocationCode=max,ExpectedHierarchyRevision="2"});
        Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,c with{OperationId=Guid.NewGuid(),LocationCode=max+"x"}))).Status);
        await Assert.ThrowsAnyAsync<Exception>(async()=>{h.Db.Locations.Add(new(){WarehouseCd="OTHER-OLD",LocationCd="É-X"});try{await h.Db.SaveChangesAsync();}finally{h.Db.ChangeTracker.Clear();}});
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_database_keeps_quarantine_and_rejects_real_stock_task_bin_and_raw_initialization_writes()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var r=await LocationDrafts(h).CreateAsync(f.Warehouse,f.Command);
        foreach(var set in new[]{"\"IsInitializationQuarantined\"=false","\"IsBlocked\"=false","\"IsPickable\"=true","\"CapacityQty\"=99","\"ReservedCapacityQty\"=1","\"WarehouseCd\"='OTHER'","\"LocationName\"='Raw'","\"IsDeleted\"=true"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE public.\"T_Location\" SET "+set+" WHERE \"Id\"={0}",r.LocationId));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM public.\"T_Location\" WHERE \"Id\"={0}",r.LocationId));
        foreach(var row in new object[]{new Stock{WarehouseCd=f.WarehouseCommand.WarehouseCode,LocationCd="ZONE",ProductCd="P",LotNo=""},
            new MobileTask{MobileTaskNo="BAD",WarehouseCd=f.WarehouseCommand.WarehouseCode,FromLocationCd="ZONE",ToLocationCd="ZONE",TaskType="MOVE"},
            new WmsBin{WarehouseCd=f.WarehouseCommand.WarehouseCode,LocationCode="ZONE",Version=1},
            new Location{WarehouseCd=f.WarehouseCommand.WarehouseCode,LocationCd="RAW",IsInitializationQuarantined=true,IsBlocked=true,IsPickable=false}})
        {h.Db.Add(row);await Assert.ThrowsAnyAsync<Exception>(()=>h.Db.SaveChangesAsync());h.Db.ChangeTracker.Clear();}
        Assert.False(await h.Db.Stocks.AnyAsync());Assert.False(await h.Db.MobileTasks.AnyAsync());Assert.Single(await LocationDrafts(h).ListAsync(f.Warehouse));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_missing_or_unregistered_actual_consumer_fence_has_zero_formal_effect()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var o=LocationDrafts(h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Stock\" DISABLE TRIGGER warehouse_draft_use");
        try{Assert.Equal("INITIALIZATION_FENCE_UNSUPPORTED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Stock\" ENABLE TRIGGER warehouse_draft_use");}
        await h.Db.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"W208Unregistered\"(\"TenantId\" uuid,\"WarehouseCd\" text)");
        try{Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command))).Status);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TABLE public.\"W208Unregistered\"");}
        Assert.False(await h.Db.Locations.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==h.Tenant));Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Warehouse,f.Command.OperationId)).State);
        await o.CreateAsync(f.Warehouse,f.Command);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_current_authority_fields_sources_and_final_revoke_roll_back_every_effect()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await LocationDraftSetup(h);var o=LocationDrafts(h);
        Assert.Equal("LOCATION_BARCODE_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command with{Barcode=new string('b',50)}))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location",FieldName="LocationName",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var c=f.Command with{AuthorityVersion=(await o.ContextAsync(f.Warehouse)).Creation.AuthorityVersion};
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,c))).Message);
        c=c with{LocationName=null};var revoked=false;
        hook.Before=async command=>
        {if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;revoked=true;
            await using var sql=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"MenuId\"=402 AND \"RoleId\"=72 AND \"ActionCode\"='add'",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);sql.Parameters.AddWithValue("tenant",h.Tenant);await sql.ExecuteNonQueryAsync();};
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,c))).Status);Assert.True(revoked);
        Assert.False(await h.Db.Locations.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==h.Tenant));Assert.Equal("1",(await Hierarchy(h).ContextAsync(f.Warehouse)).HierarchyRevision);
        Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Warehouse,c.OperationId)).State);
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task LocationDraft_new_department_binding_obeys_current_scope_and_preserves_foreign_tenant_isolation(int type)
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var admin=await MappingAdmin(h);await BindStandalone(h,admin,"warehouse",f.Warehouse);
        await StandaloneScope(h,admin,type);var o=LocationDrafts(h);var context=await o.ContextAsync(f.Warehouse);var dept=context.Creation.Departments.FirstOrDefault()?.Id;
        var c=f.Command with{AuthorityVersion=context.Creation.AuthorityVersion,DepartmentId=dept};
        if(!context.Creation.AllowsUnbound)Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,c with{DepartmentId=null}))).Status);
        var r=await o.CreateAsync(f.Warehouse,c);Assert.Equal(r.LocationId,Assert.Single(await o.ListAsync(f.Warehouse)).Id);
        await using var other=await Seed();await GrantMaster(other);await EngineeringAction(other,"space-site","query");
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>LocationDrafts(other).ReadAsync(f.Warehouse,r.LocationId))).Status);
        if(type is 2 or 3 or 4){await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_id={r.LocationId}");Assert.Empty(await o.ListAsync(f.Warehouse));}
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_unknown_close_and_create_race_cannot_revive_or_repeat_native_identity()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var o=LocationDrafts(h);var id=Guid.NewGuid();Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Warehouse,id)).State);
        Assert.Equal("CLOSED",(await o.OperationAsync(f.Warehouse,id,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command with{OperationId=id}))).Message);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var create=Task.Run(async()=>{try{return await o.CreateAsync(f.Warehouse,f.Command);}catch(OrderPriceCorrectionException e)when(e.Message=="ORIGINAL_OPERATION_CLOSED"){return null;}});
        var close=Task.Run(()=>LocationDrafts(h,other).OperationAsync(f.Warehouse,f.Command.OperationId,true));await Task.WhenAll(create,close);
        var result=await o.OperationAsync(f.Warehouse,f.Command.OperationId);Assert.Equal(result.State,(await close).State);
        Assert.Equal(result.State=="COMMITTED"?1:0,await h.Db.Locations.IgnoreQueryFilters().CountAsync(x=>x.TenantId==h.Tenant));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_concurrent_creators_recheck_one_native_hierarchy_predecessor()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        async Task<int> Create(LocationDraftOwnerV1 o,LocationDraftCommandV1 c)
        {try{await o.CreateAsync(f.Warehouse,c);return 200;}catch(OrderPriceCorrectionException e){return e.Status;}}
        var one=Task.Run(()=>Create(LocationDrafts(h),f.Command));
        var two=Task.Run(()=>Create(LocationDrafts(h,other),f.Command with{OperationId=Guid.NewGuid(),LocationCode="SECOND"}));
        var outcomes=await Task.WhenAll(one,two);Assert.Equal(new[]{200,412},outcomes.Order().ToArray());
        Assert.Single(await LocationDrafts(h).ListAsync(f.Warehouse));Assert.Equal("2",(await Hierarchy(h).ContextAsync(f.Warehouse)).HierarchyRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_unscoped_ink_and_scan_alias_writes_cannot_adopt_quarantine_or_dangling_codes()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var o=LocationDrafts(h);
        h.Db.InkLots.Add(new(){InkLotNo="ORPHAN",ColorCode="C",Quantity=1,ExpiryDate=DateTime.Today.AddDays(10),LocationCd="ORPHAN"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("LOCATION_CODE_HAS_LEGACY_REFERENCES",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(f.Warehouse,f.Command with{LocationCode="ORPHAN"}))).Message);
        await o.CreateAsync(f.Warehouse,f.Command);
        foreach(var row in new object[]{new InkLot{InkLotNo="BAD",ColorCode="C",Quantity=1,ExpiryDate=DateTime.Today.AddDays(10),LocationCd="ZONE"},
            new BarcodeAlias{Barcode="BAD",BarcodeType="LOCATION",TargetKey="ZONE"}})
        {h.Db.Add(row);await Assert.ThrowsAnyAsync<Exception>(()=>h.Db.SaveChangesAsync());h.Db.ChangeTracker.Clear();}
        h.Db.BarcodeAliases.Add(new(){Barcode="PRODUCT",BarcodeType="PRODUCT",TargetKey="ZONE"});
        h.Db.Locations.Add(new(){WarehouseCd="OLD",LocationCd="SHARED"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await o.CreateAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),LocationCode="SHARED",ExpectedHierarchyRevision="2"});
        h.Db.InkLots.Add(new(){InkLotNo="VALID",ColorCode="C",Quantity=1,ExpiryDate=DateTime.Today.AddDays(10),LocationCd="SHARED"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(2,await h.Db.InkLots.CountAsync());Assert.Equal(2,(await o.ListAsync(f.Warehouse)).Count);
        await h.Db.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"W208LocationOnly\"(\"TenantId\" uuid,\"LocationId\" uuid)");
        try{Assert.Equal("INITIALIZATION_FENCE_UNSUPPORTED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(f.Warehouse))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TABLE public.\"W208LocationOnly\"");}
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_InkLot\" DISABLE TRIGGER location_draft_unscoped");
        try{Assert.Equal("INITIALIZATION_FENCE_UNSUPPORTED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ContextAsync(f.Warehouse))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_InkLot\" ENABLE TRIGGER location_draft_unscoped");}
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_waiting_unscoped_writer_rechecks_committed_quarantine_after_creator_releases_lock()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await LocationDraftSetup(h);Task<Exception?>? writer=null;var waited=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;
            writer=Task.Run(async()=>
            {
                await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                other.InkLots.Add(new(){InkLotNo="WAITING",ColorCode="C",Quantity=1,ExpiryDate=DateTime.Today.AddDays(10),LocationCd="ZONE"});
                try{await other.SaveChangesAsync();return null;}catch(Exception e){return e;}
            });
            await Task.Delay(150);waited=!writer.IsCompleted;
        };
        await LocationDrafts(h).CreateAsync(f.Warehouse,f.Command);Assert.True(waited);Assert.NotNull(writer);Assert.NotNull(await writer!);
        Assert.False(await h.Db.InkLots.AnyAsync());Assert.Single(await LocationDrafts(h).ListAsync(f.Warehouse));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationDraft_real_program_HTTP_closed_DTO_required_parent_level_and_durable_result()
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        var root=$"api/wms/warehouses/{f.Warehouse}/location-drafts-v1";
        using var ctx=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/creation-context"));Assert.Equal(HttpStatusCode.OK,ctx.StatusCode);
        using var doc=JsonDocument.Parse(await ctx.Content.ReadAsStringAsync());var c=f.Command with{AuthorityVersion=doc.RootElement.GetProperty("data").GetProperty("creation").GetProperty("authorityVersion").GetString()!};
        var body=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        foreach(var name in new[]{"tenantId","warehouseId","capacityQty","isBlocked","siteId"})
        {var bad=body.DeepClone().AsObject();bad[name]="forged";using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,bad));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        foreach(var name in new[]{"parentLocationId","locationLevel"})
        {var bad=body.DeepClone().AsObject();bad.Remove(name);using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,bad));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,body));Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl?.NoStore);Assert.NotNull(created.Headers.ETag);
        await RevokeTransferAction(h,"wms-location","add");using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+$"/operations/{c.OperationId}"));Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Contains("COMMITTED",await recovered.Content.ReadAsStringAsync());
    }
}

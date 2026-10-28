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
    private static OperationsOwnerV1 DraftAuthority(Harness h,CP6Context? db=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-warehouse","warehouse",[1,2,3,4,5],1);
        catalog.RegisterFields("wms-warehouse",new[]{"AddressText","ManagerCd","Remarks","WarehouseName"}.Select(x=>new FieldRegistry.Field(x,x)));
        catalog.RegisterScope("space-site","site",[5],5);catalog.RegisterFields("space-site",[]);catalog.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static WarehouseDraftOwnerV1 WarehouseDrafts(Harness h,CP6Context? db=null)=>new(db??h.Db,DraftAuthority(h,db));
    private static async Task<WarehouseDraftCommandV1> NewWarehouseDraft(Harness h,string code="DRAFT-1",Guid? department=null)
    {
        await GrantMaster(h);await EngineeringAction(h,"space-site","query");var site=await PhysicalSite(h,"SITE");
        return new(Guid.NewGuid(),(await WarehouseDrafts(h).ContextAsync()).Creation.AuthorityVersion,department,site,code,"Private warehouse",WarehouseType.RawMaterial,null,"Private address",null,"Line1\nLine2");
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_real_create_original_rename_and_noop_keep_one_identity_and_revision()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h," e\u0301-x ");var o=WarehouseDrafts(h);
        var r=await o.CreateAsync(c);Assert.Equal(r,await o.CreateAsync(c));Assert.Equal("1",r.BusinessRevision);
        var v=await o.ReadAsync(r.WarehouseId);Assert.Equal("é-x",v.Warehouse.WarehouseCd);Assert.Equal(c.SiteId,v.SiteRef.Id);
        Assert.Equal("DRAFT",v.Lifecycle);Assert.False(v.AdmissionGrant);Assert.Equal("NOT_ACTIVATED",v.Eligibility);
        Assert.Single(await o.ListAsync());Assert.False(await h.Db.Stocks.AnyAsync());Assert.False(await h.Db.Locations.AnyAsync());
        var names=new WarehouseOwnerServiceV1(h.Db,DraftAuthority(h));
        var changed=await names.RenameAsync("warehouses",r.WarehouseId,Guid.NewGuid(),r.Etag,SetMasterName("Later"));
        Assert.Equal("2",(await o.ReadAsync(r.WarehouseId)).BusinessRevision);
        var noop=await names.RenameAsync("warehouses",r.WarehouseId,Guid.NewGuid(),changed.Etag,SetMasterName("Later"));
        Assert.Equal(changed.Etag,noop.Etag);Assert.Equal("2",(await o.ReadAsync(r.WarehouseId)).BusinessRevision);
        await RevokeTransferAction(h,"wms-warehouse","add");Assert.Equal(r,await o.CreateAsync(c));
        Assert.Equal(r,(await o.OperationAsync(c.OperationId)).Result);
        Assert.DoesNotContain("Private",JsonSerializer.Serialize(await o.OperationAsync(c.OperationId)));
        await RevokeTransferAction(h,"wms-warehouse","query");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_canonical_conflicts_include_other_sites_and_deleted_legacy_anchors()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);
        h.Db.Warehouses.Add(new(){WarehouseCd="é-X",WarehouseName="Reserved",IsDeleted=true});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var o=WarehouseDrafts(h);
        Assert.Equal("DUPLICATE_CODE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{WarehouseCode=" E\u0301-x "}))).Message);
        var r=await o.CreateAsync(c);var site=await PhysicalSite(h,"OTHER");
        Assert.Equal("DUPLICATE_CODE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{OperationId=Guid.NewGuid(),SiteId=site,WarehouseCode="draft-1"}))).Message);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{WarehouseName="Changed"}))).Message);
        Assert.Single(await o.ListAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_missing_sources_fields_and_current_creation_scope_have_zero_effect()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);var o=WarehouseDrafts(h);
        foreach(var bad in new[]{c with{WarehouseType=99},c with{BaseCode="UNPROVEN"},c with{ManagerCode=new string('a',20)},c with{SiteId=Guid.NewGuid()}})
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(bad));
        await h.Db.Space_Sites.Where(x=>x.Id==c.SiteId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,false));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c));
        await h.Db.Space_Sites.Where(x=>x.Id==c.SiteId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,true));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-warehouse",FieldName="AddressText",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c))).Message);
        var clean=c with{Address=null,AuthorityVersion=(await o.ContextAsync()).Creation.AuthorityVersion};
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(clean))).Status);
        Assert.False(await h.Db.Warehouses.AnyAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId)).State);
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task WarehouseDraft_current_department_scope_is_bound_by_the_actual_add_transaction(int type)
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,type);
        var o=WarehouseDrafts(h);var context=await o.ContextAsync();var department=context.Creation.Departments.FirstOrDefault()?.Id;
        c=c with{AuthorityVersion=context.Creation.AuthorityVersion,DepartmentId=department};
        if(!context.Creation.AllowsUnbound)
            Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{DepartmentId=null}))).Message);
        var r=await o.CreateAsync(c);Assert.Equal(r.WarehouseId,Assert.Single(await o.ListAsync()).Warehouse.Id);
        if(department!=null)
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_id={r.WarehouseId}");
            if(type is 2 or 3 or 4)Assert.Empty(await o.ListAsync());
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_database_blocks_raw_use_identity_rewrites_and_unjournalled_names()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);var o=WarehouseDrafts(h);var r=await o.CreateAsync(c);
        await Assert.ThrowsAnyAsync<Exception>(async()=>{h.Db.Locations.Add(new(){WarehouseCd=c.WarehouseCode,LocationCd="L"});try{await h.Db.SaveChangesAsync();}finally{h.Db.ChangeTracker.Clear();}});
        await Assert.ThrowsAnyAsync<Exception>(async()=>{h.Db.Stocks.Add(new(){WarehouseCd=c.WarehouseCode,LocationCd="L",ProductCd="P",LotNo=""});try{await h.Db.SaveChangesAsync();}finally{h.Db.ChangeTracker.Clear();}});
        foreach(var sql in new[]{"UPDATE public.\"T_Warehouse\" SET \"WarehouseName\"='Raw' WHERE \"Id\"={0}",
            "UPDATE public.\"T_Warehouse\" SET \"IsDeleted\"=true WHERE \"Id\"={0}",
            "DELETE FROM public.\"T_Warehouse\" WHERE \"Id\"={0}",
            "UPDATE cp6_warehouse_draft_v1.warehouses SET lifecycle='ACTIVE' WHERE id={0}"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql,r.WarehouseId));
        await Assert.ThrowsAnyAsync<Exception>(async()=>{h.Db.Warehouses.Add(new(){WarehouseCd="OTHER",WarehouseName="Raw"});try{await h.Db.SaveChangesAsync();}finally{h.Db.ChangeTracker.Clear();}});
        Assert.Equal(c.WarehouseName,(await o.ReadAsync(r.WarehouseId)).Warehouse.WarehouseName);Assert.Equal("1",(await o.ReadAsync(r.WarehouseId)).BusinessRevision);
        Assert.False(await h.Db.Locations.AnyAsync());Assert.False(await h.Db.Stocks.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_lost_ack_close_and_create_race_have_one_durable_result()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);var o=WarehouseDrafts(h);
        Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId)).State);
        var id=Guid.NewGuid();Assert.Equal("CLOSED",(await o.OperationAsync(id,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{OperationId=id}))).Message);
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var create=Task.Run(async()=>{try{return await o.CreateAsync(c);}catch(OrderPriceCorrectionException e)when(e.Message=="ORIGINAL_OPERATION_CLOSED"){return null;}});
        var close=Task.Run(()=>WarehouseDrafts(h,other).OperationAsync(c.OperationId,true));await Task.WhenAll(create,close);
        var result=await o.OperationAsync(c.OperationId);Assert.Equal(result.State,(await close).State);
        Assert.Equal(result.State=="COMMITTED"?1:0,await h.Db.Warehouses.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_final_native_permission_revocation_rolls_back_warehouse_and_original()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var c=await NewWarehouseDraft(h);var o=WarehouseDrafts(h);var revoked=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;revoked=true;
            await using var revoke=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"MenuId\"=401 AND \"RoleId\"=72 AND \"ActionCode\"='add'",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);
            revoke.Parameters.AddWithValue("tenant",h.Tenant);await revoke.ExecuteNonQueryAsync();
        };
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c))).Status);Assert.True(revoked);
        Assert.False(await h.Db.Warehouses.AnyAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_dangling_legacy_codes_and_missing_use_fence_prevent_creation()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);var o=WarehouseDrafts(h);
        h.Db.Locations.Add(new(){WarehouseCd=c.WarehouseCode,LocationCd="ORPHAN"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("WAREHOUSE_CODE_HAS_LEGACY_REFERENCES",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c))).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Stock\" DISABLE TRIGGER warehouse_draft_use");
        try{await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(c with{WarehouseCode="OTHER"}));}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_Stock\" ENABLE TRIGGER warehouse_draft_use");}
        Assert.False(await h.Db.Warehouses.AnyAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_rejects_foreign_context_and_foreign_site_without_aliasing_identity()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);await using var other=await Seed();var foreign=await PhysicalSite(other,"FOREIGN");
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>WarehouseDrafts(h).CreateAsync(c with{SiteId=foreign}));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new WarehouseDraftOwnerV1(other.Db,DraftAuthority(h)).CreateAsync(c));
        Assert.False(await h.Db.Warehouses.AnyAsync());Assert.False(await other.Db.Warehouses.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_waiting_legacy_stock_writer_rechecks_committed_draft_after_lock_release()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var c=await NewWarehouseDraft(h);Task<Exception?>? writer=null;var waited=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;
            writer=Task.Run(async()=>
            {
                await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                db.Stocks.Add(new(){WarehouseCd=c.WarehouseCode,LocationCd="L",ProductCd="P",LotNo=""});
                try{await db.SaveChangesAsync();return null;}catch(Exception e){return e;}
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                  AND query LIKE '%INSERT INTO%T_Stock%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            waited=true;Assert.False(writer.IsCompleted);
        };
        try{await WarehouseDrafts(h).CreateAsync(c);}finally{if(writer!=null)await writer;}
        Assert.True(waited);Assert.NotNull(writer);Assert.Contains("WAREHOUSE_NOT_ACTIVATED",(await writer)!.ToString());
        Assert.False(await h.Db.Stocks.AnyAsync());Assert.Single(await WarehouseDrafts(h).ListAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseDraft_actual_HTTP_closed_input_required_type_and_current_read_recovery()
    {
        await using var h=await Seed();var c=await NewWarehouseDraft(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var ctx=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"api/wms/master/drafts-v1/creation-context"));Assert.Equal(HttpStatusCode.OK,ctx.StatusCode);
        using var context=JsonDocument.Parse(await ctx.Content.ReadAsStringAsync());c=c with{AuthorityVersion=context.RootElement.GetProperty("data").GetProperty("creation").GetProperty("authorityVersion").GetString()!};
        var body=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        foreach(var key in new[]{"tenantId","lifecycle","allowNegative"})
        {var bad=body.DeepClone().AsObject();bad[key]="forged";using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/master/drafts-v1/warehouses",bad));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        var missing=body.DeepClone().AsObject();missing.Remove("warehouseType");using var rejected=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/master/drafts-v1/warehouses",missing));Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        using var created=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/master/drafts-v1/warehouses",body));Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl?.NoStore);Assert.NotNull(created.Headers.ETag);
        await RevokeTransferAction(h,"wms-warehouse","add");using var original=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"api/wms/master/drafts-v1/operations/{c.OperationId}"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Contains("COMMITTED",await original.Content.ReadAsStringAsync());
        using var legacy=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/wms/warehouse",new{warehouseCd="BYPASS",warehouseName="Raw"}));Assert.NotEqual(HttpStatusCode.OK,legacy.StatusCode);
    }
}

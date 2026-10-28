using System.Net;
using System.Text.Json;
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
    private static OperationsOwnerV1 RuleReferenceAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-warehouse",new[]{"WarehouseName","AddressText","ManagerCd","Remarks"}),
            ("wms-location",new[]{"LocationName","Barcode"}),("wms-location-rule",LocationRuleValuesV1.Fields),("uom-profile",Array.Empty<string>())})
        {c.RegisterScope(key,key,[1,2,3,4,5],1);c.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}c.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static LocationRuleReferencesOwnerV1 RuleReferences(Harness h,CP6Context? db=null)=>new(db??h.Db,RuleReferenceAuthority(h,db));
    private static LocationRuleOwnerV1 ReferenceRules(Harness h,CP6Context? db=null)=>new(db??h.Db,RuleReferenceAuthority(h,db));
    private static async Task<(Guid Location,Guid Warehouse,UomProfileVersionV1 Unit,RuleReferenceCommandV1 Command)> RuleReferencesSetup(Harness h,UomProfileInputV1? unit=null)
    {
        var location=await RuleSetup(h);var code=await h.Db.Locations.IgnoreQueryFilters().Where(x=>x.TenantId==h.Tenant&&x.Id==location).Select(x=>x.WarehouseCd).SingleAsync();
        var warehouse=await h.Db.Warehouses.Where(x=>x.WarehouseCd==code).Select(x=>x.Id).SingleAsync();
        await GrantUomProfile(h);await EngineeringAction(h,"wms-warehouse","rule-policy");await EngineeringAction(h,"wms-location-rule","resolve");
        var u=await MakeUnit(Units(h),unit??BaseUnit("EA","COUNT",8));
        var d=new RuleReferenceDefinitionV1(true,[new("unit-count","COUNT",[u.Reference])],[new("qty-8",8,"REJECT")],
            [new("storage",["LOCATION_OPERATION"],["unit-count"],["qty-8"],["RAW"],["RECEIVE","PICK"],true,true,true,true,true)]);
        return(location,warehouse,u,new(Guid.NewGuid(),"absent",d,"明确本仓库规则语义"));
    }
    private static LocationRuleDraftV1 ResolvableRule(string value="10")=>RuleDraft(value) with{ValidFrom="2026-10-01T08:00:00+08:00"};
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_native_UOM_originals_resolve_new_candidate_version_without_mutating_old_or_native_state()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);
        var saved=await p.SaveAsync(f.Warehouse,f.Command);var policy=(await p.ContextAsync(f.Warehouse)).Policy!;
        Assert.Equal(f.Unit.Reference,Assert.Single(policy.Original.Units).Reference);Assert.Equal(f.Unit.Reference,policy.Original.Units[0].BaseReference);
        var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule("9999999999999.99999999")));
        var original=(await o.ReadAsync(f.Location,c.Reference.Id)).OriginalJson;
        var resolved=await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),saved.Reference));
        Assert.Equal("2",resolved.Reference.Version);Assert.Equal(c.SelectorId,resolved.SelectorId);Assert.Equal("RESOLVED",resolved.Effect);
        var view=await o.ReadAsync(f.Location,c.Reference.Id);Assert.Equal("RESOLVED",view.ReferenceResolution);Assert.False(view.AdmissionGrant);
        Assert.Equal(f.Unit.Reference,Assert.Single(view.ReferenceBinding!.Units).Reference);Assert.Contains(view.ReferenceBinding.Fields,x=>x.Field=="uomRef"&&x.Owner=="Main.Erp.UomProfile.v1"&&x.Id=="EA");
        Assert.Contains("9999999999999.99999999",view.OriginalJson);Assert.Equal(original,(await o.ReadAsync(f.Location,c.Reference.Id,"1")).OriginalJson);
        var same=await o.ResolveAsync(f.Location,c.Reference.Id,resolved.Etag,new(Guid.NewGuid(),saved.Reference));Assert.Equal("NO_CHANGE",same.Effect);Assert.Equal(resolved.Reference,same.Reference);
        var patched=await o.PatchAsync(f.Location,c.Reference.Id,same.Etag,new(Guid.NewGuid(),RulePatch("""{"isPickable":{"op":"SET","value":true}}""")));
        Assert.Equal("UNRESOLVED",(await o.ReadAsync(f.Location,c.Reference.Id)).ReferenceResolution);Assert.Equal("3",patched.Reference.Version);
        Assert.Equal("RESOLVED",(await o.ReadAsync(f.Location,c.Reference.Id,"2")).ReferenceResolution);
        var native=await h.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==f.Location&&x.TenantId==h.Tenant);
        Assert.True(native.IsInitializationQuarantined);Assert.True(native.IsBlocked);Assert.False(native.IsPickable);Assert.Equal(0,native.CapacityQty);
        Assert.All((await o.ContextAsync(f.Location)).Selectors,s=>{Assert.Equal("0",s.Revision);Assert.Null(s.CurrentRuleVersionId);});
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_policy_change_and_disable_never_reinterpret_old_binding_or_reuse_selector_domain_version()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);var first=await p.SaveAsync(f.Warehouse,f.Command);
        var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule()));var r=await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),first.Reference));
        var original=(await o.ReadAsync(f.Location,c.Reference.Id)).OriginalJson;
        var revised=await p.SaveAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1",Definition=f.Command.Definition with{Precisions=[new("qty-8",2,"REJECT")]}});
        Assert.Equal("RULE_DICTIONARY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ResolveAsync(f.Location,c.Reference.Id,r.Etag,new(Guid.NewGuid(),first.Reference)))).Message);
        Assert.Equal("RULE_DOMAIN_REFERENCE_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ResolveAsync(f.Location,c.Reference.Id,r.Etag,new(Guid.NewGuid(),revised.Reference)))).Message);
        Assert.Equal(original,(await o.ReadAsync(f.Location,c.Reference.Id)).OriginalJson);
        var d=ResolvableRule() with{SelectorKey=ResolvableRule().SelectorKey with{ApplicabilityDomainRef=new("storage","2")},Capacity=new("LIMITED","10",new("unit-count","2"),new("EA","1"),new("qty-8","2"))};
        var newer=await o.CreateAsync(f.Location,new(Guid.NewGuid(),d));Assert.Equal(c.SelectorId,newer.SelectorId);
        await o.ResolveAsync(f.Location,newer.Reference.Id,newer.Etag,new(Guid.NewGuid(),revised.Reference));
        var current=(await p.ContextAsync(f.Warehouse)).Policy!;await p.SaveAsync(f.Warehouse,new(Guid.NewGuid(),"2",current.Original.Definition with{Enabled=false},"停用"));
        Assert.Equal("DISABLED",(await o.ContextAsync(f.Location)).ReferenceAuthorities);Assert.Equal(first.Reference,(await p.HistoryAsync(f.Warehouse,"1")).Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_real_unit_revision_disable_and_ratio_base_are_current_sources()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);var policy=await p.SaveAsync(f.Warehouse,f.Command);
        var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule()));
        await Units(h).CommandAsync(Revise(f.Unit,f.Unit.Profile with{Enabled=false}));
        Assert.Equal("UOM_PROFILE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),policy.Reference)))).Message);
        Assert.Equal("1",(await o.ReadAsync(f.Location,c.Reference.Id)).Reference.Version);
        await p.SaveAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1",Definition=f.Command.Definition with{Enabled=false}});
        Assert.Equal(policy.Reference,(await p.HistoryAsync(f.Warehouse,"1")).Reference);
        var basis=await MakeUnit(Units(h),BaseUnit("BASE2"));var ratio=await MakeUnit(Units(h),BaseUnit("BOX") with{Mode="RATIO",BaseRef=basis.Reference,Numerator="12"});
        var d=f.Command.Definition with{Metrics=[new("unit-count","COUNT",[ratio.Reference])]};
        var v=await p.SaveAsync(f.Warehouse,new(Guid.NewGuid(),"2",d,"新单位"));var actual=(await p.ContextAsync(f.Warehouse)).Policy!;
        Assert.Equal(basis.Reference,Assert.Single(actual.Original.Units).BaseReference);
        await Units(h).CommandAsync(Revise(basis,basis.Profile with{Name="改版"}));
        Assert.Equal("UOM_PROFILE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,new(Guid.NewGuid(),v.Reference.Version,d,"核对当前来源")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_exact_precision_pack_zero_and_domain_completeness_have_no_rounding_or_default_permission()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h,BaseUnit("EA","COUNT",2) with{PackMultiple="0.25"});var p=RuleReferences(h);var o=ReferenceRules(h);
        var domain=f.Command.Definition.Domains[0] with{AllowEmptyProducts=false,AllowEmptyOperations=false,AllowUnlimited=false,AllowOpenEnded=false,AllowPickable=false};
        var policy=await p.SaveAsync(f.Warehouse,f.Command with{Definition=f.Command.Definition with{Precisions=[new("qty-8",2,"REJECT")],Domains=[domain]}});
        var valid=ResolvableRule("0") with{ValidTo="2026-11-01T00:00:00Z"};
        foreach(var draft in new[]{valid with{Capacity=new("UNKNOWN",Reason:"未定")},valid with{Capacity=new("UNLIMITED")},valid with{AllowedProductTypes=[]},valid with{AllowedOperations=[]},valid with{IsPickable=true},valid with{ValidFrom=null},valid with{ValidTo=null},valid with{Capacity=valid.Capacity with{Value="0.001"}},valid with{Capacity=valid.Capacity with{Value="0.10"}}})
        {var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),draft));await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),policy.Reference)));Assert.Equal("1",(await o.ReadAsync(f.Location,c.Reference.Id)).Reference.Version);}
        foreach(var amount in new[]{"0","0.25","10.00000000"}){var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),valid with{Capacity=valid.Capacity with{Value=amount}}));Assert.Equal("RESOLVED",(await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),policy.Reference))).Effect);}
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_source_permissions_and_final_revocation_rollback_resolution_and_journal()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);var policy=await p.SaveAsync(f.Warehouse,f.Command);
        var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule()));var command=new LocationRuleResolveV1(Guid.NewGuid(),policy.Reference);var fired=false;
        hook.Before=async sql=>{if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;fired=true;
            await using var revoke=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"ActionCode\"='query' AND \"MenuId\" IN(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='erp-uom-profile')",(NpgsqlConnection)sql.Connection!,(NpgsqlTransaction)sql.Transaction!);revoke.Parameters.AddWithValue("tenant",h.Tenant);await revoke.ExecuteNonQueryAsync();};
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,command))).Status);Assert.True(fired);
        Assert.Equal("1",(await o.ReadAsync(f.Location,c.Reference.Id)).Reference.Version);Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Location,command.OperationId)).State);
        var r=await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,command);await RevokeTransferAction(h,"wms-location-rule","resolve");
        Assert.Equal(r,await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,command));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var hidden=await o.ReadAsync(f.Location,c.Reference.Id);Assert.Null(hidden.ReferenceBinding);Assert.Null(hidden.OriginalJson);
        await RevokeTransferAction(h,"erp-uom-profile","query");Assert.Equal(policy.Reference,(await p.HistoryAsync(f.Warehouse,"1")).Reference);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1"}))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_missing_controls_SQL_original_immutability_and_failed_journal_preserve_no_effect()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RuleReferencesSetup(h);var p=RuleReferences(h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_refs_v1.versions DISABLE TRIGGER rule_refs_versions_immutable");
        try{Assert.Equal("RULE_DICTIONARY_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_refs_v1.versions ENABLE TRIGGER rule_refs_versions_immutable");}
        hook.Before=sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations")){hook.Before=null;throw new InvalidOperationException("injected dictionary journal failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>p.SaveAsync(f.Warehouse,f.Command));Assert.Null((await p.ContextAsync(f.Warehouse)).Policy);Assert.Equal("UNKNOWN",(await p.OperationAsync(f.Warehouse,f.Command.OperationId)).State);
        var r=await p.SaveAsync(f.Warehouse,f.Command);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_location_rule_refs_v1.versions WHERE tenant_id={h.Tenant} AND id={r.Reference.Id}"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_location_rule_refs_v1.versions CASCADE"));
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_competing_policy_revisions_and_close_create_have_one_durable_result()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);await p.SaveAsync(f.Warehouse,f.Command);
        await using var second=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=RuleReferences(h,second);
        async Task<int> Save(LocationRuleReferencesOwnerV1 service,int scale){try{await service.SaveAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1",Definition=f.Command.Definition with{Precisions=[new("qty-8",scale,"REJECT")]}});return 200;}catch(OrderPriceCorrectionException e){return e.Status;}}
        var outcomes=await Task.WhenAll(Task.Run(()=>Save(p,3)),Task.Run(()=>Save(other,4)));Assert.Equal(new[]{200,412},outcomes.Order().ToArray());
        var next=f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="2",Definition=f.Command.Definition with{Precisions=[new("qty-8",6,"REJECT")]}};
        var writing=Task.Run(async()=>{try{return await p.SaveAsync(f.Warehouse,next);}catch(OrderPriceCorrectionException e)when(e.Message=="ORIGINAL_OPERATION_CLOSED"){return null;}});
        var closing=Task.Run(()=>other.OperationAsync(f.Warehouse,next.OperationId,true));await Task.WhenAll(writing,closing);
        var state=(await p.OperationAsync(f.Warehouse,next.OperationId)).State;Assert.Equal(writing.Result==null?"CLOSED":"COMMITTED",state);
        if(writing.Result==null)await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,next));else Assert.Equal(writing.Result,await p.SaveAsync(f.Warehouse,next));
        var pending=Guid.NewGuid();await p.OperationAsync(f.Warehouse,pending,true);
        var warehouseOwner=WarehouseDrafts(h);var context=await warehouseOwner.ContextAsync();var source=await warehouseOwner.ReadAsync(f.Warehouse);
        var otherWarehouse=await warehouseOwner.CreateAsync(new(Guid.NewGuid(),context.Creation.AuthorityVersion,null,source.SiteRef.Id,"SECOND","Other warehouse",source.Warehouse.WarehouseType,null,null,null,null));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.OperationAsync(otherWarehouse.WarehouseId,pending))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_metric_units_require_exact_shared_native_base_not_same_dimension_name()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var other=await MakeUnit(Units(h),BaseUnit("OTHER","COUNT",8));
        var mixed=f.Command.Definition with{Metrics=[new("unit-count","COUNT",[f.Unit.Reference,other.Reference])]};
        Assert.Equal("RULE_METRIC_BASE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command with{Definition=mixed}))).Message);
        Assert.Equal("RULE_METRIC_DIMENSION_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command with{Definition=f.Command.Definition with{Metrics=[new("unit-count","MASS",[f.Unit.Reference])]}}))).Message);
        Assert.Equal("UOM_PROFILE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command with{Definition=f.Command.Definition with{Metrics=[new("unit-count","COUNT",[f.Unit.Reference with{Digest=new string('0',64)}])]}}))).Message);
        Assert.Null((await p.ContextAsync(f.Warehouse)).Policy);Assert.Equal("UNKNOWN",(await p.OperationAsync(f.Warehouse,f.Command.OperationId)).State);
        var ratio=await MakeUnit(Units(h),BaseUnit("BOX","COUNT",8) with{Mode="RATIO",BaseRef=f.Unit.Reference,Numerator="12"});
        await p.SaveAsync(f.Warehouse,f.Command with{Definition=f.Command.Definition with{Metrics=[new("unit-count","COUNT",[f.Unit.Reference,ratio.Reference])]}});
        Assert.All((await p.ContextAsync(f.Warehouse)).Policy!.Original.Units,u=>Assert.Equal(f.Unit.Reference,u.BaseReference));
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_partial_warehouse_scope_cannot_publish_whole_warehouse_policy()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);
        await h.Db.WmsRoleScopes.Where(x=>x.RoleId==72).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"A"));
        Assert.Null((await p.ContextAsync(f.Warehouse)).Policy);
        Assert.Equal("HIERARCHY_WAREHOUSE_SCOPE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command))).Message);
        Assert.Equal("UNKNOWN",(await p.OperationAsync(f.Warehouse,f.Command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_opaque_unicode_identifiers_preserve_exact_owner_meaning_without_trimming_or_Guid_coercion()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);
        var definition=f.Command.Definition with{Metrics=[new(" 計量 A ","COUNT",[f.Unit.Reference])],Precisions=[new("精度/8",8,"REJECT")],
            Domains=[f.Command.Definition.Domains[0] with{Id=" 倉庫/域 ",MetricIds=[" 計量 A "],PrecisionIds=["精度/8"]}]};
        var policy=await p.SaveAsync(f.Warehouse,f.Command with{Definition=definition});
        var d=ResolvableRule() with{SelectorKey=new("LOCATION_OPERATION"," 計量 A ",new(" 倉庫/域 ","1")),Capacity=new("LIMITED","10",new(" 計量 A ","1"),new("EA","1"),new("精度/8","1"))};
        var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),d));await o.ResolveAsync(f.Location,c.Reference.Id,c.Etag,new(Guid.NewGuid(),policy.Reference));
        var v=await o.ReadAsync(f.Location,c.Reference.Id);Assert.Contains(v.ReferenceBinding!.Fields,x=>x.Field=="metricRef"&&x.Id==" 計量 A ");
        Assert.Equal(" 倉庫/域 ",Assert.Single((await o.ContextAsync(f.Location)).Selectors).ApplicabilityDomainId);
        foreach(var invalid in new[]{" ","bad\ncode",new string('x',101),"bad\ud800"})
        {
            var bad=f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1",Definition=definition with{Metrics=[new(invalid,"COUNT",[f.Unit.Reference])]}};
            var failure=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,bad));Assert.Equal(400,failure.Status);
        }
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public async Task RuleReferences_warehouse_and_unit_department_scopes_are_independent_and_foreign_tenant_cannot_read(int type)
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var admin=await MappingAdmin(h);await StandaloneScope(h,admin,type);
        if(type is 2 or 3 or 4)
        {
            Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.ContextAsync(f.Warehouse))).Status);
            await BindStandalone(h,admin,"warehouse",f.Warehouse);
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command))).Status);
            await BindStandalone(h,admin,"uom-profile",f.Unit.Reference.Id);
        }
        await p.SaveAsync(f.Warehouse,f.Command);Assert.Equal("1",(await p.ContextAsync(f.Warehouse)).Version);
        await using var foreign=await Seed();await GrantMaster(foreign);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RuleReferences(foreign).ContextAsync(f.Warehouse))).Status);
        if(type is 2 or 3 or 4)
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_id={f.Unit.Reference.Id}");
            Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>p.SaveAsync(f.Warehouse,f.Command with{OperationId=Guid.NewGuid(),ExpectedVersion="1"}))).Status);
            Assert.Equal("1",(await p.HistoryAsync(f.Warehouse,"1")).Reference.Version);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RuleReferences_real_program_HTTP_closed_dictionary_and_resolve_etag_current_permissions()
    {
        await using var h=await Seed();var f=await RuleReferencesSetup(h);var p=RuleReferences(h);var o=ReferenceRules(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);var root=$"api/wms/warehouses/{f.Warehouse}/rule-references-v1";
        var body=JsonSerializer.SerializeToNode(f.Command,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var forged=body.DeepClone().AsObject();forged["tenantId"]=Guid.NewGuid();using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",forged)))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var missing=body.DeepClone().AsObject();missing["definition"]!.AsObject().Remove("enabled");using(var denied=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",missing)))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var saved=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",body))){Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.True(saved.Headers.CacheControl?.NoStore);}
        var policy=(await p.ContextAsync(f.Warehouse)).Policy!;var c=await o.CreateAsync(f.Location,new(Guid.NewGuid(),ResolvableRule()));var path=$"api/wms/locations/{f.Location}/rules-v1/{c.Reference.Id}/resolve";
        var command=new LocationRuleResolveV1(Guid.NewGuid(),policy.Reference);
        using(var missingEtag=await host.Send(OperationsRequest(cookies,HttpMethod.Post,path,command)))Assert.Equal((HttpStatusCode)428,missingEtag.StatusCode);
        using var request=OperationsRequest(cookies,HttpMethod.Post,path,command);request.Headers.TryAddWithoutValidation("If-Match",c.Etag);
        using(var resolved=await host.Send(request)){Assert.Equal(HttpStatusCode.OK,resolved.StatusCode);Assert.NotNull(resolved.Headers.ETag);}
        await RevokeTransferAction(h,"wms-warehouse","rule-policy");using(var replay=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/commands",body)))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        await RevokeTransferAction(h,"wms-warehouse","query");using var hidden=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/operations/"+f.Command.OperationId));Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);
    }
}

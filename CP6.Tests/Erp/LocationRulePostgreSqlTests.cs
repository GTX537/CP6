using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 RuleAuthority(Harness h,CP6Context? db=null)
    {
        var c=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-warehouse",new[]{"WarehouseName","AddressText","ManagerCd","Remarks"}),
            ("wms-location",new[]{"LocationName","Barcode"}),("wms-location-rule",LocationRuleValuesV1.Fields)})
        {c.RegisterScope(key,key,[1,2,3,4,5],1);c.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}c.Seal();
        return new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),c.Capture);
    }
    private static LocationRuleOwnerV1 Rules(Harness h,CP6Context? db=null)=>new(db??h.Db,RuleAuthority(h,db));
    private static async Task GrantLocationRule(Harness h)
    {
        foreach(var action in new[]{"query","draft","edit","withdraw","abandon"})await EngineeringAction(h,"wms-location-rule",action);
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-location-rule",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static LocationRuleDraftV1 RuleDraft(string value="10")=>new(new("LOCATION_OPERATION","unit-count",new("storage","1")),
        new("LIMITED",value,new("unit-count","1"),new("EA","1"),new("qty-8","1")),["RAW"],["RECEIVE","PICK"],false,null,null);
    private static LocationRulePatchV1 RulePatch(string json)=>JsonSerializer.Deserialize<LocationRulePatchV1>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static async Task<Guid> RuleSetup(Harness h)
    {
        var f=await LocationDraftSetup(h);var l=await LocationDrafts(h).CreateAsync(f.Warehouse,f.Command);await GrantLocationRule(h);return l.LocationId;
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_typed_candidates_share_stable_selector_without_applying_or_changing_native_facts()
    {
        await using var h=await Seed();var location=await RuleSetup(h);var o=Rules(h);
        var c=new LocationRuleCreateV1(Guid.NewGuid(),RuleDraft("9999999999999.99999999"));var r=await o.CreateAsync(location,c);
        Assert.Equal(r,await o.CreateAsync(location,c));var v=await o.ReadAsync(location,r.Reference.Id);Assert.Contains("9999999999999.99999999",v.OriginalJson);Assert.Equal("DRAFT",v.State);
        var other=await o.CreateAsync(location,new(Guid.NewGuid(),RuleDraft("0") with{SelectorKey=RuleDraft().SelectorKey with{ApplicabilityDomainRef=new("storage","2")}}));
        Assert.Equal(r.SelectorId,other.SelectorId);Assert.Equal("1",other.Reference.Version);
        var context=await o.ContextAsync(location);Assert.Equal("0",Assert.Single(context.Selectors).Revision);Assert.Null(context.Selectors[0].CurrentRuleVersionId);Assert.False(context.AdmissionGrant);
        Assert.Equal("UNRESOLVED",v.ReferenceResolution);Assert.False(v.AdmissionGrant);Assert.Equal("NOT_SUBMITTED",v.Approval);
        var native=await h.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==location);
        Assert.True(native.IsInitializationQuarantined);Assert.True(native.IsBlocked);Assert.False(native.IsPickable);Assert.Equal(0,native.CapacityQty);Assert.Equal(0,native.ReservedCapacityQty);
        Assert.Empty(await h.Db.Stocks.ToArrayAsync());Assert.Empty(await h.Db.Locations.ToArrayAsync());
        foreach(var sql in new[]{"UPDATE cp6_location_rule_v1.versions SET digest=digest","UPDATE cp6_location_rule_v1.selectors SET revision=1","DELETE FROM cp6_location_rule_v1.proposals"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_all_capacity_modes_exact_limits_and_offset_times_preserve_distinctions()
    {
        await using var h=await Seed();var id=await RuleSetup(h);var o=Rules(h);
        foreach(var capacity in new[]{new LocationRuleCapacityV1("UNLIMITED"),new("UNKNOWN",Reason:"单位未核"),RuleDraft("0").Capacity})
        {
            var r=await o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft() with{Capacity=capacity,AllowedOperations=[],AllowedProductTypes=[],ValidFrom="2026-10-01T08:00:00.1234567+08:00",ValidTo="2026-10-02T00:00:00Z"}));
            var v=await o.ReadAsync(id,r.Reference.Id);Assert.Equal("2026-10-01T08:00:00.1234567+08:00",JsonDocument.Parse(v.OriginalJson!).RootElement.GetProperty("draft").GetProperty("validFrom").GetString());Assert.Contains(capacity.Mode,v.OriginalJson);
        }
        foreach(var value in new[]{"10000000000000","0.123456789","-0","-1","1e2","NaN","1,000"," 1"})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft(value))))).Status);
        foreach(var d in new[]{RuleDraft() with{Capacity=new("LEGACY_UNLIMITED")},RuleDraft() with{Capacity=new("UNLIMITED",Value:"0")},
            RuleDraft() with{ValidFrom="2026-01-01T00:00:00"},RuleDraft() with{ValidFrom="2026-01-01T01:00:00+01:00",ValidTo="2026-01-01T00:00:00Z"}})
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,new(Guid.NewGuid(),d)));
        Assert.Equal(3,(await o.ListAsync(id)).Count);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_strict_patch_no_change_history_and_withdrawal_keep_original_candidate()
    {
        await using var h=await Seed();var id=await RuleSetup(h);var o=Rules(h);var c=await o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft()));
        var noop=await o.PatchAsync(id,c.Reference.Id,c.Etag,new(Guid.NewGuid(),RulePatch("""{"allowedOperations":{"op":"SET","value":["PICK","RECEIVE"]},"isPickable":{"op":"SET","value":false}}""")));
        Assert.Equal("NO_CHANGE",noop.Effect);Assert.Equal(c.Reference,noop.Reference);Assert.Equal(c.Etag,noop.Etag);
        var updated=await o.PatchAsync(id,c.Reference.Id,c.Etag,new(Guid.NewGuid(),RulePatch("""{"capacity":{"op":"SET","value":{"mode":"UNKNOWN","reason":"待核"}},"allowedOperations":{"op":"SET","value":[]},"validFrom":{"op":"CLEAR"}}""")));
        Assert.Equal("2",updated.Reference.Version);Assert.NotEqual(c.Etag,updated.Etag);Assert.Contains("LIMITED",(await o.ReadAsync(id,c.Reference.Id,"1")).OriginalJson);
        foreach(var patch in new[]{"{\"capacity\":null}","{\"capacity\":{\"op\":\"CLEAR\"}}","{\"validFrom\":{\"op\":\"SET\",\"value\":null}}","{\"validTo\":{\"op\":\"CLEAR\",\"value\":\"x\"}}"})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PatchAsync(id,c.Reference.Id,updated.Etag,new(Guid.NewGuid(),RulePatch(patch))))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PatchAsync(id,c.Reference.Id,c.Etag,new(Guid.NewGuid(),new())))).Status);
        var withdrawn=await o.WithdrawAsync(id,c.Reference.Id,updated.Etag,new(Guid.NewGuid(),"取消候选"));Assert.Equal("WITHDRAWN",withdrawn.State);Assert.Equal(updated.Reference,withdrawn.Reference);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.PatchAsync(id,c.Reference.Id,withdrawn.Etag,new(Guid.NewGuid(),new())))).Status);
        Assert.Equal("2",(await o.ReadAsync(id,c.Reference.Id)).Reference.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_current_fields_action_sources_and_final_revocation_are_atomic()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var id=await RuleSetup(h);var o=Rules(h);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft())))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-location-rule").ExecuteDeleteAsync();
        var command=new LocationRuleCreateV1(Guid.NewGuid(),RuleDraft());var revoked=false;
        hook.Before=async sql=>
        {if(!sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;revoked=true;
            await using var revoke=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"ActionCode\"='draft' AND \"MenuId\" IN(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-location-rule')",(NpgsqlConnection)sql.Connection!,(NpgsqlTransaction)sql.Transaction!);revoke.Parameters.AddWithValue("tenant",h.Tenant);await revoke.ExecuteNonQueryAsync();};
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,command))).Status);Assert.True(revoked);
        Assert.Empty(await o.ListAsync(id));Assert.Empty((await o.ContextAsync(id)).Selectors);Assert.Equal("UNKNOWN",(await o.OperationAsync(id,command.OperationId)).State);
        var r=await o.CreateAsync(id,command);await RevokeTransferAction(h,"wms-location-rule","draft");Assert.Equal(r,await o.CreateAsync(id,command));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var v=await o.ReadAsync(id,r.Reference.Id);Assert.False(v.Candidate.ContainsKey("capacity"));Assert.Null(v.OriginalJson);Assert.Contains("Capacity",v.RedactedFields);
        await RevokeTransferAction(h,"wms-location","query");Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(id,command.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_missing_store_fence_cannot_create_and_failed_write_keeps_original_unknown()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var id=await RuleSetup(h);var o=Rules(h);var c=new LocationRuleCreateV1(Guid.NewGuid(),RuleDraft());
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_v1.versions DISABLE TRIGGER rule_versions_immutable");
        try{Assert.Equal("RULE_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,c))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_v1.versions ENABLE TRIGGER rule_versions_immutable");}
        hook.Before=sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations")){hook.Before=null;throw new InvalidOperationException("injected rule journal failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>o.CreateAsync(id,c));Assert.Empty((await o.ContextAsync(id)).Selectors);Assert.Empty(await o.ListAsync(id));Assert.Equal("UNKNOWN",(await o.OperationAsync(id,c.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_edit_withdraw_race_and_close_create_race_have_one_durable_outcome()
    {
        await using var h=await Seed();var id=await RuleSetup(h);var o=Rules(h);var c=await o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft()));
        await using var other=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var oo=Rules(h,other);
        async Task<int> Run(Func<Task<LocationRuleResultV1>> f){try{await f();return 200;}catch(OrderPriceCorrectionException e){return e.Status;}}
        var outcomes=await Task.WhenAll(Task.Run(()=>Run(()=>o.PatchAsync(id,c.Reference.Id,c.Etag,new(Guid.NewGuid(),RulePatch("""{"isPickable":{"op":"SET","value":true}}"""))))),Task.Run(()=>Run(()=>oo.WithdrawAsync(id,c.Reference.Id,c.Etag,new(Guid.NewGuid(),"撤回")))));
        Assert.Equal(new[]{200,412},outcomes.Order().ToArray());
        var create=new LocationRuleCreateV1(Guid.NewGuid(),RuleDraft());
        var creating=Task.Run(async()=>{try{return await o.CreateAsync(id,create);}catch(OrderPriceCorrectionException e)when(e.Message=="ORIGINAL_OPERATION_CLOSED"){return null;}});
        var closing=Task.Run(()=>oo.OperationAsync(id,create.OperationId,true));await Task.WhenAll(creating,closing);
        Assert.Equal(creating.Result==null?"CLOSED":"COMMITTED",(await o.OperationAsync(id,create.OperationId)).State);
        if(creating.Result==null)Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CreateAsync(id,create))).Message);
        else Assert.Equal(creating.Result,await o.CreateAsync(id,create));
    }
    [TenantManagementPostgresTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task LocationRule_current_native_department_scopes_and_foreign_tenant_cannot_disclose_proposals(int type)
    {
        await using var h=await Seed();var f=await LocationDraftSetup(h);var admin=await MappingAdmin(h);await BindStandalone(h,admin,"warehouse",f.Warehouse);
        var context=await LocationDrafts(h).ContextAsync(f.Warehouse);
        var id=(await LocationDrafts(h).CreateAsync(f.Warehouse,f.Command with{AuthorityVersion=context.Creation.AuthorityVersion,DepartmentId=admin.Child})).LocationId;
        await GrantLocationRule(h);var o=Rules(h);var r=await o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft()));
        await StandaloneScope(h,admin,type);Assert.Single(await o.ListAsync(id));
        await using var foreign=await Seed();await GrantMaster(foreign);await GrantLocationRule(foreign);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Rules(foreign).ReadAsync(id,r.Reference.Id))).Status);
        if(type is 2 or 3 or 4){await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_id={id}");Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(id,r.Reference.Id))).Status);}
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_legacy_native_location_candidate_never_interprets_old_capacity_as_approved()
    {
        await using var h=await Seed();await GrantMaster(h);var ids=await SeedMaster(h);await GrantLocationRule(h);var o=Rules(h);
        var ctx=await o.ContextAsync(ids.location);Assert.Equal("LEGACY_UNVERIFIED",ctx.InitializationState);
        var before=await h.Db.Locations.AsNoTracking().SingleAsync(x=>x.Id==ids.location);await o.CreateAsync(ids.location,new(Guid.NewGuid(),RuleDraft() with{Capacity=new("UNLIMITED")}));
        var after=await h.Db.Locations.AsNoTracking().SingleAsync(x=>x.Id==ids.location);Assert.Equal(before.CapacityQty,after.CapacityQty);Assert.Equal(before.ReservedCapacityQty,after.ReservedCapacityQty);Assert.Equal(before.IsBlocked,after.IsBlocked);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationRule_real_program_HTTP_closed_capacity_union_patch_etag_and_original_recovery()
    {
        await using var h=await Seed();var id=await RuleSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);var root=$"api/wms/locations/{id}/rules-v1";
        using var ctx=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/context"));Assert.Equal(HttpStatusCode.OK,ctx.StatusCode);
        var c=new LocationRuleCreateV1(Guid.NewGuid(),RuleDraft());var body=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        foreach(var field in new[]{"tenantId","selectorRevision","approved","stockQty"}){var bad=body.DeepClone().AsObject();bad[field]="forged";using var e=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,bad));Assert.Equal(HttpStatusCode.BadRequest,e.StatusCode);}
        var extra=body.DeepClone().AsObject();extra["draft"]!["capacity"]!["reason"]="not allowed";using(var e=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,extra)))Assert.Equal(HttpStatusCode.BadRequest,e.StatusCode);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root,body));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);Assert.NotNull(response.Headers.ETag);
        var result=(await Rules(h).OperationAsync(id,c.OperationId)).Result!;
        using(var e=await host.Send(OperationsRequest(cookies,HttpMethod.Patch,root+"/"+result.Reference.Id,new{operationId=Guid.NewGuid(),patch=new{isPickable=new{op="SET",value=true}}})))Assert.Equal((HttpStatusCode)428,e.StatusCode);
        await RevokeTransferAction(h,"wms-location-rule","draft");using var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/operations/"+c.OperationId));Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Contains("COMMITTED",await recovered.Content.ReadAsStringAsync());
    }
}

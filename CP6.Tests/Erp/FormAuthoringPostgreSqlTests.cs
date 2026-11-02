using System.Text;
using System.Text.Json;
using System.Net;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private FormAuthoringOwnerV2 FormAuthoring(Harness h, Func<CancellationToken, Task<Guid>>? actor = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope(FormAuthoringOwnerV2.Resource, "flow", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields(FormAuthoringOwnerV2.Resource, FormAuthoringOwnerV2.Fields.Select(f => new FieldRegistry.Field(f, f))); catalog.Seal();
        return new(h.Db, new AuthoringHttpAccessor { HttpContext = h.Http }, ScopeFence(h), actor ?? (_ => Task.FromResult(h.User)), catalog.Capture);
    }
    private async Task<FormAuthoringOwnerV2> PrepareFormAuthoring(Harness h)
    {
        if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuKey == "oa-designer"))
        { h.Db.Sys_Menus.Add(new() { MenuId = 9172, MenuKey = "oa-designer", MenuName = "designer" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
        await RegisterScope(h); await EngineeringAction(h, "oa-designer", "form-save");
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "oa-designer");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 72 && x.MenuId == menu.MenuId))
        { h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
        var owner = FormAuthoring(h); await owner.ConfigureAsync(new(Guid.NewGuid(), "absent", true, [], "local direct authoring policy")); return owner;
    }
    private static byte[] FormAuthoringCommand(string id = "first", string? version = null, string name = "draft", string schema = FormAuthoringSchema, string? category = null, string? subCategory = null)
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId = id, expectedVersion = version, body = new { name, schemaJson = schema, category, subCategory } });

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_lost_ACK_uses_original_after_edit_revoke_and_scope_disable()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var owner = await PrepareFormAuthoring(h);
        fault.Armed = true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("lost", FormAuthoringCommand()))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "form-save").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        var original = await owner.OriginalAsync("lost", "first"); Assert.Equal("SUCCEEDED", original.Outcome);
        Assert.Equal(original.ResourceVersion, (await owner.SaveAsync("lost", FormAuthoringCommand())).ResourceVersion);
        Assert.Equal("SCOPE_MAPPING_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("new", FormAuthoringCommand("next")))).Message);
        Assert.Equal("NOT_OBSERVED", (await owner.OriginalAsync("lost", "missing")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_field_hidden_filters_live_and_original_and_blocks_whole_mutation()
    {
        await using var h = await Seed(); var owner = await PrepareFormAuthoring(h); var saved = await owner.SaveAsync("private", FormAuthoringCommand());
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FormAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.DoesNotContain("SchemaJson", (await owner.ReadAsync("private")).Fields.Keys);
        Assert.DoesNotContain("SchemaJson", (await owner.OriginalAsync("private", "first")).Result!.Fields.Keys);
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("private", FormAuthoringCommand("edit", saved.ResourceVersion, "hidden edit")))).Message);
        Assert.Equal("draft", (await owner.ReadAsync("private")).Fields["Name"]);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_final_actor_failure_rolls_back_head_identity_command_and_audit()
    {
        await using var h = await Seed(); await PrepareFormAuthoring(h); var calls = 0;
        var owner = FormAuthoring(h, _ => ++calls == 1 ? Task.FromResult(h.User) : throw new ScopeAuthorizationExceptionV1("REAUTH_REQUIRED", 401));
        Assert.Equal(401, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("rollback", FormAuthoringCommand()))).Status);
        foreach (var table in new[] { "heads", "drafts", "commands", "audit" })
            Assert.Equal(0, await h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2." + table + " WHERE tenant_id={0}", h.Tenant).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_current_owner_scope_foreign_actor_and_tenant_never_share_original_slots()
    {
        await using var h = await Seed(); var owner = await PrepareFormAuthoring(h); var saved = await owner.SaveAsync("private", FormAuthoringCommand());
        var other = new Sys_User { UserName = "other-author", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant);
        var reader = FormAuthoring(h, _ => Task.FromResult(other.Id));
        Assert.Empty(await reader.ListAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => reader.ReadAsync("private"))).Status);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = FormAuthoringOwnerV2.Resource, ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(saved.ResourceId, (await reader.ReadAsync("private")).DefinitionId);
        Assert.Equal("NOT_OBSERVED", (await reader.OriginalAsync("private", "first")).Outcome);
        await using var foreign = await Seed(); var foreignOwner = await PrepareFormAuthoring(foreign);
        var separate = await foreignOwner.SaveAsync("private", FormAuthoringCommand());
        Assert.NotEqual(saved.ResourceId, separate.ResourceId); Assert.Equal(separate.ResourceId, (await foreignOwner.OriginalAsync("private", "first")).ResourceId);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_department_scope_uses_current_healthy_tree_and_filters_before_pagination()
    {
        await using var h = await Seed(); var owner = await PrepareFormAuthoring(h);
        var parent = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = "P" + h.Tenant.ToString("N"), DeptName = "parent" };
        parent.Path = "/" + parent.Id + "/";
        var child = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = "C" + h.Tenant.ToString("N"), DeptName = "child", ParentId = parent.Id };
        child.Path = parent.Path + child.Id + "/";
        h.Db.Sys_Depts.AddRange(parent, child); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeptId, child.Id));
        await owner.SaveAsync("child-draft", FormAuthoringCommand());
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeptId, parent.Id));
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = FormAuthoringOwnerV2.Resource, ScopeType = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await owner.ListAsync());
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == FormAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x => x.SetProperty(s => s.ScopeType, 3));
        Assert.Single(await owner.ListAsync());
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == FormAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x => x.SetProperty(s => s.ScopeType, 4).SetProperty(s => s.CustomDeptIds, child.Id.ToString("D")));
        Assert.Single(await owner.ListAsync());
        await h.Db.Sys_Depts.Where(d => d.Id == parent.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Enable, false));
        Assert.Empty(await owner.ListAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_same_transaction_policy_ABA_and_expiry_roll_back_every_effect()
    {
        await using var h = await Seed(); await PrepareFormAuthoring(h); var calls = 0;
        var owner = FormAuthoring(h, async _ =>
        {
            if (++calls == 2)
            {
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.policy SET enabled=false WHERE tenant_id={h.Tenant}");
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.policy SET enabled=true WHERE tenant_id={h.Tenant}");
            }
            return h.User;
        });
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("aba", FormAuthoringCommand()))).Message);
        Assert.Equal("NOT_OBSERVED", (await FormAuthoring(h).OriginalAsync("aba", "first")).Outcome);
        calls = 0;
        owner = FormAuthoring(h, _ =>
        {
            if (++calls == 2) ((System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("exp", DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds().ToString()));
            return Task.FromResult(h.User);
        });
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("expired", FormAuthoringCommand()))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_policy_revoke_waits_for_actual_owner_commit_then_blocks_the_next_write()
    {
        await using var h = await Seed(); await PrepareFormAuthoring(h);
        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var app = "authoring-revoke-" + Guid.NewGuid().ToString("N"); writer.Database.SetConnectionString(writer.Database.GetConnectionString() + ";Application Name=" + app);
        await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Task<int>? revoke = null; var calls = 0;
        var owner = FormAuthoring(h, async _ =>
        {
            if (++calls == 2)
            {
                revoke = writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.policy SET enabled=false WHERE tenant_id={h.Tenant}");
                var waiting = false;
                for (var i = 0; i < 100 && !revoke.IsCompleted; i++)
                {
                    waiting = await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if (waiting) break; await Task.Delay(20);
                }
                Assert.True(waiting); Assert.False(revoke.IsCompleted);
            }
            return h.User;
        });
        var saved = await owner.SaveAsync("linearized", FormAuthoringCommand()); Assert.Equal(1, await revoke!.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(saved.ResourceId, (await owner.OriginalAsync("linearized", "first")).ResourceId);
        Assert.Equal("AUTHORING_POLICY_DISABLED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("after-revoke", FormAuthoringCommand("next")))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_receipts_are_immutable_and_missing_policy_writer_prevents_new_effect()
    {
        await using var h = await Seed(); var owner = await PrepareFormAuthoring(h); await owner.SaveAsync("history", FormAuthoringCommand());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.commands SET result_json='{{}}' WHERE tenant_id={h.Tenant}"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.policy DISABLE TRIGGER form_policy_fence");
        try
        {
            Assert.Equal("FORM_AUTHORING_WRITER_COVERAGE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("closed", FormAuthoringCommand("new")))).Message);
            Assert.Equal("SUCCEEDED", (await owner.OriginalAsync("history", "first")).Outcome);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.policy ENABLE TRIGGER form_policy_fence"); }
    }


    private const string FormAuthoringSchema = "{\"fields\":[{\"name\":\"a\",\"type\":\"input\",\"label\":\"原文\"}],\"rules\":[],\"layout\":{\"amount\":12345678901234567890.0001}}";
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_concurrent_sessions_serialize_CAS_and_restore_one_original()
    {
        await using var h=await Seed();var initialOwner=await PrepareFormAuthoring(h);var first=await initialOwner.SaveAsync("race",FormAuthoringCommand());
        await using var secondDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var app="form-cas-"+Guid.NewGuid().ToString("N");secondDb.Database.SetConnectionString(secondDb.Database.GetConnectionString()+";Application Name="+app);
        await using var monitor=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var second=FormAuthoring(h with {Db=secondDb});Task<FormAuthoringResultV2>? competing=null;var calls=0;
        var owner=FormAuthoring(h,async _=>
        {
            if(++calls==2)
            {
                competing=second.SaveAsync("race",FormAuthoringCommand("second",first.ResourceVersion,"second"));
                var waiting=false;
                for(var i=0;i<100&&!competing.IsCompleted;i++)
                {
                    waiting=await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if(waiting)break;await Task.Delay(20);
                }
                Assert.True(waiting);
            }
            return h.User;
        });
        var raw=FormAuthoringCommand("winner",first.ResourceVersion,"winner");var saved=await owner.SaveAsync("race",raw);
        Assert.Equal("DEFINITION_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>await competing!.WaitAsync(TimeSpan.FromSeconds(10)))).Message);
        Assert.Equal(saved.ResourceVersion,(await second.SaveAsync("race",raw)).ResourceVersion);
        Assert.Equal("winner",(await initialOwner.ReadAsync("race")).Fields["Name"]);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.audit WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_failed_audit_rolls_back_new_draft_and_command_without_identity_conflict_claim()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.audit ADD CONSTRAINT test_audit_unavailable CHECK(resource_key <> 'audit-fault')");
        try
        {
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("audit-fault",FormAuthoringCommand()))).Message);
            foreach(var table in new[]{"heads","drafts","commands","audit"})
                Assert.Equal(0,await h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2."+table+" WHERE tenant_id={0}",h.Tenant).SingleAsync());
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.audit DROP CONSTRAINT test_audit_unavailable");}
        Assert.Equal("SUCCEEDED",(await owner.SaveAsync("audit-fault",FormAuthoringCommand())).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_exact_draft_CAS_original_and_disabled_native_separation()
    {
        await using var h = await Seed(); var owner = await PrepareFormAuthoring(h);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ReadAsync("missing"))).Status);
        var first=await owner.SaveAsync("form",FormAuthoringCommand(category:" 原分类 ",subCategory:""));
        Assert.False(first.Result!.Enabled); Assert.False(first.Result.Published);
        Assert.Equal(FormAuthoringSchema,first.Result.Fields["SchemaJson"]);Assert.Equal(" 原分类 ",first.Result.Fields["Category"]);Assert.Equal("",first.Result.Fields["SubCategory"]);
        Assert.Empty(await h.Db.Wf_FormDefs.ToArrayAsync());
        Assert.Equal(first.ResourceVersion,(await owner.SaveAsync("form",FormAuthoringCommand(category:" 原分类 ",subCategory:""))).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("form",FormAuthoringCommand(name:"other")))).Message);
        Assert.Equal("DEFINITION_EXISTS",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("form",FormAuthoringCommand("new")))).Message);
        var next=await owner.SaveAsync("form",FormAuthoringCommand("edit",first.ResourceVersion,"next"));
        Assert.NotEqual(first.ResourceVersion,next.ResourceVersion);Assert.Null(next.Result!.Fields["Category"]);
        Assert.Equal(first.ResourceVersion,(await owner.OriginalAsync("form","first")).ResourceVersion);
        Assert.Equal("DEFINITION_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("form",FormAuthoringCommand("stale",first.ResourceVersion)))).Message);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.drafts SET row_version={Guid.Parse(first.ResourceVersion!)} WHERE tenant_id={h.Tenant}");
        Assert.NotEqual(first.ResourceVersion,(await owner.ReadAsync("form")).Version);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.audit WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_FLOW_grant_does_not_grant_FORM_and_current_action_and_SOD_are_required()
    {
        await using var h=await Seed();await PrepareAuthoring(h);await EngineeringAction(h,"oa-designer","form-save");var owner=FormAuthoring(h);
        Assert.False((await owner.EntryAsync()).CanCreate);
        Assert.Equal("AUTHORING_POLICY_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("closed",FormAuthoringCommand()))).Message);
        await owner.ConfigureAsync(new(Guid.NewGuid(),"absent",true,[],"explicit FORM policy"));
        Assert.True((await owner.EntryAsync()).CanCreate);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="form-save").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("closed",FormAuthoringCommand()))).Message);
        await EngineeringAction(h,"oa-designer","form-save");var policy=await owner.PolicyAsync();
        await owner.ConfigureAsync(new(Guid.NewGuid(),policy!.Version.ToString("D"),true,[72],"SOD"));
        Assert.Equal("AUTHORING_SOD_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("closed",FormAuthoringCommand()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_native_identity_aliases_cannot_bypass_managed_definition()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);await owner.SaveAsync("managed",FormAuthoringCommand());
        foreach(var key in new[]{"managed","MANAGED","managed "})
        {
            h.Db.Wf_FormDefs.Add(new Wf_FormDef{FormKey=key,FormName="bypass",SchemaJson=FormAuthoringSchema});
            await Assert.ThrowsAsync<DbUpdateException>(()=>h.Db.SaveChangesAsync());h.Db.ChangeTracker.Clear();
        }
        h.Db.Wf_FormDefs.Add(new Wf_FormDef{FormKey="native",FormName="old",SchemaJson=FormAuthoringSchema});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        foreach(var key in new[]{"MANAGED","managed ","NATIVE","native "})
            Assert.Equal("DEFINITION_IDENTITY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(key,FormAuthoringCommand()))).Message);
        Assert.Equal("old",(await h.Db.Wf_FormDefs.SingleAsync()).FormName);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_readonly_fields_and_schema_failure_leave_original_unchanged()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);var first=await owner.SaveAsync("form",FormAuthoringCommand());
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="Name",Access=2});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var raw=FormAuthoringCommand("edit",first.ResourceVersion,category:"changed");var next=await owner.SaveAsync("form",raw);
        Assert.DoesNotContain("Name",next.Result!.WritableFields);
        var diagnostic=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Insert(1,"\"clientCorrelationId\":\"observation\","));
        Assert.Equal(next.ResourceVersion,(await owner.SaveAsync("form",diagnostic)).ResourceVersion);
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("form",FormAuthoringCommand("denied",next.ResourceVersion,"changed")))).Message);
        Assert.Equal(422,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync("form",FormAuthoringCommand("schema",next.ResourceVersion,schema:"{\"fields\":[{\"name\":\"x\",\"type\":\"shell\"}]}")))).Status);
        Assert.Equal(next.ResourceVersion,(await owner.ReadAsync("form")).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormAuthoring_actual_HTTP_uses_current_session_csrf_strict_body_and_original_slot()
    {
        await using var h=await Seed();await PrepareFormAuthoring(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string path="/api/oa/v2/definitions/FORM/http-form/draft";
        using var command=JsonDocument.Parse(FormAuthoringCommand());
        var denied=BoundRequest(cookies,path,command.RootElement,false);denied.Method=HttpMethod.Put;
        using(var response=await host.Send(denied))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var request=BoundRequest(cookies,path,command.RootElement);request.Method=HttpMethod.Put;
        using(var response=await host.Send(request)){Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl?.NoStore);}
        foreach(var route in new[]{path,"/api/oa/v2/definitions/FORM","/api/oa/v2/definitions/FORM/entry","/api/oa/v2/commands/first?operation=FORM_SAVE_DRAFT&resource=http-form"})
        {using var response=await host.Send(BoundRequest(cookies,route));Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl?.NoStore);}
        var forged=BoundRequest(cookies,path,new{commandId="forged",expectedVersion=(string?)null,tenantId=Guid.NewGuid(),body=command.RootElement.GetProperty("body")});forged.Method=HttpMethod.Put;
        using(var response=await host.Send(forged))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var missing=BoundRequest(cookies,path,new{commandId="missing",body=command.RootElement.GetProperty("body")});missing.Method=HttpMethod.Put;
        using(var response=await host.Send(missing))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var policy=await FormAuthoring(h).PolicyAsync();var policyCommand=new FormAuthoringPolicyCommandV2(Guid.NewGuid(),policy!.Version.ToString("D"),true,[],"HTTP FORM policy");
        using(var response=await host.Send(BoundRequest(cookies,"/api/iam/form-authoring-policy",policyCommand)))Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        using(var response=await host.Send(BoundRequest(cookies,"/api/iam/form-authoring-policy/operations/"+policyCommand.OperationId)))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }
}

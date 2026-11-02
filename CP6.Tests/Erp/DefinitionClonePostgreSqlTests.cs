using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static byte[] CloneCommand(Guid source, string key="copy", string name="copied", string id="clone")
        => JsonSerializer.SerializeToUtf8Bytes(new { commandId=id, expectedVersion=(string?)null, body=new { sourceVersionId=source,newKey=key,newName=name } });

    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_exact_old_publication_creates_disabled_independent_draft_and_keeps_immutable_origin()
    {
        await using var h=await Seed();var owner=await PreparePublication(h);
        var saved=await owner.SaveAsync("source", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(AuthoringCommand(function:"source-function")).Replace("\"flowCode\":null","\"flowCode\":\"source-code\"")));
        var one=await owner.PublishAsync("source", PublicationCommand(await Preview(owner,"source",saved)));
        var source=one.Result!.Publication!.VersionId;
        var draft=await owner.CreateNextDraftAsync("source", NextDraftCommand(one.ResourceVersion!,source));
        var second=await owner.SaveAsync("source", AuthoringCommand("edit",draft.ResourceVersion,"new source",schema:AuthoringSchema.Replace("原文","changed")));
        await owner.PublishAsync("source", PublicationCommand(await Preview(owner,"source",second),"publish2"));
        var result=await owner.CloneAsync("source",CloneCommand(source));var copy=result.Result!;
        Assert.NotEqual(saved.ResourceId,copy.DefinitionId);Assert.NotEqual(source,copy.DraftId);Assert.Equal(1,copy.VersionNumber);
        Assert.False(copy.Published);Assert.False(copy.Enabled);Assert.Equal(source,copy.ClonedFromRef);Assert.Equal(AuthoringSchema,copy.Fields["SchemaJson"]);
        Assert.Equal("copied",copy.Fields["Name"]);Assert.Null(copy.Fields["FunctionId"]);Assert.Null(copy.Fields["FlowCode"]);
        Assert.Equal(source,(await owner.ReadAsync("copy")).ClonedFromRef);Assert.Empty(await owner.VersionsAsync("copy"));
        var edited=await owner.SaveAsync("copy",AuthoringCommand("edit-copy",copy.Version,"changed copy"));Assert.Equal(source,edited.Result!.ClonedFromRef);
        Assert.Equal("new source",(await owner.ReadHeadAsync("source")).Fields["Name"]);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_workflow_authoring_v2.heads SET cloned_from_version_id=null WHERE tenant_id={h.Tenant} AND flow_key='copy'"));
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowDefs.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_requires_exact_source_current_read_and_target_create_permission()
    {
        await using var h=await Seed();var owner=await PreparePublication(h);var saved=await owner.SaveAsync("source",AuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(saved.Result!.DraftId)))).Message);
        var p=await owner.PublishAsync("source",PublicationCommand(await Preview(owner,"source",saved)));var source=p.Result!.Publication!.VersionId;
        var other=await owner.SaveAsync("other",AuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("other",CloneCommand(source)))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FlowAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(source)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey==FlowAuthoringOwnerV2.Resource).ExecuteDeleteAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="add").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(source)))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.CloneOriginalAsync("copy","clone")).Outcome);
        await using var foreign=await Seed();var foreignOwner=await PreparePublication(foreign);await foreignOwner.SaveAsync("source",AuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>foreignOwner.CloneAsync("source",CloneCommand(source)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_unknown_commit_recovers_original_after_revoke_with_current_field_disclosure()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await PreparePublication(h);var saved=await owner.SaveAsync("source",AuthoringCommand());
        var p=await owner.PublishAsync("source",PublicationCommand(await Preview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="add").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FlowAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var original=await owner.CloneOriginalAsync("copy","clone");Assert.Equal("SUCCEEDED",original.Outcome);Assert.False(original.Result!.Fields.ContainsKey("SchemaJson"));
        Assert.Equal(original.ResourceVersion,(await owner.CloneAsync("source",raw)).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(p.Result.Publication.VersionId,name:"different")))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync("copy","clone")).Outcome);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant} AND operation='FLOW_CLONE'").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_audit_failure_and_missing_guard_leave_no_target_or_original()
    {
        await using var h=await Seed();var owner=await PreparePublication(h);var saved=await owner.SaveAsync("source",AuthoringCommand());
        var p=await owner.PublishAsync("source",PublicationCommand(await Preview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.audit ADD CONSTRAINT clone_test_fault CHECK(resource_key <> 'copy') NOT VALID");
        try{Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.audit DROP CONSTRAINT clone_test_fault");}
        Assert.Equal("NOT_OBSERVED",(await owner.CloneOriginalAsync("copy","clone")).Outcome);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.heads WHERE tenant_id={h.Tenant} AND flow_key='copy'").SingleAsync());
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.heads DISABLE TRIGGER clone_origin_immutable");
        try{Assert.Equal("CLONE_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_authoring_v2.heads ENABLE TRIGGER clone_origin_immutable");}
        Assert.Equal("SUCCEEDED",(await owner.CloneAsync("source",raw)).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_final_current_permission_revoke_rolls_back_all_effects()
    {
        await using var h=await Seed();var normal=await PreparePublication(h);var saved=await normal.SaveAsync("source",AuthoringCommand());
        var p=await normal.PublishAsync("source",PublicationCommand(await Preview(normal,"source",saved)));var calls=0;
        var owner=Authoring(h,async _=>{if(++calls==2)await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="add").ExecuteDeleteAsync();return h.User;});
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(p.Result!.Publication!.VersionId)))).Message);
        Assert.Equal("NOT_OBSERVED",(await normal.CloneOriginalAsync("copy","clone")).Outcome);
        Assert.Equal("SUCCEEDED",(await normal.CloneAsync("source",CloneCommand(p.Result!.Publication!.VersionId))).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_competing_connections_do_not_overwrite_target_and_replay_one_original()
    {
        await using var h=await Seed();var normal=await PreparePublication(h);var saved=await normal.SaveAsync("source",AuthoringCommand());
        var p=await normal.PublishAsync("source",PublicationCommand(await Preview(normal,"source",saved)));var source=p.Result!.Publication!.VersionId;
        await using var secondDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var app="clone-race-"+Guid.NewGuid().ToString("N");secondDb.Database.SetConnectionString(secondDb.Database.GetConnectionString()+";Application Name="+app);
        await using var monitor=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var second=Authoring(h with {Db=secondDb});Task<WorkflowCommandResultV1>? competing=null;var calls=0;
        var owner=Authoring(h,async _=>
        {
            if(++calls==2)
            {
                competing=second.CloneAsync("source",CloneCommand(source,name:"loser",id:"loser"));var waiting=false;
                for(var i=0;i<100&&!competing.IsCompleted;i++)
                {
                    waiting=await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if(waiting)break;await Task.Delay(20);
                }
                Assert.True(waiting);
            }
            return h.User;
        });
        var winner=await owner.CloneAsync("source",CloneCommand(source));
        Assert.Equal("DEFINITION_EXISTS",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>await competing!.WaitAsync(TimeSpan.FromSeconds(10)))).Message);
        Assert.Equal(winner.ResourceVersion,(await second.CloneAsync("source",CloneCommand(source))).ResourceVersion);
        Assert.Equal("copied",(await normal.ReadAsync("copy")).Fields["Name"]);
    }
    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_actual_HTTP_uses_exact_body_session_CSRF_and_original_slot()
    {
        await using var h=await Seed();var owner=await PreparePublication(h);var saved=await owner.SaveAsync("source",AuthoringCommand());
        var p=await owner.PublishAsync("source",PublicationCommand(await Preview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string route="/api/oa/v2/definitions/FLOW/source/clone";
        foreach(var bad in new[]{Encoding.UTF8.GetString(raw).Replace("\"expectedVersion\":null,",""),Encoding.UTF8.GetString(raw).Replace("copied","bad\\u0000name"),Encoding.UTF8.GetString(raw).Replace("\"newName\"","\"unexpected\"")})
        {using var doc=JsonDocument.Parse(bad);using var response=await host.Send(BoundRequest(cookies,route,doc.RootElement));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        using var command=JsonDocument.Parse(raw);using var accepted=await host.Send(BoundRequest(cookies,route,command.RootElement));
        Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());Assert.True(accepted.Headers.CacheControl?.NoStore);
        using var original=await host.Send(BoundRequest(cookies,"/api/oa/v2/commands/clone?operation=FLOW_CLONE&resource=copy"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);
        using var json=JsonDocument.Parse(await original.Content.ReadAsStringAsync());Assert.Equal("copy",json.RootElement.GetProperty("data").GetProperty("result").GetProperty("key").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task FORMClone_exact_old_publication_creates_disabled_independent_draft_and_keeps_immutable_origin()
    {
        await using var h=await Seed();var owner=await PrepareFormPublication(h);
        var saved=await owner.SaveAsync("source", FormAuthoringCommand(category:"分类",subCategory:"子类"));
        var one=await owner.PublishAsync("source", FormPublicationCommand(await FormPreview(owner,"source",saved)));
        var source=one.Result!.Publication!.VersionId;
        var draft=await owner.CreateNextDraftAsync("source", FormNextDraftCommand(one.ResourceVersion!,source));
        var second=await owner.SaveAsync("source", FormAuthoringCommand("edit",draft.ResourceVersion,"new source",schema:FormAuthoringSchema.Replace("原文","changed")));
        await owner.PublishAsync("source", FormPublicationCommand(await FormPreview(owner,"source",second),"publish2"));
        var result=await owner.CloneAsync("source",CloneCommand(source));var copy=result.Result!;
        Assert.NotEqual(saved.ResourceId,copy.DefinitionId);Assert.NotEqual(source,copy.DraftId);Assert.Equal(1,copy.VersionNumber);
        Assert.False(copy.Published);Assert.False(copy.Enabled);Assert.Equal(source,copy.ClonedFromRef);Assert.Equal(FormAuthoringSchema,copy.Fields["SchemaJson"]);
        Assert.Equal("copied",copy.Fields["Name"]);Assert.Equal("分类",copy.Fields["Category"]);Assert.Equal("子类",copy.Fields["SubCategory"]);
        Assert.Equal(source,(await owner.ReadAsync("copy")).ClonedFromRef);Assert.Empty(await owner.VersionsAsync("copy"));
        var edited=await owner.SaveAsync("copy",FormAuthoringCommand("edit-copy",copy.Version,"changed copy"));Assert.Equal(source,edited.Result!.ClonedFromRef);
        Assert.Equal("new source",(await owner.ReadHeadAsync("source")).Fields["Name"]);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_form_authoring_v2.heads SET cloned_from_version_id=null WHERE tenant_id={h.Tenant} AND form_key='copy'"));
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FormDefs.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_requires_exact_source_current_read_and_target_create_permission()
    {
        await using var h=await Seed();var owner=await PrepareFormPublication(h);var saved=await owner.SaveAsync("source",FormAuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(saved.Result!.DraftId)))).Message);
        var p=await owner.PublishAsync("source",FormPublicationCommand(await FormPreview(owner,"source",saved)));var source=p.Result!.Publication!.VersionId;
        var other=await owner.SaveAsync("other",FormAuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("other",CloneCommand(source)))).Message);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(source)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey==FormAuthoringOwnerV2.Resource).ExecuteDeleteAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="form-save").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(source)))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.CloneOriginalAsync("copy","clone")).Outcome);
        await using var foreign=await Seed();var foreignOwner=await PrepareFormPublication(foreign);await foreignOwner.SaveAsync("source",FormAuthoringCommand());
        Assert.Equal("SOURCE_VERSION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>foreignOwner.CloneAsync("source",CloneCommand(source)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_unknown_commit_recovers_original_after_revoke_with_current_field_disclosure()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var owner=await PrepareFormPublication(h);var saved=await owner.SaveAsync("source",FormAuthoringCommand());
        var p=await owner.PublishAsync("source",FormPublicationCommand(await FormPreview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="form-save").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var original=await owner.CloneOriginalAsync("copy","clone");Assert.Equal("SUCCEEDED",original.Outcome);Assert.False(original.Result!.Fields.ContainsKey("SchemaJson"));
        Assert.Equal(original.ResourceVersion,(await owner.CloneAsync("source",raw)).ResourceVersion);
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(p.Result.Publication.VersionId,name:"different")))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync("copy","clone")).Outcome);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.commands WHERE tenant_id={h.Tenant} AND operation='FORM_CLONE'").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_audit_failure_and_missing_guard_leave_no_target_or_original()
    {
        await using var h=await Seed();var owner=await PrepareFormPublication(h);var saved=await owner.SaveAsync("source",FormAuthoringCommand());
        var p=await owner.PublishAsync("source",FormPublicationCommand(await FormPreview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.audit ADD CONSTRAINT clone_test_fault CHECK(resource_key <> 'copy') NOT VALID");
        try{Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.audit DROP CONSTRAINT clone_test_fault");}
        Assert.Equal("NOT_OBSERVED",(await owner.CloneOriginalAsync("copy","clone")).Outcome);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.heads WHERE tenant_id={h.Tenant} AND form_key='copy'").SingleAsync());
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.heads DISABLE TRIGGER clone_origin_immutable");
        try{Assert.Equal("CLONE_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",raw))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.heads ENABLE TRIGGER clone_origin_immutable");}
        Assert.Equal("SUCCEEDED",(await owner.CloneAsync("source",raw)).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_final_current_permission_revoke_rolls_back_all_effects()
    {
        await using var h=await Seed();var normal=await PrepareFormPublication(h);var saved=await normal.SaveAsync("source",FormAuthoringCommand());
        var p=await normal.PublishAsync("source",FormPublicationCommand(await FormPreview(normal,"source",saved)));var calls=0;
        var owner=FormAuthoring(h,async _=>{if(++calls==2)await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="form-save").ExecuteDeleteAsync();return h.User;});
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CloneAsync("source",CloneCommand(p.Result!.Publication!.VersionId)))).Message);
        Assert.Equal("NOT_OBSERVED",(await normal.CloneOriginalAsync("copy","clone")).Outcome);
        Assert.Equal("SUCCEEDED",(await normal.CloneAsync("source",CloneCommand(p.Result!.Publication!.VersionId))).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_competing_connections_do_not_overwrite_target_and_replay_one_original()
    {
        await using var h=await Seed();var normal=await PrepareFormPublication(h);var saved=await normal.SaveAsync("source",FormAuthoringCommand());
        var p=await normal.PublishAsync("source",FormPublicationCommand(await FormPreview(normal,"source",saved)));var source=p.Result!.Publication!.VersionId;
        await using var secondDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        var app="clone-race-"+Guid.NewGuid().ToString("N");secondDb.Database.SetConnectionString(secondDb.Database.GetConnectionString()+";Application Name="+app);
        await using var monitor=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var second=FormAuthoring(h with {Db=secondDb});Task<FormAuthoringResultV2>? competing=null;var calls=0;
        var owner=FormAuthoring(h,async _=>
        {
            if(++calls==2)
            {
                competing=second.CloneAsync("source",CloneCommand(source,name:"loser",id:"loser"));var waiting=false;
                for(var i=0;i<100&&!competing.IsCompleted;i++)
                {
                    waiting=await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                    if(waiting)break;await Task.Delay(20);
                }
                Assert.True(waiting);
            }
            return h.User;
        });
        var winner=await owner.CloneAsync("source",CloneCommand(source));
        Assert.Equal("DEFINITION_EXISTS",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>await competing!.WaitAsync(TimeSpan.FromSeconds(10)))).Message);
        Assert.Equal(winner.ResourceVersion,(await second.CloneAsync("source",CloneCommand(source))).ResourceVersion);
        Assert.Equal("copied",(await normal.ReadAsync("copy")).Fields["Name"]);
    }
    [CurrentPermissionPostgresFact]
    public async Task FORMClone_actual_HTTP_uses_exact_body_session_CSRF_and_original_slot()
    {
        await using var h=await Seed();var owner=await PrepareFormPublication(h);var saved=await owner.SaveAsync("source",FormAuthoringCommand());
        var p=await owner.PublishAsync("source",FormPublicationCommand(await FormPreview(owner,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string route="/api/oa/v2/definitions/FORM/source/clone";
        foreach(var bad in new[]{Encoding.UTF8.GetString(raw).Replace("\"expectedVersion\":null,",""),Encoding.UTF8.GetString(raw).Replace("copied","bad\\u0000name"),Encoding.UTF8.GetString(raw).Replace("\"newName\"","\"unexpected\"")})
        {using var doc=JsonDocument.Parse(bad);using var response=await host.Send(BoundRequest(cookies,route,doc.RootElement));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        using var command=JsonDocument.Parse(raw);using var accepted=await host.Send(BoundRequest(cookies,route,command.RootElement));
        Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());Assert.True(accepted.Headers.CacheControl?.NoStore);
        using var original=await host.Send(BoundRequest(cookies,"/api/oa/v2/commands/clone?operation=FORM_CLONE&resource=copy"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);
        using var json=JsonDocument.Parse(await original.Content.ReadAsStringAsync());Assert.Equal("copy",json.RootElement.GetProperty("data").GetProperty("result").GetProperty("key").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task FLOWClone_recovery_checks_expiry_at_response_completion()
    {
        await using var h=await Seed();var normal=await PreparePublication(h);var saved=await normal.SaveAsync("source",AuthoringCommand());
        var p=await normal.PublishAsync("source",PublicationCommand(await Preview(normal,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await normal.CloneAsync("source",raw);
        foreach(var query in new[]{true,false})
        {
            var calls=0;var identity=(System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!;var expiry=new System.Security.Claims.Claim("exp","1");
            var owner=Authoring(h,_=>{if(++calls==2)identity.AddClaim(expiry);return Task.FromResult(h.User);});
            try{Assert.Equal("REAUTH_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>{if(query)await owner.CloneOriginalAsync("copy","clone");else await owner.CloneAsync("source",raw);})).Message);}
            finally{foreach(var claim in identity.FindAll("exp").Where(c=>c.Value=="1").ToArray())identity.RemoveClaim(claim);}
        }
        Assert.Equal("SUCCEEDED",(await normal.CloneOriginalAsync("copy","clone")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task FORMClone_recovery_checks_expiry_at_response_completion()
    {
        await using var h=await Seed();var normal=await PrepareFormPublication(h);var saved=await normal.SaveAsync("source",FormAuthoringCommand());
        var p=await normal.PublishAsync("source",FormPublicationCommand(await FormPreview(normal,"source",saved)));var raw=CloneCommand(p.Result!.Publication!.VersionId);
        await normal.CloneAsync("source",raw);
        foreach(var query in new[]{true,false})
        {
            var calls=0;var identity=(System.Security.Claims.ClaimsIdentity)h.Http.User.Identity!;var expiry=new System.Security.Claims.Claim("exp","1");
            var owner=FormAuthoring(h,_=>{if(++calls==2)identity.AddClaim(expiry);return Task.FromResult(h.User);});
            try{Assert.Equal("REAUTH_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>{if(query)await owner.CloneOriginalAsync("copy","clone");else await owner.CloneAsync("source",raw);})).Message);}
            finally{foreach(var claim in identity.FindAll("exp").Where(c=>c.Value=="1").ToArray())identity.RemoveClaim(claim);}
        }
        Assert.Equal("SUCCEEDED",(await normal.CloneOriginalAsync("copy","clone")).Outcome);
    }
}

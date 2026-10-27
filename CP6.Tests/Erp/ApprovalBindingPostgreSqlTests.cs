using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private ApprovalBindingOwnerV2 BindingOwner(Harness h,FlowAuthoringOwnerV2 flows,Func<CancellationToken,Task<Guid>>? actor=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterScope(ApprovalBindingOwnerV2.Resource,"binding",[1,2,3,4,5],1);
        catalog.RegisterFields(ApprovalBindingOwnerV2.Resource,ApprovalBindingOwnerV2.Fields.Select(f=>new FieldRegistry.Field(f,f)));catalog.Seal();
        return new(h.Db,new AuthoringHttpAccessor{HttpContext=h.Http},ScopeFence(h),actor??(_=>Task.FromResult(h.User)),flows,catalog.Capture);
    }
    private async Task<(ApprovalBindingOwnerV2 Owner,FlowAuthoringOwnerV2 Flows,Guid Version)> PrepareBinding(Harness h)
    {
        var (flows,p)=await PrepareActivation(h,"binding-flow");var version=p.Result!.Publication!.VersionId;
        await flows.SetActivationAsync("binding-flow",ActivationCommand((await flows.ActivationPreviewAsync("binding-flow")).Current,version));
        await EngineeringAction(h,ApprovalBindingOwnerV2.Menu,ApprovalBindingOwnerV2.ReadAction);await EngineeringAction(h,ApprovalBindingOwnerV2.Menu,ApprovalBindingOwnerV2.ConfigureAction);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'TEST_DISPOSITION','TEST_TOOL','Test Owner',{Guid.NewGuid()},'test.snapshot/1','["amount","purpose"]',true,{Guid.NewGuid()},'test.workflow','/internal/test/approval-results/v2','DEMO')
            """);
        return(BindingOwner(h,flows),flows,version);
    }
    private static DateTimeOffset BindingTime()=>DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
    private static ApprovalBindingWriteV2 BindingInput(Guid version,DateTimeOffset? from=null,Guid? command=null)=>new(command??Guid.NewGuid(),"TEST_DISPOSITION","TEST_TOOL","EXACT_PINNED","binding-flow",version,from??BindingTime(),null,"EXCLUSIVE_MATCH",[],"/tools/{bizId}","reviewed configuration");
    private static byte[] BindingBytes(object body)=>Encoding.UTF8.GetBytes(ApprovalBindingOwnerV2.Serialize(body));
    private static string BindingEtag(ApprovalBindingResultV2 result)=>"\""+result.Result.RowVersion+"\"";

    [CurrentPermissionPostgresFact]
    public async Task Binding_open_intervals_revise_without_mutating_history_and_disable_cancels_scheduled_revisions()
    {
        await using var h=await Seed();var (owner,_,version)=await PrepareBinding(h);var input=BindingInput(version);var one=await owner.SaveAsync(null,null,BindingBytes(input));
        Assert.Equal(8,Convert.FromBase64String(one.Result.RowVersion).Length);Assert.Equal("Test Owner",one.Result.Owner);
        var original=await owner.TimelineAsync(one.Result.BindingId,one.Result.TimelineRevisionId);
        var two=await owner.SaveAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(input with{CommandId=Guid.NewGuid(),EffectiveFrom=input.EffectiveFrom.AddHours(1)}));
        var next=await owner.TimelineAsync(two.Result.BindingId,two.Result.TimelineRevisionId);
        Assert.Equal(2,next.Intervals.Length);Assert.Equal(next.Intervals[1].From,next.Intervals[0].To);Assert.Null(original.Intervals[0].To);
        Assert.Equal(ApprovalBindingOwnerV2.Serialize(original),ApprovalBindingOwnerV2.Serialize(await owner.TimelineAsync(one.Result.BindingId,original.TimelineRevisionId)));
        var disable=BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"stop new intents"));var stopped=await owner.DisableAsync(one.Result.BindingId,BindingEtag(two),disable);
        Assert.Equal("DISABLED",stopped.Result.State);Assert.Empty(stopped.Result.ScheduledIntervals);Assert.Equal(2,(await owner.HistoryAsync(one.Result.BindingId)).Items.Length);
        Assert.Equal(one.Result.TimelineRevisionId,(await owner.HistoryAsync(one.Result.BindingId)).Items.Single(x=>x.Revision==1).TimelineRevisionId);
        var replay=await owner.SaveAsync(null,null,BindingBytes(input));Assert.Equal(one.Result.TimelineRevisionId,replay.Result.TimelineRevisionId);
        Assert.Equal("DISABLED",(await owner.ListAsync()).Items.Single().State);Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.timelines SET body='{{}}' WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.heads SET row_version=row_version+1 WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_future_switch_conflicts_and_finite_expiry_never_restore_an_old_revision()
    {
        await using var h=await Seed();var (owner,_,version)=await PrepareBinding(h);var t=BindingTime();var one=await owner.SaveAsync(null,null,BindingBytes(BindingInput(version,t.AddHours(2))));
        var before=BindingInput(version,t);
        Assert.Equal("FUTURE_SWITCH_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(before)))).Message);
        var two=await owner.SaveAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(before with{EffectiveTo=t.AddHours(1)}));
        var timeline=await owner.TimelineAsync(one.Result.BindingId,two.Result.TimelineRevisionId);
        Assert.Equal(new[]{"ACTIVE","DISABLED","ACTIVE"},timeline.Intervals.Select(x=>x.State));Assert.Null(timeline.Intervals[1].RevisionId);
        Assert.Equal("VERSION_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(BindingInput(version,t.AddHours(3)))))).Message);
        Assert.Equal("PRECONDITION_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DisableAsync(one.Result.BindingId,null,BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"missing CAS"))))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_closed_input_bad_rules_wrong_pin_unregistered_owner_and_external_routes_write_nothing()
    {
        await using var h=await Seed();var (owner,_,version)=await PrepareBinding(h);var input=BindingInput(version);
        foreach(var invalid in new[]{input with{FlowDefVersionId=Guid.NewGuid()},input with{DetailRoute="//attacker/{bizId}"},input with{DetailRoute="/%2fhost/{bizId}"},input with{SubjectKind="FORGED"},
            input with{SelectionMode="RESOLVE_PUBLISHED_ON_SUBMIT"},input with{Rules=[new(Guid.NewGuid(),0,"missing > 1","binding-flow",version)]},input with{Rules=[new(Guid.NewGuid(),0,"amount >","binding-flow",version)]}})
            await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,BindingBytes(invalid)));
        var forged=Encoding.UTF8.GetString(BindingBytes(input)).TrimEnd('}')+",\"owner\":\"forged\"}";
        Assert.Equal(422,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,Encoding.UTF8.GetBytes(forged)))).Status);
        Assert.Equal("MALFORMED_JSON",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,Encoding.UTF8.GetBytes("{")))).Message);
        Assert.Empty((await owner.ListAsync()).Items);
        var valid=input with{Rules=[new(Guid.NewGuid(),0,"amount > 1 && purpose == 'production'","binding-flow",version)]};
        Assert.Equal("COMPLETED",(await owner.SaveAsync(null,null,BindingBytes(valid))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_unknown_commit_and_write_revocation_keep_original_readable_but_cannot_reapply()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(owner,_,version)=await PrepareBinding(h);var input=BindingInput(version);fault.Armed=true;
        Assert.Equal("OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,BindingBytes(input)))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode==ApprovalBindingOwnerV2.ConfigureAction).ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        var original=await owner.OriginalAsync(input.CommandId);Assert.Equal(original.Result.BindingId,(await owner.SaveAsync(null,null,BindingBytes(input))).Result.BindingId);
        Assert.Equal("INTENT_PAYLOAD_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,BindingBytes(input with{Remark="different"})))).Message);
        Assert.Single((await owner.HistoryAsync(original.Result.BindingId)).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_final_revoke_and_registration_ABA_roll_back_all_new_records()
    {
        await using var h=await Seed();var(normal,flows,version)=await PrepareBinding(h);var calls=0;
        var owner=BindingOwner(h,flows,async _=>{if(++calls==2)await h.Db.Sys_RoleActions.Where(x=>x.ActionCode==ApprovalBindingOwnerV2.ConfigureAction).ExecuteDeleteAsync();return h.User;});
        Assert.Equal("COMMAND_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,BindingBytes(BindingInput(version))))).Message);
        Assert.Empty((await normal.ListAsync()).Items);calls=0;
        owner=BindingOwner(h,flows,async _=>{if(++calls==2){await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.registrations SET enabled=false WHERE tenant_id={h.Tenant}");await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_binding_v2.registrations SET enabled=true WHERE tenant_id={h.Tenant}");}return h.User;});
        Assert.Equal("SCOPE_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveAsync(null,null,BindingBytes(BindingInput(version))))).Message);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_binding_v2.timelines WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_current_read_fields_scope_and_writer_coverage_remain_required()
    {
        await using var h=await Seed();var(owner,_,version)=await PrepareBinding(h);var one=await owner.SaveAsync(null,null,BindingBytes(BindingInput(version)));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=ApprovalBindingOwnerV2.Resource,FieldName="Configuration",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OriginalAsync(one.CommandId))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey==ApprovalBindingOwnerV2.Resource).ExecuteDeleteAsync();
        await using var other=await Seed();var(foreign,_,_)=await PrepareBinding(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>foreign.TimelineAsync(one.Result.BindingId,one.Result.TimelineRevisionId))).Status);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_approval_binding_v2.registrations DISABLE TRIGGER binding_scope");
        try
        {
            var rejected=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DisableAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"blocked"))));
            Assert.Equal("BINDING_WRITER_COVERAGE_UNAVAILABLE",rejected.Message);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_approval_binding_v2.registrations ENABLE TRIGGER binding_scope");}
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_independent_connections_serialize_and_stale_loser_cannot_overwrite()
    {
        await using var h=await Seed();var(normal,flows,version)=await PrepareBinding(h);var one=await normal.SaveAsync(null,null,BindingBytes(BindingInput(version)));
        await using var db2=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});
        var app="binding-race-"+Guid.NewGuid().ToString("N");db2.Database.SetConnectionString(db2.Database.GetConnectionString()+";Application Name="+app);
        await using var monitor=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var h2=h with{Db=db2};var second=BindingOwner(h2,Authoring(h2));
        Task<ApprovalBindingResultV2>? loser=null;var calls=0;
        var first=BindingOwner(h,flows,async _=>
        {
            if(++calls==2)
            {
                loser=second.DisableAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"loser")));
                var waiting=false;for(var i=0;i<100&&!loser.IsCompleted;i++)
                {waiting=await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();if(waiting)break;await Task.Delay(20);}
                Assert.True(waiting);Assert.False(loser.IsCompleted);
            }
            return h.User;
        });
        var winner=await first.DisableAsync(one.Result.BindingId,BindingEtag(one),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"winner")));
        Assert.NotNull(loser);Assert.Equal("VERSION_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>await loser!)).Message);
        Assert.Equal(winner.Result.TimelineRevisionId,(await normal.ListAsync()).Items.Single().TimelineRevisionId);
    }
    [CurrentPermissionPostgresFact]
    public async Task Binding_actual_HTTP_enforces_CSRF_closed_contract_and_original_query()
    {
        await using var h=await Seed();var(_,_,version)=await PrepareBinding(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var input=BindingInput(version);using var doc=JsonDocument.Parse(BindingBytes(input));
        using(var denied=await host.Send(BoundRequest(cookies,"/api/oa/app/v2/bindings",doc.RootElement,false)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var success=await host.Send(BoundRequest(cookies,"/api/oa/app/v2/bindings",doc.RootElement)))
        {Assert.True(success.IsSuccessStatusCode,await success.Content.ReadAsStringAsync());Assert.Equal(HttpStatusCode.Created,success.StatusCode);Assert.True(success.Headers.CacheControl?.NoStore);}
        using var read=await host.Send(BoundRequest(cookies,"/api/oa/app/v2/commands/"+input.CommandId));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        using var result=JsonDocument.Parse(await read.Content.ReadAsStringAsync());Assert.Equal("COMPLETED",result.RootElement.GetProperty("state").GetString());Assert.EndsWith("Z",result.RootElement.GetProperty("recordedAt").GetString());
    }
    [Fact]
    public void Binding_expression_validation_compiles_all_branches_without_sampling_business_values()
    {
        Assert.True(ExpressionEvaluator.ValidateSchemaExpression("dateDiff(purpose, '2026-01-01') > 0 && amount / 0 > 1",["purpose","amount"]));
        Assert.False(ExpressionEvaluator.ValidateSchemaExpression("true || missing > 1",["amount"]));
        Assert.False(ExpressionEvaluator.ValidateSchemaExpression("evil(amount)",["amount"]));
        Assert.False(ExpressionEvaluator.ValidateSchemaExpression("sum(amount,)",["amount"]));
        Assert.True(ExpressionEvaluator.Evaluate("amount > 1",new Dictionary<string,object?>{{"amount",2d}}));
    }
}

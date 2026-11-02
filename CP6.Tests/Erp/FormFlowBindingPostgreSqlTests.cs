using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<FlowPublicationV2> BindingFlow(FlowAuthoringOwnerV2 flow,string key,string form="form")
    {
        var saved=await flow.SaveAsync(key,DependentCommand(form));
        return (await flow.PublishAsync(key,PublicationCommand(await Preview(flow,key,saved)))).Result!.Publication!;
    }
    private sealed record ActiveFormBinding(Guid Flow,Guid Form,Guid FormVersion,Guid Revision);
    private static Task<ActiveFormBinding[]> ActiveFormBindings(Harness h)=>h.Db.Database.SqlQuery<ActiveFormBinding>($"""
        SELECT head_id AS "Flow",form_head_id AS "Form",form_version_id AS "FormVersion",revision AS "Revision"
        FROM cp6_workflow_authoring_v2.activation_current WHERE tenant_id={h.Tenant} AND enabled AND form_head_id IS NOT NULL
        """).ToArrayAsync();
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_exact_published_source_survives_draft_changes_and_disable_releases_unique_binding()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var f=await ActiveDependencyForm(form);await ActiveDependencyForm(form,"other");
        var a=await BindingFlow(flow,"a");var b=await BindingFlow(flow,"b");
        var originalCommand=ActivationCommand((await flow.ActivationPreviewAsync("a")).Current,a.VersionId);
        var active=await flow.SetActivationAsync("a",originalCommand);
        var draft=await flow.CreateNextDraftAsync("a",NextDraftCommand((await flow.ReadHeadAsync("a")).Version,a.VersionId));
        await flow.SaveAsync("a",DependentCommand("other",id:"edit",token:draft.ResourceVersion));
        var bound=Assert.Single(await ActiveFormBindings(h));Assert.Equal(a.DefinitionId,bound.Flow);Assert.Equal(f.VersionId,bound.FormVersion);
        var preview=await flow.ActivationPreviewAsync("a");Assert.Equal("form",preview.FormKey);Assert.Equal(f.VersionId,preview.FormVersionId);
        var bCurrent=(await flow.ActivationPreviewAsync("b")).Current;
        Assert.Equal("E-WF-008",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("b",ActivationCommand(bCurrent,b.VersionId)))).Message);
        Assert.Empty(await flow.ActivationHistoryAsync("b"));Assert.Equal("NOT_OBSERVED",(await flow.ActivationOriginalAsync("b","activate")).Outcome);
        await flow.SetActivationAsync("a",ActivationCommand(active.Result!,a.VersionId,false,"off"));Assert.Empty(await ActiveFormBindings(h));
        var bActive=await flow.SetActivationAsync("b",ActivationCommand(bCurrent,b.VersionId));Assert.Equal(bActive.Result!.Revision,Assert.Single(await ActiveFormBindings(h)).Revision);
        Assert.Equal(active.ResourceVersion,(await flow.SetActivationAsync("a",originalCommand)).ResourceVersion);
        Assert.False((await flow.ActivationPreviewAsync("a")).Current.Enabled);Assert.Equal(b.DefinitionId,Assert.Single(await ActiveFormBindings(h)).Flow);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_changing_own_selected_version_moves_binding_atomically_and_conflict_preserves_old_binding()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);var other=await ActiveDependencyForm(form,"other");
        var first=await ActiveDependencyFlow(flow,"a","form");var blocker=await ActiveDependencyFlow(flow,"blocker","other");
        var draft=await flow.CreateNextDraftAsync("a",NextDraftCommand((await flow.ReadHeadAsync("a")).Version,first.VersionId));var edit=await flow.SaveAsync("a",DependentCommand("other",id:"edit",token:draft.ResourceVersion));
        var second=(await flow.PublishAsync("a",PublicationCommand(await Preview(flow,"a",edit),"publish2"))).Result!.Publication!;
        var current=(await flow.ActivationPreviewAsync("a")).Current;
        Assert.Equal("E-WF-008",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("a",ActivationCommand(current,second.VersionId,id:"switch")))).Message);
        Assert.Equal(current,(await flow.ActivationPreviewAsync("a")).Current);Assert.Equal(2,(await ActiveFormBindings(h)).Length);
        await flow.SetActivationAsync("blocker",ActivationCommand((await flow.ActivationPreviewAsync("blocker")).Current,blocker.VersionId,false,"off"));
        await flow.SetActivationAsync("a",ActivationCommand(current,second.VersionId,id:"switch"));
        var binding=Assert.Single(await ActiveFormBindings(h));Assert.Equal(first.DefinitionId,binding.Flow);Assert.Equal(other.VersionId,binding.FormVersion);
        var reuse=await ActiveDependencyFlow(flow,"reuse","form");Assert.Contains(await ActiveFormBindings(h),x=>x.Flow==reuse.DefinitionId);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_two_connections_have_one_winner_without_disabling_the_other_flow()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);var a=await BindingFlow(flow,"a");var b=await BindingFlow(flow,"b");
        var ac=(await flow.ActivationPreviewAsync("a")).Current;var bc=(await flow.ActivationPreviewAsync("b")).Current;
        await using var db2=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var app="form-binding-"+Guid.NewGuid().ToString("N");db2.Database.SetConnectionString(db2.Database.GetConnectionString()+";Application Name="+app);
        await using var monitor=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var other=Authoring(h with{Db=db2});Task<FlowActivationResultV2>? loser=null;var calls=0;
        var owner=Authoring(h,async _=>{
            if(++calls==2){loser=other.SetActivationAsync("b",ActivationCommand(bc,b.VersionId));var waiting=false;
                for(var i=0;i<100&&!loser.IsCompleted;i++){waiting=await monitor.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={app} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();if(waiting)break;await Task.Delay(20);}
                Assert.True(waiting);Assert.False(loser.IsCompleted);}
            return h.User;
        });
        await owner.SetActivationAsync("a",ActivationCommand(ac,a.VersionId));Assert.NotNull(loser);
        Assert.Equal("E-WF-008",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(async()=>await loser!)).Message);
        Assert.Equal(a.DefinitionId,Assert.Single(await ActiveFormBindings(h)).Flow);Assert.False((await flow.ActivationPreviewAsync("b")).Current.Enabled);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_lost_ACK_replays_old_result_and_final_revoke_rolls_back_the_binding()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);var p=await BindingFlow(flow,"a");
        var current=(await flow.ActivationPreviewAsync("a")).Current;var command=ActivationCommand(current,p.VersionId);var calls=0;
        var owner=Authoring(h,async _=>{if(++calls==2)await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="enable").ExecuteDeleteAsync();return h.User;});
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SetActivationAsync("a",command))).Message);Assert.Empty(await ActiveFormBindings(h));
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("a",command))).Message);
        var binding=Assert.Single(await ActiveFormBindings(h));await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="enable").ExecuteDeleteAsync();
        var replay=await flow.SetActivationAsync("a",command);Assert.Equal(binding.Revision,replay.Result!.Revision);Assert.Single(await flow.ActivationHistoryAsync("a"));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormBinding_actual_HTTP_returns_non_disclosing_conflict_and_projects_current_FORM_fields()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var f=await ActiveDependencyForm(form);await ActiveDependencyFlow(flow,"hidden-other-name","form");var b=await BindingFlow(flow,"b");var current=(await flow.ActivationPreviewAsync("b")).Current;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        using var body=JsonDocument.Parse(ActivationCommand(current,b.VersionId));using var response=await host.Send(BoundRequest(cookies,"/api/oa/v2/definitions/FLOW/b/activation",body.RootElement));
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);var text=await response.Content.ReadAsStringAsync();Assert.Contains("E-WF-008",text);Assert.DoesNotContain("hidden-other-name",text);
        Assert.Equal(f.VersionId,Assert.Single((await flow.ActivationPreviewAsync("b")).Versions).FormVersionId);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FlowAuthoringOwnerV2.Resource,FieldName="FormKey",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var hidden=await flow.ActivationPreviewAsync("b");Assert.Null(Assert.Single(hidden.Versions).FormKey);Assert.Null(Assert.Single(hidden.Versions).FormVersionId);
    }
}

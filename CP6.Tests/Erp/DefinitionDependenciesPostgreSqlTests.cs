using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(FlowAuthoringOwnerV2 Flow, FormAuthoringOwnerV2 Form)> PrepareDependencies(Harness h)
    {
        var flow = await PreparePublication(h); var form = FormAuthoring(h);
        await EngineeringAction(h,"oa-designer","form-save"); await EngineeringAction(h,"oa-designer","form-publish");
        await form.ConfigureAsync(new(Guid.NewGuid(),"absent",true,[],"dependency form authoring"));
        await form.ConfigurePublicationAsync(new(Guid.NewGuid(),"absent",true,false,[],"dependency form publication"));
        await EngineeringAction(h,"oa-flow-admin","enable"); await EngineeringAction(h,"oa-flow-admin","form-enable");
        var menu = await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="oa-flow-admin");
        h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=menu.MenuId}); await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        return(flow,form);
    }
    private static string ChildSchema(string key) => JsonSerializer.Serialize(new { start="s",
        nodes=new object[]{new{id="s",type="start"},new{id="sub",type="subFlow",subFlowKey=key},new{id="e",type="end"}},
        edges=new[]{new{from="s",to="sub"},new{from="sub",to="e"}} });
    private static byte[] DependentCommand(string? form=null,string? child=null,string id="save",string? token=null,string name="dependent",string? schema=null)
        => JsonSerializer.SerializeToUtf8Bytes(new{commandId=id,expectedVersion=token,body=new{name,schemaJson=schema??(child==null?AuthoringSchema:ChildSchema(child)),formKey=form,functionId=(string?)null,flowCode=(string?)null}});
    private static async Task<FormPublicationV2> ActiveDependencyForm(FormAuthoringOwnerV2 owner,string key="form")
    {
        var saved=await owner.SaveAsync(key,FormAuthoringCommand());var p=(await owner.PublishAsync(key,FormPublicationCommand(await FormPreview(owner,key,saved)))).Result!.Publication!;
        await owner.SetActivationAsync(key,FormActivationCommand((await owner.ActivationPreviewAsync(key)).Current,p.VersionId));return p;
    }
    private static async Task<FlowPublicationV2> ActiveDependencyFlow(FlowAuthoringOwnerV2 owner,string key,string? form=null,string? child=null)
    {
        var saved=await owner.SaveAsync(key,DependentCommand(form,child));var preview=await Preview(owner,key,saved);Assert.True(preview.CanPublish,string.Join(',',preview.Diagnostics));
        var p=(await owner.PublishAsync(key,PublicationCommand(preview))).Result!.Publication!;
        await owner.SetActivationAsync(key,ActivationCommand((await owner.ActivationPreviewAsync(key)).Current,p.VersionId));return p;
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_preview_pins_form_and_publish_does_not_switch_when_a_new_version_is_activated()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var first=await ActiveDependencyForm(form);
        var saved=await flow.SaveAsync("parent",DependentCommand("form"));var preview=await Preview(flow,"parent",saved);
        Assert.True(preview.CanPublish);Assert.Equal(first.VersionId.ToString("D"),Assert.Single(preview.Dependencies).Version);
        var head=await form.ReadHeadAsync("form");var draft=await form.CreateNextDraftAsync("form",FormNextDraftCommand(head.Version,first.VersionId));
        var edit=await form.SaveAsync("form",FormAuthoringCommand("edit",draft.ResourceVersion,"second"));
        var second=(await form.PublishAsync("form",FormPublicationCommand(await FormPreview(form,"form",edit),"publish2"))).Result!.Publication!;
        await form.SetActivationAsync("form",FormActivationCommand((await form.ActivationPreviewAsync("form")).Current,second.VersionId,id:"activate2"));
        var published=(await flow.PublishAsync("parent",PublicationCommand(preview))).Result!.Publication!;
        Assert.Equal(first.VersionId.ToString("D"),Assert.Single(published.DependencyRefs).Version);
        await flow.SetActivationAsync("parent",ActivationCommand((await flow.ActivationPreviewAsync("parent")).Current,published.VersionId));
        Assert.Equal(first.VersionId.ToString("D"),Assert.Single((await flow.VersionsAsync("parent"))[0].DependencyRefs).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_nested_pins_keep_old_child_sources_and_disabled_descendants_block_publication_and_activation()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var f=await ActiveDependencyForm(form);
        var leaf=await ActiveDependencyFlow(flow,"leaf","form");var middle=await ActiveDependencyFlow(flow,"middle",child:"leaf");
        var saved=await flow.SaveAsync("root",DependentCommand(child:"middle"));var preview=await Preview(flow,"root",saved);Assert.True(preview.CanPublish);
        var draft=await flow.CreateNextDraftAsync("leaf",NextDraftCommand((await flow.ReadHeadAsync("leaf")).Version,leaf.VersionId));
        var edit=await flow.SaveAsync("leaf",DependentCommand(id:"edit",token:draft.ResourceVersion));
        var newer=(await flow.PublishAsync("leaf",PublicationCommand(await Preview(flow,"leaf",edit),"publish2"))).Result!.Publication!;
        await flow.SetActivationAsync("leaf",ActivationCommand((await flow.ActivationPreviewAsync("leaf")).Current,newer.VersionId,id:"activate2"));
        await form.SetActivationAsync("form",FormActivationCommand((await form.ActivationPreviewAsync("form")).Current,f.VersionId,false,"off"));
        Assert.Equal("PREVIEW_STALE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.PublishAsync("root",PublicationCommand(preview)))).Message);
        Assert.Empty(await flow.VersionsAsync("root"));
        await form.SetActivationAsync("form",FormActivationCommand((await form.ActivationPreviewAsync("form")).Current,f.VersionId,true,"on"));
        var root=(await flow.PublishAsync("root",PublicationCommand(preview))).Result!.Publication!;
        Assert.Equal(middle.VersionId.ToString("D"),Assert.Single(root.DependencyRefs).Version);
        Assert.Equal(leaf.VersionId.ToString("D"),Assert.Single((await flow.VersionsAsync("middle"))[0].DependencyRefs).Version);
        var active=await flow.SetActivationAsync("root",ActivationCommand((await flow.ActivationPreviewAsync("root")).Current,root.VersionId));
        await form.SetActivationAsync("form",FormActivationCommand((await form.ActivationPreviewAsync("form")).Current,f.VersionId,false,"off2"));
        Assert.Equal("DEPENDENCY_DISABLED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("root",ActivationCommand(active.Result!,root.VersionId,id:"recheck")))).Message);
        Assert.False((await flow.SetActivationAsync("root",ActivationCommand(active.Result!,root.VersionId,false,"off"))).Result!.Enabled);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_reject_cycles_through_older_versions_and_enforce_recursive_depth()
    {
        await using var h=await Seed();var(flow,_)=await PrepareDependencies(h);var first=await ActiveDependencyFlow(flow,"a");await ActiveDependencyFlow(flow,"b",child:"a");
        var next=await flow.CreateNextDraftAsync("a",NextDraftCommand((await flow.ReadHeadAsync("a")).Version,first.VersionId));
        var edit=await flow.SaveAsync("a",DependentCommand(child:"b",id:"cycle",token:next.ResourceVersion));
        var cycle=await Preview(flow,"a",edit);Assert.False(cycle.CanPublish);Assert.Contains("DEPENDENCY_CYCLE",cycle.Diagnostics);
        var key="depth0";await ActiveDependencyFlow(flow,key);
        for(var i=1;i<SubFlowLimits.MaxDepth;i++){var parent="depth"+i;await ActiveDependencyFlow(flow,parent,child:key);key=parent;}
        var over=await flow.SaveAsync("over-depth",DependentCommand(child:key));var deep=await Preview(flow,"over-depth",over);
        Assert.False(deep.CanPublish);Assert.Contains("DEPENDENCY_DEPTH_EXCEEDED",deep.Diagnostics);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_current_form_disclosure_and_final_revoke_are_checked_without_partial_publication()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);
        var saved=await flow.SaveAsync("parent",DependentCommand("form"));var preview=await Preview(flow,"parent",saved);var calls=0;
        var owner=Authoring(h,async _=>
        {
            if(++calls==2){h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();}
            return h.User;
        });
        Assert.Equal("PREVIEW_STALE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.PublishAsync("parent",PublicationCommand(preview)))).Message);
        Assert.Empty(await flow.VersionsAsync("parent"));Assert.Equal("NOT_OBSERVED",(await flow.LifecycleOriginalAsync("parent",FlowAuthoringOwnerV2.PublishOperation,"publish")).Outcome);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var hidden=await Preview(flow,"parent",saved);Assert.False(hidden.CanPublish);Assert.Contains("DEPENDENCY_NOT_READABLE",hidden.Diagnostics);Assert.Null(Assert.Single(hidden.Dependencies).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_binding_and_registered_service_preserve_APP_dynamic_latest_and_exact_selection()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);await ActiveDependencyForm(form);var first=await ActiveDependencyFlow(flow,"root","form");
        var draft=await flow.CreateNextDraftAsync("root",NextDraftCommand((await flow.ReadHeadAsync("root")).Version,first.VersionId));
        var edit=await flow.SaveAsync("root",DependentCommand("form",id:"edit",token:draft.ResourceVersion,name:"second"));
        var second=(await flow.PublishAsync("root",PublicationCommand(await Preview(flow,"root",edit),"publish2"))).Result!.Publication!;
        await using var tx=await h.Db.Database.BeginTransactionAsync();await AuthenticationCompletionGateV1.AcquireAsync(h.Db,default);await ScopeFence(h).AcquireAsync();
        var actor=await PostgreSqlPermissionReader.ReadAsync(h.Db,h.User,default);
        Assert.Equal(second.VersionId,(await flow.BindingSource(actor,"root",null,default)).VersionId);
        Assert.Equal(first.VersionId,(await flow.BindingSource(actor,"root",first.VersionId,default)).VersionId);
        await h.Db.Sys_RoleMenus.ExecuteDeleteAsync(); // Registered-service TxS uses its existing service/binding authority, not a governance user.
        Assert.Equal(second.VersionId,(await flow.SubmissionSource("root",null,default)).VersionId);
        Assert.Equal(first.VersionId,(await flow.SubmissionSource("root",first.VersionId,default)).VersionId);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_compatibility_uses_preview_FORM_pin_not_later_schema_and_guard_failure_is_explicit()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var first=await ActiveDependencyForm(form);
        var schema=AuthoringSchema.Replace("\"type\":\"start\"","\"type\":\"start\",\"fieldPerms\":{\"a\":\"read\"}");
        var saved=await flow.SaveAsync("compatible",DependentCommand("form",schema:schema));var preview=await Preview(flow,"compatible",saved);Assert.True(preview.CanPublish);
        var draft=await form.CreateNextDraftAsync("form",FormNextDraftCommand((await form.ReadHeadAsync("form")).Version,first.VersionId));
        var edit=await form.SaveAsync("form",FormAuthoringCommand("edit",draft.ResourceVersion,schema:FormAuthoringSchema.Replace("\"a\"","\"b\"")));
        var next=(await form.PublishAsync("form",FormPublicationCommand(await FormPreview(form,"form",edit),"publish2"))).Result!.Publication!;
        await form.SetActivationAsync("form",FormActivationCommand((await form.ActivationPreviewAsync("form")).Current,next.VersionId,id:"activate2"));
        var incompatible=await Preview(flow,"compatible",saved);Assert.False(incompatible.CanPublish);Assert.Contains("FORM_FLOW_INCOMPATIBLE",incompatible.Diagnostics);
        var published=(await flow.PublishAsync("compatible",PublicationCommand(preview))).Result!.Publication!;
        Assert.Equal(first.VersionId.ToString("D"),Assert.Single(published.DependencyRefs).Version);
        var current=(await flow.ActivationPreviewAsync("compatible")).Current;
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.versions DISABLE TRIGGER publication_versions_immutable");
        try
        {
            var error=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SetActivationAsync("compatible",ActivationCommand(current,published.VersionId)));
            Assert.Equal(503,error.Status);Assert.Equal("ACTIVATION_WRITER_COVERAGE_UNAVAILABLE",error.Message);
            Assert.False((await flow.ActivationPreviewAsync("compatible")).Current.Enabled);
        }
        finally {await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_authoring_v2.versions ENABLE TRIGGER publication_versions_immutable");}
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_source_tenant_does_not_leak_foreign_versions()
    {
        await using var h=await Seed();var(flow,_)=await PrepareDependencies(h);
        await using var other=await Seed();var(_,foreignForm)=await PrepareDependencies(other);await ActiveDependencyForm(foreignForm,"foreign");
        var saved=await flow.SaveAsync("parent",DependentCommand("foreign"));var preview=await Preview(flow,"parent",saved);
        Assert.False(preview.CanPublish);Assert.Contains("DEPENDENCY_NOT_AVAILABLE",preview.Diagnostics);Assert.Null(Assert.Single(preview.Dependencies).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_nested_service_registration_is_current_without_executing_the_service()
    {
        await using var h=await Seed();await PrepareDependencies(h);
        var connectors=new List<IWfConnector>{new CP6.Core.Services.Wf.Executors.EchoConnector()};var flow=Authoring(h,connectors:connectors);
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="call",type="serviceTask",serviceKind="webApi",serviceConnectorName="erpEcho",servicePath="/status"},new{id="e",type="end"}},edges=new[]{new{from="s",to="call"},new{from="call",to="e"}}});
        var child=await flow.SaveAsync("service-child",DependentCommand(schema:schema));var childVersion=(await flow.PublishAsync("service-child",PublicationCommand(await Preview(flow,"service-child",child)))).Result!.Publication!;
        await flow.SetActivationAsync("service-child",ActivationCommand((await flow.ActivationPreviewAsync("service-child")).Current,childVersion.VersionId));
        var parent=await flow.SaveAsync("parent",DependentCommand(child:"service-child"));var preview=await Preview(flow,"parent",parent);Assert.True(preview.CanPublish);
        connectors.Clear();
        Assert.Equal("PREVIEW_STALE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.PublishAsync("parent",PublicationCommand(preview)))).Message);
        Assert.Contains("SERVICE_DEPENDENCY_CHANGED",(await Preview(flow,"parent",parent)).Diagnostics);
        Assert.Empty(await flow.VersionsAsync("parent"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Dependencies_actual_HTTP_previews_publishes_and_reads_exact_FORM_and_child_versions()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);var f=await ActiveDependencyForm(form);var child=await ActiveDependencyFlow(flow,"child");
        var saved=await flow.SaveAsync("http-root",DependentCommand("form","child"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string root="/api/oa/v2/definitions/FLOW/http-root";
        using var response=await host.Send(BoundRequest(cookies,root+"/publish-preview",new FlowPublicationPreviewInputV2(saved.Result!.DraftId,saved.ResourceVersion!)));
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var preview=json.RootElement.GetProperty("data").Deserialize<FlowPublicationPreviewV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(preview.CanPublish,string.Join(',',preview.Diagnostics));Assert.Equal(new[]{f.VersionId.ToString("D"),child.VersionId.ToString("D")},preview.Dependencies.Select(d=>d.Version));
        using var command=JsonDocument.Parse(PublicationCommand(preview));using var publish=await host.Send(BoundRequest(cookies,root+"/publish",command.RootElement));Assert.Equal(HttpStatusCode.OK,publish.StatusCode);
        using var original=await host.Send(BoundRequest(cookies,"/api/oa/v2/commands/publish?operation=FLOW_PUBLISH&resource=http-root"));Assert.Equal(HttpStatusCode.OK,original.StatusCode);
    }
}

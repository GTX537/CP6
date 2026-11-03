using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string RulePreviewSchema="""
        {"fields":[{"name":"amount","type":"number"},{"name":"next","type":"number"},{"name":"label","type":"input"}],"rules":[{"then":[{"action":"compute","target":"next","expr":"amount + 1"},{"action":"disable","target":"amount"}]},{"when":"next > 9007199254740993","then":[{"action":"compute","target":"label","expr":"'exact-high'"},{"action":"setOptions","target":"label","options":[{"label":"准确","value":"exact-high"}]}]}]}
        """;
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_Vue_located_definition_errors_and_exact_pure_form_rules()
    {
        await using var h=await Seed();var flow=await PreparePublication(h);var form=FormAuthoring(h);
        await EngineeringAction(h,"oa-designer","form-save");await EngineeringAction(h,"oa-designer","form-publish");
        await form.ConfigureAsync(new(Guid.NewGuid(),"absent",true,[],"simulated browser authoring policy"));
        await form.ConfigurePublicationAsync(new(Guid.NewGuid(),"absent",true,false,[],"simulated browser publication policy"));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="oa-designer").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/oa/designer"));
        var saved=await form.SaveAsync("browser-rules",FormAuthoringCommand(name:"Browser rules",schema:RulePreviewSchema));
        var missing=await flow.SaveAsync("browser-locations",DependentCommand(child:"missing-dependency",name:"Browser locations"));
        using var result=await BrowserScenario(h,h.User,"oa-definition-diagnostics-browser.mjs","definition-diagnostics-browser",
            new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        Assert.Equal(saved.Result!.DraftId,result.RootElement.GetProperty("preview").GetProperty("draftVersionId").GetGuid());
        var final=await form.ReadHeadAsync("browser-rules");Assert.NotEqual(saved.ResourceVersion,final.Version);Assert.Equal(RulePreviewSchema,final.Fields["SchemaJson"]);
        Assert.Equal(missing.ResourceVersion,(await flow.ReadHeadAsync("browser-locations")).Version);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.publication_previews WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task DefinitionLocations_publication_external_file_and_flow_dependencies_point_to_current_source()
    {
        await using var h=await Seed();var(flow,form)=await PrepareDependencies(h);
        var upload=await form.SaveAsync("attachment",FormAuthoringCommand(schema:"{\"fields\":[{\"name\":\"title\",\"type\":\"input\"},{\"name\":\"files\",\"type\":\"upload\"}]}"));
        var files=await FormPreview(form,"attachment",upload);Assert.False(files.CanPublish);
        Assert.Equal("$.fields[1]",Assert.Single(files.DiagnosticDetails!).Path);
        Assert.Equal("files",Assert.Single(files.DiagnosticDetails!).FieldName);
        var missing=await flow.SaveAsync("missing",DependentCommand(form:"missing-form",child:"missing-flow"));
        var preview=await Preview(flow,"missing",missing);Assert.False(preview.CanPublish);
        Assert.Contains(preview.DiagnosticDetails!,d=>d.Code=="DEPENDENCY_NOT_AVAILABLE"&&d.Path=="$.formKey");
        Assert.Contains(preview.DiagnosticDetails!,d=>d.Code=="DEPENDENCY_NOT_AVAILABLE"&&d.Path=="$.nodes[1].subFlowKey"&&d.NodeId=="sub");
        const string service="""{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"svc","type":"serviceTask","serviceKind":"dataWriteback","serviceActionName":"missing-handler"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"svc"},{"from":"svc","to":"e"}]}""";
        var error=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>flow.SaveAsync("service",AuthoringCommand(schema:service)));
        Assert.Equal("SERVICE_REFERENCE_INVALID",error.Message);Assert.Equal("$.nodes[1].serviceActionName",Assert.Single(DefinitionDiagnosticsV2.From(error)).Path);
    }
    [CurrentPermissionPostgresFact]
    public async Task RulePreview_actual_HTTP_exact_order_current_read_and_zero_business_side_effects()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);
        var saved=(await owner.SaveAsync("rules",FormAuthoringCommand(schema:RulePreviewSchema))).Result!;
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="form-save").ExecuteDeleteAsync();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string path="/api/oa/v2/definitions/FORM/rules/rule-preview";
        object Input(string token,string data="{\"amount\":9007199254740993}")=>new{draftVersionId=saved.DraftId,expectedVersion=token,dataJson=data};
        using(var response=await host.Send(BoundRequest(cookies,path,Input(saved.Version))))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var result=body.RootElement.GetProperty("data");
            Assert.Equal("EXPLANATION_ONLY",result.GetProperty("mode").GetString());
            Assert.Contains("9007199254740994",result.GetProperty("dataJson").GetString());Assert.Contains("exact-high",result.GetProperty("dataJson").GetString());
            Assert.True(result.GetProperty("presentation").EnumerateArray().Single(f=>f.GetProperty("name").GetString()=="amount").GetProperty("disabled").GetBoolean());
            Assert.Equal(4,result.GetProperty("appliedEffects").GetArrayLength());
        }
        using(var stale=await host.Send(BoundRequest(cookies,path,Input(Guid.NewGuid().ToString("D")))))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using(var duplicate=await host.Send(BoundRequest(cookies,path,Input(saved.Version,"{\"amount\":1,\"amount\":2}"))))Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using(var hidden=await host.Send(BoundRequest(cookies,path,Input(saved.Version)))){Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.DoesNotContain("exact-high",await hidden.Content.ReadAsStringAsync());}
        Assert.Equal(saved.Version,(await owner.ReadAsync("rules")).Version);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.publication_previews WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLocations_actual_save_HTTP_returns_field_path_and_preserves_original_draft()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);var saved=(await owner.SaveAsync("located",FormAuthoringCommand())).Result!;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);
        var payload=JsonSerializer.Deserialize<JsonElement>(FormAuthoringCommand("bad",saved.Version,schema:"{\"fields\":[{\"name\":\"x\"},{\"name\":\"x\"}]}"));
        using var request=BoundRequest(cookies,"/api/oa/v2/definitions/FORM/located/draft",payload);request.Method=HttpMethod.Put;
        using var response=await host.Send(request);Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal("$.fields[1].name",body.RootElement.GetProperty("fieldErrors")[0].GetProperty("path").GetString());
        Assert.Equal(saved.Version,(await owner.ReadAsync("located")).Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task RulePreview_failed_dynamic_arithmetic_returns_exact_rule_path_without_writes()
    {
        await using var h=await Seed();var owner=await PrepareFormAuthoring(h);
        var schema=RulePreviewSchema.Replace("amount + 1","1 / amount");
        var saved=(await owner.SaveAsync("division",FormAuthoringCommand(schema:schema))).Result!;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);
        using var response=await host.Send(BoundRequest(cookies,"/api/oa/v2/definitions/FORM/division/rule-preview",new{draftVersionId=saved.DraftId,expectedVersion=saved.Version,dataJson="{\"amount\":0}"}));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORM_RULE_EVALUATION_FAILED",body.RootElement.GetProperty("code").GetString());
        Assert.Equal("$.rules[0].then[0].expr",body.RootElement.GetProperty("fieldErrors")[0].GetProperty("path").GetString());
        Assert.False(body.RootElement.TryGetProperty("data",out _));Assert.Equal(saved.Version,(await owner.ReadHeadAsync("division")).Version);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task DefinitionLocations_flow_saved_incomplete_graph_and_current_schema_read_projection()
    {
        await using var h=await Seed();var owner=await PrepareAuthoring(h);
        var saved=(await owner.SaveAsync("locations",AuthoringCommand(schema:"{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\"},{\"id\":\"a\",\"type\":\"approval\"},{\"id\":\"e\",\"type\":\"end\"}],\"edges\":[{\"from\":\"s\",\"to\":\"a\"},{\"from\":\"a\",\"to\":\"e\"}]}"))).Result!;
        Assert.Contains(saved.DiagnosticDetails!,d=>d.Path=="$.nodes[1].approverStrategy");Assert.Contains((await owner.ReadAsync("locations")).DiagnosticDetails!,d=>d.NodeId=="a");
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=FlowAuthoringOwnerV2.Resource,FieldName="SchemaJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty((await owner.ReadAsync("locations")).DiagnosticDetails!);Assert.Empty((await owner.OriginalAsync("locations","first")).Result!.DiagnosticDetails!);
    }
}

using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static byte[] SimulationRequest(FlowAuthoringViewV2 saved, string vars = "{\"amount\":9007199254740993}")
        => JsonSerializer.SerializeToUtf8Bytes(new { draftVersionId = saved.DraftId, expectedVersion = saved.Version, varsJson = vars });
    private const string SimulationSchema = """
        {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","approverStrategy":"Starter"},{"id":"b","type":"approval","approverStrategy":"Starter"},{"id":"e","type":"end"}],"edges":[{"id":"large","from":"s","to":"a","condition":"amount > 9007199254740992","priority":1},{"id":"default","from":"s","to":"b","priority":2},{"from":"a","to":"e"},{"from":"b","to":"e"}]}
        """;

    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_Vue_exact_draft_simulation_reaches_real_PostgreSQL_without_business_writes()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); await EngineeringAction(h, "user", "query");
        await h.Db.Sys_Menus.Where(x => x.MenuKey == "oa-designer").ExecuteUpdateAsync(x => x.SetProperty(m => m.RoutePath, "/oa/designer"));
        var saved = (await owner.SaveAsync("simulation", AuthoringCommand(name: "Browser simulation", schema: SimulationSchema))).Result!;
        using var result = await BrowserScenario(h, h.User, "oa-simulation-browser.mjs", "simulation-browser", new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var high = result.RootElement.GetProperty("high");
        Assert.Equal(saved.DraftId, high.GetProperty("draftVersionId").GetGuid());
        Assert.Equal(saved.Version, high.GetProperty("expectedVersion").GetString());
        Assert.Equal(saved.Version, (await owner.ReadHeadAsync("simulation")).Version);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync()); Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_ServiceJobs.ToArrayAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.publication_previews WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Simulation_actual_HTTP_exact_draft_precision_read_authority_and_no_business_effects()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h);
        var saved = (await owner.SaveAsync("simulation", AuthoringCommand(schema: SimulationSchema))).Result!;
        // Preview does not require edit/publish or execution admission: it is a current-authority read.
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit" || x.ActionCode == "add").ExecuteDeleteAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h); const string path = "/api/oa/v2/definitions/FLOW/simulation/simulate";
        object Input(string version, string vars = "{\"amount\":9007199254740993}") => new { draftVersionId = saved.DraftId, expectedVersion = version, varsJson = vars };
        using (var response = await host.Send(BoundRequest(cookies, path, Input(saved.Version))))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var result = body.RootElement.GetProperty("data");
            Assert.Equal("EXPLANATION_ONLY", result.GetProperty("mode").GetString());
            Assert.Equal(saved.DraftId, result.GetProperty("draftVersionId").GetGuid());
            Assert.Equal(new[] { "s", "a", "e" }, result.GetProperty("nodes").EnumerateArray().Select(x => x.GetProperty("nodeId").GetString()));
            Assert.Contains(result.GetProperty("diagnostics").EnumerateArray(), x => x.GetProperty("code").GetString() == "APPROVER_AUTHORITY_UNPROVEN");
            Assert.DoesNotContain(h.User.ToString(), await response.Content.ReadAsStringAsync());
        }
        using (var stale = await host.Send(BoundRequest(cookies, path, Input(Guid.NewGuid().ToString("D")))))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var duplicate = await host.Send(BoundRequest(cookies, path, Input(saved.Version, "{\"amount\":1,\"amount\":2}"))))
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "workflow-definition", FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var hidden = await host.Send(BoundRequest(cookies, path, Input(saved.Version))))
        { Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode); Assert.DoesNotContain("9007199254740993", await hidden.Content.ReadAsStringAsync()); }
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync()); Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_ServiceJobs.ToArrayAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(saved.Version, (await owner.ReadHeadAsync("simulation")).Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Simulation_current_IAM_read_resolves_real_actor_but_other_owner_and_tenant_stay_hidden()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h); await EngineeringAction(h, "user", "query");
        var saved = (await owner.SaveAsync("people", AuthoringCommand(schema: SimulationSchema))).Result!;
        var result = await owner.SimulateAsync("people", SimulationRequest(saved));
        var stage = Assert.Single(result.Nodes.Single(x => x.NodeId == "a").Stages);
        Assert.Equal("RESOLVED_NOW", stage.State); Assert.Equal(new[] { h.User }, stage.ApproverIds);
        var large = (await owner.SaveAsync("bounded-preview", AuthoringCommand(schema: SimulationSchema.Replace("\"approverStrategy\":\"Starter\"", "\"stages\":[{\"kind\":\"managerChain\",\"maxLevels\":2147483647}]")))).Result!;
        var bounded = await owner.SimulateAsync(large.Key, SimulationRequest(large));
        Assert.Contains(bounded.Diagnostics, x => x.Code == "APPROVER_EXPANSION_LIMIT");
        Assert.All(bounded.Nodes, node => Assert.Empty(node.Stages));
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "query").ExecuteDeleteAsync();
        var revoked = await owner.SimulateAsync("people", SimulationRequest(saved));
        Assert.All(revoked.Nodes, node => Assert.Empty(node.Stages));
        Assert.Contains(revoked.Diagnostics, x => x.Code == "APPROVER_AUTHORITY_UNPROVEN");
        var other = new Sys_User { UserName = "simulation-other", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(other); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(other.Id, h.Tenant);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => Authoring(h, _ => Task.FromResult(other.Id)).SimulateAsync("people", SimulationRequest(saved)))).Status);
        await using var foreign = await Seed(); var foreignOwner = await PrepareAuthoring(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => foreignOwner.SimulateAsync("people", SimulationRequest(saved)))).Status);
    }

    private sealed class SimulationConnector : IWfConnector
    {
        public string Name => "simulation-only";
        public string DisplayName => "Simulation test connector";
        public int Calls { get; private set; }
        public Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx)
        { Calls++; throw new InvalidOperationException("SIMULATION_MUST_NOT_EXECUTE"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task Simulation_service_registration_loss_is_explicit_and_never_calls_connector_or_creates_preview()
    {
        await using var h = await Seed(); await PrepareAuthoring(h); var connector = new SimulationConnector();
        var owner = Authoring(h, connectors: [connector]);
        const string schema = """
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"service","type":"serviceTask","serviceKind":"webApi","serviceConnectorName":"simulation-only","servicePath":"/mock"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"service"},{"from":"service","to":"e"}]}
            """;
        var saved = (await owner.SaveAsync("service-preview", AuthoringCommand(schema: schema))).Result!;
        var result = await owner.SimulateAsync(saved.Key, SimulationRequest(saved));
        Assert.Equal(new[] { "s", "service" }, result.Nodes.Select(x => x.NodeId));
        Assert.Contains(result.Diagnostics, x => x.Code == "DEPENDENT_EXECUTION_NOT_SIMULATED"); Assert.Equal(0, connector.Calls);
        var unavailable = await Authoring(h).SimulateAsync(saved.Key, SimulationRequest(saved));
        Assert.Contains(unavailable.Diagnostics, x => x.Code == "SERVICE_REFERENCE_INVALID");
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_ServiceJobs.ToArrayAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.publication_previews WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}

using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ForecastRoot = "/api/oa/v2/definitions/FLOW/";
    private static object DraftForecast(FlowAuthoringViewV2 draft, string vars = "{}", params WorkflowForecastPinV1[] dependencies) =>
        new { draftVersionId = draft.DraftId, expectedVersion = draft.Version, varsJson = vars, draftDependencies = dependencies };

    private async Task<WorkflowLifecycleResultV2> PublishForecastGraph(FlowAuthoringOwnerV2 owner, string key, string schema)
    {
        var saved = await owner.SaveAsync(key, AuthoringCommand("save-" + key, schema: schema));
        var preview = await Preview(owner, key, saved);
        Assert.True(preview.CanPublish, string.Join(", ", preview.Diagnostics));
        return await owner.PublishAsync(key, PublicationCommand(preview, "publish-" + key));
    }

    private static string SubFlowForecastGraph(string child) => $$"""
        {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"child","type":"subFlow","subFlowKey":"{{child}}"},
        {"id":"e","type":"end"}],"edges":[{"from":"s","to":"child"},{"from":"child","to":"e"}]}
        """;

    [CurrentPermissionPostgresFact]
    public async Task Forecast_actual_HTTP_reads_exact_published_and_draft_pins_without_effects_or_lazy_artifacts()
    {
        await using var h = await Seed();
        var (owner, form) = await PrepareDependencies(h);
        await ForecastActionFixture(h);
        var originalForm = await ActiveDependencyForm(form, "forecast-form");
        var childPin = (await ActiveDependencyFlow(owner, "forecast-child")).VersionId;
        const string serviceSchema = """
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"delay","type":"serviceTask","serviceKind":"timer",
            "serviceDelayMode":"duration","serviceDelayValue":"PT2H"},{"id":"e","type":"end"}],
            "edges":[{"from":"s","to":"delay"},{"from":"delay","to":"e"}]}
            """;
        var service = await PublishForecastGraph(owner, "forecast-service", serviceSchema);
        var parentDraft = await owner.SaveAsync("forecast-parent", DependentCommand("forecast-form", "forecast-child", id: "save-parent"));
        var parent = await owner.PublishAsync("forecast-parent",
            PublicationCommand(await Preview(owner, "forecast-parent", parentDraft), "publish-parent"));
        var formDraft = await form.CreateNextDraftAsync("forecast-form",
            FormNextDraftCommand((await form.ReadHeadAsync("forecast-form")).Version, originalForm.VersionId, "next-forecast-form"));
        var formEdit = await form.SaveAsync("forecast-form", FormAuthoringCommand("edit-forecast-form", formDraft.ResourceVersion, "较新的表单"));
        var newForm = (await form.PublishAsync("forecast-form",
            FormPublicationCommand(await FormPreview(form, "forecast-form", formEdit), "publish-form-2"))).Result!.Publication!;
        await form.SetActivationAsync("forecast-form", FormActivationCommand(
            (await form.ActivationPreviewAsync("forecast-form")).Current, newForm.VersionId, id: "activate-form-2"));
        var pin = parent.Result!.Publication!;
        var newDraft = await owner.CreateNextDraftAsync("forecast-parent", NextDraftCommand(parent.ResourceVersion!, pin.VersionId, "next-forecast-parent"));
        var edited = (await owner.SaveAsync("forecast-parent", AuthoringCommand("edit-forecast-parent", newDraft.ResourceVersion,
            schema: AuthoringSchema))).Result!;
        var before = await ForecastReadState(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var original = await api.Data(ForecastRoot + "forecast-parent/forecast", new { publishedVersionId = pin.VersionId, varsJson = "{}" });
        Assert.Equal("PUBLISHED_PIN", original.GetProperty("source").GetString());
        Assert.Equal("PARTIAL", original.GetProperty("coverage").GetString());
        Assert.Equal(pin.VersionId, original.GetProperty("versionId").GetGuid());
        var pins = original.GetProperty("dependencies").EnumerateArray().ToDictionary(x => x.GetProperty("kind").GetString()!);
        Assert.Equal(childPin, pins["FLOW"].GetProperty("versionId").GetGuid());
        Assert.Equal(originalForm.VersionId, pins["FORM"].GetProperty("versionId").GetGuid());
        Assert.NotEqual(newForm.VersionId, pins["FORM"].GetProperty("versionId").GetGuid());
        Assert.Equal(["s", "sub"], original.GetProperty("nodes").EnumerateArray().Select(x => x.GetProperty("nodeId").GetString()));
        Assert.DoesNotContain(pin.SchemaDigest!, original.GetRawText());

        var draft = await api.Data(ForecastRoot + "forecast-parent/forecast", DraftForecast(edited));
        Assert.Equal("DRAFT_SIMULATION", draft.GetProperty("source").GetString());
        Assert.Equal(edited.Version, draft.GetProperty("draftRevision").GetString());
        Assert.Equal("EXPLANATION_ONLY", draft.GetProperty("mode").GetString());
        Assert.False(draft.GetProperty("isExecutionPermit").GetBoolean());
        Assert.True(draft.GetProperty("knowledgeCutUtc").GetDateTimeOffset() > DateTimeOffset.UnixEpoch);
        Assert.Equal("CURRENT_ACTOR_AS_STARTER", draft.GetProperty("starterBasis").GetString());

        var timer = await api.Data(ForecastRoot + "forecast-service/forecast",
            new { publishedVersionId = service.Result!.Publication!.VersionId, varsJson = "{}" });
        Assert.Contains(timer.GetProperty("diagnostics").EnumerateArray(), x => x.GetProperty("code").GetString() == "DEPENDENT_EXECUTION_NOT_SIMULATED");
        Assert.DoesNotContain("PT2H", timer.GetRawText());
        await api.Reject(ForecastRoot + "forecast-parent/forecast", new { publishedVersionId = edited.DraftId, varsJson = "{}" },
            HttpStatusCode.NotFound, "VERSION_NOT_FOUND");
        await api.Reject(ForecastRoot + "forecast-parent/forecast", DraftForecast(edited with { Version = Guid.NewGuid().ToString("D") }),
            HttpStatusCode.Conflict, "DEFINITION_CHANGED");
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("Forecast actual Main HTTP: named publication survives new draft, original form and child versions retained, service stops, no change in durable workflow/effect counters.");
    }

    [CurrentPermissionPostgresFact]
    public async Task Forecast_exact_child_chain_reaches_depth_eight_and_unpinned_draft_never_uses_active_child()
    {
        await using var h = await Seed();
        var (owner, leaf) = await PrepareActivation(h, "depth-leaf");
        await ForecastActionFixture(h);
        var version = leaf.Result!.Publication!.VersionId;
        var key = "depth-leaf";
        for (var i = 1; i <= SubFlowLimits.MaxDepth; i++)
        {
            await owner.SetActivationAsync(key, ActivationCommand((await owner.ActivationPreviewAsync(key)).Current, version, id: "enable-depth-" + i));
            var nextKey = "depth-parent-" + i;
            if (i == SubFlowLimits.MaxDepth)
            {
                var draft = (await owner.SaveAsync(nextKey, AuthoringCommand("depth-last", schema: SubFlowForecastGraph(key)))).Result!;
                await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
                var api = new DeadlineApi(host, await Login(host, h));
                var path = ForecastRoot + nextKey + "/forecast";
                var unpinned = await api.Data(path, DraftForecast(draft));
                Assert.Contains("DEPENDENCY_PIN_REQUIRED", unpinned.GetRawText());
                Assert.DoesNotContain(version.ToString("D"), unpinned.GetRawText());
                var bounded = await api.Data(path, DraftForecast(draft, "{}", new WorkflowForecastPinV1("FLOW", "child", version)));
                Assert.Equal("PARTIAL", bounded.GetProperty("coverage").GetString());
                Assert.Contains("DEPENDENCY_DEPTH_EXCEEDED", bounded.GetRawText());
                Assert.NotEmpty(bounded.GetProperty("unvisited").EnumerateArray());
                output.WriteLine("Forecast actual Main HTTP: the ninth definition is rejected for expansion; an explicit missing draft pin stays missing despite active publication.");
                return;
            }
            var publication = await PublishForecastGraph(owner, nextKey, SubFlowForecastGraph(key));
            key = nextKey;
            version = publication.Result!.Publication!.VersionId;
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Forecast_service_registration_is_only_inspected_and_external_calls_stay_zero()
    {
        await using var h = await Seed();
        await ForecastActionFixture(h);
        await PreparePublication(h);
        var connector = new SimulationConnector();
        var owner = Authoring(h, connectors: [connector]);
        const string graph = """
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"service","type":"serviceTask","serviceKind":"webApi",
            "serviceConnectorName":"simulation-only","servicePath":"/synthetic-binding-private"},{"id":"e","type":"end"}],
            "edges":[{"from":"s","to":"service"},{"from":"service","to":"e"}]}
            """;
        var publication = (await PublishForecastGraph(owner, "forecast-external", graph)).Result!.Publication!;
        var before = await ForecastReadState(h);
        var data = await owner.ForecastAsync("forecast-external",
            JsonSerializer.SerializeToUtf8Bytes(new { publishedVersionId = publication.VersionId, varsJson = "{}" }));
        Assert.Equal("PARTIAL", data.Coverage);
        Assert.Equal(["s", "service"], data.Nodes.Select(x => x.NodeId));
        Assert.Contains(data.Diagnostics, d => d.Code == "DEPENDENT_EXECUTION_NOT_SIMULATED");
        Assert.DoesNotContain("synthetic-binding-private", JsonSerializer.Serialize(data));
        Assert.Equal(0, connector.Calls);
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("Isolated registered connector observed exactly 0 calls; no service binding, receipt or write is produced by forecast.");
    }

    private static async Task<int[]> ForecastReadState(Harness h)
    {
        var facts = await h.Db.Database.SqlQuery<int>($"""
            SELECT (SELECT count(*) FROM cp6_workflow_authoring_v2.heads WHERE tenant_id={h.Tenant})::int AS "Value"
            UNION ALL SELECT count(*)::int FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_workflow_authoring_v2.publication_previews WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_oa_conn_v1.commands WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_oa_conn_v1.dispatch_admissions WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_oa_conn_v1.result_outbox WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM cp6_approval_start_v2.starts WHERE tenant_id={h.Tenant}
            """).ToArrayAsync();
        return [.. facts, await h.Db.Wf_FlowInstances.CountAsync(), await h.Db.Wf_FlowTokens.CountAsync(),
            await h.Db.Wf_FlowTasks.CountAsync(), await h.Db.Wf_FormDatas.CountAsync(),
            await h.Db.Wf_Notifications.CountAsync(), await h.Db.Wf_ServiceJobs.CountAsync()];
    }

    [CurrentPermissionPostgresFact]
    public async Task Forecast_actual_Main_reports_parallel_unknown_and_bounded_traversal()
    {
        await using var h = await Seed();
        await ForecastActionFixture(h);
        var owner = await PrepareAuthoring(h);
        const string graph = """
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"fork","type":"parallelSplit"},{"id":"choose","type":"gateway"},
              {"id":"join","type":"parallelJoin"},{"id":"e","type":"end"},{"id":"left","type":"approval","approverStrategy":"Starter"},
              {"id":"other","type":"approval","approverStrategy":"Starter"},{"id":"default","type":"approval","approverStrategy":"Starter"}],
             "edges":[{"from":"s","to":"fork"},{"from":"fork","to":"choose"},{"from":"fork","to":"other"},
              {"from":"choose","to":"left","condition":"missing > 2","priority":1},
              {"from":"choose","to":"default","priority":2},{"from":"left","to":"join"},{"from":"default","to":"join"},
              {"from":"other","to":"join"},{"from":"join","to":"e"}]}
            """;
        var branch = (await owner.SaveAsync("forecast-branches", AuthoringCommand("save-forecast-branches", schema: graph))).Result!;
        var nodes = new List<FlowNode> { new() { Id = "s", Type = "start" } };
        var edges = new List<FlowEdge>();
        for (var index = 0; index < 101; index++)
        {
            var next = new FlowNode { Id = "a" + index, Type = "approval", ApproverStrategy = "Starter" };
            edges.Add(new() { From = nodes[^1].Id, To = next.Id });
            nodes.Add(next);
        }
        edges.Add(new() { From = nodes[^1].Id, To = "e" });
        nodes.Add(new() { Id = "e", Type = "end" });
        var chain = JsonSerializer.Serialize(new FlowSchema { Start = "s", Nodes = nodes, Edges = edges }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bounded = (await owner.SaveAsync("forecast-bounded", AuthoringCommand("save-forecast-bounded", schema: chain))).Result!;
        var before = await ForecastReadState(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var partial = await api.Data(ForecastRoot + "forecast-branches/forecast", DraftForecast(branch));
        Assert.Equal("PARTIAL", partial.GetProperty("coverage").GetString());
        var path = partial.GetProperty("edges").EnumerateArray().ToArray();
        Assert.True(path[1].GetProperty("traversed").GetBoolean());
        Assert.True(path[2].GetProperty("traversed").GetBoolean());
        Assert.Equal("UNKNOWN", path[3].GetProperty("condition").GetString());
        Assert.False(path[4].GetProperty("traversed").GetBoolean());
        Assert.Contains("JOIN_SYNCHRONIZATION_UNPROVEN", partial.GetRawText());

        var limited = await api.Data(ForecastRoot + "forecast-bounded/forecast", DraftForecast(bounded));
        Assert.Equal("PARTIAL", limited.GetProperty("coverage").GetString());
        Assert.Equal(100, limited.GetProperty("visitedNodeCount").GetInt32());
        Assert.Contains("SIMULATION_BUDGET_EXCEEDED", limited.GetRawText());
        Assert.DoesNotContain(limited.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("nodeId").GetString() == "e");
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("Forecast actual Main HTTP: both parallel entrances exposed, unknown exclusive condition with no default guess; chain stops at the 100th visit.");
    }

    [CurrentPermissionPostgresFact]
    public async Task Forecast_people_and_in_flight_export_use_current_scope_and_authority()
    {
        await using var h = await Seed();
        await ForecastActionFixture(h);
        var owner = await PrepareAuthoring(h);
        await EngineeringAction(h, "user", "query");
        var userMenu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "user");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 72 && x.MenuId == userMenu.MenuId))
        {
            h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = userMenu.MenuId });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
        const string canary = "synthetic-private-input-wp1215";
        const string schema = """
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"current","type":"approval","approverStrategy":"Starter"},
            {"id":"later","type":"approval","approverStrategy":"FormField","approverFieldName":"assigned"},{"id":"e","type":"end"}],
            "edges":[{"from":"s","to":"current"},{"from":"current","to":"later"},{"from":"later","to":"e"}]}
            """;
        var draft = (await owner.SaveAsync("forecast-people", AuthoringCommand(schema: schema))).Result!;
        var input = DraftForecast(draft, $$"""{"memo":"{{canary}}"}""");
        var before = await ForecastReadState(h);
        var application = "forecast-export-" + Guid.NewGuid().ToString("N");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()! + ";Application Name=" + application,
            CalendarHostSettings());
        var cookies = await Login(host, h);
        var api = new DeadlineApi(host, cookies);
        var path = ForecastRoot + "forecast-people/forecast";
        var good = await api.Data(path, input);
        JsonElement Stage(JsonElement data, string id) => Assert.Single(data.GetProperty("nodes").EnumerateArray()
            .Single(x => x.GetProperty("nodeId").GetString() == id).GetProperty("stages").EnumerateArray());
        var starter = Stage(good, "current");
        Assert.Equal("RESOLVED_AT_CUT", starter.GetProperty("state").GetString());
        Assert.Equal(h.User, Assert.Single(starter.GetProperty("people").EnumerateArray()).GetProperty("id").GetGuid());
        var later = Stage(good, "later");
        Assert.Equal("ARRIVAL_TIME", later.GetProperty("state").GetString());
        Assert.Empty(later.GetProperty("people").EnumerateArray());
        Assert.DoesNotContain(canary, good.GetRawText());

        await using var other = await Seed();
        await PrepareAuthoring(other);
        await ForecastActionFixture(other);
        using (var crossTenant = await host.Send(BoundRequest(await Login(host, other), path, input)))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
            Assert.DoesNotContain(canary, await crossTenant.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == userMenu.MenuId && x.ActionCode == "query").ExecuteDeleteAsync();
        var redacted = await api.Data(path, input);
        Assert.Equal("REDACTED", Stage(redacted, "current").GetProperty("state").GetString());
        Assert.Empty(Stage(redacted, "current").GetProperty("people").EnumerateArray());
        Assert.DoesNotContain(h.User.ToString("D"), redacted.GetRawText());

        using (var download = await host.Send(BoundRequest(cookies, path + "/export", input)))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl?.NoStore);
            Assert.DoesNotContain(canary, await download.Content.ReadAsStringAsync());
        }

        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var tx = await writer.Database.BeginTransactionAsync();
        await AuthenticationCompletionGateV1.AcquireAsync(writer, default);
        writer.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = FlowAuthoringOwnerV2.Resource, FieldName = "SchemaJson", Access = 3 });
        await writer.SaveChangesAsync();
        var pending = host.Send(BoundRequest(cookies, path + "/export", input));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name={application}
                AND wait_event_type='Lock' AND state='active') AS "Value"
            """).SingleAsync(timeout.Token))
        {
            Assert.False(pending.IsCompleted);
            await Task.Delay(50, timeout.Token);
        }
        await tx.CommitAsync();
        using var denied = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var safe = await denied.Content.ReadAsStringAsync();
        Assert.Contains("FIELD_READ_FORBIDDEN", safe);
        foreach (var canaries in new[] { canary, h.User.ToString("D"), "schemaDigest", "people", "credentials" })
            Assert.DoesNotContain(canaries, safe, StringComparison.OrdinalIgnoreCase);
        Assert.True(denied.Headers.CacheControl?.NoStore);
        await api.Reject(path, input, HttpStatusCode.Forbidden, "FIELD_READ_FORBIDDEN");
        Assert.Equal(before, await ForecastReadState(h));
        output.WriteLine("Forecast actual Main HTTP: resolved / arrival / redacted, foreign tenant 404, observed export wait on current IAM fence, committed revoke 403 without private fields.");
    }
}

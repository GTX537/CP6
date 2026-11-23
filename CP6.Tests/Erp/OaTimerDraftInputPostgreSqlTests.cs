using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static string TimerDraftCron200() =>
        string.Join(",", Enumerable.Range(0, 60)) + " 0,1,2,3,4,5,6,7,10,11,12 * * *";

    [CurrentPermissionPostgresFact]
    public async Task OaConn_TimerDraft_HTTP_accepts_200_rejects_invalid_and_preserves_legacy_behavior()
    {
        await using var h = await Seed();
        var (flowKey, flowVersion, profile) = await TimerDraftSelection(h);
        await EngineeringAction(h, "oa-flow-admin", "FlowTrigger.View");
        var legacy = new Wf_FlowTrigger
        {
            FlowKey = flowKey,
            TriggerType = WfTriggerType.Timer,
            ConfigJson = """{"cron":"0 9 * * *","varsJson":"{}"}""",
            Enabled = false,
            StarterUserId = h.User,
            NextDueUtc = new DateTime(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc)
        };
        h.Db.Wf_FlowTriggers.Add(legacy);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var maker = new ConnApi(host, await Login(host, h));
        var before = await TriggerRuntimeRowCounts(h.Db);
        var grants = await TimerDraftGrants(h.Db);

        var valid = TimerDraftCron200();
        var tooLong = string.Join(",", Enumerable.Range(0, 60)) + " 0,1,2,3,4,5,6,10,11,12,13 * * *";
        Assert.Equal(200, valid.Length);
        Assert.Equal(201, tooLong.Length);
        var selection = new ConnSelectionV1("timer-draft-boundary", "TIMER", profile, flowKey, flowVersion);
        ConnDraftV1 Draft(string cron) => new(selection, "TIMER draft fixture",
            new ConnConfigV1(Cron: cron, Vars: JsonSerializer.SerializeToElement(new { })));

        var start = await maker.Data("creation-contexts?kind=TRIGGER", selection);
        var expected = start.GetProperty("expectedVersion").GetString()!;
        var invalid = new[] { tooLong, "", "* * * *", "0 0 * * * *", "60 * * * *" };
        foreach (var cron in invalid)
        {
            await maker.Reject("triggers", ConnCmd(expected, Draft(cron)), HttpStatusCode.UnprocessableEntity, "TIMER_CONFIG_INVALID");
            output.WriteLine($"Versioned create len={cron.Length}: 422/TIMER_CONFIG_INVALID");
        }
        Assert.Equal(before, await TriggerRuntimeRowCounts(h.Db));
        Assert.Equal("ABSENT", (await maker.Data("creation-contexts?kind=TRIGGER", selection)).GetProperty("absence").GetString());

        var created = await maker.Data("triggers", ConnCmd(expected, Draft(valid)));
        AssertTimerDraftReceipt(created, 1);
        var root = created.GetProperty("rootId").GetGuid();
        var first = created.GetProperty("versionId").GetGuid();
        var route = $"triggers/{root:D}/versions";
        expected = created.GetProperty("result").GetProperty("revision").GetString()!;
        var afterCreate = await TriggerRuntimeRowCounts(h.Db);
        foreach (var cron in invalid)
        {
            await maker.Reject(route, ConnCmd(expected, Draft(cron)), HttpStatusCode.UnprocessableEntity, "TIMER_CONFIG_INVALID");
            output.WriteLine($"Versioned revise len={cron.Length}: 422/TIMER_CONFIG_INVALID");
        }
        Assert.Equal(afterCreate, await TriggerRuntimeRowCounts(h.Db));

        var revised = await maker.Data(route, ConnCmd(expected, Draft(valid)));
        AssertTimerDraftReceipt(revised, 2);
        var second = revised.GetProperty("versionId").GetGuid();
        Assert.Equal(root, revised.GetProperty("rootId").GetGuid());
        Assert.NotEqual(first, second);
        foreach (var version in new[] { first, second })
        {
            var context = await maker.Data($"{route}/{version:D}");
            Assert.Equal("DRAFT", context.GetProperty("state").GetString());
            Assert.False(context.GetProperty("published").GetBoolean());
            Assert.Equal(valid, context.GetProperty("content").GetProperty("config").GetProperty("cron").GetString());
        }
        output.WriteLine("Versioned create and revise len=200: two distinct, unpublished DRAFT versions with the original value.");

        const string legacyCron = "api/oa/flow-triggers/cron-preview";
        using var original = await host.Send(OperationsRequest(maker.Cookies, HttpMethod.Post, legacyCron, new { cron = tooLong }));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var next = (await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("next");
        Assert.Equal(5, next.GetArrayLength());
        using var malformed = await host.Send(OperationsRequest(maker.Cookies, HttpMethod.Post, legacyCron, new { cron = "60 * * * *" }));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("E-WF-022", (await malformed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        using var forecast = await host.Send(OperationsRequest(maker.Cookies, HttpMethod.Post, "api/oa/forecast/preview",
            new { flowKey = "synthetic-nonexistent-flow" }));
        Assert.Equal(HttpStatusCode.BadRequest, forecast.StatusCode);
        Assert.Equal("E-WF-006", (await forecast.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        output.WriteLine("Legacy cron-preview len=201: 200/data.next[5]; malformed: 400/E-WF-022. Legacy forecast on absent flow: 400/E-WF-006.");

        var unchanged = await h.Db.Wf_FlowTriggers.AsNoTracking().SingleAsync();
        Assert.Equal(legacy.Id, unchanged.Id);
        Assert.Equal(legacy.ConfigJson, unchanged.ConfigJson);
        Assert.Equal(legacy.NextDueUtc, unchanged.NextDueUtc);
        Assert.Equal(legacy.LastFiredUtc, unchanged.LastFiredUtc);
        Assert.Equal(legacy.RowVersion, unchanged.RowVersion);
        Assert.False(unchanged.Enabled);
        await AssertTimerDraftWrites(h.Db, before, grants, expectedCommands: 2);
    }

    [OaNativeBrowserPostgresFact]
    public async Task OaConn_TimerDraft_Chrome_links_hint_and_saves_200_as_draft()
    {
        await using var h = await Seed();
        var (_, flowVersion, profile) = await TimerDraftSelection(h);
        await h.Db.Sys_Menus.Where(m => m.MenuKey == "oa-flow-admin")
            .ExecuteUpdateAsync(m => m.SetProperty(row => row.RoutePath, "/oa/flow-admin"));

        var before = await TriggerRuntimeRowCounts(h.Db);
        var grants = await TimerDraftGrants(h.Db);
        using var evidence = await BrowserScenario(h, h.User, "oa-timer-draft-input-browser.mjs", "timer-draft-browser", ConnSettings(),
            new()
            {
                ["CP6_TIMER_PROFILE"] = profile.ToString("D"),
                ["CP6_TIMER_FLOW"] = flowVersion.ToString("D"),
                ["CP6_TIMER_CRON"] = TimerDraftCron200()
            });

        var version = evidence.RootElement.GetProperty("versionId").GetGuid();
        var state = await h.Db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_oa_conn_v1.version_control
            WHERE tenant_id={h.Tenant} AND version_id={version} AND NOT published
            """).SingleAsync();
        Assert.Equal("DRAFT", state);
        Assert.Empty(await h.Db.Wf_FlowTriggers.ToArrayAsync());
        await AssertTimerDraftWrites(h.Db, before, grants, expectedCommands: 1);
    }

    private async Task<(string FlowKey, Guid FlowVersion, Guid Profile)> TimerDraftSelection(Harness h)
    {
        const string key = "timer-draft-fixture";
        var (owner, published) = await PrepareActivation(h, key);
        var version = published.Result!.Publication!.VersionId;
        await owner.SetActivationAsync(key, ActivationCommand((await owner.ActivationPreviewAsync(key)).Current, version));
        await EngineeringAction(h, "oa-flow-admin", "conn.read");
        await EngineeringAction(h, "oa-flow-admin", "conn.trigger.write");
        var profile = await ConnRegistration(h, "TIMER", new ConnProfileV1(TimeZone: "UTC", TimeRules: "fixture-time",
            Calendar: "fixture-calendar", StarterAuthority: "fixture-starter", StarterId: h.User));
        return (key, version, profile);
    }

    private static void AssertTimerDraftReceipt(JsonElement receipt, int version)
    {
        Assert.Equal("SUCCEEDED", receipt.GetProperty("outcome").GetString());
        var result = receipt.GetProperty("result");
        Assert.Equal(version, result.GetProperty("number").GetInt32());
        Assert.Equal("DRAFT", result.GetProperty("state").GetString());
    }

    private static async Task<string> TimerDraftGrants(CP6Context db)
    {
        var actions = await db.Sys_RoleActions.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.MenuId)
            .ThenBy(x => x.ActionCode).Select(x => new { x.RoleId, x.MenuId, x.ActionCode }).ToArrayAsync();
        var menus = await db.Sys_RoleMenus.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.MenuId)
            .Select(x => new { x.RoleId, x.MenuId }).ToArrayAsync();
        Assert.DoesNotContain(actions, x => x.ActionCode == "conn.forecast");
        return JsonSerializer.Serialize(new { actions, menus });
    }

    private async Task AssertTimerDraftWrites(CP6Context db, long[] before, string grants, int expectedCommands)
    {
        var after = await TriggerRuntimeRowCounts(db);
        Assert.Equal(new long[] { 0, 0, 0, 0, expectedCommands, 0, 0 }, after.Zip(before, (a, b) => a - b).ToArray());
        Assert.Equal(grants, await TimerDraftGrants(db));
        Assert.Empty(await db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await db.Wf_ServiceJobs.ToArrayAsync());
        Assert.Equal(0L, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM cp6_oa_conn_v1.runtime_members").SingleAsync());
        output.WriteLine($"Configuration command receipts +{expectedCommands}; role grants, instances, tokens, tasks, service jobs, runtime members, fires, result/start outboxes +0.");
    }
}

using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_existing_designer_routes_use_current_commands_and_legacy_is_read_only()
    {
        await using var h = await Seed(); var legacy = await LegacyReadSetup(h);
        await legacy.Flow.ConfigurePublicationAsync(new(Guid.NewGuid(), "absent", true, false, [], "simulated direct publication"));
        await EngineeringAction(h, "oa-designer", "publish");
        await h.Db.Sys_Menus.Where(x => x.MenuKey == "oa-designer").ExecuteUpdateAsync(x => x.SetProperty(m => m.RoutePath, "/oa/designer"));
        var saved = await legacy.Flow.SaveAsync("browser-current", AuthoringCommand(name: "Browser current flow", schema: DraftFlowSchema));
        var first = await legacy.Flow.PublishAsync("browser-current", PublicationCommand(await Preview(legacy.Flow, "browser-current", saved)));
        var legacyBefore = await h.Db.Wf_FlowDefVersions.CountAsync();
        using var result = await BrowserScenario(h, h.User, "oa-legacy-definition-browser.mjs", "legacy-definition-browser",
            new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") },
            new() { ["CP6_LEGACY_FLOW_KEY"] = legacy.FlowKey });
        var versions = await legacy.Flow.VersionsAsync("browser-current");
        Assert.Equal(2, versions.Length); Assert.Equal("Browser revised through existing route", versions[0].Fields["Name"]);
        Assert.Equal(first.Result!.Publication!.VersionId, versions[1].VersionId); Assert.Equal(DraftFlowSchema, versions[1].Fields["SchemaJson"]);
        Assert.Equal(legacyBefore, await h.Db.Wf_FlowDefVersions.CountAsync()); Assert.Empty(await h.Db.Wf_FormDefVersions.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}

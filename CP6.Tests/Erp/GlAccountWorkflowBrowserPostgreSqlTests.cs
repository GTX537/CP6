using CP6.Tests.Platform;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task Gl_owner_browser_checks_actual_workflow_and_original_operation_recovery()
    {
        await using var h = await Seed();
        var actors = await SeedGlBoard(h, "browser");
        using var result = await BrowserScenario(h, actors.Checker, "gl-account-workflow-browser.mjs", "gl-browser", GlHostSettings());
        Assert.Equal(5, result.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Equal(1, result.RootElement.GetProperty("publishWrites").GetInt32());
    }
}

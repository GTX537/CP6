// CP6.Tests/Wf/FlowTriggerValidatorTests.cs —— 基座同 A-T2（需 Sys_User/Wf_FlowDef seed）
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerValidatorTests
{
    private static FlowTriggerSaveReq Req(int type, Guid starter, string configJson,
        string flowKey = "fk-trig", string? eventKey = null)
        => new(flowKey, type, configJson, Enabled: true, eventKey, starter);

    private static async Task AssertThrowsCodeAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn, FlowTriggerSaveReq req, string code)
    {
        using var db = Ctx(conn);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => FlowTriggerValidator.ValidateAsync(db, req, CancellationToken.None));
        Assert.Contains(code, ex.Message);
    }

    [Fact]
    public async Task Timer_BadCron_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"not a cron\"}"), "E-WF-022");
    }

    [Fact]
    public async Task Event_BadEventKeyFormat_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        foreach (var badKey in new[] { "noSeparator", "|x", "x|", "", null })
            await AssertThrowsCodeAsync(conn,
                Req(WfTriggerType.Event, starter, "{}", eventKey: badKey), "E-WF-022");
    }

    [Fact]
    public async Task Event_BadVarsMapPath_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        // 空模板值
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"a\":\"\"}}", eventKey: "WMS|OnXAsync"), "E-WF-022");
        // 空变量名
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"\":\"$.x\"}}", eventKey: "WMS|OnXAsync"), "E-WF-022");
    }

    [Fact]
    public async Task Starter_MissingOrDisabled_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        await SeedFlowAndUsersAsync(conn);                              // 流程 enabled
        // 不存在的发起人
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, Guid.NewGuid(), "{\"cron\":\"0 9 * * *\"}"), "E-WF-022");
        // 停用的发起人（独立库避免 flowKey 撞车）
        using var conn2 = NewSqliteWithSchema();
        var (disabledStarter, _) = await SeedFlowAndUsersAsync(conn2, starterEnabled: false);
        await AssertThrowsCodeAsync(conn2,
            Req(WfTriggerType.Timer, disabledStarter, "{\"cron\":\"0 9 * * *\"}"), "E-WF-022");
    }

    [Fact]
    public async Task Flow_MissingOrDisabled_EWF023()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        // FlowKey 不存在
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}", flowKey: "nope"), "E-WF-023");
        // FlowKey 存在但停用
        using var conn2 = NewSqliteWithSchema();
        var (starter2, _) = await SeedFlowAndUsersAsync(conn2, flowEnabled: false);
        await AssertThrowsCodeAsync(conn2,
            Req(WfTriggerType.Timer, starter2, "{\"cron\":\"0 9 * * *\"}"), "E-WF-023");
    }

    [Fact]
    public async Task Timer_BadVarsJson_EWF022()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        await AssertThrowsCodeAsync(conn,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\",\"varsJson\":\"not-json\"}"), "E-WF-022");
    }

    [Fact]
    public async Task ValidThreeTypes_Pass()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        // 三型合法配置全过（不抛）
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\",\"varsJson\":\"{\\\"a\\\":1}\"}"),
            CancellationToken.None);
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Event, starter, "{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}",
                eventKey: "WMS|OnShipmentConfirmedAsync"),
            CancellationToken.None);
        await FlowTriggerValidator.ValidateAsync(db,
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\",\"amount\"]}"),
            CancellationToken.None);
    }
}

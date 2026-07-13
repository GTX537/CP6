// CP6.Tests/Wf/FlowTriggerConfigTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class FlowTriggerConfigTests
{
    [Fact]
    public void ParseTimer_ReadsCronAndVars()
    {
        var c = WfTriggerConfig.ParseTimer("{\"cron\":\"0 0 25 * *\",\"varsJson\":\"{\\\"a\\\":1}\"}");
        Assert.Equal("0 0 25 * *", c.Cron);
        Assert.Equal("{\"a\":1}", c.VarsJson);
    }

    [Fact]
    public void ParseEvent_ReadsVarsMap()
    {
        var c = WfTriggerConfig.ParseEvent("{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}");
        Assert.Equal("$.OutboundNo", c.VarsMap!["orderNo"]);
    }

    [Fact]
    public void ParseMessage_ReadsVarsSchema()
    {
        var c = WfTriggerConfig.ParseMessage("{\"varsSchema\":[\"orderNo\",\"amount\"]}");
        Assert.Equal(new[] { "orderNo", "amount" }, c.VarsSchema);
    }

    [Fact]
    public void Parse_EmptyOrBadJson_YieldsEmptyConfig()
    {
        Assert.Null(WfTriggerConfig.ParseTimer("{}").Cron == "" ? null : "x"); // Cron 默认空串
        Assert.Null(WfTriggerConfig.ParseEvent("not-json").VarsMap);
        Assert.Null(WfTriggerConfig.ParseMessage("").VarsSchema);
    }
}

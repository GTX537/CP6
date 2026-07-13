// CP6.Tests/Wf/WfTriggerVarsMapperTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class WfTriggerVarsMapperTests
{
    [Fact]
    public void MapVars_DotPath_And_Literal()
    {
        var payload = "{\"OutboundNo\":\"OB-9\",\"detail\":{\"qty\":3}}";
        var map = new Dictionary<string, string> { ["orderNo"] = "$.OutboundNo", ["qty"] = "$.detail.qty", ["src"] = "wms" };
        var vars = WfTriggerVarsMapper.MapVars(map, payload);
        Assert.Contains("\"orderNo\":\"OB-9\"", vars);
        Assert.Contains("\"qty\":\"3\"", vars);      // ServiceVarsHelper 口径：值统一字符串（已记档限制）
        Assert.Contains("\"src\":\"wms\"", vars);
    }

    [Fact]
    public void MapVars_MissingPath_EmptyString()
    {
        var vars = WfTriggerVarsMapper.MapVars(new() { ["x"] = "$.nope" }, "{}");
        Assert.Contains("\"x\":\"\"", vars);
    }

    [Fact]
    public void MapVars_NullOrEmptyMap_EmptyVars_NoPassthrough()
    {
        // 无 varsMap 不透传原负载（防变量注入，与 message 白名单同哲学）
        Assert.Equal("{}", WfTriggerVarsMapper.MapVars(null, "{\"a\":1}"));
        Assert.Equal("{}", WfTriggerVarsMapper.MapVars(new(), "{\"a\":1}"));
    }

    [Fact]
    public void FilterBySchema_KeepsWhitelisted_DropsRest()
    {
        var vars = WfTriggerVarsMapper.FilterBySchema("{\"orderNo\":\"PO-1\",\"amount\":5,\"evil\":\"x\"}",
                                                      new[] { "orderNo", "amount" });
        Assert.Contains("\"orderNo\":\"PO-1\"", vars);
        Assert.Contains("\"amount\":5", vars);
        Assert.DoesNotContain("evil", vars);
    }

    [Fact]
    public void FilterBySchema_NullSchema_DropsAll()
    {
        Assert.Equal("{}", WfTriggerVarsMapper.FilterBySchema("{\"a\":1}", null));
    }

    [Fact]
    public void FilterBySchema_NonObjectBody_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => WfTriggerVarsMapper.FilterBySchema("[1,2]", new[] { "a" }));
    }
}

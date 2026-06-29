// CP6.Tests/Wf/ServiceVarsHelperTests.cs
using System.Collections.Generic;
using CP6.Core.Services.Wf;
using Xunit;

namespace CP6.Tests.Wf;

public class ServiceVarsHelperTests
{
    [Fact]
    public void Resolve_DollarVar_FromVars()
    {
        var vars = "{\"orderId\":\"PO-1\",\"detail\":{\"lineNo\":3}}";
        var ctx = new ServiceTemplateCtx(vars, actorId: "u1", jobId: "j1", instanceId: "i1", nowUtcIso: "2026-06-29T00:00:00Z");
        Assert.Equal("PO-1", ServiceVarsHelper.ResolveValue("$.orderId", ctx));
        Assert.Equal("3", ServiceVarsHelper.ResolveValue("$.detail.lineNo", ctx));
        Assert.Equal("u1", ServiceVarsHelper.ResolveValue("$wf.actorId", ctx));
        Assert.Equal("固定值", ServiceVarsHelper.ResolveValue("固定值", ctx));
    }

    [Fact]
    public void Merge_TopLevelOnly_And_BlocksReserved()
    {
        var vars = "{\"a\":1}";
        var output = new Dictionary<string, object?> { ["b"] = 2, ["wf"] = new { x = 1 }, ["sys"] = "no", ["_internal"] = true };
        var res = ServiceVarsHelper.MergeOutputVars(vars, output);
        Assert.Contains("\"b\":2", res.VarsJson);
        Assert.DoesNotContain("\"sys\"", res.VarsJson);          // reserved blocked
        Assert.Contains("b", res.MergedKeys);
        Assert.Contains("wf", res.SkippedKeys);
        Assert.Contains("sys", res.SkippedKeys);
        Assert.Contains("_internal", res.SkippedKeys);

        // dotted reserved prefix blocked; non-reserved lookalike allowed
        var res2 = ServiceVarsHelper.MergeOutputVars("{}", new Dictionary<string, object?> { ["wf.foo"] = 1, ["wffoo"] = 2 });
        Assert.Contains("wf.foo", res2.SkippedKeys);     // dotted prefix blocked
        Assert.Contains("wffoo", res2.MergedKeys);       // not reserved → allowed
        Assert.DoesNotContain("\"wf.foo\"", res2.VarsJson);
        Assert.Contains("\"wffoo\"", res2.VarsJson);
    }

    [Fact]
    public void Merge_OverwriteExistingNonReserved()
    {
        var res = ServiceVarsHelper.MergeOutputVars("{\"a\":1}", new Dictionary<string, object?> { ["a"] = 9 });
        Assert.Contains("\"a\":9", res.VarsJson);
    }
}

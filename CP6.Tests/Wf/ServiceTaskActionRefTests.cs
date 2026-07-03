// CP6.Tests/Wf/ServiceTaskActionRefTests.cs
using CP6.Core.Services.Wf;
using Xunit;

namespace CP6.Tests.Wf;

public class ServiceTaskActionRefTests
{
    [Fact]
    public void Snapshot_WebApi()
    {
        var n = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
            ServiceConnectorName = "erpEcho", ServicePath = "/o/{orderId}", ServiceParamsJson = "{}" };
        var json = ServiceTaskActionRef.Snapshot(n);
        var r = ServiceTaskActionRef.Parse(json);
        Assert.Equal("webApi", r.ServiceKind);
        Assert.Equal("webApi", r.ActionKind);
        Assert.Equal("erpEcho", r.ConnectorName);
        Assert.Equal("webApi", ServiceTaskActionRef.ResolveExecutorKey(r));
    }

    [Fact]
    public void Snapshot_DataWriteback()
    {
        var n = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
            ServiceActionName = "poConfirm", ServiceParamsJson = "{}" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("poConfirm", ServiceTaskActionRef.ResolveExecutorKey(r));
    }

    [Fact]
    public void Snapshot_Timer_PureWait_ResolvesNull()
    {
        var n = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "duration", ServiceDelayValue = "PT2H" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("none", r.ActionKind);
        Assert.Null(ServiceTaskActionRef.ResolveExecutorKey(r));   // 纯等待无 executor
    }

    [Fact]
    public void Snapshot_Timer_WithWebApiAction()
    {
        var n = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
            ServiceDelayMode = "untilDate", ServiceDelayValue = "2026-07-01",
            ServiceConnectorName = "erpEcho", ServicePath = "/x" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("webApi", r.ActionKind);
        Assert.Equal("webApi", ServiceTaskActionRef.ResolveExecutorKey(r));
    }
}

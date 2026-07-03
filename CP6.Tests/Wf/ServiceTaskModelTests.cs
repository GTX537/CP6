// CP6.Tests/Wf/ServiceTaskModelTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

public class ServiceTaskModelTests
{
    [Fact]
    public void FlowNode_HasServiceTaskFields()
    {
        var n = new FlowNode { Id = "n1", Type = "serviceTask",
            ServiceKind = ServiceKind.WebApi, ServiceMode = ServiceMode.Async,
            ServiceConnectorName = "erpEcho", ServicePath = "/x", ServiceParamsJson = "{}",
            ServiceActionName = null, ServiceDelayMode = "duration", ServiceDelayValue = "PT2H",
            ServiceMaxRetries = 3, ServiceRetryBackoffSec = 30 };
        Assert.Equal("webApi", n.ServiceKind);
    }

    [Fact]
    public void FlowEdge_HasIsError()
    {
        var e = new FlowEdge { From = "a", To = "b", IsError = true };
        Assert.True(e.IsError);
    }

    [Fact]
    public void ServiceJobStatus_Constants()
    {
        Assert.Equal(0, ServiceJobStatus.Pending);
        Assert.Equal(1, ServiceJobStatus.Running);
        Assert.Equal(2, ServiceJobStatus.Succeeded);
        Assert.Equal(3, ServiceJobStatus.Failed);
        Assert.Equal(4, ServiceJobStatus.Cancelled);
    }

    [Fact]
    public void Wf_ServiceJob_Defaults()
    {
        var j = new Wf_ServiceJob { InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(),
            NodeId = "n1", Kind = ServiceKind.Timer, Status = ServiceJobStatus.Pending,
            AttemptCount = 0, MaxAttempts = 4 };
        Assert.Equal(0, j.AttemptCount);
        Assert.Null(j.LockedBy);
    }
}

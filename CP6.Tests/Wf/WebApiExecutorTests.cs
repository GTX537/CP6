// CP6.Tests/Wf/WebApiExecutorTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using Xunit;

namespace CP6.Tests.Wf;

public class WebApiExecutorTests
{
    // ── Fake connector：把收到的 path / jobId echo 回 OutputVars ──────────────
    private sealed class FakeConn : IWfConnector
    {
        public string Name => "erpEcho";
        public string DisplayName => "ERP Echo";
        public Task<ServiceTaskResult> CallAsync(string path, string? prms, ServiceTaskContext ctx)
            => Task.FromResult(ServiceTaskResult.Ok(new Dictionary<string, object?>
            {
                ["resolvedPath"] = path,
                ["jobId"]        = ctx.JobId.ToString()
            }));
    }

    private static ServiceTaskContext MakeCtx(Guid jobId, string? varsJson = null, string? actionRefJson = null) =>
        new ServiceTaskContext
        {
            InstanceId    = Guid.NewGuid(),
            TokenId       = Guid.NewGuid(),
            NodeId        = "n1",
            StarterId     = Guid.NewGuid(),
            JobId         = jobId,
            AttemptNo     = 1,
            ActorId       = Guid.Empty,
            NowUtc        = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc),
            VarsJson      = varsJson,
            ActionRefJson = actionRefJson
        };

    // ── T1: 找到连接器→透传 path + OutputVars；元数据正确 ───────────────────
    [Fact]
    public async Task Resolves_Connector_And_Templates_Path()
    {
        // actionRef: connectorName=erpEcho, path="/o/{orderId}", vars{orderId:"PO-1"}
        var node = new FlowNode
        {
            Id = "n1", Type = "serviceTask",
            ServiceKind = ServiceKind.WebApi,
            ServiceConnectorName = "erpEcho",
            ServicePath = "/o/{orderId}",
            ServiceParamsJson = "{}"
        };
        var actionRefJson = ServiceTaskActionRef.Snapshot(node);
        var jobId = Guid.NewGuid();
        var ctx = MakeCtx(jobId, varsJson: "{\"orderId\":\"PO-1\"}", actionRefJson: actionRefJson);

        var executor = new WebApiExecutor(new IWfConnector[] { new FakeConn() });

        // Act
        var result = await executor.ExecuteAsync(ctx);

        // Assert: executor metadata
        Assert.Equal("webApi", executor.Key);
        Assert.Equal("webApi", executor.Kind);
        Assert.False(executor.VisibleInDesigner);

        // Assert: connector's OutputVars are forwarded as-is
        Assert.True(result.Success);
        Assert.NotNull(result.OutputVars);
        Assert.True(result.OutputVars!.ContainsKey("resolvedPath"));
        // FakeConn echoes back the raw path template it received
        Assert.Equal("/o/{orderId}", result.OutputVars["resolvedPath"]?.ToString());
        Assert.Equal(jobId.ToString(), result.OutputVars["jobId"]?.ToString());
    }

    // ── T2: 连接器名不在注册表 → Fail + E-WF-018 ─────────────────────────────
    [Fact]
    public async Task UnknownConnector_Fails()
    {
        var node = new FlowNode
        {
            Id = "n1", Type = "serviceTask",
            ServiceKind = ServiceKind.WebApi,
            ServiceConnectorName = "unknownConnector",
            ServicePath = "/x"
        };
        var actionRefJson = ServiceTaskActionRef.Snapshot(node);
        var ctx = MakeCtx(Guid.NewGuid(), actionRefJson: actionRefJson);

        var executor = new WebApiExecutor(new IWfConnector[] { new FakeConn() });

        var result = await executor.ExecuteAsync(ctx);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("E-WF-018", result.Error!);
    }

    // ── T3: EchoConnector 自己把 {orderId} 解析 + 发出幂等键 ──────────────────
    [Fact]
    public async Task EchoConnector_ResolvesTemplate_And_EmitsIdempotencyKey()
    {
        var jobId = Guid.NewGuid();
        var ctx = MakeCtx(jobId, varsJson: "{\"orderId\":\"PO-1\"}");
        var connector = new EchoConnector();

        var result = await connector.CallAsync("/o/{orderId}", "{}", ctx);

        Assert.True(result.Success);
        Assert.NotNull(result.OutputVars);
        Assert.Equal("/o/PO-1", result.OutputVars!["echoedPath"]?.ToString());
        Assert.Equal($"wf-service-job-{jobId}", result.OutputVars["idempotencyKey"]?.ToString());
    }
}

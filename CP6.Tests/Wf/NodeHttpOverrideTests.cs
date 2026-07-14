using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests;

/// <summary>E-T1：FlowNode 节点级 HTTP method/timeout 覆盖 + E-WF-028 双面校验（静态值域 + 保存侧租约）。
/// 覆盖优先级：node.ServiceHttpMethod/ServiceTimeoutSec（经 ActionRefJson 固化快照承载）→ 连接器默认。</summary>
public class NodeHttpOverrideTests
{
    // ── 捕获 HTTP 请求的 handler（断言 method）+ 单实例 factory（断言 client.Timeout）──
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? LastMethod;
        public bool HadBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastMethod = request.Method;
            HadBody = request.Content != null;
            if (request.Content != null) await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class OneClientFactory : IHttpClientFactory
    {
        public readonly HttpClient Client;
        public OneClientFactory(HttpMessageHandler h) => Client = new HttpClient(h);
        public HttpClient CreateClient(string name) => Client;
    }

    private static ServiceTaskContext CtxFor(FlowNode node) => new()
    {
        InstanceId    = Guid.NewGuid(),
        TokenId       = Guid.NewGuid(),
        NodeId        = node.Id,
        StarterId     = Guid.NewGuid(),
        JobId         = Guid.NewGuid(),
        AttemptNo     = 1,
        ActorId       = Guid.Empty,
        NowUtc        = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc),
        VarsJson      = null,
        ActionRefJson = ServiceTaskActionRef.Snapshot(node),   // 固化快照承载节点覆盖值
    };

    private static FlowNode WebApiNode(string? method = null, int? timeout = null, string? paramsJson = "{\"a\":1}") => new()
    {
        Id = "n1", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
        ServiceConnectorName = "c", ServicePath = "api/x", ServiceParamsJson = paramsJson,
        ServiceHttpMethod = method, ServiceTimeoutSec = timeout,
    };

    // ── 覆盖优先：节点 method/timeout 覆盖连接器默认 ─────────────────────
    [Fact]
    public async Task NodeOverride_Method_And_Timeout_WinOverConnectorDefault()
    {
        var node = WebApiNode(method: "PUT", timeout: 5);
        var row = new Wf_Connector { Name = "c", DisplayName = "C", BaseUrl = "http://localhost/", TimeoutSec = 30 };
        var handler = new CapturingHandler();
        var factory = new OneClientFactory(handler);
        var conn = new DbWfConnector(row, null, factory);

        var res = await conn.CallAsync(node.ServicePath!, node.ServiceParamsJson, CtxFor(node));

        Assert.True(res.Success);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);                 // 节点 method 覆盖 (非默认 POST)
        Assert.Equal(TimeSpan.FromSeconds(5), factory.Client.Timeout);    // 节点 timeout 覆盖 (非连接器 30)
    }

    // ── 缺省回落：无节点覆盖 → 连接器默认 method(有 body→POST) + 连接器 TimeoutSec ──
    [Fact]
    public async Task NoOverride_FallsBackToConnectorDefaults()
    {
        var node = WebApiNode(method: null, timeout: null, paramsJson: "{\"a\":1}");
        var row = new Wf_Connector { Name = "c", DisplayName = "C", BaseUrl = "http://localhost/", TimeoutSec = 30 };
        var handler = new CapturingHandler();
        var factory = new OneClientFactory(handler);
        var conn = new DbWfConnector(row, null, factory);

        var res = await conn.CallAsync(node.ServicePath!, node.ServiceParamsJson, CtxFor(node));

        Assert.True(res.Success);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);                // 有 body → 默认 POST
        Assert.Equal(TimeSpan.FromSeconds(30), factory.Client.Timeout);   // 连接器默认 30
    }

    // ── 缺省 + 无 body → 默认 GET，且无 body ──────────────────────────
    [Fact]
    public async Task NoOverride_NoBody_DefaultsToGet()
    {
        var node = WebApiNode(method: null, timeout: null, paramsJson: "{}");
        var row = new Wf_Connector { Name = "c", DisplayName = "C", BaseUrl = "http://localhost/", TimeoutSec = 30 };
        var handler = new CapturingHandler();
        var conn = new DbWfConnector(row, null, new OneClientFactory(handler));

        await conn.CallAsync(node.ServicePath!, node.ServiceParamsJson, CtxFor(node));

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.False(handler.HadBody);
    }

    // ── 节点覆盖 GET 时不带 body（即使有 params）──────────────────────
    [Fact]
    public async Task NodeOverride_Get_DropsBody()
    {
        var node = WebApiNode(method: "GET", timeout: null, paramsJson: "{\"a\":1}");
        var row = new Wf_Connector { Name = "c", DisplayName = "C", BaseUrl = "http://localhost/", TimeoutSec = 30 };
        var handler = new CapturingHandler();
        var conn = new DbWfConnector(row, null, new OneClientFactory(handler));

        await conn.CallAsync(node.ServicePath!, node.ServiceParamsJson, CtxFor(node));

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.False(handler.HadBody);
    }

    // ── round-trip：Snapshot → Parse 保留 HttpMethod/TimeoutSec ───────
    [Fact]
    public void ActionRef_RoundTrips_HttpMethod_And_TimeoutSec()
    {
        var node = WebApiNode(method: "DELETE", timeout: 42);
        var json = ServiceTaskActionRef.Snapshot(node);
        var r = ServiceTaskActionRef.Parse(json);
        Assert.Equal("DELETE", r.HttpMethod);
        Assert.Equal(42, r.TimeoutSec);
    }

    [Fact]
    public void ActionRef_OmitsNullOverrides()
    {
        var node = WebApiNode(method: null, timeout: null);
        var json = ServiceTaskActionRef.Snapshot(node);
        var r = ServiceTaskActionRef.Parse(json);
        Assert.Null(r.HttpMethod);
        Assert.Null(r.TimeoutSec);
    }

    // ── E-WF-028 静态：ServiceTimeoutSec 非正 / 超上限 → 值域拒 ─────────
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Static_TimeoutOutOfRange_E028(int timeout)
    {
        var schema = OneServiceTaskSchema(WebApiNode(timeout: timeout));
        Assert.Contains("E-WF-028", FlowSchemaValidator.Validate(schema));
    }

    // ── E-WF-028 静态：ServiceHttpMethod 非法值 → 拒 ───────────────────
    [Theory]
    [InlineData("PATCH")]
    [InlineData("FOO")]
    public void Static_IllegalHttpMethod_E028(string method)
    {
        var schema = OneServiceTaskSchema(WebApiNode(method: method));
        Assert.Contains("E-WF-028", FlowSchemaValidator.Validate(schema));
    }

    // ── E-WF-028 静态：合法 method + 合法值域 → 不报 028 ────────────────
    [Fact]
    public void Static_ValidOverride_NoE028()
    {
        var schema = OneServiceTaskSchema(WebApiNode(method: "PUT", timeout: 5));
        Assert.DoesNotContain("E-WF-028", FlowSchemaValidator.Validate(schema));
    }

    // ── E-WF-028 保存侧：ServiceTimeoutSec >= 租约(300) → 拒（静态值域内 600 仍拒）──
    [Fact]
    public async Task SaveSide_TimeoutAtOrAboveLease_E028()
    {
        using var db = NewDb();
        var svc = DesignerSvc(db);
        var schemaJson = JsonSerializer.Serialize(OneServiceTaskSchema(WebApiNode(timeout: 600)));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveAsync(new SaveFlowRequest("f", "F", "form", null, null, schemaJson), null));
        Assert.Equal("E-WF-028", ex.Message);
    }

    // ── E-WF-028 保存侧：ServiceTimeoutSec < 租约 → 放行 ────────────────
    [Fact]
    public async Task SaveSide_TimeoutBelowLease_Accepted()
    {
        using var db = NewDb();
        var svc = DesignerSvc(db);
        var schemaJson = JsonSerializer.Serialize(OneServiceTaskSchema(WebApiNode(timeout: 60)));
        await svc.SaveAsync(new SaveFlowRequest("f", "F", "form", null, null, schemaJson), null);
        Assert.True(await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == "f"));
    }

    // ── 脚手架 ────────────────────────────────────────────────────────
    private static FlowSchema OneServiceTaskSchema(FlowNode svcNode) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            svcNode,
            new FlowNode { Id = "e", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = svcNode.Id },
            new FlowEdge { From = svcNode.Id, To = "e" },
        },
    };

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed class FakeConnector : IWfConnector
    {
        public string Name => "c";
        public string DisplayName => "C";
        public Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext c)
            => Task.FromResult(ServiceTaskResult.Ok());
    }

    private static IDesignerService DesignerSvc(CP6Context db) => new DesignerService(
        db, new FlowDefService(db), Array.Empty<IServiceTaskExecutor>(), new IWfConnector[] { new FakeConnector() });
}

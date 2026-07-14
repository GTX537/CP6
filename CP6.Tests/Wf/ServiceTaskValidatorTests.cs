// CP6.Tests/Wf/ServiceTaskValidatorTests.cs
// E-T1: FlowSchemaValidator serviceTask 规则(E-WF-016/017) + DesignerService.save 注册校验(E-WF-018)。
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Wf;

public class ServiceTaskValidatorTests
{
    // ── 基准合法 schema：webApi serviceTask，配齐 connector+path，1 成功出边 + 1 错误出边 ──
    // s(start) → svc(serviceTask/webApi) → e(end,成功)  ；svc → ee(end) 标 IsError
    private static FlowSchema Base() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode
            {
                Id = "svc", Type = "serviceTask",
                ServiceKind = ServiceKind.WebApi,
                ServiceConnectorName = "erpEcho",
                ServicePath = "/orders/{{id}}",
            },
            new FlowNode { Id = "e", Type = "end" },
            new FlowNode { Id = "ee", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "svc" },
            new FlowEdge { From = "svc", To = "e" },
            new FlowEdge { From = "svc", To = "ee", IsError = true },
        },
    };

    private static FlowNode Svc(FlowSchema s) => s.Nodes.First(n => n.Id == "svc");

    // ── Step 1/3: FlowSchemaValidator serviceTask 规则 ──

    [Fact]
    public void WebApi_MissingConnector_E_WF_016()
    {
        var s = Base(); Svc(s).ServiceConnectorName = null;   // webApi 缺 connector
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void DataWriteback_MissingAction_E_WF_016()
    {
        var s = Base();
        Svc(s).ServiceKind = ServiceKind.DataWriteback;
        Svc(s).ServiceActionName = null;                       // dataWriteback 缺 action
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void Timer_MissingDelay_E_WF_016()
    {
        var s = Base();
        Svc(s).ServiceKind = ServiceKind.Timer;
        Svc(s).ServiceDelayMode = null;                        // timer 缺 delayMode
        Svc(s).ServiceDelayValue = null;                       // 及 delayValue（后端双字段严查）
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void Timer_MissingOnlyDelayMode_E_WF_016()
    {
        var s = Base();
        Svc(s).ServiceKind = ServiceKind.Timer;
        Svc(s).ServiceDelayMode = null;                        // 只缺 mode（delayValue 有）→ 后端仍拦
        Svc(s).ServiceDelayValue = "3d";
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void ServiceTask_NonEnd_NoSuccessEdge_E_WF_016()
    {
        // P2-3 最危险的洞：serviceTask 仅配错误出边 → 引擎成功路径无后继 → 误结 Approved。
        var s = Base();
        s.Edges.RemoveAll(e => e.From == "svc" && e.IsError != true);   // 删掉唯一成功出边
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void MoreThanOneErrorEdge_E_WF_017()
    {
        var s = Base();
        s.Nodes.Add(new FlowNode { Id = "ee2", Type = "end" });
        s.Edges.Add(new FlowEdge { From = "svc", To = "ee2", IsError = true });   // 第 2 条错误出边
        Assert.Contains("E-WF-017", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void ErrorEdge_FromNonServiceTask_E_WF_017()
    {
        // B-T1 放宽后 IsError 边合法来源 = {serviceTask, approval, subFlow}（ErrorEdgeSourceTypes）。
        // 本用例改用 start 节点作错误边来源（∉ 合法集）验证「非法来源仍拦 E-WF-017」这一不变量
        // （原用例用 approval 作来源，approval 现已是合法来源——该正向放行由 ErrorEdgeSourceValidatorTests 覆盖）。
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "e", Type = "end" },
                new FlowNode { Id = "ee", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "a" },
                new FlowEdge { From = "a", To = "e" },
                new FlowEdge { From = "s", To = "ee", IsError = true },   // 错误边来源为 start（∉ 合法来源集）→ 非法
            },
        };
        Assert.Contains("E-WF-017", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void ValidServiceTask_Passes()
    {
        Assert.Empty(FlowSchemaValidator.Validate(Base()));
    }

    [Fact]
    public void WebApi_PathWithArraySubscript_E_WF_016()
    {
        var s = Base();
        Svc(s).ServicePath = "/o/{lines[0]}";   // 路径模板含数组下标 → 设计期拦
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(s));
    }

    // ── 票5：ServiceMode 值域校验(sync|async) ──

    [Fact]
    public void ServiceMode_Invalid_E_WF_016()
    {
        var schema = new FlowSchema {
            Nodes = {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                    ServiceActionName = "sampleWriteback", ServiceMode = "batch" },   // 非法 mode
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
        };
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void ServiceMode_SyncOrAsync_Or_Null_Passes()
    {
        foreach (var mode in new string?[] { null, "sync", "async" })
        {
            var schema = new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "s", Type = "start" },
                    new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                        ServiceActionName = "sampleWriteback", ServiceMode = mode },
                    new FlowNode { Id = "e", Type = "end" },
                },
                Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
            };
            Assert.DoesNotContain("E-WF-016", FlowSchemaValidator.Validate(schema));
        }
    }

    // ── Step 4: DesignerService.save 注册名校验(E-WF-018) ──

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed class FakeExec : IServiceTaskExecutor
    {
        public string Key { get; init; } = "";
        public string Kind { get; init; } = "";
        public bool VisibleInDesigner { get; init; } = true;
        public string DisplayName { get; init; } = "";
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx) => Task.FromResult(ServiceTaskResult.Ok());
    }

    private sealed class FakeConnector : IWfConnector
    {
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public Task<ServiceTaskResult> CallAsync(string p, string? j, ServiceTaskContext ctx) => Task.FromResult(ServiceTaskResult.Ok());
    }

    private static DesignerService SvcWith(CP6Context db, string actionKey, string connName) =>
        new(db, new FlowDefService(db),
            new IServiceTaskExecutor[] { new FakeExec { Key = actionKey, Kind = ServiceKind.DataWriteback, DisplayName = actionKey } },
            new IWfConnector[] { new FakeConnector { Name = connName, DisplayName = connName } });

    private static string Json(FlowSchema s) => JsonSerializer.Serialize(s);

    private static FlowSchema DataWritebackSchema(string actionName)
    {
        var s = Base();
        Svc(s).ServiceKind = ServiceKind.DataWriteback;
        Svc(s).ServiceConnectorName = null;
        Svc(s).ServicePath = null;
        Svc(s).ServiceActionName = actionName;
        return s;
    }

    [Fact]
    public async Task Save_DataWriteback_UnregisteredAction_ThrowsE_WF_018()
    {
        using var db = NewDb();
        var svc = SvcWith(db, actionKey: "realWb", connName: "erpEcho");
        var req = new SaveFlowRequest("dw", "回写流程", "leave", null, null, Json(DataWritebackSchema("ghostAction")));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(req, null));
        Assert.Equal("E-WF-018", ex.Message);
    }

    [Fact]
    public async Task Save_WebApi_UnregisteredConnector_ThrowsE_WF_018()
    {
        using var db = NewDb();
        var svc = SvcWith(db, actionKey: "realWb", connName: "realConn");
        var s = Base(); Svc(s).ServiceConnectorName = "ghostConn";   // webApi 引用未注册连接器
        var req = new SaveFlowRequest("wa", "webApi流程", "leave", null, null, Json(s));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(req, null));
        Assert.Equal("E-WF-018", ex.Message);
    }

    [Fact]
    public async Task Save_RegisteredNames_Persists()
    {
        using var db = NewDb();
        var svc = SvcWith(db, actionKey: "realWb", connName: "erpEcho");
        var req = new SaveFlowRequest("ok", "合法流程", "leave", null, null, Json(DataWritebackSchema("realWb")));
        await svc.SaveAsync(req, "tester");                          // action 已注册 → 不抛
        Assert.NotNull(await db.Wf_FlowDefs.SingleOrDefaultAsync(d => d.FlowKey == "ok"));
    }

    [Fact]
    public async Task Save_ConnectorName_CaseInsensitive_Persists()
    {
        // E-WF-018 注册名比较须镜像运行时字典(OrdinalIgnoreCase)：
        // 注册名 "erpEcho" vs schema 引用 "ErpEcho" 仅大小写不同 → 运行时找得到 → save 也须放行。
        using var db = NewDb();
        var svc = SvcWith(db, actionKey: "realWb", connName: "erpEcho");
        var s = Base(); Svc(s).ServiceConnectorName = "ErpEcho";      // 大小写不符的已注册连接器
        var req = new SaveFlowRequest("ci", "大小写流程", "leave", null, null, Json(s));
        await svc.SaveAsync(req, "tester");                          // 不应抛 E-WF-018
        Assert.NotNull(await db.Wf_FlowDefs.SingleOrDefaultAsync(d => d.FlowKey == "ci"));
    }
}

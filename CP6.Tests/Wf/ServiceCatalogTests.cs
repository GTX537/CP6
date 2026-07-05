// CP6.Tests/Wf/ServiceCatalogTests.cs
using System;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Wf;

public class ServiceCatalogTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    // fake executor to cover the rest of the filter matrix (dataWriteback-but-hidden / wrong-kind)
    private sealed class FakeExec : IServiceTaskExecutor
    {
        public string Key { get; init; } = "";
        public string Kind { get; init; } = "";
        public bool VisibleInDesigner { get; init; }
        public string DisplayName { get; init; } = "";
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx) => Task.FromResult(ServiceTaskResult.Ok());
    }

    private sealed class FakeConnector : IWfConnector
    {
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx)
            => Task.FromResult(ServiceTaskResult.Ok());
    }

    [Fact]
    public void GetServiceCatalog_FiltersWebApiExecutor_From_Actions()
    {
        using var db = NewDb();

        var execs = new IServiceTaskExecutor[]
        {
            new SampleDataWritebackExecutor(),                                   // dataWriteback + visible → 入
            new WebApiExecutor(Array.Empty<IWfConnector>()),                     // webApi + invisible → 出（反例）
            new FakeExec { Key = "hiddenWb",      Kind = ServiceKind.DataWriteback, VisibleInDesigner = false, DisplayName = "隐藏回写" }, // dataWriteback 但不可见 → 出
            new FakeExec { Key = "internalThing", Kind = "internal",               VisibleInDesigner = true,  DisplayName = "内部件"   }, // 非 dataWriteback → 出
        };
        var conns = new IWfConnector[]
        {
            new EchoConnector(),                                                 // erpEcho / ERP Echo (demo)
            new FakeConnector { Name = "erp2", DisplayName = "ERP Two" },
        };

        var svc = new DesignerService(db, new FlowDefService(db), execs, conns);

        var catalog = svc.GetServiceCatalog();

        // actions: 只含 Kind==dataWriteback && VisibleInDesigner 的 → 恰好 sampleWriteback
        var action = Assert.Single(catalog.Actions);
        Assert.Equal("sampleWriteback", action.Name);
        Assert.Equal("样例数据回写", action.Label);   // label = DisplayName
        Assert.DoesNotContain(catalog.Actions, a => a.Name == "webApi");        // WebApiExecutor 不出现

        // connectors: 含全部；每项 {name, label(DisplayName)}
        Assert.Equal(2, catalog.Connectors.Count);
        Assert.Contains(catalog.Connectors, c => c.Name == "erpEcho" && c.Label == "ERP Echo (demo)");
        Assert.Contains(catalog.Connectors, c => c.Name == "erp2"    && c.Label == "ERP Two");
    }
}

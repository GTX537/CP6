using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>OA 章03/04 流程定义服务（C-4）。Def upsert/升版 + 实例详情聚合（含 submit→查 集成冒烟）。</summary>
public class FlowDefServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private const string Schema =
        """{"nodes":[{"id":"n1","type":"approval","approverStrategy":"Specified","approverUserId":"11111111-1111-1111-1111-111111111111"},{"id":"end","type":"end"}],"edges":[{"from":"n1","to":"end"}]}""";

    [Fact]
    public async Task SaveDef_Then_GetDef_RoundTrip()
    {
        using var db = NewDb();
        var svc = new FlowDefService(db);
        await svc.SaveDefAsync("leave", "请假流程", "leave", Schema, "tester");

        var def = await svc.GetDefAsync("leave");
        Assert.NotNull(def);
        Assert.Equal("请假流程", def!.FlowName);
        Assert.Equal("leave", def.FormKey);
        Assert.Equal(1, def.Version);
    }

    [Fact]
    public async Task SaveDef_SchemaChange_BumpsVersion()
    {
        using var db = NewDb();
        var svc = new FlowDefService(db);
        await svc.SaveDefAsync("leave", "请假流程", "leave", Schema, null);
        await svc.SaveDefAsync("leave", "请假流程v2", "leave", Schema.Replace("end", "done"), null);

        var def = await svc.GetDefAsync("leave");
        Assert.Equal(2, def!.Version);
        Assert.Equal("请假流程v2", def.FlowName);
    }

    [Fact]
    public async Task GetInstanceDetail_AfterSubmit_ReturnsInstanceHistoryTasks()
    {
        using var db = NewDb();
        var defSvc = new FlowDefService(db);
        await defSvc.SaveDefAsync("leave", "请假流程", "leave", Schema, null);

        var engine = new FlowEngine(db, new ApproverResolver(db));
        var instId = await engine.SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var detail = await defSvc.GetInstanceDetailAsync(instId);
        Assert.NotNull(detail);
        Assert.Equal(instId, detail!.Instance.Id);
        Assert.Equal("n1", detail.Instance.CurrentNode);
        Assert.Contains(detail.History, h => h.Action == "submit");   // 痕迹有提交
        Assert.Single(detail.Tasks);                                   // n1 一条待办
    }

    [Fact]
    public async Task GetInstanceDetail_NotFound_ReturnsNull()
    {
        using var db = NewDb();
        var detail = await new FlowDefService(db).GetInstanceDetailAsync(Guid.NewGuid());
        Assert.Null(detail);
    }
}

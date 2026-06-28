using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class DesignerServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IDesignerService Svc(CP6Context db) => new DesignerService(db, new FlowDefService(db));

    private static string ValidSchema() => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" },
                  new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                  new FlowNode { Id = "e", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    });

    [Fact]
    public async Task Save_Valid_PersistsWithIdentity()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假流程", "leave", "MSBBPA010", "2887", ValidSchema()), "tester");
        var def = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "leave");
        Assert.Equal("MSBBPA010", def.FunctionId);
        Assert.Equal("2887", def.FlowCode);
    }

    [Fact]
    public async Task Save_InvalidSchema_ThrowsE010()
    {
        using var db = NewDb();
        var bad = JsonSerializer.Serialize(new FlowSchema { Nodes = { new FlowNode { Id = "a", Type = "approval" } } }); // 无 start/end/策略
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveAsync(new SaveFlowRequest("x", "x", "x", null, null, bad), null));
        Assert.Equal("E-WF-010", ex.Message);
    }

    [Fact]
    public async Task Save_DuplicateFunctionId_ThrowsE009()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假", "leave", "MSBBPA010", "2887", ValidSchema()), null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveAsync(new SaveFlowRequest("expense", "报销", "expense", "MSBBPA010", "2889", ValidSchema()), null));
        Assert.Equal("E-WF-009", ex.Message);     // FunctionId 撞
    }

    [Fact]
    public async Task Clone_ProducesIndependentCopy()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假", "leave", "MSBBPA010", "2887", ValidSchema()), null);
        await Svc(db).CloneAsync(new CloneRequest("leave", "leave_v2", "请假副本"), null);

        var copy = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "leave_v2");
        Assert.Equal("leave", copy.FormKey);          // 同表单
        Assert.Null(copy.FunctionId);                 // 身份码清空（避免撞唯一）
        Assert.Null(copy.FlowCode);
        Assert.False(copy.Enable);                    // 副本默认停用
        Assert.Equal(2, await db.Wf_FlowDefs.CountAsync()); // 两条独立定义
    }
}

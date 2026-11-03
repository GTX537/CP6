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
    private static IDesignerService Svc(CP6Context db) => new DesignerService(db, new FlowDefService(db),
        Array.Empty<IServiceTaskExecutor>(), Array.Empty<IWfConnector>());

    private static string ValidSchema() => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" },
                  new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                  new FlowNode { Id = "e", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    });

    [Fact]
    public async Task Retired_save_rejects_valid_body_without_creating_identity()
    {
        using var db = NewDb();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假流程", "leave", "MSBBPA010", "2887", ValidSchema()), "tester"));
        Assert.Equal(LegacyDefinitionWrites.Code, error.Message);
        Assert.Empty(await db.Wf_FlowDefs.ToArrayAsync());
    }

    [Fact]
    public async Task Save_InvalidSchema_ThrowsE010()
    {
        using var db = NewDb();
        var bad = JsonSerializer.Serialize(new FlowSchema { Nodes = { new FlowNode { Id = "a", Type = "approval" } } }); // 无 start/end/策略
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(JsonSerializer.Deserialize<FlowSchema>(bad)!));
    }

    [Fact]
    public async Task Save_DuplicateFunctionId_ThrowsE009()
    {
        using var db = NewDb();
        await CP6.Tests.Wf.LegacyDefinitionFixture.Flow(db, "leave", "请假", "leave", ValidSchema());
        var original = await db.Wf_FlowDefs.SingleAsync(); original.FunctionId = "MSBBPA010"; await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveAsync(new SaveFlowRequest("expense", "报销", "expense", "MSBBPA010", "2889", ValidSchema()), null));
        Assert.Equal(LegacyDefinitionWrites.Code, ex.Message);
        Assert.Single(await db.Wf_FlowDefs.ToArrayAsync());
    }

    [Fact]
    public async Task Save_BadSerialStage_ThrowsE_WF_011()
    {
        using var db = NewDb();
        // managerChain 档缺 MaxLevels → FlowSchemaValidator 返回 E-WF-011
        var badSchema = JsonSerializer.Serialize(new FlowSchema
        {
            Start = "s",
            Nodes = { new FlowNode { Id = "s", Type = "start" },
                      new FlowNode { Id = "a", Type = "approval", Stages = new()
                      {
                          new ApprovalStage { Kind = "managerChain" /* MaxLevels 缺失 */, Countersign = "all" },
                      }},
                      new FlowNode { Id = "e", Type = "end" } },
            Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
        });
        Assert.Contains("E-WF-011", FlowSchemaValidator.Validate(JsonSerializer.Deserialize<FlowSchema>(badSchema)!));
    }

    [Fact]
    public async Task Retired_clone_cannot_create_copy_from_current_head()
    {
        using var db = NewDb();
        await CP6.Tests.Wf.LegacyDefinitionFixture.Flow(db, "leave", "请假", "leave", ValidSchema());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).CloneAsync(new CloneRequest("leave", "leave_v2", "请假副本"), null));
        Assert.Equal(LegacyDefinitionWrites.Code, error.Message);
        Assert.Equal("leave", (await db.Wf_FlowDefs.SingleAsync()).FlowKey);
    }
}

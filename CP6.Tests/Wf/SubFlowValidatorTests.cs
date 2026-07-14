using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>E-WF-025/026 双层校验（spec §5）：FlowSchemaValidator 纯静态规则 + SubFlowRefValidator DI 层
/// （FlowKey 存在性/启用 + 引用环 DFS 深度 8）+ DesignerService 保存接线。</summary>
public class SubFlowValidatorTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static FlowSchema SubSchema(Action<FlowNode>? mutate = null)
    {
        var sub = new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = "target" };
        mutate?.Invoke(sub);
        return new FlowSchema
        {
            Start = "s",
            Nodes = { new FlowNode { Id = "s", Type = "start" }, sub, new FlowNode { Id = "e", Type = "end" } },
            Edges = { new FlowEdge { From = "s", To = "sub" }, new FlowEdge { From = "sub", To = "e" } },
        };
    }

    // ── 静态层（FlowSchemaValidator,无 DI）──

    [Fact]
    public void Static_ValidSubFlow_NoErrors()
        => Assert.Empty(FlowSchemaValidator.Validate(SubSchema()));

    [Fact]
    public void Static_MissingSubFlowKey_E_WF_025()
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubFlowKey = " ")));

    [Fact]
    public void Static_NoNonErrorOutEdge_E_WF_025()
    {
        var schema = SubSchema();
        schema.Edges.Single(e => e.From == "sub").IsError = true;   // 仅错误出边=成功路径无后继(对齐 E-WF-016 同款规则)
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(schema));
    }

    [Theory]
    [InlineData("quorum")]
    [InlineData("first")]
    public void Static_BadPolicy_E_WF_025(string bad)
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubCompletionPolicy = bad)));

    [Fact]
    public void Static_BlankCollectionVar_E_WF_025()
        => Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubCollectionVar = "  ")));

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"a\":1}")]                       // 值非字符串路径
    [InlineData("{\"a\":\"$.items[0]\"}")]          // 不支持下标(ContainsUnsupportedSubscript)
    public void Static_BadVarsMap_E_WF_025(string bad)
    {
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubVarsInJson = bad)));
        Assert.Contains("E-WF-025", FlowSchemaValidator.Validate(SubSchema(n => n.SubVarsOutJson = bad)));
    }

    // ── DI 层（SubFlowRefValidator）──

    private static void SeedDef(CP6Context db, string key, FlowSchema schema, bool enable = true)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = enable });

    private static FlowSchema RefSchema(string targetKey) => SubSchema(n => n.SubFlowKey = targetKey);

    private static FlowSchema PlainApproval() => new()
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" },
                  new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                  new FlowNode { Id = "e", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    };

    [Fact]
    public async Task Ref_TargetMissing_E_WF_025()
    {
        using var db = NewDb();
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("ghost")));
        Assert.Contains("E-WF-025", ex.Message);
    }

    [Fact]
    public async Task Ref_TargetDisabled_E_WF_025()
    {
        using var db = NewDb();
        SeedDef(db, "target", PlainApproval(), enable: false);
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("target")));
        Assert.Contains("E-WF-025", ex.Message);
    }

    [Fact]
    public async Task Ref_SelfReference_E_WF_026()
    {
        using var db = NewDb();
        SeedDef(db, "me", RefSchema("me"));
        await db.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "me", RefSchema("me")));
        Assert.Contains("E-WF-026", ex.Message);
    }

    [Fact]
    public async Task Ref_TwoNodeCycle_E_WF_026()
    {
        using var db = NewDb();
        SeedDef(db, "a", RefSchema("b"));
        SeedDef(db, "b", PlainApproval());   // b 现存版本不引用 a
        await db.SaveChangesAsync();
        // 保存 b 的新 schema 引用 a → a→b→a 成环（校验时刻的当前已发布版口径,spec §3.1）
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db, "b", RefSchema("a")));
        Assert.Contains("E-WF-026", ex.Message);
    }

    [Fact]
    public async Task Ref_ChainDepth8_E_WF_026_Depth7_Ok()
    {
        using var db = NewDb();
        SeedDef(db, "d7", PlainApproval());
        for (int i = 6; i >= 1; i--) SeedDef(db, $"d{i}", RefSchema($"d{i + 1}"));
        await db.SaveChangesAsync();
        SubFlowRefValidator.Validate(db, "d0", RefSchema("d1"));   // 链长 8 节点(d0..d7)=深度 7 引用,放行

        using var db2 = NewDb();
        SeedDef(db2, "d8", PlainApproval());
        for (int i = 7; i >= 1; i--) SeedDef(db2, $"d{i}", RefSchema($"d{i + 1}"));
        await db2.SaveChangesAsync();
        var ex = Assert.Throws<InvalidOperationException>(
            () => SubFlowRefValidator.Validate(db2, "d0", RefSchema("d1")));   // 深度 8 → 拦
        Assert.Contains("E-WF-026", ex.Message);
    }

    // ── DesignerService 接线 ──

    [Fact]
    public async Task DesignerSave_SubFlowGhostTarget_Throws_E_WF_025()
    {
        using var db = NewDb();
        await db.SaveChangesAsync();
        var svc = new DesignerService(db, new FlowDefService(db),
            Array.Empty<IServiceTaskExecutor>(), Array.Empty<IWfConnector>());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(
            new SaveFlowRequest("me", "me", "f", null, null, JsonSerializer.Serialize(RefSchema("ghost"))), "u"));
        Assert.Contains("E-WF-025", ex.Message);
    }
}

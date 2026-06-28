using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

public class ApprovalStagePlannerTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApprovalStagePlanner Planner(CP6Context db) => new(new ApproverResolver(db));

    private static async Task<Guid> SeedUserChainAsync(CP6Context db, int levels)
    {
        // 造 levels 级管理链:u0(发起人) → u1 → ... → u_levels(链顶)
        Guid? mgr = null;
        for (int i = levels; i >= 0; i--)
        {
            var id = Guid.NewGuid();
            db.Sys_Users.Add(new Sys_User { Id = id, UserName = $"u{i}", Password = "x", Enable = true, ManagerId = mgr });
            mgr = id;
        }
        await db.SaveChangesAsync();
        return mgr!.Value;   // 最后赋的 mgr = u0(发起人)
    }

    [Fact]
    public async Task NoStages_YieldsSingleStageFromNodeFields()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var node = new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u, Countersign = "any" };
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = u }, new FlowSchema(), node);
        Assert.Single(plan);
        Assert.Equal(0, plan[0].StageIndex);
        Assert.Equal("any", plan[0].Countersign);
        Assert.Equal(ApproverStrategy.Specified, plan[0].Rule.Strategy);
    }

    [Fact]
    public async Task FixedStages_FlattenInOrderWithIndices()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid(), Countersign = "all" },
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 1, Countersign = "any" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = u }, new FlowSchema(), node);
        Assert.Equal(2, plan.Count);
        Assert.Equal(0, plan[0].StageIndex);
        Assert.Equal(1, plan[1].StageIndex);
        Assert.Equal("any", plan[1].Countersign);
    }

    [Fact]
    public async Task ManagerChain_ExpandsPerLevel_StopsAtChainEnd()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 3);   // 3 级主管
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "managerChain", MaxLevels = 5, Countersign = "all" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(3, plan.Count);   // 链 3 级 → 3 档(MaxLevels=5 未封顶,链断即止)
        Assert.All(plan, s => Assert.Equal(ApproverStrategy.DirectManager, s.Rule.Strategy));
        Assert.Equal(1, plan[0].Rule.Levels);
        Assert.Equal(2, plan[1].Rule.Levels);
        Assert.Equal(3, plan[2].Rule.Levels);
    }

    [Fact]
    public async Task ManagerChain_CapsAtMaxLevels()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 4);
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "managerChain", MaxLevels = 2, Countersign = "all" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(2, plan.Count);   // 封顶 2 档
    }

    [Fact]
    public async Task ManagerChain_ZeroExpansion_EmitsOneUnresolvableStage_NotDropped()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();   // no manager at all
        db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "lonely", Enable = true, ManagerId = null, Password = "x" });
        await db.SaveChangesAsync();
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "managerChain", MaxLevels = 3, Countersign = "all" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Single(plan);   // 不是 0,而是 1 个(激活时不可解析 → Suspend)
        Assert.Equal(ApproverStrategy.DirectManager, plan[0].Rule.Strategy);
    }

    [Fact]
    public async Task MixedFixed_ManagerChain_Fixed_IndicesContiguous()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 2);
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new ApprovalStage { Kind = "managerChain", MaxLevels = 5 },   // 2 级 → 2 档
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 1 },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(4, plan.Count);   // 1 + 2 + 1
        Assert.Equal(ApproverStrategy.Specified, plan[0].Rule.Strategy);
        Assert.Equal(ApproverStrategy.DirectManager, plan[1].Rule.Strategy);
        Assert.Equal(ApproverStrategy.DirectManager, plan[2].Rule.Strategy);
        Assert.Equal(ApproverStrategy.Role, plan[3].Rule.Strategy);   // 关键:最后 fixed 档 StageIndex=3 不被 managerChain 挤位
        Assert.Equal(3, plan[3].StageIndex);
    }
}

using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>OA 章04 待办中心（D-2）。我的待办/我的申请/撤回。复用 FlowEngine 起流程造数据。</summary>
public class TaskCenterServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    /// <summary>flow leave：n1(Specified=approver) → end。</summary>
    private static async Task SeedFlowAsync(CP6Context db, Guid approver)
    {
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    [Fact]
    public async Task MyTodos_ReturnsOnlyCurrentUserPendingTasks()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");

        var svc = new TaskCenterService(db);
        Assert.Single(await svc.MyTodosAsync(approver));            // 审批人有 1 条待办
        Assert.Empty(await svc.MyTodosAsync(Guid.NewGuid()));       // 他人无待办
    }

    [Fact]
    public async Task MyApplications_ByStarter()
    {
        using var db = NewDb();
        await SeedFlowAsync(db, Guid.NewGuid());
        var starter = Guid.NewGuid();
        await Engine(db).SubmitAsync("leave", starter, "{}");

        var svc = new TaskCenterService(db);
        var mine = await svc.MyApplicationsAsync(starter);
        Assert.Single(mine);
        Assert.Equal("leave", mine[0].FlowKey);
        Assert.Empty(await svc.MyApplicationsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Withdraw_Success_SetsWithdrawn_CancelsTasks_AddsHistory()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var starter = Guid.NewGuid();
        var instId = await Engine(db).SubmitAsync("leave", starter, "{}");

        await new TaskCenterService(db).WithdrawAsync(instId, starter);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Withdrawn, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync(t => t.Status == FlowTaskStatus.Pending));   // 在途清空
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "withdraw"));
    }

    [Fact]
    public async Task Withdraw_NotStarter_Throws()
    {
        using var db = NewDb();
        await SeedFlowAsync(db, Guid.NewGuid());
        var instId = await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TaskCenterService(db).WithdrawAsync(instId, Guid.NewGuid()));   // 非发起人
        Assert.Contains("发起人", ex.Message);
    }

    [Fact]
    public async Task Withdraw_NotRunning_Throws()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var starter = Guid.NewGuid();
        var instId = await Engine(db).SubmitAsync("leave", starter, "{}");
        var svc = new TaskCenterService(db);
        await svc.WithdrawAsync(instId, starter);   // 第一次撤回 → Withdrawn

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.WithdrawAsync(instId, starter));   // 已非进行中
        Assert.Contains("进行中", ex.Message);
    }
}

using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class TransferServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task<(Guid inst, Guid task)> SeedAsync(CP6Context db, Guid starter, Guid approver, Guid receiver)
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x" },
            new Sys_User { Id = approver, UserName = "a", NickName = "原审批王", Password = "x" },
            new Sys_User { Id = receiver, UserName = "r", NickName = "受让赵", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var inst = await Engine(db).SubmitAsync("leave", starter, "{}");
        var task = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).Select(t => t.Id).SingleAsync();
        return (inst, task);
    }

    [Fact]
    public async Task Transfer_Reassigns_AndWritesDualFormToRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);

        await Engine(db).TransferAsync(task, approver, receiver, "我出差，转给你");

        // 任务被改派（同一条 task）
        var t = await db.Wf_FlowTasks.SingleAsync(x => x.Id == task);
        Assert.Equal(receiver, t.AssigneeId);
        Assert.Equal(FlowTaskStatus.Pending, t.Status);
        // 履历：原审批人行 Transferred + 受让人新 Pending 行
        var rows = await db.Wf_FlowFormTos.Where(f => f.InstanceId == inst).ToListAsync();
        Assert.Contains(rows, r => r.ExpectedHandlerId == approver && r.Status == FlowFormToStatus.Transferred && r.ActualHandlerId == approver);
        Assert.Contains(rows, r => r.ExpectedHandlerId == receiver && r.Status == FlowFormToStatus.Pending);
    }

    [Fact]
    public async Task Transfer_NotPendingTask_Throws()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);
        await Engine(db).ActAsync(task, approver, approve: true, "先批了");   // 任务已办

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(task, approver, receiver, null));
        Assert.Equal("E-WF-002", ex.Message);
    }

    [Fact]
    public async Task Transfer_TargetNotExist_Throws()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(task, approver, Guid.NewGuid(), null));   // 目标不存在
        Assert.Equal("E-WF-002", ex.Message);
    }
}

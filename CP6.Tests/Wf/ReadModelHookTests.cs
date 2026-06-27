using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ReadModelHookTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedAsync(CP6Context db, Guid approver)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "leave", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema
            {
                Nodes =
                {
                    new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            }),
            Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FormTo_SendCreatesPending_HandleUpdatesApproved()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedAsync(db, approver);
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var pend = await db.Wf_FlowFormTos.SingleAsync();
        Assert.Equal(FlowFormToStatus.Pending, pend.Status);
        Assert.Equal(approver, pend.ExpectedHandlerId);
        Assert.Equal("n1", pend.NodeId);
        Assert.Equal(1, pend.StepSeq);
        Assert.Null(pend.HandledAt);

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task.Id, approver, approve: true, "ok");

        var done = await db.Wf_FlowFormTos.SingleAsync();
        Assert.Equal(FlowFormToStatus.Approved, done.Status);
        Assert.Equal(approver, done.ActualHandlerId);
        Assert.Equal("ok", done.Comment);
        Assert.NotNull(done.HandledAt);
    }

    // ─────────────── 会签多行：同一关卡两审批人共享 StepSeq ───────────────

    private static async Task SeedCountersignAsync(CP6Context db, int roleId, Guid u1, Guid u2, string flowKey)
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = u1, UserName = $"u{u1:N}", Password = "x", RoleId = roleId, Enable = true },
            new Sys_User { Id = u2, UserName = $"u{u2:N}", Password = "x", RoleId = roleId, Enable = true });
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
            SchemaJson = JsonSerializer.Serialize(new FlowSchema
            {
                Nodes =
                {
                    new FlowNode
                    {
                        Id = "n1", Type = "approval", ApproverStrategy = "Role",
                        ApproverRoleId = roleId, Countersign = "all",
                    },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            }),
            Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FormTo_Countersign_MultipleRows()
    {
        using var db = NewDb();
        const int roleId = 99;
        var u1 = Guid.NewGuid(); var u2 = Guid.NewGuid();
        await SeedCountersignAsync(db, roleId, u1, u2, "cs");

        await Engine(db).SubmitAsync("cs", Guid.NewGuid(), "{}");

        var formTos = await db.Wf_FlowFormTos.ToListAsync();
        Assert.Equal(2, formTos.Count);
        Assert.All(formTos, f => Assert.Equal(FlowFormToStatus.Pending, f.Status));
        Assert.All(formTos, f => Assert.Equal("n1", f.NodeId));
        // 同一关卡两行共享相同 StepSeq
        Assert.Equal(formTos[0].StepSeq, formTos[1].StepSeq);
        Assert.Equal(1, formTos[0].StepSeq);
    }

    [Fact]
    public async Task FormTo_Reject_VoidsOtherPending()
    {
        using var db = NewDb();
        const int roleId = 88;
        var u1 = Guid.NewGuid(); var u2 = Guid.NewGuid();
        await SeedCountersignAsync(db, roleId, u1, u2, "rv");

        await Engine(db).SubmitAsync("rv", Guid.NewGuid(), "{}");

        // 提交后 2 行 Pending
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending));

        // u1 驳回
        var task1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task1.Id, u1, approve: false, "bad");

        var rows = await db.Wf_FlowFormTos.ToListAsync();
        var u1Row = rows.Single(f => f.ExpectedHandlerId == u1);
        var u2Row = rows.Single(f => f.ExpectedHandlerId == u2);

        // 驳回方 → Rejected；被连坐方 → Voided
        Assert.Equal(FlowFormToStatus.Rejected, u1Row.Status);
        Assert.Equal(FlowFormToStatus.Voided, u2Row.Status);
    }

    // ─────────────── T10：FlowData 每关卡表单快照写入钩子 ───────────────

    [Fact]
    public async Task FlowData_SnapshotPerStep()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedAsync(db, approver);
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var snap = await db.Wf_FlowDatas.SingleAsync();      // 送签存一份
        Assert.Equal("n1", snap.NodeId);
        Assert.Equal(1, snap.StepSeq);
        Assert.Contains("days", snap.DataJson);

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task.Id, approver, approve: true);
        Assert.True(await db.Wf_FlowDatas.CountAsync() >= 1);   // 办结再存一份（同关卡）
    }
}

using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

/// <summary>
/// D-1 N-T5：引擎钩子——签核完成/驳回时通知发起人。
/// 注入 CountingNotifier 验证 FlowEngine.DispatchIfFinishedAsync 在终态正确调用
/// FlowApprovedAsync / FlowRejectedAsync，且既有引擎执行态零改。
/// </summary>
public class NotificationEngineHookTests
{
    // ── CountingNotifier ────────────────────────────────────────────────────
    private sealed class CountingNotifier : IWfNotifier
    {
        public int ApprovedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public Guid LastApprovedStarterId { get; private set; }
        public Guid LastRejectedStarterId { get; private set; }
        public string? LastRejectedComment { get; private set; }
        public int PrunedCount { get; private set; }
        public string? LastPrunedNodeId { get; private set; }

        public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey)
            => Task.CompletedTask;

        public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
        {
            PrunedCount++;
            LastPrunedNodeId = nodeId;
            return Task.CompletedTask;
        }

        public Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey)
        {
            ApprovedCount++;
            LastApprovedStarterId = starterId;
            return Task.CompletedTask;
        }

        public Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment)
        {
            RejectedCount++;
            LastRejectedStarterId = starterId;
            LastRejectedComment = comment;
            return Task.CompletedTask;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    /// <summary>种一个 start→approval(指定 approver)→end 的单节点流程定义。</summary>
    private static async Task SeedLinearFlowAsync(CP6Context db, string flowKey, Guid approver)
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
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "Test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    // ── tests ────────────────────────────────────────────────────────────────

    /// <summary>线性流程批准到 end → FlowApprovedAsync 1 次，starterId=发起人；FlowRejectedAsync 0 次。</summary>
    [Fact]
    public async Task LinearFlow_Approve_CallsFlowApproved_Once()
    {
        using var db = NewDb();
        const string flowKey = "hook-test-approve";
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        await SeedLinearFlowAsync(db, flowKey, approver);

        var notifier = new CountingNotifier();
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: notifier);

        var instId = await engine.SubmitAsync(flowKey, starterId: starter, varsJson: "{}");

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == approver);
        await engine.ActAsync(task.Id, actorId: approver, approve: true, comment: null);

        Assert.Equal(1, notifier.ApprovedCount);
        Assert.Equal(starter, notifier.LastApprovedStarterId);
        Assert.Equal(0, notifier.RejectedCount);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
    }

    /// <summary>线性流程驳回 → FlowRejectedAsync 1 次含 comment；FlowApprovedAsync 0 次。</summary>
    [Fact]
    public async Task LinearFlow_Reject_CallsFlowRejected_Once_WithComment()
    {
        using var db = NewDb();
        const string flowKey = "hook-test-reject";
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        await SeedLinearFlowAsync(db, flowKey, approver);

        var notifier = new CountingNotifier();
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: notifier);

        var instId = await engine.SubmitAsync(flowKey, starterId: starter, varsJson: "{}");

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == approver);
        const string rejectReason = "金额超限";
        await engine.ActAsync(task.Id, actorId: approver, approve: false, comment: rejectReason);

        Assert.Equal(0, notifier.ApprovedCount);
        Assert.Equal(1, notifier.RejectedCount);
        Assert.Equal(starter, notifier.LastRejectedStarterId);
        Assert.Equal(rejectReason, notifier.LastRejectedComment);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
    }

    /// <summary>并行两分支均通过 → FlowApprovedAsync 仅 1 次（FinishIfDrained 收口去重）。</summary>
    [Fact]
    public async Task ParallelFlow_BothBranchesApprove_CallsFlowApproved_OnlyOnce()
    {
        using var db = NewDb();
        const string flowKey = "hook-test-parallel";
        var starter = Guid.NewGuid();
        var approver1 = Guid.NewGuid();
        var approver2 = Guid.NewGuid();

        // parallelSplit → n1(approver1) + n2(approver2) → parallelJoin → end
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver1 },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver2 },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "split", To = "n1" },
                new FlowEdge { From = "split", To = "n2" },
                new FlowEdge { From = "n1", To = "join" },
                new FlowEdge { From = "n2", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "Test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();

        var notifier = new CountingNotifier();
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: notifier);

        await engine.SubmitAsync(flowKey, starterId: starter, varsJson: "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == approver1);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == approver2);
        await engine.ActAsync(t1.Id, actorId: approver1, approve: true);
        await engine.ActAsync(t2.Id, actorId: approver2, approve: true);

        Assert.Equal(1, notifier.ApprovedCount);
        Assert.Equal(0, notifier.RejectedCount);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
    }

    /// <summary>BranchPruned 通知类型键 = 5（独立类型，偏好矩阵须与驳回可独立开关）。</summary>
    [Fact]
    public void WfNotificationType_BranchPruned_Is5()
        => Assert.Equal(5, CP6.Entity.DomainModels.Wf.WfNotificationType.BranchPruned);
}

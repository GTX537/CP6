using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>入队-复核两段式接线（spec §3.2 D5）：第一段凭据落库形态 / fast path 同请求收敛 /
/// worker 内部 Kind 短路兜底 / 手工撤回入计票。InMemory 单线程面；竞态面在 SubFlowConcurrencyTests。</summary>
public class SubFlowTwoPhaseTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task ChildTerminal_JobPersisted_KindTokenNodePayload()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2]}");
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
        var c0 = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid && i.SubIndex == 0);

        var t0 = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == c0.Id && x.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(t0.Id, ca, approve: true);   // c0 终态（组未齐,父不动）

        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Kind == WfJobKind.SubFlowResume);
        Assert.Equal(c0.Id, job.TokenId);                          // ★ 防撞定案：TokenId=子实例 Id
        Assert.Equal(SubFlowResume.JobNodeId, job.NodeId);         // 哨兵
        Assert.Equal(pid, job.InstanceId);                         // 归父实例
        var payload = SubFlowResumePayload.Parse(job.ActionRefJson);
        Assert.NotNull(payload);
        Assert.Equal(parked.Id, payload!.ParentTokenId);
        Assert.Equal(ServiceJobStatus.Succeeded, job.Status);      // fast path 已消费凭据（组未齐也算消费）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task ApproveLastChild_FastPath_ResumesWithinRequest()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2]}");

        foreach (var c in await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync())
        {
            var t = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == c.Id && x.Status == FlowTaskStatus.Pending);
            await eng.ActAsync(t.Id, ca, approve: true);   // 无任何手动 Check——fast path 自动收敛
        }

        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
    }

    [Fact]
    public async Task InstantTerminalChild_SubmitFastPath_ParentAdvancesImmediately()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid();
        SeedDef(db, "instant", InstantChildSchema());
        SeedDef(db, "parent", ParentSchema(pa, "instant"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{}");   // 子起即 Approved → SubmitAsync 尾 fast path

        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(FlowInstanceStatus.Approved,
            (await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid)).Status);
    }

    [Fact]
    public async Task ManualChildWithdraw_NullEngine_JobPending_WorkerInterceptDisposes()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);

        // engine=null 的既有构造 → 无 fast path（=「fast path 前崩溃」行为等价面）：凭据必须已落库
        await new TaskCenterService(db).WithdrawAsync(child.Id, child.StarterId);
        var job = await db.Wf_ServiceJobs.SingleAsync(j => j.Kind == WfJobKind.SubFlowResume);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 尚未处置

        // worker 兜底：内部 Kind 短路 → 复核 → all 策略 Withdrawn=死 → 错误处置（手工撤回入计票,spec §3.3 末条）
        var svc = new WfServiceJobService(db, eng, Array.Empty<IServiceTaskExecutor>());
        var n = await svc.ScanOnceAsync(DateTime.UtcNow, "w1");
        Assert.Equal(1, n);

        Assert.Equal(ServiceJobStatus.Succeeded, (await db.Wf_ServiceJobs.SingleAsync(j => j.Id == job.Id)).Status);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task GrandChild_NestedResume_PropagatesTwoLevels()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid(), pa = Guid.NewGuid();
        SeedDef(db, "leaf", ChildSchema(ca));
        SeedDef(db, "mid", ParentSchema(pa, "leaf"));      // mid 的 pa 审批在 leaf 恢复后出现
        SeedDef(db, "top", ParentSchema(pa, "mid"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var topId = await eng.SubmitAsync("top", Guid.NewGuid(), "{}");
        var midInst = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == topId);
        var leafInst = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == midInst.Id);

        // leaf 审批过 → mid 恢复到 pa；mid 的 pa 过 → mid Approved → top 恢复（孙 subFlow 递归,全靠 fast path 链）
        var tLeaf = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == leafInst.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tLeaf.Id, ca, true);
        var tMid = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == midInst.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tMid.Id, pa, true);

        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == midInst.Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == topId && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }
}

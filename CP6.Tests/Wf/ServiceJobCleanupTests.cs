using System;
using System.Linq;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>B-T3：撤回/驳回时清理 Pending 服务任务 job（P0-5 入队侧）。
/// 撤回路径（TaskCenterService.WithdrawAsync）和驳回路径（FlowEngine.CancelAllActiveTokens）均须把
/// 该实例 Status==Pending 的 Wf_ServiceJob 标 Cancelled；Running job 不强杀（由 B-T1 状态闸处理）。</summary>
public class ServiceJobCleanupTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static Wf_FlowInstance MakeRunningInst(Guid starter) => new()
    {
        Id = Guid.NewGuid(), FlowKey = "svc", StarterId = starter,
        Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = "{}",
    };

    private static Wf_ServiceJob MakeJob(Guid instanceId, int status = ServiceJobStatus.Pending) => new()
    {
        Id = Guid.NewGuid(), InstanceId = instanceId, TokenId = Guid.NewGuid(), NodeId = "svc",
        Kind = ServiceKind.DataWriteback, Status = status,
        AttemptCount = 0, MaxAttempts = 4,
        DueAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow,
        CreateDate = DateTime.UtcNow,
    };

    // ── 撤回路径（TaskCenterService.WithdrawAsync） ──────────────────────────────

    [Fact]
    public async Task Withdraw_CancelsPendingServiceJobs()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var inst = MakeRunningInst(starter);
        db.Wf_FlowInstances.Add(inst);
        var job = MakeJob(inst.Id, ServiceJobStatus.Pending);
        db.Wf_ServiceJobs.Add(job);
        await db.SaveChangesAsync();

        await new TaskCenterService(db).WithdrawAsync(inst.Id, starter);

        var j = await db.Wf_ServiceJobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(ServiceJobStatus.Cancelled, j.Status);
        Assert.NotNull(j.CompletedAtUtc);
    }

    [Fact]
    public async Task Withdraw_DoesNotTouchOtherInstancesJobs()
    {
        using var db = NewDb();
        var starter1 = Guid.NewGuid();
        var inst1 = MakeRunningInst(starter1);
        db.Wf_FlowInstances.Add(inst1);
        var job1 = MakeJob(inst1.Id, ServiceJobStatus.Pending);
        db.Wf_ServiceJobs.Add(job1);

        // different instance (not withdrawn)
        var inst2 = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "svc", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = "{}",
        };
        db.Wf_FlowInstances.Add(inst2);
        var job2 = MakeJob(inst2.Id, ServiceJobStatus.Pending);
        db.Wf_ServiceJobs.Add(job2);
        await db.SaveChangesAsync();

        await new TaskCenterService(db).WithdrawAsync(inst1.Id, starter1);

        // inst1's job → Cancelled
        var j1 = await db.Wf_ServiceJobs.SingleAsync(x => x.Id == job1.Id);
        Assert.Equal(ServiceJobStatus.Cancelled, j1.Status);

        // inst2's job → untouched (still Pending)
        var j2 = await db.Wf_ServiceJobs.SingleAsync(x => x.Id == job2.Id);
        Assert.Equal(ServiceJobStatus.Pending, j2.Status);
        Assert.Null(j2.CompletedAtUtc);
    }

    [Fact]
    public async Task Withdraw_DoesNotTouchRunningJob()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var inst = MakeRunningInst(starter);
        db.Wf_FlowInstances.Add(inst);

        var pendingJob = MakeJob(inst.Id, ServiceJobStatus.Pending);
        var runningJob = new Wf_ServiceJob
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = Guid.NewGuid(), NodeId = "svc2",
            Kind = ServiceKind.WebApi, Status = ServiceJobStatus.Running,
            AttemptCount = 1, MaxAttempts = 4,
            DueAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow,
            LockedBy = "worker1", LockedAtUtc = DateTime.UtcNow,
            LockExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreateDate = DateTime.UtcNow,
        };
        db.Wf_ServiceJobs.AddRange(pendingJob, runningJob);
        await db.SaveChangesAsync();

        await new TaskCenterService(db).WithdrawAsync(inst.Id, starter);

        var pj = await db.Wf_ServiceJobs.SingleAsync(x => x.Id == pendingJob.Id);
        Assert.Equal(ServiceJobStatus.Cancelled, pj.Status);   // Pending → Cancelled ✓

        var rj = await db.Wf_ServiceJobs.SingleAsync(x => x.Id == runningJob.Id);
        Assert.Equal(ServiceJobStatus.Running, rj.Status);     // Running → unchanged ✓
    }
}

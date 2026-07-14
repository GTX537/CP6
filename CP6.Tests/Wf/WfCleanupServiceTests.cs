using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

/// <summary>C-T1：终态 job/流水清理服务（保留期 180 天硬删终态、在途永不清、占坑永不清、老化占坑仅告警计数）。
/// 时间全部注入 <c>nowUtc</c>（无 wall-clock 依赖）；SQLite in-memory 共享连接基座。</summary>
public class WfCleanupServiceTests
{
    private static Wf_ServiceJob Job(int status, DateTime? completedUtc)
        => new()
        {
            Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(), NodeId = "n",
            Kind = "webApi", Status = status, DueAtUtc = DateTime.UtcNow.AddDays(-300),
            NextAttemptAtUtc = DateTime.UtcNow.AddDays(-300), CompletedAtUtc = completedUtc,
        };

    private static Wf_TriggerFire Fire(DateTime firedUtc, Guid? instanceId, string? error)
        => new()
        {
            Id = Guid.NewGuid(), TriggerId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("N"),
            FiredUtc = firedUtc, InstanceId = instanceId, Error = error, Source = 2,
        };

    private static WfCleanupService Svc(Microsoft.Data.Sqlite.SqliteConnection conn, int retentionDays = 180, int staleDays = 7)
        => new(Ctx(conn), new WfsInfraOptions { CleanupRetentionDays = retentionDays, StaleReservationAlertDays = staleDays });

    // ── Wf_ServiceJob 终态删 / 在途留 ────────────────────────────────────────────

    [Fact]
    public async Task Cleanup_DeletesTerminalOlderThanRetention_KeepsRunningAndRecent()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            db.Wf_ServiceJobs.AddRange(
                Job(ServiceJobStatus.Succeeded, now.AddDays(-200)),   // 删（终态+超龄）
                Job(ServiceJobStatus.Failed, now.AddDays(-181)),      // 删
                Job(ServiceJobStatus.Cancelled, now.AddDays(-181)),   // 删
                Job(ServiceJobStatus.Succeeded, now.AddDays(-10)),    // 留（终态但未超龄）
                Job(ServiceJobStatus.Running, now.AddDays(-300)),     // 留（非终态，在途）
                Job(ServiceJobStatus.Pending, now.AddDays(-300)));    // 留（非终态）
            await db.SaveChangesAsync();
        }
        int deleted = (await Svc(conn).CleanupOnceAsync(now, CancellationToken.None)).ServiceJobsDeleted;
        Assert.Equal(3, deleted);
        using var check = Ctx(conn);
        Assert.Equal(3, await check.Wf_ServiceJobs.CountAsync());
    }

    [Fact]
    public async Task Cleanup_RetentionZero_Disabled_NothingDeleted()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn)) { db.Wf_ServiceJobs.Add(Job(ServiceJobStatus.Succeeded, now.AddDays(-500))); await db.SaveChangesAsync(); }
        var r = await Svc(conn, retentionDays: 0).CleanupOnceAsync(now, CancellationToken.None);
        Assert.Equal(0, r.ServiceJobsDeleted);
        using var check = Ctx(conn);
        Assert.Equal(1, await check.Wf_ServiceJobs.CountAsync());
    }

    [Fact]
    public async Task Cleanup_Batches_DeletesAllOverMultiplePasses()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            for (int i = 0; i < 1200; i++) db.Wf_ServiceJobs.Add(Job(ServiceJobStatus.Succeeded, now.AddDays(-300)));
            await db.SaveChangesAsync();
        }
        var r = await Svc(conn).CleanupOnceAsync(now, CancellationToken.None);   // 内部每批 500，多批删尽
        Assert.Equal(1200, r.ServiceJobsDeleted);
        using var check = Ctx(conn);
        Assert.Equal(0, await check.Wf_ServiceJobs.CountAsync());
    }

    // ── Wf_TriggerFire 终态删 / 占坑永不清 / 老化告警计数（波③已并 main） ─────────

    [Fact]
    public async Task Cleanup_DeletesTerminalTriggerFires_KeepsPlaceholdersForever()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            db.Wf_TriggerFires.AddRange(
                Fire(now.AddDays(-200), Guid.NewGuid(), null),        // 删（成功起单+超龄）
                Fire(now.AddDays(-181), null, "E-WF-xxx 失败"),        // 删（失败+超龄）
                Fire(now.AddDays(-200), null, null),                  // 留（占坑：两者皆 null，永不清）
                Fire(now.AddDays(-10), Guid.NewGuid(), null));        // 留（终态但未超龄）
            await db.SaveChangesAsync();
        }
        var r = await Svc(conn).CleanupOnceAsync(now, CancellationToken.None);
        Assert.Equal(2, r.TriggerFiresDeleted);
        using var check = Ctx(conn);
        Assert.Equal(2, await check.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task Cleanup_CountsStalePlaceholders_WithoutDeleting()
    {
        using var conn = NewSqliteWithSchema();
        var now = DateTime.UtcNow;
        using (var db = Ctx(conn))
        {
            db.Wf_TriggerFires.AddRange(
                Fire(now.AddDays(-8), null, null),                    // 老化占坑（> 7 天）→ 计数，不删
                Fire(now.AddDays(-30), null, null),                   // 老化占坑 → 计数，不删
                Fire(now.AddHours(-1), null, null),                   // 新鲜占坑（< 7 天）→ 不计
                Fire(now.AddDays(-30), Guid.NewGuid(), null));        // 非占坑（已起单）→ 不计
            await db.SaveChangesAsync();
        }
        var r = await Svc(conn).CleanupOnceAsync(now, CancellationToken.None);
        Assert.Equal(2, r.StaleReservationCount);
        Assert.Equal(0, r.TriggerFiresDeleted);   // 占坑永不清；未超龄的非占坑也没到保留期
        using var check = Ctx(conn);
        Assert.Equal(4, await check.Wf_TriggerFires.CountAsync());
    }
}

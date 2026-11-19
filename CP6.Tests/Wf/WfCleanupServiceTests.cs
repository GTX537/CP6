using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task WfsRetentionContract_approved_policy_preserves_180_day_anchor_and_existing_counts()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        var source = RemainderSource();
        await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        await RemainderPolicy(h, source, "WFS_TRIGGER_FIRE", 180);
        var now = DateTime.UtcNow;
        var old = now.AddDays(-200);
        var active = RemainderJob(old, ServiceJobStatus.Running);
        var recent = RemainderJob(now.AddDays(-10), ServiceJobStatus.Succeeded);
        var anchorMissing = RemainderJob(old, ServiceJobStatus.Cancelled);
        anchorMissing.CompletedAtUtc = null;
        var oldReservation = RemainderFire(old, null, null);
        var freshReservation = RemainderFire(now.AddHours(-1), null, null);
        var terminalAtBoundary = RemainderFire(now.AddDays(-180), Guid.NewGuid(), null);
        h.Db.Wf_ServiceJobs.AddRange(active, recent, anchorMissing,
            RemainderJob(old, ServiceJobStatus.Failed), RemainderJob(old, ServiceJobStatus.Cancelled));
        h.Db.Wf_TriggerFires.AddRange(oldReservation, freshReservation, terminalAtBoundary,
            RemainderFire(old, Guid.NewGuid(), null), RemainderFire(old, null, "SIMULATED-FAILURE"));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var disabled = await new WfCleanupService(h.Db, new WfsInfraOptions { CleanupRetentionDays = 0 }, source)
            .CleanupOnceAsync(now, default);
        Assert.Equal((0, 0, 0), (disabled.ServiceJobsDeleted, disabled.TriggerFiresDeleted, disabled.StaleReservationCount));
        Assert.Equal(5, await h.Db.Wf_ServiceJobs.CountAsync());
        Assert.Equal(5, await h.Db.Wf_TriggerFires.CountAsync());

        var owner = new WfCleanupService(h.Db, new WfsInfraOptions(), source);
        var result = await owner.CleanupOnceAsync(now, default);
        Assert.Equal((2, 2, 1), (result.ServiceJobsDeleted, result.TriggerFiresDeleted, result.StaleReservationCount));
        Assert.Equal(new[] { active.Id, recent.Id, anchorMissing.Id }.Order(),
            (await h.Db.Wf_ServiceJobs.Select(j => j.Id).ToArrayAsync()).Order());
        Assert.Equal(new[] { oldReservation.Id, freshReservation.Id, terminalAtBoundary.Id }.Order(),
            (await h.Db.Wf_TriggerFires.Select(f => f.Id).ToArrayAsync()).Order());

        var repeat = await owner.CleanupOnceAsync(now, default);
        Assert.Equal((0, 0, 1), (repeat.ServiceJobsDeleted, repeat.TriggerFiresDeleted, repeat.StaleReservationCount));
        Assert.Equal("COMPLETED_NO_CHANGES", repeat.ServiceJobsPlan!.State);
        Assert.Equal("COMPLETED_NO_CHANGES", repeat.TriggerFiresPlan!.State);

        var afterBoundary = await owner.CleanupOnceAsync(now.AddSeconds(1), default);
        Assert.Equal(1, afterBoundary.TriggerFiresDeleted);
        Assert.False(await h.Db.Wf_TriggerFires.AnyAsync(f => f.Id == terminalAtBoundary.Id));
    }

    [CurrentPermissionPostgresFact]
    public async Task WfsRetentionContract_real_batches_preserve_original_cursor_for_1200_rows()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        var now = DateTime.UtcNow;
        var source = RemainderSource();
        await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        h.Db.Wf_ServiceJobs.AddRange(Enumerable.Range(0, 1200)
            .Select(_ => RemainderJob(now.AddDays(-300), ServiceJobStatus.Succeeded)));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await new WfCleanupService(h.Db, new WfsInfraOptions(), source).CleanupOnceAsync(now, default);
        Assert.Equal(1200, result.ServiceJobsDeleted);
        Assert.Equal(new[] { 0, 500, 1000 }, result.ServiceJobsPlan!.Shards.Select(s => s.Cursor));
        Assert.Equal(new[] { 500, 1000, 1200 }, result.ServiceJobsPlan.Shards.Select(s => s.NextCursor));
        Assert.False(await h.Db.Wf_ServiceJobs.AnyAsync());
    }
}

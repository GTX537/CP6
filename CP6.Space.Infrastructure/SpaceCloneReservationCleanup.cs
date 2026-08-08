using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

internal static class SpaceCloneReservationCleanup
{
    public static async Task ReleaseIfTerminalAsync(
        SpaceContext context,
        SpaceJob job,
        CancellationToken cancellationToken)
    {
        if (job.JobType != SpaceJobType.CloneVersion ||
            job.SubjectType != SpaceJobSubjectType.ModelVersion ||
            !job.IsTerminal ||
            job.Status == SpaceJobStatus.Succeeded)
        {
            return;
        }

        var target = await context.Versions.SingleOrDefaultAsync(
            version => version.Id == job.SubjectId,
            cancellationToken);
        if (target?.Status != SpaceVersionStatus.Initializing)
            return;

        if (job.Status == SpaceJobStatus.Cancelled)
            target.AbandonInitialization();
        else
            target.FailInitialization();

        var model = await context.Models.SingleAsync(
            candidate => candidate.Id == target.ModelId,
            cancellationToken);
        if (model.ActiveDraftVersionId == target.Id)
            model.ReleaseFailedClone(target);
    }

    public static async Task ReleaseTrackedTerminalAsync(
        SpaceContext context,
        CancellationToken cancellationToken)
    {
        var jobs = context.ChangeTracker
            .Entries<SpaceJob>()
            .Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified &&
                entry.Entity.IsTerminal)
            .Select(entry => entry.Entity)
            .ToArray();

        foreach (var job in jobs)
            await ReleaseIfTerminalAsync(context, job, cancellationToken);
    }
}

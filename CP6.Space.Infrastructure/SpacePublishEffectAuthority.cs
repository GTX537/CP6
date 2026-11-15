using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

internal sealed class SpacePublishEffectAuthority(SpaceJob job, SpaceJobLease lease, DateTime? expiresAt)
{
    internal const string TenantInactiveCode = "SPACE_PUBLISH_TENANT_INACTIVE";
    internal const string TenantInactiveSummary = "The tenant is paused or expired. The existing publish Job can retry when it is active.";

    internal static async Task<IDbContextTransaction> BeginAsync(DbContext owner, Guid tenantId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (owner is CP6Context core)
                {
                    await AuthenticationCompletionGateV1.AcquireAsync(core, cancellationToken);
                }
                else
                {
                    var tenant = new TenantContext { CurrentTenantId = tenantId };
                    await using var shared = await DatabaseSharedContext.CreateAsync<CP6Context>(owner,
                        DatabaseContextKind.Core, options => new(options, tenant), cancellationToken);
                    await AuthenticationCompletionGateV1.AcquireAsync(shared, cancellationToken);
                }
                return transaction;
            }
            catch (Exception error) when (attempt < 3 && DatabaseFailureClassifier.Classify(error).CanRetryTransaction)
            {
                // The Main root invalidates a stale Serializable snapshot before any dependent write.
                await transaction.DisposeAsync();
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }
    }

    internal static async Task<SpacePublishEffectAuthority> RequireAsync(CP6Context core, SpaceContext space,
        SpaceJobLease lease, Guid publishAttemptId, CancellationToken cancellationToken)
    {
        if (lease.TenantId != core.CurrentTenantId || lease.SubjectType != SpaceJobSubjectType.PublishAttempt ||
            lease.SubjectId != publishAttemptId || lease.JobType is not (SpaceJobType.Publish or SpaceJobType.Reconcile))
        {
            throw LeaseLost();
        }

        // Lock the lease across the business write. A heartbeat updates the same Job,
        // so use the persisted expiry rather than the original execution envelope.
        var job = await space.Jobs.FromSqlInterpolated($"""
            SELECT j.* FROM public."Space_Job" AS j
            WHERE j."TenantId" = {lease.TenantId} AND j."Id" = {lease.JobId}
            FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (job is null || job.JobType != lease.JobType || job.SubjectType != lease.SubjectType ||
            job.SubjectId != publishAttemptId || job.AttemptCount != lease.AttemptNo || job.InputHash != lease.InputHash)
        {
            throw LeaseLost();
        }

        var tenant = await core.Sys_Tenants.AsNoTracking()
            .Where(row => row.Id == lease.TenantId)
            .Select(row => new { row.Enable, row.ExpireDate })
            .SingleOrDefaultAsync(cancellationToken);
        var authority = new SpacePublishEffectAuthority(job, lease, tenant?.ExpireDate);
        await authority.ConfirmAsync(space, cancellationToken);
        if (tenant is not { Enable: true })
            throw TenantInactive();
        return authority;
    }

    internal async Task ConfirmAsync(DbContext owner, CancellationToken cancellationToken)
    {
        job.EnsureLease(lease.AttemptId, lease.WorkerId, await DatabaseUtcClock.ReadUtcNowAsync(owner, cancellationToken));
        if (job.CancellationRequestedAtUtc.HasValue)
            throw LeaseLost();
        if (expiresAt <= DateTime.Now)
            throw TenantInactive();
    }

    private static SpaceJobProcessingException TenantInactive() =>
        new(SpaceJobFailureKind.Transient, TenantInactiveCode, TenantInactiveSummary);

    private static SpaceJobLeaseLostException LeaseLost() =>
        new("The worker no longer owns the current publish Job lease.");
}

using System.Data;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceAiRetentionStore(
    SpaceContext context,
    ISpaceClock clock) : ISpaceAiRetentionStore
{
    public async Task<SpaceAiRetentionCleanupResult> PurgeAsync(
        Guid tenantId,
        SpaceAiRetentionJobPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var now = clock.UtcNow;
        RequireScope(tenantId, now);
        payload.Validate(now);

        context.ChangeTracker.Clear();
        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            await AcquireTenantLockAsync(tenantId, cancellationToken);

            var candidateRunIds = await (
                    from run in context.GenerationRuns
                    join version in context.Versions
                        on new { run.TenantId, Id = run.ModelVersionId }
                        equals new { version.TenantId, version.Id }
                    where !run.IsCurrent &&
                          !run.PayloadPurgedAtUtc.HasValue &&
                          run.CreatedAtUtc <= payload.RunPayloadCutoffUtc &&
                          (!run.RetentionHoldUntilUtc.HasValue ||
                           run.RetentionHoldUntilUtc <= payload.WindowEndUtc) &&
                          (run.Status == SpaceGenerationRunStatus.Succeeded ||
                           run.Status == SpaceGenerationRunStatus.Failed ||
                           run.Status == SpaceGenerationRunStatus.Stale ||
                           run.Status == SpaceGenerationRunStatus.Cancelled) &&
                          (version.Status == SpaceVersionStatus.Draft ||
                           version.Status == SpaceVersionStatus.Failed ||
                           version.Status == SpaceVersionStatus.Abandoned)
                    orderby run.CreatedAtUtc, run.Id
                    select run.Id)
                .Take(payload.BatchSize)
                .ToListAsync(cancellationToken);

            var proposalPayloadsPurged = 0;
            var diagnosticPayloadsPurged = 0;
            var stagingRowsDeleted = 0;
            var runPayloadsPurged = 0;
            if (candidateRunIds.Count > 0)
            {
                var proposals = await context.GenerationProposals
                    .Where(proposal =>
                        candidateRunIds.Contains(proposal.RunId) &&
                        !proposal.PayloadPurgedAtUtc.HasValue)
                    .OrderBy(proposal => proposal.RunId)
                    .ThenBy(proposal => proposal.Id)
                    .Take(payload.BatchSize)
                    .ToListAsync(cancellationToken);
                foreach (var proposal in proposals)
                {
                    if (proposal.PurgeRetainedPayload(now))
                        proposalPayloadsPurged++;
                }

                var issues = await context.Issues
                    .Where(issue =>
                        issue.GenerationRunId.HasValue &&
                        candidateRunIds.Contains(issue.GenerationRunId.Value) &&
                        !issue.PayloadPurgedAtUtc.HasValue)
                    .OrderBy(issue => issue.GenerationRunId)
                    .ThenBy(issue => issue.Id)
                    .Take(payload.BatchSize)
                    .ToListAsync(cancellationToken);
                foreach (var issue in issues)
                {
                    if (issue.PurgeRetainedPayload(now))
                        diagnosticPayloadsPurged++;
                }

                var staging = await context.GenerationStagingElements
                    .Where(element => candidateRunIds.Contains(element.RunId))
                    .OrderBy(element => element.RunId)
                    .ThenBy(element => element.SequenceNo)
                    .Take(payload.BatchSize)
                    .ToListAsync(cancellationToken);
                foreach (var element in staging)
                {
                    if (element.RetireForRetention())
                        stagingRowsDeleted++;
                }
                await context.SaveChangesAsync(cancellationToken);

                var remainingRunIds = new HashSet<Guid>(
                    await context.GenerationProposals
                        .Where(proposal =>
                            candidateRunIds.Contains(proposal.RunId) &&
                            !proposal.PayloadPurgedAtUtc.HasValue)
                        .Select(proposal => proposal.RunId)
                        .Distinct()
                        .ToListAsync(cancellationToken));
                remainingRunIds.UnionWith(
                    await context.Issues
                        .Where(issue =>
                            issue.GenerationRunId.HasValue &&
                            candidateRunIds.Contains(issue.GenerationRunId.Value) &&
                            !issue.PayloadPurgedAtUtc.HasValue)
                        .Select(issue => issue.GenerationRunId!.Value)
                        .Distinct()
                        .ToListAsync(cancellationToken));
                remainingRunIds.UnionWith(
                    await context.GenerationStagingElements
                        .Where(element =>
                            candidateRunIds.Contains(element.RunId))
                        .Select(element => element.RunId)
                        .Distinct()
                        .ToListAsync(cancellationToken));

                var completedRuns = await context.GenerationRuns
                    .Where(run =>
                        candidateRunIds.Contains(run.Id) &&
                        !remainingRunIds.Contains(run.Id))
                    .ToListAsync(cancellationToken);
                foreach (var run in completedRuns)
                {
                    if (run.PurgeRetainedPayload(now))
                        runPayloadsPurged++;
                }
            }

            var usages = await context.AiUsageRecords
                .Where(usage =>
                    !usage.ArchivedAtUtc.HasValue &&
                    usage.RecordedAtUtc <= payload.UsageArchiveCutoffUtc &&
                    context.GenerationRuns.Any(run =>
                        run.Id == usage.RunId &&
                        (!run.RetentionHoldUntilUtc.HasValue ||
                         run.RetentionHoldUntilUtc <= payload.WindowEndUtc)))
                .OrderBy(usage => usage.RecordedAtUtc)
                .ThenBy(usage => usage.Id)
                .Take(payload.BatchSize)
                .ToListAsync(cancellationToken);
            var usageRowsArchived = 0;
            foreach (var usage in usages)
            {
                if (usage.ArchiveForRetention(now))
                    usageRowsArchived++;
            }

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new SpaceAiRetentionCleanupResult(
                candidateRunIds.Count,
                runPayloadsPurged,
                proposalPayloadsPurged,
                diagnosticPayloadsPurged,
                stagingRowsDeleted,
                usageRowsArchived);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private async Task AcquireTenantLockAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName !=
            "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter(
            "@resource",
            SqlDbType.NVarChar,
            255)
        {
            Value = $"cp6:space:ai-retention:{tenantId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 0;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw new SpaceAiRetentionBusyException(
                "Another AI retention cleanup owns the tenant lease.");
        }
    }

    private void RequireScope(Guid tenantId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || tenantId != context.CurrentTenantId)
        {
            throw new SpaceTenantScopeException(
                "AI retention cleanup requires the current Space tenant.");
        }
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
    }
}

public sealed class SpaceAiRetentionJobStepExecutor(
    SpaceContext context,
    ISpaceClock clock,
    ISpaceAiRetentionStore store) : ISpaceAiRetentionJobStepExecutor
{
    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var lease = execution.Lease;
        if (lease.JobType != SpaceJobType.AiRetentionCleanup ||
            lease.SubjectType != SpaceJobSubjectType.Tenant ||
            lease.SubjectId != lease.TenantId ||
            execution.StepCode != SpaceAiRetentionJobSteps.PurgeExpiredPayloads)
        {
            throw Invalid("The AI retention Job lease is invalid.");
        }

        try
        {
            var job = await context.Jobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == lease.JobId,
                    cancellationToken)
                ?? throw new SpaceAiRetentionPayloadException(
                    "The AI retention Job was not found.");
            var payload = SpaceAiRetentionPayloadCodec
                .ParsePayload(job.PayloadJson)
                .Validate(clock.UtcNow);
            var result = await store.PurgeAsync(
                lease.TenantId,
                payload,
                cancellationToken);
            var checkpoint = SpaceAiRetentionPayloadCodec.Serialize(result);
            return new SpaceJobStepOutput(
                checkpoint,
                SpaceAiRetentionPayloadCodec.Hash(checkpoint));
        }
        catch (SpaceAiRetentionBusyException exception)
        {
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Transient,
                SpaceErrorCodes.AiRetentionBusy,
                exception.Message);
        }
        catch (SpaceAiRetentionPayloadException exception)
        {
            throw Invalid(exception.Message);
        }
    }

    private static SpaceJobProcessingException Invalid(string message) =>
        new(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.AiRetentionInvalid,
            message);
}

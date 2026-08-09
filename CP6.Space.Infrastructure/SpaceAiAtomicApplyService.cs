using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAiAtomicApplyService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceClock clock) : ISpaceAiAtomicApplyService
{
    private const string Operation = "space.ai-proposal.apply";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceAiAtomicApplyAcceptedDto> QueueAsync(
        Guid runId,
        CreateSpaceAiAtomicApplyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireInternalTenant();
        var requestHash = Hash(JsonSerializer.Serialize(
            new { runId, request },
            JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        var transactionCompleted = false;
        try
        {
            await AcquireRunQueueLockAsync(runId, cancellationToken);
            context.ChangeTracker.Clear();
            var run = await LoadRunAsync(runId, write: true, cancellationToken);
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }
                return concurrentReplay;
            }

            EnsureQueueable(run);
            var version = await context.Versions.SingleOrDefaultAsync(
                item => item.Id == run.ModelVersionId,
                cancellationToken) ?? throw RunNotFound();
            if (version.Status != SpaceVersionStatus.Draft)
            {
                throw Problem(
                    SpaceErrorCodes.VersionStateInvalid,
                    409,
                    "Only a Draft version accepts an AI proposal Apply.",
                    "open-or-create-draft");
            }
            if (request.ExpectedContentRevision != run.BaseContentRevision ||
                version.ContentRevision != run.BaseContentRevision)
            {
                run.MarkStale();
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                }
                throw Problem(
                    SpaceErrorCodes.AiRunStale,
                    409,
                    "The Draft changed after this generation run was created.",
                    "create-run-based-on-latest-draft");
            }

            var actualRowVersion = Convert.ToBase64String(run.RowVersion);
            if (!FixedEquals(
                    request.ExpectedRunRowVersion,
                    actualRowVersion))
            {
                throw Problem(
                    SpaceErrorCodes.AiReviewConflict,
                    409,
                    "The generation run changed concurrently.",
                    "refresh-review-and-retry");
            }
            var review = await SpaceAiReviewStateReader.ReadAsync(
                context,
                run.Id,
                actualRowVersion,
                cancellationToken);
            if (!FixedEquals(request.ReviewEtag, review.ReviewEtag))
            {
                throw Problem(
                    SpaceErrorCodes.AiReviewConflict,
                    409,
                    "The proposal review changed concurrently.",
                    "refresh-review-and-retry");
            }
            if (review.Summary.ProposedCount != 0 ||
                review.Summary.OpenRunBlockingIssueCount != 0 ||
                review.Summary.OpenProposalBlockingIssueCount != 0 ||
                review.Summary.AcceptedCount +
                    review.Summary.ModifiedCount == 0)
            {
                throw Problem(
                    SpaceErrorCodes.AiReviewIncomplete,
                    422,
                    "The proposal review is incomplete or has no accepted work.",
                    "complete-review-before-apply");
            }

            var now = UtcNow();
            var commandBatchId = Guid.NewGuid();
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.ApplyGeneration,
                SpaceJobSubjectType.GenerationRun,
                run.Id,
                Hash($"{run.Id:D}\n{review.ReviewEtag}"),
                requestHash,
                priority: 80,
                maxAttempts: 5,
                execution.ActorId,
                now,
                CorrelationId(),
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = SpaceAiAtomicApplyContract.SchemaVersion,
                        runId = run.Id,
                        commandBatchId,
                        expectedContentRevision = request.ExpectedContentRevision,
                        reviewEtag = review.ReviewEtag,
                    },
                    JsonOptions));
            context.Jobs.Add(job);
            run.BeginApplying(
                job.Id,
                commandBatchId,
                review.ReviewEtag,
                actualRowVersion);
            var response = new SpaceAiAtomicApplyAcceptedDto(
                SpaceAiAtomicApplyContract.SchemaVersion,
                run.Id,
                job.Id,
                "Queued",
                request.ExpectedContentRevision,
                review.ReviewEtag,
                IdempotentReplay: false);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                Operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                202,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            return response;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null && !transactionCompleted)
                await transaction.RollbackAsync(CancellationToken.None);
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                409,
                "The proposal review changed concurrently.",
                "refresh-review-and-retry");
        }
        catch
        {
            if (transaction is not null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
    }

    public async Task<SpaceAiGenerationRunDto> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        var job = run.ApplyJobId is null
            ? null
            : await context.Jobs.AsNoTracking()
                .Where(item => item.Id == run.ApplyJobId)
                .SingleOrDefaultAsync(cancellationToken);
        var committed = run.ApplyCommandBatchId is not null &&
            await context.ElementCommandBatches.AsNoTracking().AnyAsync(
                item => item.Id == run.ApplyCommandBatchId &&
                        item.ResultVersionContentRevision != null &&
                        item.ResponseJson != null,
                cancellationToken);
        var recovery = SpaceAiRunRecoveryClassifier.Classify(
            run,
            job,
            committed);
        return new SpaceAiGenerationRunDto(
            SpaceAiAtomicApplyContract.SchemaVersion,
            run.Id,
            run.SiteId,
            run.ModelVersionId,
            run.SourceId,
            run.MappingProfileVersionId,
            run.RackGenerationProfileVersionId,
            run.TargetFloorLogicalId,
            run.Status.ToString(),
            run.Progress,
            run.BaseContentRevision,
            run.AppliedContentRevision,
            run.ApplyJobId,
            job?.Status.ToString(),
            run.ApplyPlanHash,
            run.AppliedCountsJson is null
                ? null
                : Parse(run.AppliedCountsJson),
            run.FailureCode,
            run.FailureSummary,
            run.BasedOnRunId,
            run.DegradedReason,
            run.CancelPending,
            recovery.Retryable,
            recovery.RecoveryAction,
            recovery.ApplyCommitState,
            Convert.ToBase64String(run.RowVersion));
    }

    private async Task<SpaceGenerationRun> LoadRunAsync(
        Guid runId,
        bool write,
        CancellationToken cancellationToken)
    {
        RequireInternalTenant();
        if (runId == Guid.Empty)
            throw RunNotFound();
        var query = write
            ? context.GenerationRuns.AsQueryable()
            : context.GenerationRuns.AsNoTracking();
        var run = await query.SingleOrDefaultAsync(
            item => item.Id == runId,
            cancellationToken) ?? throw RunNotFound();
        access.EnsureSiteAccess(run.SiteId, write);
        return run;
    }

    private static void EnsureQueueable(SpaceGenerationRun run)
    {
        if (!run.IsCurrent ||
            run.Status != SpaceGenerationRunStatus.AwaitingReview)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "The generation run is not awaiting review Apply.",
                "refresh-generation-run");
        }
        if (run.ReviewCompletedAtUtc is null)
        {
            throw Problem(
                SpaceErrorCodes.AiReviewIncomplete,
                422,
                "The generation review is not complete.",
                "complete-review-before-apply");
        }
        if (run.TargetFloorLogicalId is null)
        {
            throw Problem(
                SpaceErrorCodes.AiApplyInvalid,
                422,
                "The generation run did not pin a target Floor.",
                "create-run-with-target-floor");
        }
    }

    private async Task<SpaceAiAtomicApplyAcceptedDto?> ReadReplayAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.PrincipalId == execution.ActorId &&
                item.Operation == Operation &&
                item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!FixedEquals(record.RequestHash, requestHash) ||
            record.ReplayUntilUtc < UtcNow())
        {
            throw Problem(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was reused with different or expired input.",
                "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<SpaceAiAtomicApplyAcceptedDto>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The stored AI Apply response is invalid.")) with
        { IdempotentReplay = true };
    }

    private string IdempotencyKeyHash(string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw Problem(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                "supply-idempotency-key");
        }
        return Hash($"{execution.TenantId:D}\n{Operation}\n{normalized}");
    }

    private void RequireInternalTenant()
    {
        if (execution.IsExternal)
        {
            throw Problem(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot apply AI proposals.",
                "use-internal-space-editor");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified internal Space tenant context is required.");
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private async Task AcquireRunQueueLockAsync(
        Guid runId,
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
            Value = $"cp6:space:ai-apply:{execution.TenantId:N}:{runId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                409,
                "Another Apply request is currently updating this run.",
                "retry-with-same-idempotency-key");
        }
    }

    private DateTime UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private Guid CorrelationId() =>
        execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : Guid.NewGuid();

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static SpaceProblemException RunNotFound() => Problem(
        SpaceErrorCodes.AiRunNotFound,
        404,
        "The generation run was not found.",
        "refresh-generation-runs");

    internal static SpaceProblemException Problem(
        string code,
        int status,
        string title,
        string recovery) =>
        new(code, status, title, recoveryAction: recovery);
}

internal sealed record SpaceAiReviewState(
    SpaceAiGenerationReviewSummaryDto Summary,
    string ReviewEtag);

internal static class SpaceAiReviewStateReader
{
    public static async Task<SpaceAiReviewState> ReadAsync(
        SpaceContext context,
        Guid runId,
        string runRowVersion,
        CancellationToken cancellationToken)
    {
        var proposals = await context.GenerationProposals.AsNoTracking()
            .Where(item => item.RunId == runId)
            .Select(item => new { item.Status, item.HasBlockingIssue })
            .ToArrayAsync(cancellationToken);
        var issues = await context.Issues.AsNoTracking()
            .Where(item =>
                item.GenerationRunId == runId &&
                item.Status == SpaceIssueStatus.Open &&
                item.Severity == SpaceIssueSeverity.Blocking)
            .Select(item => item.GenerationProposalId)
            .ToArrayAsync(cancellationToken);
        var lastDecision = await context.ProposalDecisions.AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        long Count(SpaceGenerationProposalStatus status) =>
            proposals.LongCount(item => item.Status == status);
        var summary = new SpaceAiGenerationReviewSummaryDto(
            proposals.LongLength,
            Count(SpaceGenerationProposalStatus.Proposed),
            Count(SpaceGenerationProposalStatus.Accepted),
            Count(SpaceGenerationProposalStatus.Rejected),
            Count(SpaceGenerationProposalStatus.Modified),
            Count(SpaceGenerationProposalStatus.Obsolete),
            proposals.LongCount(item => item.HasBlockingIssue),
            issues.LongCount(item => item is null),
            issues.LongCount(item => item is not null));
        var etag = SpaceAiAtomicApplyService.Hash(string.Join(
            "\n",
            runRowVersion,
            summary.TotalCount,
            summary.ProposedCount,
            summary.AcceptedCount,
            summary.RejectedCount,
            summary.ModifiedCount,
            summary.ObsoleteCount,
            summary.OpenRunBlockingIssueCount,
            summary.OpenProposalBlockingIssueCount,
            lastDecision?.ToString("D") ?? "none"));
        return new SpaceAiReviewState(summary, etag);
    }
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAiRunRecoveryService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceClock clock,
    ISpaceAiLockedFactService lockedFacts) : ISpaceAiRunRecoveryService
{
    private const string CancelOperation = "space.ai-generation-run.cancel";
    private const string RetryOperation = "space.ai-generation-run.retry";
    private const string DiscardOperation = "space.ai-generation-run.discard";
    private const string ReconcileOperation = "space.ai-generation-run.reconcile";
    private const string RecoverOperation = "space.ai-generation-run.recover";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<SpaceAiGenerationRunActionDto> CancelAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            CancelOperation,
            runId,
            request,
            idempotencyKey,
            CancelRunAsync,
            cancellationToken);

    public Task<SpaceAiGenerationRunActionDto> RetryAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            RetryOperation,
            runId,
            request,
            idempotencyKey,
            RetryRunAsync,
            cancellationToken);

    public Task<SpaceAiGenerationRunActionDto> DiscardAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            DiscardOperation,
            runId,
            request,
            idempotencyKey,
            DiscardRunAsync,
            cancellationToken);

    public Task<SpaceAiGenerationRunActionDto> ReconcileAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        MutateRunAsync(
            ReconcileOperation,
            runId,
            request,
            idempotencyKey,
            ReconcileRunAsync,
            cancellationToken);

    public async Task<SpaceAiGenerationRunActionDto> RecoverAsync(
        Guid versionId,
        CreateSpaceAiGenerationRecoveryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireInternalTenant();
        if (versionId == Guid.Empty || request.BasedOnRunId == Guid.Empty)
            throw RunNotFound();
        if (request.ExpectedContentRevision < 0)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                400,
                "A non-negative expected ContentRevision is required.",
                "refresh-generation-run");
        }
        var mode = NormalizeMode(request.Mode);
        var requestHash = Hash(JsonSerializer.Serialize(
            new { versionId, request, mode },
            JsonOptions));
        var keyHash = IdempotencyKeyHash(RecoverOperation, idempotencyKey);
        var replay = await ReadReplayAsync(
            RecoverOperation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            var source = await LoadRunForUpdateAsync(
                request.BasedOnRunId,
                cancellationToken);
            var concurrentReplay = await ReadReplayAsync(
                RecoverOperation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await CommitAsync(transaction, cancellationToken);
                return concurrentReplay;
            }
            EnsureExpectedRowVersion(
                source,
                request.ExpectedBasedOnRunRowVersion);
            if (source.ModelVersionId != versionId ||
                source.Status is not (
                    SpaceGenerationRunStatus.Stale or
                    SpaceGenerationRunStatus.Failed))
            {
                throw Problem(
                    SpaceErrorCodes.AiRunStateInvalid,
                    409,
                    "Only a Failed or Stale run can create a recovery run.",
                    "refresh-generation-run");
            }

            var version = await context.Versions.SingleOrDefaultAsync(
                item => item.Id == versionId,
                cancellationToken) ?? throw RunNotFound();
            if (version.Status != SpaceVersionStatus.Draft ||
                version.ContentRevision != request.ExpectedContentRevision)
            {
                throw Problem(
                    SpaceErrorCodes.AiRunStale,
                    409,
                    "The recovery target is not the expected current Draft.",
                    "refresh-draft-and-rebuild-generation-run");
            }

            var active = await context.GenerationRuns.SingleOrDefaultAsync(
                item => item.Id != source.Id &&
                        item.IsCurrent &&
                        item.BusinessKeyHash == source.BusinessKeyHash,
                cancellationToken);
            if (active is not null)
            {
                throw Problem(
                    SpaceErrorCodes.AiRunStateInvalid,
                    409,
                    "A current recovery run already exists for this input.",
                    "open-current-generation-run");
            }

            var now = UtcNow();
            if (source.Status == SpaceGenerationRunStatus.Failed)
            {
                source.Discard(now);
                await context.SaveChangesAsync(cancellationToken);
            }

            var replacementRunId = Guid.NewGuid();
            var previewPin = await LoadPinnedPreviewAsync(
                source.JobId,
                cancellationToken);
            var jobInputHash = Hash(string.Join(
                "\n",
                source.SourceHash,
                request.ExpectedContentRevision.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                mode,
                source.RuleVersion,
                source.MappingProfileVersionId?.ToString("N") ?? string.Empty,
                source.RackGenerationProfileVersionId?.ToString("N") ??
                    string.Empty,
                previewPin?.ArtifactId.ToString("N") ?? string.Empty,
                previewPin?.FileSha256 ?? string.Empty));
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion,
                source.ModelVersionId,
                Hash($"{source.BusinessKeyHash}\n{replacementRunId:N}"),
                jobInputHash,
                priority: 70,
                maxAttempts: 5,
                execution.ActorId,
                now,
                CorrelationId(),
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = SpaceAiRunRecoveryContract.SchemaVersion,
                        runId = replacementRunId,
                        basedOnRunId = source.Id,
                        sourceId = source.SourceId,
                        expectedContentRevision =
                            request.ExpectedContentRevision,
                        mode,
                        previewArtifactId = previewPin?.ArtifactId,
                        previewArtifactSha256 = previewPin?.FileSha256,
                    },
                    JsonOptions));
            var ruleOnly = string.Equals(
                mode,
                SpaceAiRunRecoveryContract.RuleOnlyMode,
                StringComparison.Ordinal);
            var replacement = SpaceGenerationRun.Create(
                new SpaceGenerationRunDefinition(
                    execution.TenantId,
                    source.SiteId,
                    source.ModelVersionId,
                    source.SourceId,
                    source.SourceHash,
                    request.ExpectedContentRevision,
                    keyHash,
                    source.BusinessKeyHash,
                    source.Id,
                    source.MappingProfileVersionId,
                    source.RackGenerationProfileVersionId,
                    source.RuleVersion,
                    ruleOnly
                        ? SpaceAiPolicySnapshot.Disabled
                        : source.PolicySnapshot,
                    ruleOnly ? null : source.ProviderConfigVersionId,
                    source.InputSchemaVersion,
                    job.Id,
                    source.TargetFloorLogicalId,
                    replacementRunId));
            context.Jobs.Add(job);
            context.GenerationRuns.Add(replacement);
            await context.SaveChangesAsync(cancellationToken);

            _ = await lockedFacts.MaterializeAsync(
                replacement.Id,
                cancellationToken);
            await ObsoleteProposalsAsync(source.Id, cancellationToken);

            var response = BuildResponse(
                source,
                job: null,
                commandBatchCommitted: false,
                replacement.Id,
                job.Id,
                idempotentReplay: false);
            AddIdempotency(
                RecoverOperation,
                keyHash,
                requestHash,
                response,
                now);
            await context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return response;
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task<SpaceAiGenerationRunActionDto> MutateRunAsync(
        string operation,
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        Func<SpaceGenerationRun, SpaceJob?, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireInternalTenant();
        if (runId == Guid.Empty)
            throw RunNotFound();
        var requestHash = Hash(JsonSerializer.Serialize(
            new { runId, request },
            JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            var run = await LoadRunForUpdateAsync(runId, cancellationToken);
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await CommitAsync(transaction, cancellationToken);
                return concurrentReplay;
            }
            EnsureExpectedRowVersion(run, request.ExpectedRunRowVersion);

            var job = await LoadCurrentJobAsync(run, cancellationToken);
            await mutation(run, job, cancellationToken);
            var committed = await HasCommittedBatchAsync(
                run,
                cancellationToken);
            var response = BuildResponse(
                run,
                job,
                committed,
                replacementRunId: null,
                jobId: job?.Id,
                idempotentReplay: false);
            AddIdempotency(
                operation,
                keyHash,
                requestHash,
                response,
                UtcNow());
            await context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return response;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction);
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                409,
                "The generation run changed concurrently.",
                "refresh-generation-run");
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task CancelRunAsync(
        SpaceGenerationRun run,
        SpaceJob? job,
        CancellationToken cancellationToken)
    {
        if (run.Status == SpaceGenerationRunStatus.Succeeded)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "A succeeded generation run cannot be cancelled.",
                "open-updated-draft");
        }
        if (run.Status == SpaceGenerationRunStatus.Cancelled)
            return;
        if (run.Status is SpaceGenerationRunStatus.AwaitingReview or
            SpaceGenerationRunStatus.Failed or
            SpaceGenerationRunStatus.Stale)
        {
            run.Discard(UtcNow());
            await ObsoleteProposalsAsync(run.Id, cancellationToken);
            return;
        }
        if (job is null || job.IsTerminal)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "The generation Job must be reconciled before cancellation.",
                "reconcile-apply-result");
        }

        var now = UtcNow();
        var responsePending = job.Status == SpaceJobStatus.Running;
        run.RequestCancellation(now, responsePending);
        job.RequestCancellation(execution.ActorId, now);
        if (run.Status == SpaceGenerationRunStatus.Cancelled)
            await ObsoleteProposalsAsync(run.Id, cancellationToken);
    }

    private Task RetryRunAsync(
        SpaceGenerationRun run,
        SpaceJob? job,
        CancellationToken cancellationToken)
    {
        if (run.Status != SpaceGenerationRunStatus.Failed || job is null)
        {
            throw Problem(
                SpaceErrorCodes.JobNotRetryable,
                409,
                "Only a safely classified failed generation Job can retry.",
                "refresh-generation-run");
        }
        try
        {
            job.RequeueSameInput(UtcNow());
        }
        catch (SpaceJobNotRetryableException)
        {
            throw Problem(
                SpaceErrorCodes.JobNotRetryable,
                409,
                "This failure is not safe to retry with unchanged input.",
                string.Equals(
                    run.FailureCode,
                    SpaceErrorCodes.AiProviderUnavailable,
                    StringComparison.Ordinal)
                    ? "use-rule-only-generation"
                    : "create-new-generation-run");
        }

        if (run.ApplyJobId == job.Id)
            run.RetryApply();
        else if (run.JobId == job.Id)
            run.Retry();
        else
            throw new InvalidOperationException(
                "The recovery Job is not bound to the generation run.");
        return Task.CompletedTask;
    }

    private async Task DiscardRunAsync(
        SpaceGenerationRun run,
        SpaceJob? job,
        CancellationToken cancellationToken)
    {
        if (run.Status == SpaceGenerationRunStatus.Cancelled)
            return;
        if (run.Status is not (
            SpaceGenerationRunStatus.AwaitingReview or
            SpaceGenerationRunStatus.Failed or
            SpaceGenerationRunStatus.Stale))
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "Only an AwaitingReview, Failed, or Stale run can be discarded.",
                run.Status == SpaceGenerationRunStatus.Applying
                    ? "cancel-generation-run"
                    : "refresh-generation-run");
        }
        run.Discard(UtcNow());
        await ObsoleteProposalsAsync(run.Id, cancellationToken);
    }

    private async Task ReconcileRunAsync(
        SpaceGenerationRun run,
        SpaceJob? job,
        CancellationToken cancellationToken)
    {
        if (run.Status == SpaceGenerationRunStatus.Succeeded)
            return;
        var batch = run.ApplyCommandBatchId is null
            ? null
            : await context.ElementCommandBatches.SingleOrDefaultAsync(
                item => item.Id == run.ApplyCommandBatchId &&
                        item.ResultVersionContentRevision != null &&
                        item.ResponseJson != null,
                cancellationToken);
        if (batch is not null)
        {
            var currentRevision = await context.Versions.AsNoTracking()
                .Where(item => item.Id == run.ModelVersionId)
                .Select(item => item.ContentRevision)
                .SingleAsync(cancellationToken);
            if (batch.ModelVersionId != run.ModelVersionId ||
                batch.FloorLogicalId != run.TargetFloorLogicalId ||
                batch.ResultVersionContentRevision != currentRevision)
            {
                throw Problem(
                    SpaceErrorCodes.AiApplyResultUnknown,
                    409,
                    "The command batch does not match the current Draft revision.",
                    "escalate-apply-reconciliation");
            }
            var countsJson = ValidateCommittedBatch(run, batch);
            run.ReconcileSucceeded(
                batch.ResultVersionContentRevision!.Value,
                countsJson);
            return;
        }
        if (run.Status != SpaceGenerationRunStatus.Applying ||
            job is null || !job.IsTerminal)
        {
            return;
        }
        if (job.Status == SpaceJobStatus.Cancelled && run.CancelPending)
        {
            run.CompleteCancellation(UtcNow());
            await ObsoleteProposalsAsync(run.Id, cancellationToken);
            return;
        }
        if (string.Equals(
                job.LastErrorCode,
                SpaceErrorCodes.AiRunStale,
                StringComparison.Ordinal))
        {
            run.MarkStale();
            return;
        }
        if (job.Status is SpaceJobStatus.Failed or SpaceJobStatus.DeadLetter)
        {
            run.MarkFailed(
                job.LastErrorCode ?? SpaceErrorCodes.AiApplyFailed,
                job.LastErrorSummary ??
                    "The atomic AI Apply failed without changing Draft.");
            return;
        }
        if (job.Status == SpaceJobStatus.Succeeded)
        {
            run.MarkFailed(
                SpaceErrorCodes.AiApplyResultUnknown,
                "The Apply Job completed without an authoritative command batch.");
        }
    }

    private async Task<SpaceGenerationRun> LoadRunForUpdateAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        SpaceGenerationRun? run;
        if (context.Database.ProviderName ==
            "Microsoft.EntityFrameworkCore.SqlServer")
        {
            run = await context.GenerationRuns.FromSqlInterpolated(
                    $"SELECT * FROM [Space_GenerationRun] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {execution.TenantId} AND [Id] = {runId} AND [IsDeleted] = CAST(0 AS bit)")
                .SingleOrDefaultAsync(cancellationToken);
        }
        else
        {
            run = await context.GenerationRuns.SingleOrDefaultAsync(
                item => item.Id == runId,
                cancellationToken);
        }
        if (run is null)
            throw RunNotFound();
        access.EnsureSiteAccess(run.SiteId, write: true);
        return run;
    }

    private async Task<PreviewPin?> LoadPinnedPreviewAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var payload = await context.Jobs.AsNoTracking()
            .Where(item => item.Id == jobId)
            .Select(item => item.PayloadJson)
            .SingleAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var hasArtifactId = root.TryGetProperty(
                "previewArtifactId",
                out var artifactId);
            var hasArtifactSha256 = root.TryGetProperty(
                "previewArtifactSha256",
                out var artifactSha256);
            if (!hasArtifactId && !hasArtifactSha256)
            {
                return null;
            }
            if (!hasArtifactId || !hasArtifactSha256)
                throw new JsonException();
            if (artifactId.ValueKind == JsonValueKind.Null &&
                artifactSha256.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            var id = artifactId.GetGuid();
            var sha256 = artifactSha256.GetString();
            if (id == Guid.Empty ||
                sha256 is not { Length: 64 } ||
                sha256.Any(character =>
                    !Uri.IsHexDigit(character) || char.IsUpper(character)))
            {
                throw new JsonException();
            }
            return new PreviewPin(id, sha256);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or
            FormatException)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "The source generation run has an invalid pinned PreviewSet.",
                "discard-and-create-generation-run");
        }
    }

    private Task<SpaceJob?> LoadCurrentJobAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        var jobId = run.ApplyJobId ?? run.JobId;
        return context.Jobs.SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
    }

    private async Task ObsoleteProposalsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var proposals = await context.GenerationProposals
            .Where(item => item.RunId == runId)
            .ToArrayAsync(cancellationToken);
        foreach (var proposal in proposals.Where(item =>
                     item.Status is not (
                         SpaceGenerationProposalStatus.Applied or
                         SpaceGenerationProposalStatus.Obsolete)))
        {
            proposal.MarkObsolete();
        }
    }

    private async Task<bool> HasCommittedBatchAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken) =>
        run.ApplyCommandBatchId is not null &&
        await context.ElementCommandBatches.AsNoTracking().AnyAsync(
            item => item.Id == run.ApplyCommandBatchId &&
                    item.ResultVersionContentRevision != null &&
                    item.ResponseJson != null,
            cancellationToken);

    private static string ValidateCommittedBatch(
        SpaceGenerationRun run,
        SpaceElementCommandBatch batch)
    {
        using var response = JsonDocument.Parse(batch.ResponseJson!);
        var root = response.RootElement;
        if (!root.TryGetProperty("runId", out var runId) ||
            runId.GetGuid() != run.Id ||
            !root.TryGetProperty("applyPlanHash", out var planHash) ||
            !SpaceAiAtomicApplyService.FixedEquals(
                planHash.GetString(),
                run.ApplyPlanHash) ||
            !root.TryGetProperty("appliedCounts", out var counts) ||
            counts.ValueKind != JsonValueKind.Object)
        {
            throw Problem(
                SpaceErrorCodes.AiApplyResultUnknown,
                409,
                "The command batch does not match the frozen AI Apply plan.",
                "escalate-apply-reconciliation");
        }
        return counts.GetRawText();
    }

    private SpaceAiGenerationRunActionDto BuildResponse(
        SpaceGenerationRun run,
        SpaceJob? job,
        bool commandBatchCommitted,
        Guid? replacementRunId,
        Guid? jobId,
        bool idempotentReplay)
    {
        var state = SpaceAiRunRecoveryClassifier.Classify(
            run,
            job,
            commandBatchCommitted);
        return new SpaceAiGenerationRunActionDto(
            SpaceAiRunRecoveryContract.SchemaVersion,
            run.Id,
            replacementRunId,
            jobId,
            replacementRunId.HasValue ? "Queued" : run.Status.ToString(),
            replacementRunId.HasValue
                ? "review-rebuilt-generation-run"
                : state.RecoveryAction,
            replacementRunId.HasValue ? false : state.Retryable,
            replacementRunId.HasValue ? false : run.CancelPending,
            idempotentReplay);
    }

    private async Task<SpaceAiGenerationRunActionDto?> ReadReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.PrincipalId == execution.ActorId &&
                item.Operation == operation &&
                item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!SpaceAiAtomicApplyService.FixedEquals(
                record.RequestHash,
                requestHash) ||
            record.ReplayUntilUtc < UtcNow())
        {
            throw Problem(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was reused with different or expired input.",
                "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<SpaceAiGenerationRunActionDto>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The stored AI recovery response is invalid.")) with
        { IdempotentReplay = true };
    }

    private void AddIdempotency(
        string operation,
        string keyHash,
        string requestHash,
        SpaceAiGenerationRunActionDto response,
        DateTime now)
    {
        context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
            execution.TenantId,
            execution.ActorId,
            operation,
             keyHash,
             requestHash,
             JsonSerializer.Serialize(response, JsonOptions),
             operation is RetryOperation or RecoverOperation ? 202 : 200,
             now.AddHours(24),
            now.AddDays(90)));
    }

    private string IdempotencyKeyHash(string operation, string key)
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
        return Hash($"{execution.TenantId:D}\n{operation}\n{normalized}");
    }

    private static string NormalizeMode(string mode)
    {
        var normalized = mode?.Trim();
        if (normalized is not (
            SpaceAiRunRecoveryContract.SamePolicyMode or
            SpaceAiRunRecoveryContract.RuleOnlyMode))
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                400,
                "Recovery mode must be SamePolicy or RuleOnly.",
                "select-generation-recovery-mode");
        }
        return normalized;
    }

    private void EnsureExpectedRowVersion(
        SpaceGenerationRun run,
        string expected)
    {
        if (!SpaceAiAtomicApplyService.FixedEquals(
                expected,
                Convert.ToBase64String(run.RowVersion)))
        {
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                409,
                "The generation run changed concurrently.",
                "refresh-generation-run");
        }
    }

    private void RequireInternalTenant()
    {
        if (execution.IsExternal)
        {
            throw Problem(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot recover AI generation runs.",
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

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(CancellationToken.None);
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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException RunNotFound() => Problem(
        SpaceErrorCodes.AiRunNotFound,
        404,
        "The generation run was not found.",
        "refresh-generation-runs");

    private static SpaceProblemException Problem(
        string code,
        int status,
        string title,
        string recovery) =>
        new(code, status, title, recoveryAction: recovery);

    private sealed record PreviewPin(
        Guid ArtifactId,
        string FileSha256);
}

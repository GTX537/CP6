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

public sealed class SpaceAiProposalDecisionService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceCursorCodec cursorCodec,
    ISpaceClock clock,
    SpaceAiProposalReviewOptions options) :
    ISpaceAiProposalDecisionService
{
    private const string SingleOperation = "space.ai-proposal.decide";
    private const string BatchOperation = "space.ai-proposal.decide-batch";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PatchResolvableIssueCodes =
        new(StringComparer.Ordinal)
        {
            "AI_RELATION_AMBIGUOUS",
            "AI_PROPOSAL_TYPE_INVALID",
            "AI_BUSINESS_ENUM_INVALID",
        };
    private static readonly IReadOnlyDictionary<string, string> RelationTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/relations/zoneSourceKey"] = "Zone",
            ["/relations/aisleSourceKey"] = "Aisle",
            ["/relations/wallSourceKey"] = "Wall",
        };

    public async Task<SpaceAiGenerationReviewDto> GetReviewAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        return await BuildReviewAsync(run, cancellationToken);
    }

    public async Task<SpaceAiProposalPageDto> GetProposalsAsync(
        Guid runId,
        SpaceAiProposalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        var review = await BuildReviewAsync(run, cancellationToken);
        var normalized = Normalize(query);
        var filterHash = Hash(JsonSerializer.Serialize(normalized, JsonOptions));
        var resource = CursorResource("proposals", run.Id, review.ReviewEtag);
        var offset = ReadOffset(query.Cursor, resource, filterHash);

        IQueryable<SpaceGenerationProposal> rows = context.GenerationProposals
            .AsNoTracking()
            .Where(item => item.RunId == run.Id);
        if (normalized.Status is not null)
            rows = rows.Where(item => item.Status == normalized.Status);
        if (normalized.ConfidenceBand is not null)
            rows = rows.Where(item => item.ConfidenceBand == normalized.ConfidenceBand);
        if (normalized.ProposalType is not null)
            rows = rows.Where(item => item.ProposalType == normalized.ProposalType);
        if (normalized.HasBlockingIssue is not null)
            rows = rows.Where(item =>
                item.HasBlockingIssue == normalized.HasBlockingIssue);

        var total = await rows.LongCountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(item => item.ConfidenceScore)
            .ThenBy(item => item.ProposalType)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(normalized.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var items = page.Take(normalized.Limit).Select(ToDto).ToArray();
        var next = page.Length > normalized.Limit
            ? cursorCodec.Encode(new SpaceCursorState(
                resource,
                filterHash,
                checked(offset + normalized.Limit)))
            : null;
        return new SpaceAiProposalPageDto(
            items,
            total,
            normalized.Limit,
            review.ReviewEtag,
            filterHash,
            next);
    }

    public async Task<SpaceAiProposalIssuePageDto> GetIssuesAsync(
        Guid runId,
        SpaceAiProposalIssueQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        var review = await BuildReviewAsync(run, cancellationToken);
        var normalized = Normalize(query);
        var filterHash = Hash(JsonSerializer.Serialize(normalized, JsonOptions));
        var resource = CursorResource("issues", run.Id, review.ReviewEtag);
        var offset = ReadOffset(query.Cursor, resource, filterHash);

        IQueryable<SpaceModelIssue> rows = context.Issues
            .AsNoTracking()
            .Where(item => item.GenerationRunId == run.Id);
        if (normalized.ProposalId is not null)
            rows = rows.Where(item =>
                item.GenerationProposalId == normalized.ProposalId);
        if (normalized.Severity is not null)
            rows = rows.Where(item => item.Severity == normalized.Severity);
        if (normalized.Status is not null)
            rows = rows.Where(item => item.Status == normalized.Status);
        if (normalized.IssueCode is not null)
            rows = rows.Where(item => item.Code == normalized.IssueCode);

        var total = await rows.LongCountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(normalized.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var items = page.Take(normalized.Limit).Select(ToDto).ToArray();
        var next = page.Length > normalized.Limit
            ? cursorCodec.Encode(new SpaceCursorState(
                resource,
                filterHash,
                checked(offset + normalized.Limit)))
            : null;
        return new SpaceAiProposalIssuePageDto(
            items,
            total,
            normalized.Limit,
            review.ReviewEtag,
            filterHash,
            next);
    }

    public async Task<SpaceAiProposalDecisionHistoryDto> GetDecisionsAsync(
        Guid runId,
        Guid? proposalId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        if (proposalId == Guid.Empty || limit is < 1 or > 500)
            throw InvalidDecision("The decision history query is invalid.");
        var review = await BuildReviewAsync(run, cancellationToken);
        IQueryable<SpaceProposalDecision> rows = context.ProposalDecisions
            .AsNoTracking()
            .Where(item => item.RunId == run.Id);
        if (proposalId is not null)
            rows = rows.Where(item => item.ProposalId == proposalId);
        var page = await rows
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        return new SpaceAiProposalDecisionHistoryDto(
            page.Take(limit).Select(ToDto).ToArray(),
            page.Length > limit,
            review.ReviewEtag);
    }

    public Task<SpaceAiProposalDecisionResponse> CreateDecisionAsync(
        Guid runId,
        CreateSpaceAiProposalDecisionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            runId,
            SingleOperation,
            request,
            idempotencyKey,
            (run, batchId, token) =>
                DecideOneAsync(run, request, batchId, token),
            cancellationToken);

    public Task<SpaceAiProposalDecisionResponse> CreateBatchDecisionAsync(
        Guid runId,
        CreateSpaceAiProposalBatchDecisionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            runId,
            BatchOperation,
            request,
            idempotencyKey,
            (run, batchId, token) =>
                DecideBatchAsync(run, request, batchId, token),
            cancellationToken);

    private async Task<SpaceAiProposalDecisionResponse> ExecuteAsync<TRequest>(
        Guid runId,
        string operation,
        TRequest request,
        string idempotencyKey,
        Func<SpaceGenerationRun, Guid, CancellationToken,
            Task<IReadOnlyList<SpaceProposalDecision>>> decide,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var initialRun = await LoadRunAsync(runId, write: true, cancellationToken);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var replay = await ReadReplayAsync(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;
        await EnsureFreshAsync(initialRun, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            context.ChangeTracker.Clear();
            var run = await LoadRunAsync(runId, write: true, cancellationToken);
            EnsureReviewable(run);
            await EnsureFreshAsync(run, cancellationToken);
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var batchId = Guid.NewGuid();
            var decisions = await decide(run, batchId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await CompleteReviewWhenReadyAsync(run, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            var review = await BuildReviewAsync(run, cancellationToken);
            var response = new SpaceAiProposalDecisionResponse(
                review.ReviewCompleted ? "review-completed" : "decided",
                batchId,
                decisions.Select(ToDto).ToArray(),
                review,
                IdempotentReplay: false);
            var now = UtcNow();
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                200,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw ReviewConflict(exception);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<SpaceProposalDecision>> DecideOneAsync(
        SpaceGenerationRun run,
        CreateSpaceAiProposalDecisionRequest request,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        if (request.ProposalId == Guid.Empty)
            throw InvalidDecision("A proposal ID is required.");
        var proposal = await context.GenerationProposals
            .SingleOrDefaultAsync(item =>
                item.RunId == run.Id && item.Id == request.ProposalId,
                cancellationToken)
            ?? throw Problem(
                SpaceErrorCodes.AiProposalNotFound,
                404,
                "The generation proposal was not found.",
                "refresh-review");
        EnsureExpectedRowVersion(proposal, request.ExpectedProposalRowVersion);
        var decisionType = ParseDecision(request.Decision, allowModify: true);
        var before = SpaceAiProposalPatchPolicyV1.BuildSnapshot(
            proposal.ProposalType,
            proposal.SuggestedGeometryJson,
            proposal.SuggestedAttributesJson,
            proposal.SuggestedRelationsJson);
        string? after = null;
        string? locks = null;
        var resolvesBlocking = false;

        switch (decisionType)
        {
            case SpaceProposalDecisionType.Accept:
                RequireNoPatch(request.Patch, request.LockedFields);
                proposal.Accept();
                after = before;
                break;
            case SpaceProposalDecisionType.Reject:
                RequireNoPatch(request.Patch, request.LockedFields);
                proposal.Reject();
                break;
            case SpaceProposalDecisionType.Modify:
                if (request.Patch is null || request.LockedFields is null)
                    throw PatchDenied("Modify requires a patch and locked fields.");
                SpaceAiProposalPatchResult patch;
                try
                {
                    patch = SpaceAiProposalPatchPolicyV1.Apply(
                        proposal.ProposalType,
                        proposal.SuggestedGeometryJson,
                        proposal.SuggestedAttributesJson,
                        proposal.SuggestedRelationsJson,
                        request.Patch,
                        request.LockedFields);
                }
                catch (SpaceProposalPatchException exception)
                {
                    throw PatchDenied(exception.Message, exception);
                }
                await ValidateRelationsAsync(run.Id, proposal, patch, cancellationToken);
                var openBlocking = await context.Issues
                    .Where(issue =>
                        issue.GenerationRunId == run.Id &&
                        issue.GenerationProposalId == proposal.Id &&
                        issue.Status == SpaceIssueStatus.Open &&
                        issue.Severity == SpaceIssueSeverity.Blocking)
                    .ToArrayAsync(cancellationToken);
                resolvesBlocking = openBlocking.Length > 0 &&
                    openBlocking.All(issue =>
                        PatchResolvableIssueCodes.Contains(issue.Code));
                if (proposal.HasBlockingIssue && !resolvesBlocking)
                {
                    throw Problem(
                        SpaceErrorCodes.AiReviewIncomplete,
                        422,
                        "This blocking proposal cannot be repaired by an allowlisted patch.",
                        "reject-proposal-or-create-new-run");
                }
                proposal.Modify(
                    patch.PatchJson,
                    patch.LockedFieldsJson,
                    resolvesBlocking);
                after = patch.FinalSnapshotJson;
                locks = patch.LockedFieldsJson;
                break;
        }

        var decision = SpaceProposalDecision.Create(
            execution.TenantId,
            run.Id,
            proposal.Id,
            decisionType,
            before,
            after,
            locks,
            request.ReasonCode,
            request.Comment,
            batchId);
        context.ProposalDecisions.Add(decision);
        await ResolveProposalIssuesAsync(
            run.Id,
            proposal.Id,
            decision,
            decisionType == SpaceProposalDecisionType.Reject,
            resolvesBlocking,
            cancellationToken);
        return [decision];
    }

    private async Task<IReadOnlyList<SpaceProposalDecision>> DecideBatchAsync(
        SpaceGenerationRun run,
        CreateSpaceAiProposalBatchDecisionRequest request,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var decisionType = ParseDecision(request.Decision, allowModify: false);
        var review = await BuildReviewAsync(run, cancellationToken);
        if (!string.Equals(request.ReviewEtag, review.ReviewEtag,
                StringComparison.Ordinal))
            throw ReviewConflict();
        var hasIds = request.ProposalIds is { Count: > 0 };
        var hasFilter = request.Selection is not null;
        if (hasIds == hasFilter)
            throw BatchInvalid("Specify proposal IDs or one selection filter, not both.");

        IQueryable<SpaceGenerationProposal> query = context.GenerationProposals
            .Where(item =>
                item.RunId == run.Id &&
                item.Status == SpaceGenerationProposalStatus.Proposed);
        Guid[]? ids = null;
        if (hasIds)
        {
            ids = request.ProposalIds!
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
            if (ids.Length != request.ProposalIds!.Count ||
                ids.Length > SpaceAiProposalDecisionContract.MaximumBatchSize)
                throw BatchInvalid("The proposal ID selection is invalid or too large.");
            query = query.Where(item => ids.Contains(item.Id));
        }
        else
        {
            var selection = Normalize(request.Selection!);
            if (selection.Status is not null &&
                selection.Status != SpaceGenerationProposalStatus.Proposed)
                throw BatchInvalid("Batch decisions can select only Proposed items.");
            if (selection.ConfidenceBand is not null)
                query = query.Where(item =>
                    item.ConfidenceBand == selection.ConfidenceBand);
            if (selection.ProposalTypes is { Length: > 0 })
                query = query.Where(item =>
                    selection.ProposalTypes.Contains(item.ProposalType));
            if (selection.HasBlockingIssue is not null)
                query = query.Where(item =>
                    item.HasBlockingIssue == selection.HasBlockingIssue);
        }

        var proposals = await query
            .OrderByDescending(item => item.ConfidenceScore)
            .ThenBy(item => item.ProposalType)
            .ThenBy(item => item.Id)
            .Take(SpaceAiProposalDecisionContract.MaximumBatchSize + 1)
            .ToArrayAsync(cancellationToken);
        if (proposals.Length == 0 ||
            proposals.Length > SpaceAiProposalDecisionContract.MaximumBatchSize ||
            ids is not null && proposals.Length != ids.Length)
            throw BatchInvalid("The batch selection is empty, stale, or too large.");
        if (decisionType == SpaceProposalDecisionType.Accept)
        {
            if (!options.EnableHighConfidenceBatchAccept)
            {
                throw Problem(
                    SpaceErrorCodes.AiBatchAcceptDisabled,
                    422,
                    "High-confidence batch acceptance is disabled.",
                    "review-proposals-individually");
            }
            if (proposals.Any(item =>
                    item.ConfidenceBand != SpaceConfidenceBand.High ||
                    item.HasBlockingIssue))
                throw BatchInvalid(
                    "Batch acceptance requires High confidence and no blockers.");
        }

        var decisions = new List<SpaceProposalDecision>(proposals.Length);
        foreach (var proposal in proposals)
        {
            var before = SpaceAiProposalPatchPolicyV1.BuildSnapshot(
                proposal.ProposalType,
                proposal.SuggestedGeometryJson,
                proposal.SuggestedAttributesJson,
                proposal.SuggestedRelationsJson);
            if (decisionType == SpaceProposalDecisionType.Accept)
                proposal.Accept();
            else
                proposal.Reject();
            var decision = SpaceProposalDecision.Create(
                execution.TenantId,
                run.Id,
                proposal.Id,
                decisionType,
                before,
                decisionType == SpaceProposalDecisionType.Accept ? before : null,
                null,
                request.ReasonCode,
                request.Comment,
                batchId);
            context.ProposalDecisions.Add(decision);
            decisions.Add(decision);
            if (decisionType == SpaceProposalDecisionType.Reject)
            {
                await ResolveProposalIssuesAsync(
                    run.Id,
                    proposal.Id,
                    decision,
                    rejected: true,
                    repaired: false,
                    cancellationToken);
            }
        }
        return decisions;
    }

    private async Task ValidateRelationsAsync(
        Guid runId,
        SpaceGenerationProposal proposal,
        SpaceAiProposalPatchResult patch,
        CancellationToken cancellationToken)
    {
        var relationPaths = patch.PatchedPaths
            .Where(RelationTypes.ContainsKey)
            .ToArray();
        if (relationPaths.Length == 0)
            return;
        using var document = JsonDocument.Parse(patch.RelationsJson);
        foreach (var path in relationPaths)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            var sourceKey = document.RootElement.GetProperty(name).GetString()!;
            if (string.Equals(sourceKey, proposal.SourceKey, StringComparison.Ordinal))
                throw PatchDenied("A proposal relation cannot reference itself.");
            var expectedType = RelationTypes[path];
            var exists = await context.GenerationProposals.AsNoTracking()
                .AnyAsync(item =>
                    item.RunId == runId &&
                    item.SourceKey == sourceKey &&
                    item.ProposalType == expectedType &&
                    item.Status != SpaceGenerationProposalStatus.Obsolete,
                    cancellationToken);
            if (!exists)
                throw PatchDenied(
                    $"Relation {path} must reference a valid {expectedType} SourceKey in this run.");
        }
    }

    private async Task ResolveProposalIssuesAsync(
        Guid runId,
        Guid proposalId,
        SpaceProposalDecision decision,
        bool rejected,
        bool repaired,
        CancellationToken cancellationToken)
    {
        if (!rejected && !repaired)
            return;
        var issues = await context.Issues
            .Where(issue =>
                issue.GenerationRunId == runId &&
                issue.GenerationProposalId == proposalId &&
                issue.Status == SpaceIssueStatus.Open &&
                issue.Severity == SpaceIssueSeverity.Blocking)
            .ToArrayAsync(cancellationToken);
        foreach (var issue in issues)
        {
            if (rejected || PatchResolvableIssueCodes.Contains(issue.Code))
                issue.ResolveByProposalDecision(decision.Id, rejected);
        }
    }

    private async Task CompleteReviewWhenReadyAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        if (run.ReviewCompletedAtUtc is not null)
            return;
        var hasUndecided = await context.GenerationProposals.AsNoTracking()
            .AnyAsync(item =>
                item.RunId == run.Id &&
                item.Status != SpaceGenerationProposalStatus.Obsolete &&
                item.Status != SpaceGenerationProposalStatus.Accepted &&
                item.Status != SpaceGenerationProposalStatus.Rejected &&
                item.Status != SpaceGenerationProposalStatus.Modified,
                cancellationToken);
        var hasOpenBlocking = await context.Issues.AsNoTracking()
            .AnyAsync(item =>
                item.GenerationRunId == run.Id &&
                item.Status == SpaceIssueStatus.Open &&
                item.Severity == SpaceIssueSeverity.Blocking,
                cancellationToken);
        if (!hasUndecided && !hasOpenBlocking)
            run.MarkReviewCompleted(UtcNow());
    }

    private async Task<SpaceAiGenerationReviewDto> BuildReviewAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        var proposals = await context.GenerationProposals.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .Select(item => new { item.Status, item.HasBlockingIssue })
            .ToArrayAsync(cancellationToken);
        var issues = await context.Issues.AsNoTracking()
            .Where(item =>
                item.GenerationRunId == run.Id &&
                item.Status == SpaceIssueStatus.Open &&
                item.Severity == SpaceIssueSeverity.Blocking)
            .Select(item => item.GenerationProposalId)
            .ToArrayAsync(cancellationToken);
        var lastDecision = await context.ProposalDecisions.AsNoTracking()
            .Where(item => item.RunId == run.Id)
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
        var rowVersion = Convert.ToBase64String(run.RowVersion);
        var etag = Hash(string.Join("\n",
            rowVersion,
            summary.TotalCount,
            summary.ProposedCount,
            summary.AcceptedCount,
            summary.RejectedCount,
            summary.ModifiedCount,
            summary.ObsoleteCount,
            summary.OpenRunBlockingIssueCount,
            summary.OpenProposalBlockingIssueCount,
            lastDecision?.ToString("D") ?? "none"));
        return new SpaceAiGenerationReviewDto(
            SpaceAiProposalDecisionContract.SchemaVersion,
            run.Id,
            run.SiteId,
            run.ModelVersionId,
            run.BaseContentRevision,
            run.Status.ToString(),
            rowVersion,
            etag,
            ToOffset(run.ReviewCompletedAtUtc),
            run.ReviewCompletedAtUtc is not null,
            options.EnableHighConfidenceBatchAccept,
            summary);
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

    private async Task EnsureFreshAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        EnsureReviewable(run);
        var revision = await context.Versions.AsNoTracking()
            .Where(item => item.Id == run.ModelVersionId)
            .Select(item => (long?)item.ContentRevision)
            .SingleOrDefaultAsync(cancellationToken);
        if (revision == run.BaseContentRevision)
            return;
        if (run.Status == SpaceGenerationRunStatus.AwaitingReview)
        {
            run.MarkStale();
            await context.SaveChangesAsync(cancellationToken);
        }
        throw Problem(
            SpaceErrorCodes.AiRunStale,
            409,
            "The draft changed after this generation run was created.",
            "create-run-based-on-latest-draft");
    }

    private static void EnsureReviewable(SpaceGenerationRun run)
    {
        if (!run.IsCurrent || run.Status != SpaceGenerationRunStatus.AwaitingReview ||
            run.ReviewCompletedAtUtc is not null)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "The generation run is not open for review decisions.",
                "refresh-review");
        }
    }

    private async Task<SpaceAiProposalDecisionResponse?> ReadReplayAsync(
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
        if (!string.Equals(record.RequestHash, requestHash,
                StringComparison.Ordinal) || record.ReplayUntilUtc < UtcNow())
            throw Problem(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was reused with different or expired input.",
                "use-new-idempotency-key");
        return (JsonSerializer.Deserialize<SpaceAiProposalDecisionResponse>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The stored proposal decision response is invalid."))
            with
        { IdempotentReplay = true };
    }

    private static NormalizedProposalQuery Normalize(SpaceAiProposalQuery query)
    {
        var limit = NormalizeLimit(query.Limit);
        return new NormalizedProposalQuery(
            ParseOptional<SpaceGenerationProposalStatus>(query.Status),
            ParseOptional<SpaceConfidenceBand>(query.ConfidenceBand),
            NormalizeText(query.ProposalType, 64),
            query.HasBlockingIssue,
            limit);
    }

    private static NormalizedIssueQuery Normalize(SpaceAiProposalIssueQuery query)
    {
        if (query.ProposalId == Guid.Empty)
            throw InvalidDecision("The issue query proposal ID is invalid.");
        return new NormalizedIssueQuery(
            query.ProposalId,
            ParseOptional<SpaceIssueSeverity>(query.Severity),
            ParseOptional<SpaceIssueStatus>(query.Status),
            NormalizeText(query.IssueCode, 100),
            NormalizeLimit(query.Limit));
    }

    private static NormalizedBatchSelection Normalize(
        SpaceAiProposalBatchSelectionDto selection)
    {
        var types = selection.ProposalTypes?
            .Select(value => NormalizeText(value, 64))
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (types is { Length: 0 } || types?.Length > 20)
            throw BatchInvalid("The batch proposal type filter is invalid.");
        return new NormalizedBatchSelection(
            ParseOptional<SpaceGenerationProposalStatus>(selection.Status),
            ParseOptional<SpaceConfidenceBand>(selection.ConfidenceBand),
            types,
            selection.HasBlockingIssue);
    }

    private static TEnum? ParseOptional<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<TEnum>(value.Trim(), true, out var parsed) ||
            !Enum.IsDefined(parsed))
            throw InvalidDecision($"The {typeof(TEnum).Name} filter is invalid.");
        return parsed;
    }

    private static SpaceProposalDecisionType ParseDecision(
        string value,
        bool allowModify)
    {
        if (!Enum.TryParse<SpaceProposalDecisionType>(
                value?.Trim(), true, out var parsed) ||
            !Enum.IsDefined(parsed) ||
            !allowModify && parsed == SpaceProposalDecisionType.Modify)
            throw InvalidDecision("The proposal decision is invalid.");
        return parsed;
    }

    private static int NormalizeLimit(int limit) =>
        limit is >= 1 and <= SpaceAiProposalDecisionContract.MaximumPageSize
            ? limit
            : throw InvalidDecision("The page size is invalid.");

    private static string? NormalizeText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum || normalized.Any(char.IsControl))
            throw InvalidDecision("A query filter is invalid.");
        return normalized;
    }

    private static void RequireNoPatch(
        IReadOnlyList<SpaceAiProposalPatchOperationDto>? patch,
        IReadOnlyList<string>? locks)
    {
        if (patch is { Count: > 0 } || locks is { Count: > 0 })
            throw InvalidDecision("Only Modify decisions may include patch data.");
    }

    private static void EnsureExpectedRowVersion(
        SpaceGenerationProposal proposal,
        string expected)
    {
        byte[] value;
        try
        {
            value = Convert.FromBase64String(expected?.Trim() ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw ReviewConflict(exception);
        }
        if (value.Length == 0 || proposal.RowVersion.Length == 0 ||
            value.Length != proposal.RowVersion.Length ||
            !CryptographicOperations.FixedTimeEquals(value, proposal.RowVersion))
            throw ReviewConflict();
    }

    private int ReadOffset(string? cursor, string resource, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = cursorCodec.Decode(cursor, resource, filterHash);
        if (state.Offset < 0)
            throw InvalidDecision("The cursor offset is invalid.");
        return state.Offset;
    }

    private string CursorResource(string kind, Guid runId, string etag) =>
        $"space.ai-review.{kind}:{execution.TenantId:D}:{runId:D}:{etag}";

    private string IdempotencyKeyHash(string operation, string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
            throw Problem(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                "supply-idempotency-key");
        return Hash($"{execution.TenantId:D}\n{operation}\n{normalized}");
    }

    private Guid RequireInternalTenant()
    {
        if (execution.IsExternal)
            throw Problem(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot review AI generation proposals.",
                "use-internal-space-editor");
        if (execution.TenantId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId ||
            execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified internal Space tenant context is required.");
        return execution.TenantId;
    }

    private DateTime UtcNow()
    {
        var value = clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return value;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return null;
        return await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    private SpaceAiProposalDto ToDto(SpaceGenerationProposal item) => new(
        item.Id,
        item.RunId,
        item.ModelVersionId,
        item.BaseContentRevision,
        item.SourceHash,
        item.SourceKey,
        item.ProposalType,
        Element(item.SuggestedGeometryJson),
        Element(item.SuggestedAttributesJson),
        Element(item.SuggestedRelationsJson),
        Element(item.SourceRefsJson),
        Element(item.EvidenceJson),
        Element(item.FieldProvenanceJson),
        item.ConfidenceScore,
        item.ConfidenceBand.ToString(),
        item.Status.ToString(),
        item.HasBlockingIssue,
        item.HumanPatchJson is null ? null : Element(item.HumanPatchJson),
        Strings(item.LockedFieldsJson),
        item.AppliedLogicalId,
        Convert.ToBase64String(item.RowVersion),
        SpaceAiProposalPatchPolicyV1.AllowedPaths(item.ProposalType));

    private static SpaceAiProposalIssueDto ToDto(SpaceModelIssue item) => new(
        item.Id,
        item.GenerationRunId!.Value,
        item.GenerationProposalId,
        item.Severity.ToString(),
        item.Code,
        item.SourceRef,
        item.Status.ToString(),
        item.ResolutionKind.ToString(),
        item.ResolutionDecisionId,
        Element(item.MessageArgsJson),
        item.SuggestedActionCode,
        new DateTimeOffset(DateTime.SpecifyKind(
            item.CreatedAtUtc,
            DateTimeKind.Utc)));

    private static SpaceAiProposalDecisionDto ToDto(
        SpaceProposalDecision item) => new(
        item.Id,
        item.DecisionBatchId,
        item.RunId,
        item.ProposalId,
        item.DecisionType.ToString(),
        Element(item.BeforeJson),
        item.AfterJson is null ? null : Element(item.AfterJson),
        Strings(item.LockedFieldsJson),
        item.ReasonCode,
        item.Comment,
        new DateTimeOffset(DateTime.SpecifyKind(
            item.CreatedAtUtc,
            DateTimeKind.Utc)),
        item.CreatedBy);

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> Strings(string? json) =>
        json is null
            ? []
            : JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(
                value.Value,
                DateTimeKind.Utc));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException RunNotFound() => Problem(
        SpaceErrorCodes.AiRunNotFound,
        404,
        "The generation run was not found.",
        "refresh-generation-runs");

    private static SpaceProblemException InvalidDecision(string detail) =>
        Problem(
            SpaceErrorCodes.AiDecisionInvalid,
            400,
            detail,
            "correct-decision-input");

    private static SpaceProblemException BatchInvalid(string detail) =>
        Problem(
            SpaceErrorCodes.AiBatchSelectionInvalid,
            422,
            detail,
            "refine-batch-selection");

    private static SpaceProblemException PatchDenied(
        string detail,
        Exception? inner = null) =>
        new(
            SpaceErrorCodes.AiPatchPathDenied,
            422,
            "The proposal patch is not allowed.",
            detail,
            "use-allowlisted-replace-paths",
            retryable: false);

    private static SpaceProblemException ReviewConflict(Exception? inner = null) =>
        Problem(
            SpaceErrorCodes.AiReviewConflict,
            409,
            "The proposal review changed concurrently.",
            "refresh-review-and-retry");

    private static SpaceProblemException Problem(
        string code,
        int status,
        string title,
        string recovery) =>
        new(code, status, title, recoveryAction: recovery);

    private sealed record NormalizedProposalQuery(
        SpaceGenerationProposalStatus? Status,
        SpaceConfidenceBand? ConfidenceBand,
        string? ProposalType,
        bool? HasBlockingIssue,
        int Limit);

    private sealed record NormalizedIssueQuery(
        Guid? ProposalId,
        SpaceIssueSeverity? Severity,
        SpaceIssueStatus? Status,
        string? IssueCode,
        int Limit);

    private sealed record NormalizedBatchSelection(
        SpaceGenerationProposalStatus? Status,
        SpaceConfidenceBand? ConfidenceBand,
        string[]? ProposalTypes,
        bool? HasBlockingIssue);
}

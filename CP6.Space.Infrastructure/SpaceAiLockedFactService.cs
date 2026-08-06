using System.Data;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAiLockedFactService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access) : ISpaceAiLockedFactService
{
    public async Task<IReadOnlyList<SpaceAiInheritedLockedFact>>
        MaterializeAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
    {
        var target = await LoadRunAsync(runId, write: true, cancellationToken);
        var current = await LoadFactsAsync(target.Id, cancellationToken);
        if (current.Length > 0 || target.BasedOnRunId is null)
            return current.Select(ToDto).ToArray();

        var source = await context.GenerationRuns.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == target.BasedOnRunId.Value,
                cancellationToken)
            ?? throw InvalidLineage("The based-on generation run was not found.");
        if (source.SiteId != target.SiteId ||
            source.ModelVersionId != target.ModelVersionId)
            throw InvalidLineage(
                "Locked facts cannot cross a site or model version boundary.");

        // Different source bytes require deterministic geometry matching and
        // explicit human confirmation. That later matching path must never be
        // promoted to an automatic lock here.
        if (!string.Equals(
                source.SourceHash,
                target.SourceHash,
                StringComparison.Ordinal))
            return [];

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await LoadFactsAsync(target.Id, cancellationToken);
            if (replay.Length > 0)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return replay.Select(ToDto).ToArray();
            }

            var proposals = await context.GenerationProposals.AsNoTracking()
                .Where(item =>
                    item.RunId == source.Id &&
                    (item.Status == SpaceGenerationProposalStatus.Modified ||
                     item.Status == SpaceGenerationProposalStatus.Applied))
                .ToArrayAsync(cancellationToken);
            if (proposals.Length == 0)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return [];
            }
            var proposalIds = proposals.Select(item => item.Id).ToArray();
            var decisions = await context.ProposalDecisions.AsNoTracking()
                .Where(item =>
                    item.RunId == source.Id &&
                    proposalIds.Contains(item.ProposalId) &&
                    item.DecisionType == SpaceProposalDecisionType.Modify)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);
            var decisionByProposal = decisions
                .GroupBy(item => item.ProposalId)
                .ToDictionary(group => group.Key, group => group.Last());

            var facts = new List<SpaceGenerationLockedFact>();
            foreach (var proposal in proposals.OrderBy(item => item.Id))
            {
                if (!decisionByProposal.TryGetValue(proposal.Id, out var decision) ||
                    decision.AfterJson is null ||
                    decision.LockedFieldsJson is null)
                    continue;
                using var after = JsonDocument.Parse(decision.AfterJson);
                var paths = JsonSerializer.Deserialize<string[]>(
                                decision.LockedFieldsJson)
                            ?? [];
                foreach (var path in paths
                             .Distinct(StringComparer.Ordinal)
                             .Order(StringComparer.Ordinal))
                {
                    var value = ReadValue(after.RootElement, path);
                    facts.Add(SpaceGenerationLockedFact.CreateSameSource(
                        execution.TenantId,
                        target.Id,
                        source.Id,
                        proposal.Id,
                        decision.Id,
                        source.SourceHash,
                        proposal.SourceKey,
                        proposal.ProposalType,
                        path,
                        value.GetRawText()));
                }
            }
            if (facts.Count == 0)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return [];
            }
            context.GenerationLockedFacts.AddRange(facts);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return facts.Select(ToDto).ToArray();
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var replay = await LoadFactsAsync(target.Id, cancellationToken);
            if (replay.Length > 0)
                return replay.Select(ToDto).ToArray();
            throw;
        }
    }

    public async Task<IReadOnlyList<SpaceAiInheritedLockedFact>> GetAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await LoadRunAsync(runId, write: false, cancellationToken);
        return (await LoadFactsAsync(run.Id, cancellationToken))
            .Select(ToDto)
            .ToArray();
    }

    private async Task<SpaceGenerationRun> LoadRunAsync(
        Guid runId,
        bool write,
        CancellationToken cancellationToken)
    {
        RequireInternalTenant();
        if (runId == Guid.Empty)
            throw InvalidLineage("A generation run ID is required.");
        var query = write
            ? context.GenerationRuns.AsQueryable()
            : context.GenerationRuns.AsNoTracking();
        var run = await query.SingleOrDefaultAsync(
            item => item.Id == runId,
            cancellationToken) ?? throw InvalidLineage(
                "The generation run was not found.");
        access.EnsureSiteAccess(run.SiteId, write);
        return run;
    }

    private Task<SpaceGenerationLockedFact[]> LoadFactsAsync(
        Guid runId,
        CancellationToken cancellationToken) =>
        context.GenerationLockedFacts.AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.SourceKey)
            .ThenBy(item => item.ProposalType)
            .ThenBy(item => item.FieldPath)
            .ToArrayAsync(cancellationToken);

    private void RequireInternalTenant()
    {
        if (execution.IsExternal)
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot load AI locked facts.",
                recoveryAction: "use-internal-space-editor");
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
            throw new SpaceTenantScopeException(
                "A verified internal Space tenant context is required.");
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

    private static JsonElement ReadValue(JsonElement snapshot, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 ||
            segments[0] is not ("attributes" or "relations") ||
            snapshot.ValueKind != JsonValueKind.Object ||
            !snapshot.TryGetProperty(segments[0], out var group) ||
            group.ValueKind != JsonValueKind.Object ||
            !group.TryGetProperty(segments[1], out var value) ||
            value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or
                JsonValueKind.Undefined)
            throw InvalidLineage(
                "A source Decision contains an invalid locked field snapshot.");
        return value.Clone();
    }

    private static SpaceAiInheritedLockedFact ToDto(
        SpaceGenerationLockedFact item)
    {
        using var value = JsonDocument.Parse(item.ValueJson);
        return new SpaceAiInheritedLockedFact(
            item.Id,
            item.RunId,
            item.BasedOnRunId,
            item.SourceProposalId,
            item.SourceDecisionId,
            item.SourceHash,
            item.SourceKey,
            item.ProposalType,
            item.FieldPath,
            value.RootElement.Clone(),
            item.MatchMethod.ToString(),
            item.MatchScore,
            item.IsConfirmed);
    }

    private static SpaceProblemException InvalidLineage(string detail) =>
        new(
            SpaceErrorCodes.AiDecisionInvalid,
            422,
            "The AI locked-fact lineage is invalid.",
            detail,
            "create-new-run-from-valid-decision-history");
}

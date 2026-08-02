using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePlanningComparisonService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access)
    : ISpacePlanningComparisonService
{
    public const int MaximumListItems = 100;
    public const int MaximumRuns = 10;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] Limitations =
    [
        "COMPARISON_USES_IMMUTABLE_COMPLETED_SIMULATION_EVIDENCE",
        "ALL_RUNS_SHARE_SITE_BASELINE_SOURCE_DATA_RATES_AND_TIME_WINDOW",
        "DELTAS_ARE_RELATIVE_TO_THE_HUMAN_SELECTED_BASELINE",
        "RISK_FLAGS_USE_CALLER_THRESHOLDS_AND_ARE_NOT_RECOMMENDATIONS",
        "CAPACITY_ASSUMPTION_DIFFERENCES_ARE_EXPOSED_AS_RISK_EVIDENCE",
        "NO_COMPOSITE_SCORE_AUTOMATED_RANKING_OR_WINNER_IS_PRODUCED",
        "DECISIONS_ARE_HUMAN_AUTHORED_APPEND_ONLY_RECORDS",
        "COMPARISONS_AND_DECISIONS_CANNOT_WRITE_OR_PUBLISH_TO_PRODUCTION",
    ];

    public async Task<CreateSpacePlanningComparisonResponse>
        CreateComparisonAsync(
            Guid siteId,
            Guid comparisonId,
            CreateSpacePlanningComparisonRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(comparisonId, nameof(comparisonId));
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);

        var normalized = Normalize(request);
        var requestHash = HashRequest(siteId, normalized);
        var existing = await context.PlanningComparisons
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == comparisonId,
                cancellationToken);
        if (existing is not null)
        {
            return await DuplicateComparisonAsync(
                existing,
                siteId,
                requestHash,
                cancellationToken);
        }

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            existing = await context.PlanningComparisons
                .SingleOrDefaultAsync(value => value.Id == comparisonId,
                    cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return await DuplicateComparisonAsync(
                    existing,
                    siteId,
                    requestHash,
                    cancellationToken);
            }

            var runIds = normalized.RunIds.ToArray();
            var runs = await context.PlanningSimulationRuns
                .AsNoTracking()
                .Where(value => runIds.Contains(value.Id))
                .ToArrayAsync(cancellationToken);
            if (runs.Length != runIds.Length ||
                runs.Any(value => value.SiteId != siteId))
            {
                throw Comparable(
                    "Every simulation run must exist in the requested site.");
            }

            var branchIds = runs.Select(value => value.BranchId).ToArray();
            var branches = await (
                    from branch in context.PlanningScenarioBranches.AsNoTracking()
                    join version in context.Versions.AsNoTracking()
                        on branch.ScenarioVersionId equals version.Id
                    join job in context.Jobs.AsNoTracking()
                        on branch.CloneJobId equals job.Id
                    join model in context.Models.AsNoTracking()
                        on branch.ModelId equals model.Id
                    where branchIds.Contains(branch.Id)
                    select new BranchAggregate(branch, version, job, model))
                .ToArrayAsync(cancellationToken);
            if (branches.Length != runs.Length)
            {
                throw Comparable(
                    "Each comparison run must belong to a distinct scenario branch.");
            }
            ValidateBranches(siteId, branches);

            var datasetIds = runs.Select(value => value.DatasetId).ToArray();
            var datasets = await context.PlanningHistoricalDatasets
                .AsNoTracking()
                .Where(value => datasetIds.Contains(value.Id))
                .ToArrayAsync(cancellationToken);
            if (datasets.Length != runs.Length)
            {
                throw Comparable(
                    "Every comparison run must retain its historical dataset evidence.");
            }
            ValidateComparableEvidence(runs, datasets);

            var runById = runs.ToDictionary(value => value.Id);
            var orderedRuns = normalized.RunIds
                .Select(value => runById[value])
                .ToArray();
            var analysis = new SpacePlanningComparisonEngine().Compare(
                normalized.BaselineRunId,
                orderedRuns.Select(MapInput).ToArray(),
                normalized.Thresholds);
            var firstBranch = branches[0].Branch;
            var firstDataset = datasets[0];
            var comparisonHash = HashComparison(
                comparisonId,
                requestHash,
                firstDataset.SourceDatasetHash,
                analysis);
            var comparison = SpacePlanningComparison.Create(
                execution.TenantId,
                comparisonId,
                new SpacePlanningComparisonData(
                    siteId,
                    firstBranch.ModelId,
                    firstBranch.BasePublishedVersionId,
                    normalized.BaselineRunId,
                    normalized.Name,
                    SpacePlanningComparisonEngine.DefinitionVersion,
                    requestHash,
                    comparisonHash,
                    firstDataset.SourceDatasetHash,
                    orderedRuns[0].CurrencyCode,
                    firstDataset.HistoricalFromUtc,
                    firstDataset.HistoricalToUtc,
                    orderedRuns.Length,
                    normalized.Thresholds.MinimumDistanceCoveragePercent,
                    normalized.Thresholds
                        .MaximumPeakCapacityUtilizationPercent,
                    normalized.Thresholds.MaximumCongestionTaskHours,
                    normalized.Thresholds.MaximumTotalCost));
            var entries = analysis.Entries.Select(value =>
                SpacePlanningComparisonEntry.Create(
                    comparison,
                    new SpacePlanningComparisonEntryData(
                        value.SequenceNo,
                        value.Run.RunId,
                        value.Run.BranchId,
                        value.Run.ScenarioVersionId,
                        value.Run.ScenarioContentRevision,
                        value.Run.RunName,
                        value.Run.RunResultHash,
                        value.IsBaseline,
                        value.Run.DistanceCoveragePercent,
                        value.Run.TotalDistanceMeters,
                        value.Run.CongestionTaskSeconds,
                        value.Run.OverloadedLocationCount,
                        value.Run.PeakCapacityUtilizationPercent,
                        value.Run.AverageCompletedTasksPerHour,
                        value.Run.PeakCompletedTasksPerHour,
                        value.Run.TotalCost,
                        value.DistanceDeltaMeters,
                        value.CongestionTaskSecondsDelta,
                        value.OverloadedLocationCountDelta,
                        value.PeakCapacityUtilizationDeltaPercentagePoints,
                        value.AverageCompletedTasksPerHourDelta,
                        value.TotalCostDelta,
                        value.Risks.Count)))
                .ToArray();
            var risks = analysis.Entries
                .Zip(entries)
                .SelectMany(pair => pair.First.Risks.Select(value =>
                    SpacePlanningComparisonRisk.Create(
                        comparison,
                        pair.Second,
                        value.Code,
                        value.Severity)))
                .ToArray();
            context.PlanningComparisons.Add(comparison);
            context.PlanningComparisonEntries.AddRange(entries);
            context.PlanningComparisonRisks.AddRange(risks);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpacePlanningComparisonResponse(
                "Created",
                Map(comparison, entries, risks));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            existing = await context.PlanningComparisons
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == comparisonId,
                    cancellationToken);
            if (existing is null)
                throw;
            return await DuplicateComparisonAsync(
                existing,
                siteId,
                requestHash,
                cancellationToken);
        }
    }

    public async Task<SpacePlanningComparisonDto> GetComparisonAsync(
        Guid siteId,
        Guid comparisonId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(comparisonId, nameof(comparisonId));
        access.EnsureSiteAccess(siteId, write: false);
        var comparison = await LoadComparisonAsync(
            siteId,
            comparisonId,
            cancellationToken);
        var (entries, risks) = await LoadEvidenceAsync(
            comparisonId,
            cancellationToken);
        return Map(comparison, entries, risks);
    }

    public async Task<SpacePlanningComparisonListResponse> GetComparisonsAsync(
        Guid siteId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        ValidateLimit(limit);
        access.EnsureSiteAccess(siteId, write: false);
        var comparisons = await context.PlanningComparisons
            .AsNoTracking()
            .Where(value => value.SiteId == siteId)
            .OrderByDescending(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var selected = comparisons.Take(limit).ToArray();
        var ids = selected.Select(value => value.Id).ToArray();
        var riskCounts = await context.PlanningComparisonRisks
            .AsNoTracking()
            .Where(value => ids.Contains(value.ComparisonId))
            .GroupBy(value => value.ComparisonId)
            .Select(group => new { ComparisonId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.ComparisonId, value => value.Count,
                cancellationToken);
        return new SpacePlanningComparisonListResponse(
            selected.Select(value => new SpacePlanningComparisonSummaryDto(
                value.Id,
                value.BaselineRunId,
                value.Name,
                value.CurrencyCode,
                value.RunCount,
                riskCounts.GetValueOrDefault(value.Id),
                Utc(value.CreatedAtUtc))).ToArray(),
            comparisons.Length > limit);
    }

    public async Task<CreateSpacePlanningDecisionResponse> CreateDecisionAsync(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        CreateSpacePlanningDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(comparisonId, nameof(comparisonId));
        Identity(decisionId, nameof(decisionId));
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);
        var normalized = Normalize(request);
        var comparison = await LoadComparisonAsync(
            siteId,
            comparisonId,
            cancellationToken);
        var requestHash = HashDecisionRequest(
            siteId,
            comparisonId,
            comparison.ComparisonHash,
            normalized);
        var existing = await context.PlanningDecisionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == decisionId,
                cancellationToken);
        if (existing is not null)
            return DuplicateDecision(existing, siteId, comparisonId, requestHash);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            existing = await context.PlanningDecisionRecords
                .SingleOrDefaultAsync(value => value.Id == decisionId,
                    cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return DuplicateDecision(
                    existing,
                    siteId,
                    comparisonId,
                    requestHash);
            }
            var decisions = await context.PlanningDecisionRecords
                .AsNoTracking()
                .Where(value => value.ComparisonId == comparisonId)
                .OrderBy(value => value.CreatedAtUtc)
                .ThenBy(value => value.Id)
                .ToArrayAsync(cancellationToken);
            ValidateDecisionChain(decisions, normalized.SupersedesDecisionId);
            if (normalized.SelectedRunId.HasValue &&
                !await context.PlanningComparisonEntries.AsNoTracking()
                    .AnyAsync(value =>
                            value.ComparisonId == comparisonId &&
                            value.RunId == normalized.SelectedRunId.Value,
                        cancellationToken))
            {
                throw DecisionInvalid(
                    "The selected run is not part of this comparison.");
            }
            var decision = SpacePlanningDecisionRecord.Create(
                execution.TenantId,
                decisionId,
                new SpacePlanningDecisionRecordData(
                    siteId,
                    comparisonId,
                    normalized.SelectedRunId,
                    normalized.SupersedesDecisionId,
                    normalized.Outcome,
                    normalized.Rationale,
                    comparison.ComparisonHash,
                    requestHash,
                    SpacePlanningComparisonEngine.DefinitionVersion));
            context.PlanningDecisionRecords.Add(decision);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpacePlanningDecisionResponse(
                "Created",
                Map(decision));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            existing = await context.PlanningDecisionRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == decisionId,
                    cancellationToken);
            if (existing is null)
                throw;
            return DuplicateDecision(existing, siteId, comparisonId, requestHash);
        }
    }

    public async Task<SpacePlanningDecisionDto> GetDecisionAsync(
        Guid siteId,
        Guid comparisonId,
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(comparisonId, nameof(comparisonId));
        Identity(decisionId, nameof(decisionId));
        access.EnsureSiteAccess(siteId, write: false);
        await LoadComparisonAsync(siteId, comparisonId, cancellationToken);
        var decision = await context.PlanningDecisionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                    value.Id == decisionId &&
                    value.SiteId == siteId &&
                    value.ComparisonId == comparisonId,
                cancellationToken)
            ?? throw DecisionNotFound();
        return Map(decision);
    }

    public async Task<SpacePlanningDecisionListResponse> GetDecisionsAsync(
        Guid siteId,
        Guid comparisonId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(comparisonId, nameof(comparisonId));
        ValidateLimit(limit);
        access.EnsureSiteAccess(siteId, write: false);
        await LoadComparisonAsync(siteId, comparisonId, cancellationToken);
        var decisions = await context.PlanningDecisionRecords
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.ComparisonId == comparisonId)
            .OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        return new SpacePlanningDecisionListResponse(
            decisions.Take(limit).Select(Map).ToArray(),
            decisions.Length > limit);
    }

    private async Task<CreateSpacePlanningComparisonResponse>
        DuplicateComparisonAsync(
            SpacePlanningComparison existing,
            Guid siteId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        if (existing.SiteId != siteId)
            throw ComparisonNotFound();
        if (!string.Equals(existing.RequestHash, requestHash,
            StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningComparisonConflict,
                409,
                "The comparison identity already has a different request.",
                recoveryAction: "use-new-comparison-id");
        }
        var (entries, risks) = await LoadEvidenceAsync(
            existing.Id,
            cancellationToken);
        return new CreateSpacePlanningComparisonResponse(
            "Duplicate",
            Map(existing, entries, risks));
    }

    private static CreateSpacePlanningDecisionResponse DuplicateDecision(
        SpacePlanningDecisionRecord existing,
        Guid siteId,
        Guid comparisonId,
        string requestHash)
    {
        if (existing.SiteId != siteId || existing.ComparisonId != comparisonId)
            throw DecisionNotFound();
        if (!string.Equals(existing.RequestHash, requestHash,
            StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningDecisionConflict,
                409,
                "The decision identity already has a different request.",
                recoveryAction: "use-new-decision-id");
        }
        return new CreateSpacePlanningDecisionResponse("Duplicate", Map(existing));
    }

    private async Task<SpacePlanningComparison> LoadComparisonAsync(
        Guid siteId,
        Guid comparisonId,
        CancellationToken cancellationToken) =>
        await context.PlanningComparisons
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                    value.Id == comparisonId && value.SiteId == siteId,
                cancellationToken)
        ?? throw ComparisonNotFound();

    private async Task<(SpacePlanningComparisonEntry[] Entries,
        SpacePlanningComparisonRisk[] Risks)> LoadEvidenceAsync(
            Guid comparisonId,
            CancellationToken cancellationToken)
    {
        var entries = await context.PlanningComparisonEntries
            .AsNoTracking()
            .Where(value => value.ComparisonId == comparisonId)
            .OrderBy(value => value.SequenceNo)
            .ToArrayAsync(cancellationToken);
        var risks = await context.PlanningComparisonRisks
            .AsNoTracking()
            .Where(value => value.ComparisonId == comparisonId)
            .OrderBy(value => value.EntryId)
            .ThenBy(value => value.Code)
            .ToArrayAsync(cancellationToken);
        return (entries, risks);
    }

    private static void ValidateBranches(
        Guid siteId,
        IReadOnlyList<BranchAggregate> values)
    {
        var first = values[0];
        foreach (var value in values)
        {
            if (value.Branch.SiteId != siteId ||
                value.Branch.ModelId != first.Branch.ModelId ||
                value.Branch.BasePublishedVersionId !=
                    first.Branch.BasePublishedVersionId ||
                value.ScenarioVersion.Purpose !=
                    SpaceModelVersionPurpose.PlanningScenario ||
                value.ScenarioVersion.Status is not (
                    SpaceVersionStatus.Draft or
                    SpaceVersionStatus.Validating or
                    SpaceVersionStatus.Ready) ||
                value.CloneJob.Status != SpaceJobStatus.Succeeded ||
                value.Model.ActiveDraftVersionId == value.ScenarioVersion.Id ||
                value.Model.CurrentPublishedVersionId == value.ScenarioVersion.Id)
            {
                throw Comparable(
                    "Runs must use production-isolated scenario branches " +
                    "cloned from the same published baseline.");
            }
        }
    }

    private static void ValidateComparableEvidence(
        IReadOnlyList<SpacePlanningSimulationRun> runs,
        IReadOnlyList<SpacePlanningHistoricalDataset> datasets)
    {
        var firstRun = runs[0];
        var firstDataset = datasets[0];
        if (runs.Select(value => value.BranchId).Distinct().Count() != runs.Count)
        {
            throw Comparable(
                "Only one simulation run may represent each scenario branch.");
        }
        foreach (var run in runs)
        {
            if (run.DefinitionVersion != firstRun.DefinitionVersion ||
                run.GeometryBasis != firstRun.GeometryBasis ||
                run.CurrencyCode != firstRun.CurrencyCode ||
                run.ThroughputWindowMinutes != firstRun.ThroughputWindowMinutes ||
                run.DistanceCostPerMeter != firstRun.DistanceCostPerMeter ||
                run.LaborCostPerHour != firstRun.LaborCostPerHour ||
                run.CongestionCostPerTaskHour !=
                    firstRun.CongestionCostPerTaskHour ||
                run.TaskCount != firstRun.TaskCount ||
                run.CompletedTaskCount != firstRun.CompletedTaskCount ||
                run.CompletedQuantity != firstRun.CompletedQuantity)
            {
                throw Comparable(
                    "Runs must share simulation definition, currency, rates, " +
                    "throughput window and completed workload evidence.");
            }
        }
        foreach (var dataset in datasets)
        {
            if (dataset.SourceDatasetHash != firstDataset.SourceDatasetHash ||
                dataset.HistoricalFromUtc != firstDataset.HistoricalFromUtc ||
                dataset.HistoricalToUtc != firstDataset.HistoricalToUtc ||
                dataset.TaskCount != firstDataset.TaskCount ||
                dataset.DefinitionVersion != firstDataset.DefinitionVersion ||
                dataset.DeidentificationVersion !=
                    firstDataset.DeidentificationVersion)
            {
                throw Comparable(
                    "Runs must use the same de-identified source dataset, " +
                    "historical window and workload size.");
            }
        }
    }

    private static void ValidateDecisionChain(
        IReadOnlyList<SpacePlanningDecisionRecord> decisions,
        Guid? supersedesDecisionId)
    {
        if (decisions.Count == 0)
        {
            if (supersedesDecisionId.HasValue)
                throw DecisionInvalid("The first decision cannot supersede a record.");
            return;
        }
        if (!supersedesDecisionId.HasValue)
        {
            throw DecisionInvalid(
                "A later decision must supersede the current decision.");
        }
        var superseded = decisions
            .Select(value => value.SupersedesDecisionId)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet();
        var latest = decisions
            .Where(value => !superseded.Contains(value.Id))
            .ToArray();
        if (latest.Length != 1 || latest[0].Id != supersedesDecisionId.Value)
        {
            throw DecisionInvalid(
                "supersedesDecisionId must identify the current decision head.");
        }
    }

    private static SpacePlanningComparisonRunInput MapInput(
        SpacePlanningSimulationRun value) =>
        new(
            value.Id,
            value.BranchId,
            value.ScenarioVersionId,
            value.ScenarioContentRevision,
            value.Name,
            value.ResultHash,
            value.DefaultQuantityCapacity,
            value.DefaultConcurrentTaskCapacity,
            value.LocationCapacityOverrideCount,
            value.DistanceCoveragePercent,
            value.TotalDistanceMeters,
            value.CongestionTaskSeconds,
            value.OverloadedLocationCount,
            value.PeakCapacityUtilizationPercent,
            value.AverageCompletedTasksPerHour,
            value.PeakCompletedTasksPerHour,
            value.TotalCost);

    private static SpacePlanningComparisonDto Map(
        SpacePlanningComparison comparison,
        IReadOnlyList<SpacePlanningComparisonEntry> entries,
        IReadOnlyList<SpacePlanningComparisonRisk> risks)
    {
        if (!comparison.CreatedBy.HasValue ||
            comparison.CreatedAtUtc == default ||
            entries.Count != comparison.RunCount ||
            entries.Count(value => value.IsBaseline) != 1 ||
            entries.Single(value => value.IsBaseline).RunId !=
                comparison.BaselineRunId)
        {
            throw new InvalidOperationException(
                "Stored planning comparison evidence is invalid.");
        }
        var riskLookup = risks.ToLookup(value => value.EntryId);
        return new SpacePlanningComparisonDto(
            comparison.Id,
            comparison.SiteId,
            comparison.ModelId,
            comparison.BasePublishedVersionId,
            comparison.BaselineRunId,
            comparison.Name,
            "Completed",
            comparison.DefinitionVersion,
            comparison.RequestHash,
            comparison.ComparisonHash,
            comparison.SourceDatasetHash,
            comparison.CurrencyCode,
            comparison.HistoricalFromUtc,
            comparison.HistoricalToUtc,
            new SpacePlanningComparisonThresholdsDto(
                comparison.MinimumDistanceCoveragePercent,
                comparison.MaximumPeakCapacityUtilizationPercent,
                comparison.MaximumCongestionTaskHours,
                comparison.MaximumTotalCost),
            entries.OrderBy(value => value.SequenceNo).Select(value =>
                new SpacePlanningComparisonEntryDto(
                    value.SequenceNo,
                    value.RunId,
                    value.BranchId,
                    value.ScenarioVersionId,
                    value.ScenarioContentRevision,
                    value.RunName,
                    value.RunResultHash,
                    value.IsBaseline,
                    new SpacePlanningComparisonMetricsDto(
                        value.DistanceCoveragePercent,
                        value.TotalDistanceMeters,
                        value.CongestionTaskSeconds,
                        Round(value.CongestionTaskSeconds / 3_600m),
                        value.OverloadedLocationCount,
                        value.PeakCapacityUtilizationPercent,
                        value.AverageCompletedTasksPerHour,
                        value.PeakCompletedTasksPerHour,
                        value.TotalCost),
                    new SpacePlanningComparisonDeltaDto(
                        value.DistanceDeltaMeters,
                        value.CongestionTaskSecondsDelta,
                        value.OverloadedLocationCountDelta,
                        value.PeakCapacityUtilizationDeltaPercentagePoints,
                        value.AverageCompletedTasksPerHourDelta,
                        value.TotalCostDelta),
                    riskLookup[value.Id]
                        .OrderBy(risk => risk.Severity)
                        .ThenBy(risk => risk.Code)
                        .Select(risk => new SpacePlanningComparisonRiskDto(
                            risk.Code,
                            risk.Severity.ToString()))
                        .ToArray())).ToArray(),
            false,
            false,
            Utc(comparison.CreatedAtUtc),
            comparison.CreatedBy.Value,
            Limitations);
    }

    private static SpacePlanningDecisionDto Map(
        SpacePlanningDecisionRecord value)
    {
        if (!value.CreatedBy.HasValue || value.CreatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "Stored planning decision evidence is invalid.");
        }
        return new SpacePlanningDecisionDto(
            value.Id,
            value.SiteId,
            value.ComparisonId,
            value.SelectedRunId,
            value.SupersedesDecisionId,
            value.Outcome.ToString(),
            value.Rationale,
            value.ComparisonHash,
            value.DefinitionVersion,
            true,
            false,
            false,
            Utc(value.CreatedAtUtc),
            value.CreatedBy.Value);
    }

    private static NormalizedComparisonRequest Normalize(
        CreateSpacePlanningComparisonRequest request)
    {
        var name = Text(request.Name, 200, "name");
        Identity(request.BaselineRunId, "baselineRunId");
        var runIds = (request.RunIds ?? [])
            .Select(value =>
            {
                Identity(value, "runId");
                return value;
            })
            .Distinct()
            .ToArray();
        if (runIds.Length is < 2 or > MaximumRuns ||
            runIds.Length != request.RunIds?.Count ||
            !runIds.Contains(request.BaselineRunId))
        {
            throw Invalid(
                "runIds must contain two to ten distinct runs including " +
                "baselineRunId.");
        }
        var ordered = new[] { request.BaselineRunId }
            .Concat(runIds.Where(value => value != request.BaselineRunId)
                .OrderBy(value => value))
            .ToArray();
        var thresholds = new SpacePlanningComparisonThresholds(
            Percentage(request.MinimumDistanceCoveragePercent,
                "minimumDistanceCoveragePercent"),
            NonNegative(request.MaximumPeakCapacityUtilizationPercent,
                4,
                "maximumPeakCapacityUtilizationPercent"),
            NonNegative(request.MaximumCongestionTaskHours,
                6,
                "maximumCongestionTaskHours"),
            request.MaximumTotalCost.HasValue
                ? NonNegative(request.MaximumTotalCost.Value,
                    6,
                    "maximumTotalCost")
                : null);
        return new NormalizedComparisonRequest(
            name,
            request.BaselineRunId,
            ordered,
            thresholds);
    }

    private static NormalizedDecisionRequest Normalize(
        CreateSpacePlanningDecisionRequest request)
    {
        if (!Enum.TryParse<SpacePlanningDecisionOutcome>(
                request.Outcome?.Trim(),
                true,
                out var outcome) ||
            !Enum.IsDefined(outcome))
        {
            throw DecisionInvalid(
                "outcome must be Selected, Deferred or RejectedAll.");
        }
        if (outcome == SpacePlanningDecisionOutcome.Selected)
        {
            if (!request.SelectedRunId.HasValue ||
                request.SelectedRunId.Value == Guid.Empty)
            {
                throw DecisionInvalid(
                    "A Selected decision requires selectedRunId.");
            }
        }
        else if (request.SelectedRunId.HasValue)
        {
            throw DecisionInvalid(
                "Only a Selected decision may include selectedRunId.");
        }
        if (request.SupersedesDecisionId == Guid.Empty)
            throw DecisionInvalid("supersedesDecisionId cannot be empty.");
        return new NormalizedDecisionRequest(
            outcome,
            request.SelectedRunId,
            Rationale(request.Rationale),
            request.SupersedesDecisionId);
    }

    private static string HashRequest(
        Guid siteId,
        NormalizedComparisonRequest request) =>
        Sha256(JsonSerializer.Serialize(new
        {
            siteId,
            request.Name,
            request.BaselineRunId,
            request.RunIds,
            request.Thresholds,
            definitionVersion = SpacePlanningComparisonEngine.DefinitionVersion,
        }, Json));

    private static string HashComparison(
        Guid comparisonId,
        string requestHash,
        string sourceDatasetHash,
        SpacePlanningComparisonAnalysis analysis) =>
        Sha256(JsonSerializer.Serialize(new
        {
            comparisonId,
            requestHash,
            sourceDatasetHash,
            definitionVersion = SpacePlanningComparisonEngine.DefinitionVersion,
            analysis.BaselineRunId,
            entries = analysis.Entries.Select(value => new
            {
                value.SequenceNo,
                value.Run.RunId,
                value.Run.BranchId,
                value.Run.ScenarioVersionId,
                value.Run.ScenarioContentRevision,
                value.Run.RunResultHash,
                value.Run.DistanceCoveragePercent,
                value.Run.TotalDistanceMeters,
                value.Run.CongestionTaskSeconds,
                value.Run.OverloadedLocationCount,
                value.Run.PeakCapacityUtilizationPercent,
                value.Run.AverageCompletedTasksPerHour,
                value.Run.PeakCompletedTasksPerHour,
                value.Run.TotalCost,
                value.DistanceDeltaMeters,
                value.CongestionTaskSecondsDelta,
                value.OverloadedLocationCountDelta,
                value.PeakCapacityUtilizationDeltaPercentagePoints,
                value.AverageCompletedTasksPerHourDelta,
                value.TotalCostDelta,
                risks = value.Risks.Select(risk => new
                {
                    risk.Code,
                    severity = risk.Severity.ToString(),
                }),
            }),
        }, Json));

    private static string HashDecisionRequest(
        Guid siteId,
        Guid comparisonId,
        string comparisonHash,
        NormalizedDecisionRequest request) =>
        Sha256(JsonSerializer.Serialize(new
        {
            siteId,
            comparisonId,
            comparisonHash,
            outcome = request.Outcome.ToString(),
            request.SelectedRunId,
            request.Rationale,
            request.SupersedesDecisionId,
            definitionVersion = SpacePlanningComparisonEngine.DefinitionVersion,
        }, Json));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private void EnsureInternal()
    {
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioInternalOnly,
                403,
                "Planning comparisons are restricted to internal users.",
                recoveryAction: "use-internal-planning-account");
        }
    }

    private static void Identity(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw Invalid($"{name} is required.");
    }

    private static string Text(string value, int maximumLength, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                $"{name} must contain at most {maximumLength} characters.");
        }
        return normalized;
    }

    private static string Rationale(string value)
    {
        var normalized = value?.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 2_000 ||
            normalized.Any(character =>
                char.IsControl(character) && character is not '\n' and not '\t'))
        {
            throw DecisionInvalid(
                "rationale must contain between 1 and 2000 characters.");
        }
        return normalized;
    }

    private static decimal Percentage(decimal value, string name)
    {
        if (value is < 0 or > 100 || decimal.Round(value, 4) != value)
            throw Invalid($"{name} must be between 0 and 100 with scale 4.");
        return value;
    }

    private static decimal NonNegative(decimal value, int scale, string name)
    {
        if (value < 0 || decimal.Round(value, scale) != value)
            throw Invalid($"{name} must be non-negative with scale {scale}.");
        return value;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > MaximumListItems)
            throw Invalid($"limit must be between 1 and {MaximumListItems}.");
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.ToEven);

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningComparisonRequestInvalid,
            422,
            detail,
            recoveryAction: "correct-comparison-parameters");

    private static SpaceProblemException Comparable(string detail) =>
        new(
            SpaceErrorCodes.PlanningComparisonEvidenceInvalid,
            422,
            detail,
            recoveryAction: "select-comparable-simulation-runs");

    private static SpaceProblemException ComparisonNotFound() =>
        new(
            SpaceErrorCodes.PlanningComparisonNotFound,
            404,
            "The planning comparison was not found.",
            recoveryAction: "refresh");

    private static SpaceProblemException DecisionInvalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningDecisionInvalid,
            422,
            detail,
            recoveryAction: "correct-decision-record");

    private static SpaceProblemException DecisionNotFound() =>
        new(
            SpaceErrorCodes.PlanningDecisionNotFound,
            404,
            "The planning decision was not found.",
            recoveryAction: "refresh");

    private sealed record NormalizedComparisonRequest(
        string Name,
        Guid BaselineRunId,
        IReadOnlyList<Guid> RunIds,
        SpacePlanningComparisonThresholds Thresholds);

    private sealed record NormalizedDecisionRequest(
        SpacePlanningDecisionOutcome Outcome,
        Guid? SelectedRunId,
        string Rationale,
        Guid? SupersedesDecisionId);

    private sealed record BranchAggregate(
        SpacePlanningScenarioBranch Branch,
        SpaceModelVersion ScenarioVersion,
        SpaceJob CloneJob,
        SpaceModel Model);
}

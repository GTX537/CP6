using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePlanningSimulationService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access)
    : ISpacePlanningSimulationService
{
    public const int MaximumListItems = 100;
    public const int MaximumReturnedLocationResults = 100;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] Limitations =
    [
        "DISTANCE_IS_RACK_CELL_STRAIGHT_LINE_NOT_AISLE_ROUTE",
        "CROSS_FLOOR_AND_MISSING_SOURCE_GEOMETRY_DISTANCE_IS_UNKNOWN",
        "CONGESTION_USES_HISTORICAL_TASK_EXECUTION_WINDOW_OVERLAP",
        "CAPACITY_QUANTITY_UNITS_ARE_CALLER_DEFINED_NOT_LOCATION_MAX_LOAD",
        "LABOR_USES_UNIONED_TOKENIZED_WORKER_INTERVALS",
        "COST_IS_A_PARAMETERIZED_PLANNING_ESTIMATE_NOT_FINANCIAL_ACTUAL",
        "THIS_IS_NOT_HIGH_PRECISION_PHYSICAL_OR_TRAFFIC_SIMULATION",
        "SIMULATION_RESULTS_CANNOT_WRITE_OR_PUBLISH_TO_PRODUCTION",
        "MULTI_SCENARIO_RANKING_AND_DECISIONS_BELONG_TO_E12_S04",
    ];

    public async Task<CreateSpacePlanningSimulationRunResponse> CreateAsync(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CreateSpacePlanningSimulationRunRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(branchId, nameof(branchId));
        Identity(runId, nameof(runId));
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);

        var normalized = Normalize(request);
        var requestHash = HashRequest(siteId, branchId, normalized);
        var existing = await context.PlanningSimulationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == runId, cancellationToken);
        if (existing is not null)
        {
            return await DuplicateAsync(
                existing,
                siteId,
                branchId,
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
            existing = await context.PlanningSimulationRuns
                .SingleOrDefaultAsync(
                    value => value.Id == runId,
                    cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return await DuplicateAsync(
                    existing,
                    siteId,
                    branchId,
                    requestHash,
                    cancellationToken);
            }

            var branch = await LoadReadyBranchAsync(
                siteId,
                branchId,
                cancellationToken);
            var dataset = await context.PlanningHistoricalDatasets
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.Id == normalized.DatasetId &&
                        value.SiteId == siteId &&
                        value.BranchId == branchId &&
                        value.ModelId == branch.Branch.ModelId &&
                        value.ScenarioVersionId == branch.ScenarioVersion.Id,
                    cancellationToken)
                ?? throw DatasetInvalid(
                    "The immutable historical dataset is not bound to this " +
                    "scenario branch and snapshot.");
            var tasks = await context.PlanningHistoricalTasks
                .AsNoTracking()
                .Where(value => value.DatasetId == dataset.Id)
                .OrderBy(value => value.SequenceNo)
                .ToArrayAsync(cancellationToken);
            if (tasks.Length != dataset.TaskCount)
            {
                throw DatasetInvalid(
                    "The historical dataset task evidence is incomplete.");
            }
            if (dataset.HistoricalToUtc - dataset.HistoricalFromUtc <
                TimeSpan.FromSeconds(1))
            {
                throw DatasetInvalid(
                    "The historical dataset window must span at least one " +
                    "second for throughput simulation.");
            }

            var locations = await LoadLocationsAsync(
                branch.ScenarioVersion.Id,
                tasks,
                normalized,
                cancellationToken);
            var analysis = new SpacePlanningSimulationEngine().Analyze(
                tasks.Select(value => new SpacePlanningSimulationTaskInput(
                    value.SequenceNo,
                    value.WorkerToken,
                    value.Outcome,
                    value.OriginalCreatedAtUtc,
                    value.OriginalCompletedAtUtc,
                    value.FromLocationLogicalId,
                    value.ToLocationLogicalId,
                    value.Quantity)).ToArray(),
                locations,
                new SpacePlanningSimulationParameters(
                    dataset.HistoricalFromUtc,
                    dataset.HistoricalToUtc,
                    normalized.ThroughputWindowMinutes,
                    normalized.DistanceCostPerMeter,
                    normalized.LaborCostPerHour,
                    normalized.CongestionCostPerTaskHour));
            var resultHash = HashResult(
                runId,
                branch.ScenarioVersion.ContentRevision,
                dataset.RequestHash,
                requestHash,
                analysis);
            var run = SpacePlanningSimulationRun.Create(
                execution.TenantId,
                runId,
                new SpacePlanningSimulationRunData(
                    siteId,
                    branch.Branch.ModelId,
                    branchId,
                    branch.ScenarioVersion.Id,
                    branch.ScenarioVersion.ContentRevision,
                    dataset.Id,
                    normalized.Name,
                    SpacePlanningSimulationEngine.DefinitionVersion,
                    requestHash,
                    dataset.RequestHash,
                    resultHash,
                    SpacePlanningSimulationEngine.GeometryBasis,
                    normalized.CurrencyCode,
                    normalized.DefaultQuantityCapacity,
                    normalized.DefaultConcurrentTaskCapacity,
                    normalized.LocationCapacities.Count,
                    normalized.ThroughputWindowMinutes,
                    normalized.DistanceCostPerMeter,
                    normalized.LaborCostPerHour,
                    normalized.CongestionCostPerTaskHour,
                    analysis.TaskCount,
                    analysis.CompletedTaskCount,
                    analysis.CompletedQuantity,
                    analysis.DistanceEligibleTaskCount,
                    analysis.TotalDistanceMeters,
                    analysis.DistanceCoveragePercent,
                    analysis.PeakConcurrentTasks,
                    analysis.CongestionSeconds,
                    analysis.CongestionTaskSeconds,
                    analysis.OverloadedLocationCount,
                    analysis.PeakCapacityUtilizationPercent,
                    analysis.AverageCompletedTasksPerHour,
                    analysis.PeakCompletedTasksPerHour,
                    analysis.AverageCompletedQuantityPerHour,
                    analysis.PeakCompletedQuantityPerHour,
                    analysis.LaborHours,
                    analysis.DistanceCost,
                    analysis.LaborCost,
                    analysis.CongestionCost,
                    analysis.TotalCost));
            var locationResults = analysis.Locations
                .Select(value =>
                    SpacePlanningSimulationLocationResult.Create(
                        run,
                        new SpacePlanningSimulationLocationResultData(
                            value.LocationLogicalId,
                            value.TaskCount,
                            value.CompletedTaskCount,
                            value.TotalQuantity,
                            value.DistanceEligibleTaskCount,
                            value.TotalDistanceMeters,
                            value.QuantityCapacity,
                            value.ConcurrentTaskCapacity,
                            value.PeakConcurrentTasks,
                            value.PeakConcurrentQuantity,
                            value.CapacityUtilizationPercent,
                            value.CongestionSeconds,
                            value.CongestionTaskSeconds)))
                .ToArray();
            context.PlanningSimulationRuns.Add(run);
            context.PlanningSimulationLocationResults.AddRange(
                locationResults);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpacePlanningSimulationRunResponse(
                "Created",
                Map(run, dataset, locationResults));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            existing = await context.PlanningSimulationRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == runId,
                    cancellationToken);
            if (existing is null)
                throw;
            return await DuplicateAsync(
                existing,
                siteId,
                branchId,
                requestHash,
                cancellationToken);
        }
    }

    public async Task<SpacePlanningSimulationRunDto> GetAsync(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(branchId, nameof(branchId));
        Identity(runId, nameof(runId));
        access.EnsureSiteAccess(siteId, write: false);
        return await GetCoreAsync(
            siteId,
            branchId,
            runId,
            cancellationToken);
    }

    public async Task<SpacePlanningSimulationRunListResponse> GetListAsync(
        Guid siteId,
        Guid branchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(branchId, nameof(branchId));
        if (limit is < 1 or > MaximumListItems)
            throw Invalid($"limit must be between 1 and {MaximumListItems}.");
        access.EnsureSiteAccess(siteId, write: false);
        await EnsureBranchExistsAsync(siteId, branchId, cancellationToken);

        var values = await context.PlanningSimulationRuns
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.BranchId == branchId)
            .OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        return new SpacePlanningSimulationRunListResponse(
            values.Take(limit).Select(MapSummary).ToArray(),
            values.Length > limit);
    }

    private async Task<CreateSpacePlanningSimulationRunResponse>
        DuplicateAsync(
            SpacePlanningSimulationRun existing,
            Guid siteId,
            Guid branchId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        if (existing.SiteId != siteId ||
            existing.BranchId != branchId ||
            !string.Equals(
                existing.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningSimulationConflict,
                409,
                "The planning simulation run identity is already in use.",
                recoveryAction: "use-new-simulation-run-id");
        }
        return new CreateSpacePlanningSimulationRunResponse(
            "Duplicate",
            await GetCoreAsync(
                siteId,
                branchId,
                existing.Id,
                cancellationToken));
    }

    private async Task<SpacePlanningSimulationRunDto> GetCoreAsync(
        Guid siteId,
        Guid branchId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await context.PlanningSimulationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.Id == runId &&
                    value.SiteId == siteId &&
                    value.BranchId == branchId,
                cancellationToken)
            ?? throw NotFound();
        var dataset = await context.PlanningHistoricalDatasets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == run.DatasetId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Stored simulation dataset evidence is missing.");
        var locations = await context.PlanningSimulationLocationResults
            .AsNoTracking()
            .Where(value => value.RunId == runId)
            .ToArrayAsync(cancellationToken);
        if (locations.Length == 0 ||
            locations.Sum(value => value.TaskCount) != run.TaskCount)
        {
            throw new InvalidOperationException(
                "Stored simulation location evidence is inconsistent.");
        }
        return Map(run, dataset, locations);
    }

    private async Task<BranchAggregate> LoadReadyBranchAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var value = await (
                from branch in context.PlanningScenarioBranches.AsNoTracking()
                join version in context.Versions.AsNoTracking()
                    on branch.ScenarioVersionId equals version.Id
                join job in context.Jobs.AsNoTracking()
                    on branch.CloneJobId equals job.Id
                join model in context.Models.AsNoTracking()
                    on branch.ModelId equals model.Id
                where branch.Id == branchId && branch.SiteId == siteId
                select new BranchAggregate(branch, version, job, model))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioNotFound,
                404,
                "The planning scenario branch was not found.",
                recoveryAction: "refresh");
        if (value.ScenarioVersion.Purpose !=
                SpaceModelVersionPurpose.PlanningScenario ||
            value.ScenarioVersion.Status is not (
                SpaceVersionStatus.Draft or
                SpaceVersionStatus.Validating or
                SpaceVersionStatus.Ready) ||
            value.CloneJob.Status != SpaceJobStatus.Succeeded ||
            value.Model.ActiveDraftVersionId == value.ScenarioVersion.Id ||
            value.Model.CurrentPublishedVersionId ==
                value.ScenarioVersion.Id)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningSimulationBranchNotReady,
                409,
                "The scenario clone must be complete and production-isolated " +
                "before running a simulation.",
                recoveryAction: "wait-for-scenario-clone");
        }
        return value;
    }

    private async Task EnsureBranchExistsAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (!await context.PlanningScenarioBranches
                .AsNoTracking()
                .AnyAsync(
                    value =>
                        value.Id == branchId &&
                        value.SiteId == siteId,
                    cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioNotFound,
                404,
                "The planning scenario branch was not found.",
                recoveryAction: "refresh");
        }
    }

    private async Task<IReadOnlyDictionary<Guid,
        SpacePlanningSimulationLocationInput>> LoadLocationsAsync(
            Guid scenarioVersionId,
            IReadOnlyList<SpacePlanningHistoricalTask> tasks,
            NormalizedRequest request,
            CancellationToken cancellationToken)
    {
        var requestedIds = tasks
            .SelectMany(value => value.FromLocationLogicalId.HasValue
                ? new[]
                {
                    value.FromLocationLogicalId.Value,
                    value.ToLocationLogicalId,
                }
                : [value.ToLocationLogicalId])
            .Distinct()
            .ToArray();
        var destinationIds = tasks
            .Select(value => value.ToLocationLogicalId)
            .Distinct()
            .ToHashSet();
        var unusedOverrides = request.LocationCapacities
            .Select(value => value.LocationLogicalId)
            .Where(value => !destinationIds.Contains(value))
            .ToArray();
        if (unusedOverrides.Length > 0)
        {
            throw Invalid(
                "Every location capacity override must target a dataset " +
                "destination location.");
        }

        var locations = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == scenarioVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                requestedIds.Contains(value.LogicalId))
            .Select(value => new LocationRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.RackLogicalId,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo))
            .ToArrayAsync(cancellationToken);
        var missing = requestedIds.Except(
            locations.Select(value => value.LogicalId)).ToArray();
        if (missing.Length > 0)
        {
            throw GeometryInvalid(
                $"{missing.Length} dataset locations are no longer active " +
                "in the scenario content revision.");
        }

        var rackIds = locations
            .Where(value => value.RackLogicalId.HasValue)
            .Select(value => value.RackLogicalId!.Value)
            .Distinct()
            .ToArray();
        var racks = await context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == scenarioVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                rackIds.Contains(value.LogicalId))
            .Select(value => new RackRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.X,
                value.Y,
                value.RotationZ))
            .ToArrayAsync(cancellationToken);
        var levels = await context.RackLevelRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == scenarioVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                rackIds.Contains(value.RackLogicalId))
            .Select(value => new RackLevelRow(
                value.RackLogicalId,
                value.LevelNo,
                value.CellWidth,
                value.CellDepth))
            .ToArrayAsync(cancellationToken);
        var rackById = racks.ToDictionary(value => value.LogicalId);
        var levelByPosition = levels.ToDictionary(
            value => (value.RackLogicalId, value.LevelNo));
        var capacities = request.LocationCapacities.ToDictionary(
            value => value.LocationLogicalId);
        var result = new Dictionary<Guid,
            SpacePlanningSimulationLocationInput>();
        foreach (var location in locations)
        {
            decimal? x = null;
            decimal? y = null;
            if (location.RackLogicalId.HasValue)
            {
                if (!rackById.TryGetValue(
                        location.RackLogicalId.Value,
                        out var rack) ||
                    rack.FloorLogicalId != location.FloorLogicalId ||
                    !levelByPosition.TryGetValue(
                        (rack.LogicalId, location.LevelNo),
                        out var level))
                {
                    throw GeometryInvalid(
                        "An active dataset location has inconsistent rack " +
                        "or level geometry.");
                }
                var angle = (double)rack.RotationZ * Math.PI / 180d;
                var localX = (location.ColumnNo - 0.5m) * level.CellWidth;
                var localY = (location.DepthNo - 0.5m) * level.CellDepth;
                x = rack.X + localX * (decimal)Math.Cos(angle) -
                    localY * (decimal)Math.Sin(angle);
                y = rack.Y + localX * (decimal)Math.Sin(angle) +
                    localY * (decimal)Math.Cos(angle);
            }
            capacities.TryGetValue(location.LogicalId, out var capacity);
            result.Add(
                location.LogicalId,
                new SpacePlanningSimulationLocationInput(
                    location.LogicalId,
                    location.FloorLogicalId,
                    x,
                    y,
                    capacity?.QuantityCapacity ??
                        request.DefaultQuantityCapacity,
                    capacity?.ConcurrentTaskCapacity ??
                        request.DefaultConcurrentTaskCapacity));
        }
        return result;
    }

    private static NormalizedRequest Normalize(
        CreateSpacePlanningSimulationRunRequest request)
    {
        Identity(request.DatasetId, "datasetId");
        var name = Text(request.Name, 200, "name");
        var defaultCapacity = QuantityCapacity(
            request.DefaultQuantityCapacity,
            "defaultQuantityCapacity");
        var defaultConcurrency = ConcurrentCapacity(
            request.DefaultConcurrentTaskCapacity,
            "defaultConcurrentTaskCapacity");
        if (request.ThroughputWindowMinutes is < 1 or > 1_440)
        {
            throw Invalid(
                "throughputWindowMinutes must be between 1 and 1440.");
        }
        var distanceRate = Rate(
            request.DistanceCostPerMeter,
            "distanceCostPerMeter");
        var laborRate = Rate(
            request.LaborCostPerHour,
            "laborCostPerHour");
        var congestionRate = Rate(
            request.CongestionCostPerTaskHour,
            "congestionCostPerTaskHour");
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant();
        if (currency is null ||
            currency.Length != 3 ||
            !currency.All(char.IsAsciiLetter))
        {
            throw Invalid(
                "currencyCode must be a three-letter alphabetic code.");
        }
        var overrides = (request.LocationCapacities ?? [])
            .Select(value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                Identity(value.LocationLogicalId, "locationLogicalId");
                return new NormalizedLocationCapacity(
                    value.LocationLogicalId,
                    QuantityCapacity(
                        value.QuantityCapacity,
                        "quantityCapacity"),
                    ConcurrentCapacity(
                        value.ConcurrentTaskCapacity,
                        "concurrentTaskCapacity"));
            })
            .OrderBy(value => value.LocationLogicalId)
            .ToArray();
        if (overrides.Length > 10_000 ||
            overrides.Select(value => value.LocationLogicalId)
                .Distinct().Count() != overrides.Length)
        {
            throw Invalid(
                "locationCapacities must contain at most 10000 unique " +
                "location identities.");
        }
        return new NormalizedRequest(
            name,
            request.DatasetId,
            defaultCapacity,
            defaultConcurrency,
            request.ThroughputWindowMinutes,
            distanceRate,
            laborRate,
            congestionRate,
            currency,
            overrides);
    }

    private static string HashRequest(
        Guid siteId,
        Guid branchId,
        NormalizedRequest request)
    {
        var canonical = JsonSerializer.Serialize(
            new
            {
                siteId,
                branchId,
                request.Name,
                request.DatasetId,
                request.DefaultQuantityCapacity,
                request.DefaultConcurrentTaskCapacity,
                request.ThroughputWindowMinutes,
                request.DistanceCostPerMeter,
                request.LaborCostPerHour,
                request.CongestionCostPerTaskHour,
                request.CurrencyCode,
                locationCapacities = request.LocationCapacities.Select(
                    value => new
                    {
                        value.LocationLogicalId,
                        value.QuantityCapacity,
                        value.ConcurrentTaskCapacity,
                    }),
                definitionVersion =
                    SpacePlanningSimulationEngine.DefinitionVersion,
            },
            Json);
        return Sha256(canonical);
    }

    private static string HashResult(
        Guid runId,
        long scenarioContentRevision,
        string datasetRequestHash,
        string requestHash,
        SpacePlanningSimulationAnalysis analysis)
    {
        var canonical = JsonSerializer.Serialize(
            new
            {
                runId,
                scenarioContentRevision,
                datasetRequestHash,
                requestHash,
                definitionVersion =
                    SpacePlanningSimulationEngine.DefinitionVersion,
                geometryBasis = SpacePlanningSimulationEngine.GeometryBasis,
                analysis.TaskCount,
                analysis.CompletedTaskCount,
                analysis.CompletedQuantity,
                analysis.DistanceEligibleTaskCount,
                analysis.TotalDistanceMeters,
                analysis.DistanceCoveragePercent,
                analysis.PeakConcurrentTasks,
                analysis.CongestionSeconds,
                analysis.CongestionTaskSeconds,
                analysis.OverloadedLocationCount,
                analysis.PeakCapacityUtilizationPercent,
                analysis.HistoricalWindowHours,
                analysis.AverageCompletedTasksPerHour,
                analysis.PeakCompletedTasksPerHour,
                analysis.AverageCompletedQuantityPerHour,
                analysis.PeakCompletedQuantityPerHour,
                analysis.LaborHours,
                analysis.DistanceCost,
                analysis.LaborCost,
                analysis.CongestionCost,
                analysis.TotalCost,
                locations = analysis.Locations
                    .OrderBy(value => value.LocationLogicalId),
            },
            Json);
        return Sha256(canonical);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpacePlanningSimulationRunDto Map(
        SpacePlanningSimulationRun run,
        SpacePlanningHistoricalDataset dataset,
        IReadOnlyCollection<SpacePlanningSimulationLocationResult> locations)
    {
        if (!run.CreatedBy.HasValue || run.CreatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "Stored simulation audit evidence is invalid.");
        }
        var ordered = locations
            .Select(Map)
            .OrderByDescending(value => value.IsOverloaded)
            .ThenByDescending(value => value.CongestionTaskSeconds)
            .ThenByDescending(value => value.CapacityUtilizationPercent)
            .ThenBy(value => value.LocationLogicalId)
            .ToArray();
        var historicalHours = Round(
            (dataset.HistoricalToUtc - dataset.HistoricalFromUtc).Ticks /
                (decimal)TimeSpan.TicksPerHour,
            9);
        return new SpacePlanningSimulationRunDto(
            run.Id,
            run.SiteId,
            run.BranchId,
            run.ScenarioVersionId,
            run.ScenarioContentRevision,
            run.DatasetId,
            run.Name,
            "Completed",
            run.DefinitionVersion,
            run.DatasetRequestHash,
            run.ResultHash,
            false,
            false,
            new SpacePlanningSimulationParametersDto(
                run.DefaultQuantityCapacity,
                run.DefaultConcurrentTaskCapacity,
                run.ThroughputWindowMinutes,
                run.DistanceCostPerMeter,
                run.LaborCostPerHour,
                run.CongestionCostPerTaskHour,
                run.CurrencyCode,
                run.LocationCapacityOverrideCount),
            new SpacePlanningSimulationDistanceDto(
                run.GeometryBasis,
                run.TaskCount,
                run.DistanceEligibleTaskCount,
                run.TaskCount - run.DistanceEligibleTaskCount,
                run.DistanceCoveragePercent,
                run.TotalDistanceMeters,
                run.DistanceEligibleTaskCount == 0
                    ? null
                    : Round(
                        run.TotalDistanceMeters /
                            run.DistanceEligibleTaskCount,
                        6)),
            new SpacePlanningSimulationCongestionDto(
                locations.Count,
                run.OverloadedLocationCount,
                run.PeakConcurrentTasks,
                run.CongestionSeconds,
                run.CongestionTaskSeconds,
                Round(run.CongestionTaskSeconds / 3_600m, 6)),
            new SpacePlanningSimulationCapacityDto(
                locations.Count,
                run.OverloadedLocationCount,
                run.PeakCapacityUtilizationPercent,
                "CALLER_DEFINED_TASK_QUANTITY_UNITS"),
            new SpacePlanningSimulationThroughputDto(
                run.CompletedTaskCount,
                run.CompletedQuantity,
                historicalHours,
                run.ThroughputWindowMinutes,
                run.AverageCompletedTasksPerHour,
                run.PeakCompletedTasksPerHour,
                run.AverageCompletedQuantityPerHour,
                run.PeakCompletedQuantityPerHour),
            new SpacePlanningSimulationCostDto(
                run.CurrencyCode,
                run.LaborHours,
                run.DistanceCost,
                run.LaborCost,
                run.CongestionCost,
                run.TotalCost,
                "UNIONED_TOKENIZED_WORKER_INTERVALS_PLUS_UNASSIGNED_TASKS"),
            ordered.Take(MaximumReturnedLocationResults).ToArray(),
            ordered.Length > MaximumReturnedLocationResults,
            new DateTimeOffset(DateTime.SpecifyKind(
                run.CreatedAtUtc,
                DateTimeKind.Utc)),
            run.CreatedBy.Value,
            Limitations);
    }

    private static SpacePlanningSimulationLocationResultDto Map(
        SpacePlanningSimulationLocationResult value)
    {
        var overloaded =
            value.PeakConcurrentTasks > value.ConcurrentTaskCapacity ||
            value.PeakConcurrentQuantity > value.QuantityCapacity;
        return new SpacePlanningSimulationLocationResultDto(
            value.LocationLogicalId,
            value.TaskCount,
            value.CompletedTaskCount,
            value.TotalQuantity,
            value.DistanceEligibleTaskCount,
            value.TotalDistanceMeters,
            value.QuantityCapacity,
            value.ConcurrentTaskCapacity,
            value.PeakConcurrentTasks,
            value.PeakConcurrentQuantity,
            value.CapacityUtilizationPercent,
            value.CongestionSeconds,
            value.CongestionTaskSeconds,
            overloaded);
    }

    private static SpacePlanningSimulationRunSummaryDto MapSummary(
        SpacePlanningSimulationRun run) =>
        new(
            run.Id,
            run.DatasetId,
            run.ScenarioContentRevision,
            run.Name,
            "Completed",
            run.CurrencyCode,
            run.TaskCount,
            run.DistanceCoveragePercent,
            run.TotalDistanceMeters,
            run.OverloadedLocationCount,
            run.AverageCompletedTasksPerHour,
            run.TotalCost,
            new DateTimeOffset(DateTime.SpecifyKind(
                run.CreatedAtUtc,
                DateTimeKind.Utc)));

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
                "Planning simulations are restricted to internal users.",
                recoveryAction: "use-internal-planning-account");
        }
    }

    private static void Identity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw Invalid($"{parameterName} is required.");
    }

    private static string Text(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                $"{parameterName} must contain at most " +
                $"{maximumLength} characters.");
        }
        return normalized;
    }

    private static decimal QuantityCapacity(decimal value, string name)
    {
        if (value <= 0 ||
            value > 99_999_999_999_999.9999m ||
            decimal.Round(value, 4) != value)
        {
            throw Invalid(
                $"{name} must be positive with at most four decimal places.");
        }
        return value;
    }

    private static int ConcurrentCapacity(int value, string name)
    {
        if (value is < 1 or > 10_000)
            throw Invalid($"{name} must be between 1 and 10000.");
        return value;
    }

    private static decimal Rate(decimal value, string name)
    {
        if (value < 0 ||
            value > 1_000_000 ||
            decimal.Round(value, 6) != value)
        {
            throw Invalid(
                $"{name} must be between 0 and 1000000 with at most six " +
                "decimal places.");
        }
        return value;
    }

    private static decimal Round(decimal value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningSimulationRequestInvalid,
            422,
            detail,
            recoveryAction: "correct-simulation-parameters");

    private static SpaceProblemException DatasetInvalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningSimulationDatasetInvalid,
            422,
            detail,
            recoveryAction: "select-valid-historical-dataset");

    private static SpaceProblemException GeometryInvalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningSimulationGeometryInvalid,
            422,
            detail,
            recoveryAction: "repair-scenario-geometry-or-import-new-dataset");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.PlanningSimulationNotFound,
            404,
            "The planning simulation run was not found.",
            recoveryAction: "refresh");

    private sealed record NormalizedRequest(
        string Name,
        Guid DatasetId,
        decimal DefaultQuantityCapacity,
        int DefaultConcurrentTaskCapacity,
        int ThroughputWindowMinutes,
        decimal DistanceCostPerMeter,
        decimal LaborCostPerHour,
        decimal CongestionCostPerTaskHour,
        string CurrencyCode,
        IReadOnlyList<NormalizedLocationCapacity> LocationCapacities);

    private sealed record NormalizedLocationCapacity(
        Guid LocationLogicalId,
        decimal QuantityCapacity,
        int ConcurrentTaskCapacity);

    private sealed record LocationRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        int ColumnNo,
        int LevelNo,
        int DepthNo);

    private sealed record RackRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        int X,
        int Y,
        decimal RotationZ);

    private sealed record RackLevelRow(
        Guid RackLogicalId,
        int LevelNo,
        int CellWidth,
        int CellDepth);

    private sealed record BranchAggregate(
        SpacePlanningScenarioBranch Branch,
        SpaceModelVersion ScenarioVersion,
        SpaceJob CloneJob,
        SpaceModel Model);
}

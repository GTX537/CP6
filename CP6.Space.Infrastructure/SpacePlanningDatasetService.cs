using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePlanningDatasetService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access)
    : ISpacePlanningDatasetService
{
    public const string DefinitionVersion =
        "space-planning-historical-dataset-v1";
    public const string DeidentificationVersion =
        "sha256-upstream-token-v1";
    public const int MaximumTasks = 10_000;
    public const int MaximumListItems = 100;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] Limitations =
    [
        "ONLY_UPSTREAM_SHA256_TOKENIZED_IDENTIFIERS_ARE_ACCEPTED",
        "RAW_ORDER_WORKER_AND_MATERIAL_IDENTIFIERS_ARE_NOT_STORED",
        "REPLAY_TIME_IS_DETERMINISTIC_AND_DOES_NOT_WAIT_IN_REAL_TIME",
        "DATASET_AND_REPLAY_RESULTS_CANNOT_WRITE_PRODUCTION",
        "SCENARIO_LOCATION_IDENTITIES_MUST_EXIST_AT_IMPORT_TIME",
    ];

    public async Task<CreateSpacePlanningHistoricalDatasetResponse>
        CreateAsync(
            Guid siteId,
            Guid branchId,
            Guid datasetId,
            CreateSpacePlanningHistoricalDatasetRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(branchId, nameof(branchId));
        Identity(datasetId, nameof(datasetId));
        ArgumentNullException.ThrowIfNull(request);
        access.EnsureSiteAccess(siteId, write: true);

        var normalized = Normalize(request);
        var requestHash = HashRequest(
            siteId,
            branchId,
            normalized);
        var existing = await context.PlanningHistoricalDatasets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == datasetId,
                cancellationToken);
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
            existing = await context.PlanningHistoricalDatasets
                .SingleOrDefaultAsync(
                    value => value.Id == datasetId,
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
            await EnsureLocationsAsync(
                branch.ScenarioVersion.Id,
                normalized.Tasks,
                cancellationToken);

            var dataset = SpacePlanningHistoricalDataset.Create(
                execution.TenantId,
                datasetId,
                new SpacePlanningHistoricalDatasetData(
                    siteId,
                    branch.Branch.ModelId,
                    branchId,
                    branch.ScenarioVersion.Id,
                    normalized.Name,
                    normalized.Clock.HistoricalFromUtc,
                    normalized.Clock.HistoricalToUtc,
                    normalized.Clock.ReplayStartUtc,
                    normalized.Clock.SpeedFactor,
                    normalized.Tasks.Count,
                    normalized.SourceDatasetHash,
                    requestHash,
                    DefinitionVersion,
                    DeidentificationVersion));
            var tasks = normalized.Tasks
                .Select((value, index) =>
                    SpacePlanningHistoricalTask.Create(
                        dataset,
                        new SpacePlanningHistoricalTaskData(
                            index + 1,
                            value.TaskToken,
                            value.WorkerToken,
                            value.TaskType,
                            value.Outcome,
                            value.OriginalCreatedAtUtc,
                            value.OriginalCompletedAtUtc,
                            normalized.Clock.Map(
                                value.OriginalCreatedAtUtc),
                            normalized.Clock.Map(
                                value.OriginalCompletedAtUtc),
                            value.FromLocationLogicalId,
                            value.ToLocationLogicalId,
                            value.Quantity)))
                .ToArray();
            context.PlanningHistoricalDatasets.Add(dataset);
            context.PlanningHistoricalTasks.AddRange(tasks);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpacePlanningHistoricalDatasetResponse(
                "Created",
                Map(dataset, tasks));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            existing = await context.PlanningHistoricalDatasets
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == datasetId,
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

    public async Task<SpacePlanningHistoricalDatasetDto> GetAsync(
        Guid siteId,
        Guid branchId,
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        Identity(siteId, nameof(siteId));
        Identity(branchId, nameof(branchId));
        Identity(datasetId, nameof(datasetId));
        access.EnsureSiteAccess(siteId, write: false);
        return await GetCoreAsync(
            siteId,
            branchId,
            datasetId,
            cancellationToken);
    }

    public async Task<SpacePlanningHistoricalDatasetListResponse>
        GetListAsync(
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
        await EnsureBranchExistsAsync(
            siteId,
            branchId,
            cancellationToken);

        var values = await context.PlanningHistoricalDatasets
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.BranchId == branchId)
            .OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        return new SpacePlanningHistoricalDatasetListResponse(
            values.Take(limit).Select(MapSummary).ToArray(),
            values.Length > limit);
    }

    private async Task<CreateSpacePlanningHistoricalDatasetResponse>
        DuplicateAsync(
            SpacePlanningHistoricalDataset existing,
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
                SpaceErrorCodes.PlanningDatasetConflict,
                409,
                "The historical dataset identity is already in use.",
                recoveryAction: "use-new-dataset-id");
        }
        return new CreateSpacePlanningHistoricalDatasetResponse(
            "Duplicate",
            await GetCoreAsync(
                siteId,
                branchId,
                existing.Id,
                cancellationToken));
    }

    private async Task<SpacePlanningHistoricalDatasetDto> GetCoreAsync(
        Guid siteId,
        Guid branchId,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var dataset = await context.PlanningHistoricalDatasets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.Id == datasetId &&
                    value.SiteId == siteId &&
                    value.BranchId == branchId,
                cancellationToken)
            ?? throw NotFound();
        var tasks = await context.PlanningHistoricalTasks
            .AsNoTracking()
            .Where(value => value.DatasetId == datasetId)
            .OrderBy(value => value.SequenceNo)
            .ToArrayAsync(cancellationToken);
        if (tasks.Length != dataset.TaskCount)
        {
            throw new InvalidOperationException(
                "The historical dataset task count is inconsistent.");
        }
        return Map(dataset, tasks);
    }

    private async Task<BranchAggregate> LoadReadyBranchAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var value = await (
                from branch in
                    context.PlanningScenarioBranches.AsNoTracking()
                join version in context.Versions.AsNoTracking()
                    on branch.ScenarioVersionId equals version.Id
                join job in context.Jobs.AsNoTracking()
                    on branch.CloneJobId equals job.Id
                join model in context.Models.AsNoTracking()
                    on branch.ModelId equals model.Id
                where
                    branch.Id == branchId &&
                    branch.SiteId == siteId
                select new BranchAggregate(
                    branch,
                    version,
                    job,
                    model))
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
            value.Model.ActiveDraftVersionId ==
                value.ScenarioVersion.Id ||
            value.Model.CurrentPublishedVersionId ==
                value.ScenarioVersion.Id)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningDatasetBranchNotReady,
                409,
                "The scenario clone must be complete and production-isolated " +
                "before importing historical tasks.",
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

    private async Task EnsureLocationsAsync(
        Guid scenarioVersionId,
        IReadOnlyList<NormalizedTask> tasks,
        CancellationToken cancellationToken)
    {
        var requested = tasks
            .SelectMany(value =>
                value.FromLocationLogicalId.HasValue
                    ? new[]
                    {
                        value.FromLocationLogicalId.Value,
                        value.ToLocationLogicalId,
                    }
                    : [value.ToLocationLogicalId])
            .Distinct()
            .ToArray();
        var found = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == scenarioVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active &&
                requested.Contains(value.LogicalId))
            .Select(value => value.LogicalId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var missing = requested.Except(found).ToArray();
        if (missing.Length > 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningDatasetLocationInvalid,
                422,
                $"{missing.Length} task location identities are absent " +
                "from the scenario snapshot.",
                recoveryAction: "correct-task-locations");
        }
    }

    private static NormalizedRequest Normalize(
        CreateSpacePlanningHistoricalDatasetRequest request)
    {
        if (!request.ConfirmDeidentified)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningDatasetDeidentificationRequired,
                422,
                "The caller must attest that task and worker identifiers " +
                "were irreversibly tokenized before upload.",
                recoveryAction: "deidentify-source-dataset");
        }
        var name = Text(request.Name, 200, "name");
        var sourceHash = Token(
            request.SourceDatasetHash,
            "sourceDatasetHash");
        if (decimal.Round(request.ReplaySpeedFactor, 4) !=
            request.ReplaySpeedFactor)
        {
            throw Invalid(
                "replaySpeedFactor supports at most four decimal places.");
        }
        SpaceReplayClock clock;
        try
        {
            clock = SpaceReplayClock.Create(
                request.HistoricalFromUtc,
                request.HistoricalToUtc,
                request.ReplayStartUtc,
                request.ReplaySpeedFactor);
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            throw Invalid("The replay clock definition is invalid.");
        }
        if (clock.HistoricalToUtc - clock.HistoricalFromUtc >
            TimeSpan.FromDays(366))
        {
            throw Invalid(
                "The historical window cannot exceed 366 days.");
        }
        if (request.Tasks is null ||
            request.Tasks.Count is < 1 or > MaximumTasks)
        {
            throw Invalid(
                $"tasks must contain between 1 and {MaximumTasks} rows.");
        }

        var normalized = request.Tasks.Select(NormalizeTask)
            .OrderBy(value => value.OriginalCreatedAtUtc)
            .ThenBy(value => value.TaskToken, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Select(value => value.TaskToken).Distinct(
                StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw Invalid("Task tokens must be unique within a dataset.");
        }
        foreach (var task in normalized)
        {
            if (task.OriginalCreatedAtUtc < clock.HistoricalFromUtc ||
                task.OriginalCompletedAtUtc > clock.HistoricalToUtc ||
                task.OriginalCreatedAtUtc >
                    task.OriginalCompletedAtUtc)
            {
                throw Invalid(
                    "Every task must be contained in the historical window.");
            }
        }
        return new NormalizedRequest(name, sourceHash, clock, normalized);
    }

    private static NormalizedTask NormalizeTask(
        CreateSpacePlanningHistoricalTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.TryParse<SpacePlanningTaskType>(
                request.TaskType?.Trim(),
                ignoreCase: true,
                out var taskType) ||
            !Enum.IsDefined(taskType))
        {
            throw Invalid("A supported taskType is required.");
        }
        if (!Enum.TryParse<SpacePlanningTaskOutcome>(
                request.Outcome?.Trim(),
                ignoreCase: true,
                out var outcome) ||
            !Enum.IsDefined(outcome))
        {
            throw Invalid("A supported outcome is required.");
        }
        if (request.OriginalCreatedAtUtc.Offset != TimeSpan.Zero ||
            request.OriginalCompletedAtUtc.Offset != TimeSpan.Zero ||
            request.ToLocationLogicalId == Guid.Empty ||
            request.FromLocationLogicalId == Guid.Empty ||
            request.Quantity <= 0 ||
            request.Quantity > 99_999_999_999_999.9999m ||
            decimal.Round(request.Quantity, 4) != request.Quantity)
        {
            throw Invalid("A historical task row is invalid.");
        }
        return new NormalizedTask(
            Token(request.TaskToken, "taskToken"),
            string.IsNullOrWhiteSpace(request.WorkerToken)
                ? null
                : Token(request.WorkerToken, "workerToken"),
            taskType,
            outcome,
            request.OriginalCreatedAtUtc,
            request.OriginalCompletedAtUtc,
            request.FromLocationLogicalId,
            request.ToLocationLogicalId,
            request.Quantity);
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
                request.SourceDatasetHash,
                request.Clock.HistoricalFromUtc,
                request.Clock.HistoricalToUtc,
                request.Clock.ReplayStartUtc,
                request.Clock.SpeedFactor,
                tasks = request.Tasks.Select(value => new
                {
                    value.TaskToken,
                    value.WorkerToken,
                    taskType = value.TaskType.ToString(),
                    outcome = value.Outcome.ToString(),
                    value.OriginalCreatedAtUtc,
                    value.OriginalCompletedAtUtc,
                    value.FromLocationLogicalId,
                    value.ToLocationLogicalId,
                    value.Quantity,
                }),
                definitionVersion = DefinitionVersion,
                deidentificationVersion = DeidentificationVersion,
            },
            Json);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static SpacePlanningHistoricalDatasetDto Map(
        SpacePlanningHistoricalDataset dataset,
        IReadOnlyCollection<SpacePlanningHistoricalTask> tasks)
    {
        if (!dataset.CreatedBy.HasValue || dataset.CreatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "Stored historical dataset audit evidence is invalid.");
        }
        var createdAtUtc = new DateTimeOffset(DateTime.SpecifyKind(
            dataset.CreatedAtUtc,
            DateTimeKind.Utc));
        var clock = SpaceReplayClock.Create(dataset);
        return new SpacePlanningHistoricalDatasetDto(
            dataset.Id,
            dataset.BranchId,
            dataset.SiteId,
            dataset.ScenarioVersionId,
            dataset.Name,
            dataset.TaskCount,
            dataset.SourceDatasetHash,
            dataset.DefinitionVersion,
            dataset.DeidentificationVersion,
            true,
            false,
            Map(clock),
            tasks.OrderBy(value => value.SequenceNo)
                .Select(Map)
                .ToArray(),
            createdAtUtc,
            dataset.CreatedBy.Value,
            Limitations);
    }

    private static SpacePlanningHistoricalDatasetSummaryDto MapSummary(
        SpacePlanningHistoricalDataset dataset)
    {
        var clock = SpaceReplayClock.Create(dataset);
        return new SpacePlanningHistoricalDatasetSummaryDto(
            dataset.Id,
            dataset.BranchId,
            dataset.ScenarioVersionId,
            dataset.Name,
            dataset.TaskCount,
            dataset.HistoricalFromUtc,
            dataset.HistoricalToUtc,
            dataset.ReplayStartUtc,
            clock.ReplayEndUtc,
            dataset.ReplaySpeedFactor,
            new DateTimeOffset(DateTime.SpecifyKind(
                dataset.CreatedAtUtc,
                DateTimeKind.Utc)));
    }

    private static SpacePlanningReplayClockDto Map(SpaceReplayClock value) =>
        new(
            value.HistoricalFromUtc,
            value.HistoricalToUtc,
            value.ReplayStartUtc,
            value.ReplayEndUtc,
            value.SpeedFactor,
            DurationSeconds(
                value.HistoricalToUtc - value.HistoricalFromUtc),
            DurationSeconds(value.ReplayEndUtc - value.ReplayStartUtc));

    private static SpacePlanningHistoricalTaskDto Map(
        SpacePlanningHistoricalTask value) =>
        new(
            value.SequenceNo,
            value.TaskToken,
            value.WorkerToken,
            value.TaskType.ToString(),
            value.Outcome.ToString(),
            value.OriginalCreatedAtUtc,
            value.OriginalCompletedAtUtc,
            value.ReplayCreatedAtUtc,
            value.ReplayCompletedAtUtc,
            value.FromLocationLogicalId,
            value.ToLocationLogicalId,
            value.Quantity);

    private static decimal DurationSeconds(TimeSpan value) =>
        value.Ticks / (decimal)TimeSpan.TicksPerSecond;

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
                "Planning historical datasets are restricted to internal users.",
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

    private static string Token(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningDatasetDeidentificationRequired,
                422,
                $"{parameterName} must be a 64-character SHA-256 token.",
                recoveryAction: "deidentify-source-dataset");
        }
        return normalized;
    }

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningDatasetInvalid,
            422,
            detail,
            recoveryAction: "correct-historical-dataset");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.PlanningDatasetNotFound,
            404,
            "The planning historical dataset was not found.",
            recoveryAction: "refresh");

    private sealed record NormalizedRequest(
        string Name,
        string SourceDatasetHash,
        SpaceReplayClock Clock,
        IReadOnlyList<NormalizedTask> Tasks);

    private sealed record NormalizedTask(
        string TaskToken,
        string? WorkerToken,
        SpacePlanningTaskType TaskType,
        SpacePlanningTaskOutcome Outcome,
        DateTimeOffset OriginalCreatedAtUtc,
        DateTimeOffset OriginalCompletedAtUtc,
        Guid? FromLocationLogicalId,
        Guid ToLocationLogicalId,
        decimal Quantity);

    private sealed record BranchAggregate(
        SpacePlanningScenarioBranch Branch,
        SpaceModelVersion ScenarioVersion,
        SpaceJob CloneJob,
        SpaceModel Model);
}

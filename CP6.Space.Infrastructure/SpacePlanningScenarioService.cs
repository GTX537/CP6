using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePlanningScenarioService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceDesignAccessEvaluator access)
    : ISpacePlanningScenarioService
{
    public const string DefinitionVersion = "space-planning-scenario-v1";
    public const string CloneProcessorVersion = "space-clone-v1";
    public const int MaximumListItems = 100;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private static readonly string[] Limitations =
    [
        "SCENARIO_BRANCH_NEVER_OCCUPIES_PRODUCTION_DRAFT_SLOT",
        "SCENARIO_VERSION_CANNOT_ENTER_PRODUCTION_PUBLISH_LIFECYCLE",
        "BASE_PUBLISHED_VERSION_IS_PINNED_AT_BRANCH_CREATION",
        "LATER_PRODUCTION_CHANGES_ARE_NOT_MERGED_AUTOMATICALLY",
    ];

    public async Task<CreateSpacePlanningScenarioBranchResponse>
        CreateBranchAsync(
            Guid siteId,
            Guid branchId,
            CreateSpacePlanningScenarioBranchRequest request,
            CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(branchId, "branchId");
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdentity(
            request.BasePublishedVersionId,
            "basePublishedVersionId");
        access.EnsureSiteAccess(siteId, write: true);

        var name = NormalizeName(request.Name);
        var requestHash = HashRequest(
            siteId,
            request.BasePublishedVersionId,
            name);
        var existing = await context.PlanningScenarioBranches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == branchId,
                cancellationToken);
        if (existing is not null)
        {
            return await DuplicateAsync(
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
            existing = await context.PlanningScenarioBranches
                .SingleOrDefaultAsync(
                    value => value.Id == branchId,
                    cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return await DuplicateAsync(
                    existing,
                    siteId,
                    requestHash,
                    cancellationToken);
            }

            var model = await context.Models.SingleOrDefaultAsync(
                            value => value.SiteId == siteId,
                            cancellationToken)
                        ?? throw NotFound(
                            "The Space model for the planning site was not found.");
            if (model.CurrentPublishedVersionId !=
                request.BasePublishedVersionId)
            {
                throw BaseInvalid(
                    "The planning branch must pin the site's current " +
                    "Published version.");
            }

            var source = await context.Versions.SingleOrDefaultAsync(
                             value =>
                                 value.Id == request.BasePublishedVersionId &&
                                 value.ModelId == model.Id,
                             cancellationToken)
                         ?? throw BaseInvalid(
                             "The planning base version was not found.");
            if (source.Purpose != SpaceModelVersionPurpose.Production ||
                source.Status != SpaceVersionStatus.Published ||
                string.IsNullOrWhiteSpace(source.ContentHash))
            {
                throw BaseInvalid(
                    "A complete production Published snapshot is required.");
            }

            var nextVersionNo =
                (await context.Versions
                    .Where(value => value.ModelId == model.Id)
                    .MaxAsync(
                        value => (long?)value.VersionNo,
                        cancellationToken) ?? 0) + 1;
            var target =
                SpaceModelVersion.CreateInitializingPlanningScenario(
                    execution.TenantId,
                    model.Id,
                    nextVersionNo,
                    name,
                    source.Id,
                    branchId);
            var payload = new SpaceVersionClonePayload(
                model.Id,
                source.Id,
                target.Id,
                branchId,
                branchId);
            var enqueue = new SpaceJobEnqueueRequest(
                SpaceJobType.CloneVersion,
                SpaceJobSubjectType.ModelVersion,
                target.Id,
                source.ContentHash,
                CloneProcessorVersion,
                $"planning-scenario:{branchId:N}",
                Priority: 45,
                MaxAttempts: 3,
                PayloadJson: JsonSerializer.Serialize(payload, Json));
            var now = RequireUtcNow();
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                enqueue.JobType,
                enqueue.SubjectType,
                enqueue.SubjectId,
                SpaceJobBusinessKey.Create(enqueue),
                enqueue.InputHash,
                enqueue.Priority,
                enqueue.MaxAttempts,
                execution.ActorId,
                now,
                CorrelationId(branchId),
                enqueue.PayloadJson);
            var branch = SpacePlanningScenarioBranch.Create(
                execution.TenantId,
                branchId,
                new SpacePlanningScenarioBranchData(
                    siteId,
                    model.Id,
                    source.Id,
                    target.Id,
                    job.Id,
                    name,
                    DefinitionVersion,
                    requestHash));
            context.Versions.Add(target);
            context.Jobs.Add(job);
            context.PlanningScenarioBranches.Add(branch);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpacePlanningScenarioBranchResponse(
                "Created",
                await GetBranchCoreAsync(
                    siteId,
                    branchId,
                    cancellationToken));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            existing = await context.PlanningScenarioBranches
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == branchId,
                    cancellationToken);
            if (existing is null)
                throw;
            return await DuplicateAsync(
                existing,
                siteId,
                requestHash,
                cancellationToken);
        }
    }

    public async Task<SpacePlanningScenarioBranchDto> GetBranchAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(branchId, "branchId");
        access.EnsureSiteAccess(siteId, write: false);
        return await GetBranchCoreAsync(
            siteId,
            branchId,
            cancellationToken);
    }

    public async Task<SpacePlanningScenarioBranchListResponse>
        GetBranchesAsync(
            Guid siteId,
            int limit,
            CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        EnsureIdentity(siteId, "siteId");
        if (limit is < 1 or > MaximumListItems)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioConflict,
                422,
                $"limit must be between 1 and {MaximumListItems}.",
                recoveryAction: "choose-valid-limit");
        }
        access.EnsureSiteAccess(siteId, write: false);
        var rows = await Query(siteId)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        return new SpacePlanningScenarioBranchListResponse(
            rows.Take(limit).Select(Map).ToArray(),
            rows.Length > limit);
    }

    private async Task<CreateSpacePlanningScenarioBranchResponse>
        DuplicateAsync(
            SpacePlanningScenarioBranch existing,
            Guid siteId,
            string requestHash,
            CancellationToken cancellationToken)
    {
        if (existing.SiteId != siteId ||
            !string.Equals(
                existing.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioConflict,
                409,
                "The planning branch identity is already in use.",
                recoveryAction: "use-new-branch-id");
        }
        return new CreateSpacePlanningScenarioBranchResponse(
            "Duplicate",
            await GetBranchCoreAsync(
                siteId,
                existing.Id,
                cancellationToken));
    }

    private async Task<SpacePlanningScenarioBranchDto> GetBranchCoreAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var value = await Query(siteId, branchId)
            .SingleOrDefaultAsync(cancellationToken);
        return value is null
            ? throw NotFound("The planning scenario branch was not found.")
            : Map(value);
    }

    private IQueryable<BranchAggregate> Query(
        Guid? siteId = null,
        Guid? branchId = null) =>
        from branch in context.PlanningScenarioBranches.AsNoTracking()
        where
            (!siteId.HasValue || branch.SiteId == siteId.Value) &&
            (!branchId.HasValue || branch.Id == branchId.Value)
        join baseVersion in context.Versions.AsNoTracking()
            on branch.BasePublishedVersionId equals baseVersion.Id
        join scenarioVersion in context.Versions.AsNoTracking()
            on branch.ScenarioVersionId equals scenarioVersion.Id
        join job in context.Jobs.AsNoTracking()
            on branch.CloneJobId equals job.Id
        join model in context.Models.AsNoTracking()
            on branch.ModelId equals model.Id
        orderby branch.CreatedAtUtc descending, branch.Id descending
        select new BranchAggregate(
            branch,
            baseVersion,
            scenarioVersion,
            job,
            model);

    private static SpacePlanningScenarioBranchDto Map(
        BranchAggregate value)
    {
        var isolated =
            value.BaseVersion.Purpose ==
                SpaceModelVersionPurpose.Production &&
            value.ScenarioVersion.Purpose ==
                SpaceModelVersionPurpose.PlanningScenario &&
            value.Model.ActiveDraftVersionId != value.ScenarioVersion.Id &&
            value.Model.CurrentPublishedVersionId != value.ScenarioVersion.Id;
        if (!isolated ||
            !value.Branch.CreatedBy.HasValue ||
            value.Branch.CreatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "Stored planning scenario isolation evidence is invalid.");
        }
        var createdAtUtc = DateTime.SpecifyKind(
            value.Branch.CreatedAtUtc,
            DateTimeKind.Utc);

        return new SpacePlanningScenarioBranchDto(
            value.Branch.Id,
            value.Branch.SiteId,
            value.Branch.ModelId,
            value.BaseVersion.Id,
            FormatVersion(value.BaseVersion.VersionNo),
            value.ScenarioVersion.Id,
            FormatVersion(value.ScenarioVersion.VersionNo),
            value.Branch.Name,
            BranchStatus(value.ScenarioVersion, value.Job),
            value.ScenarioVersion.Status.ToString(),
            value.Job.Id,
            value.Job.Status.ToString(),
            new DateTimeOffset(createdAtUtc),
            value.Branch.CreatedBy.Value,
            value.Branch.DefinitionVersion,
            true,
            Limitations);
    }

    private static string BranchStatus(
        SpaceModelVersion version,
        SpaceJob job) =>
        job.Status switch
        {
            SpaceJobStatus.Queued or SpaceJobStatus.Running =>
                "Initializing",
            SpaceJobStatus.Succeeded
                when version.Status is
                    SpaceVersionStatus.Draft or
                    SpaceVersionStatus.Validating or
                    SpaceVersionStatus.Ready =>
                "Ready",
            SpaceJobStatus.Cancelled => "Cancelled",
            SpaceJobStatus.Failed or SpaceJobStatus.DeadLetter => "Failed",
            _ => "Inconsistent",
        };

    private static string FormatVersion(long value) =>
        $"v{value:D4}";

    private static string HashRequest(
        Guid siteId,
        Guid basePublishedVersionId,
        string name)
    {
        var canonical = JsonSerializer.Serialize(
            new
            {
                siteId,
                basePublishedVersionId,
                name,
                definitionVersion = DefinitionVersion,
            },
            Json);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private Guid CorrelationId(Guid fallback) =>
        execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : fallback;

    private DateTime RequireUtcNow()
    {
        var value = clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return value;
    }

    private static string NormalizeName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 200 ||
            normalized.Any(char.IsControl))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioConflict,
                422,
                "A planning branch name of at most 200 characters is required.",
                recoveryAction: "correct-branch-name");
        }
        return normalized;
    }

    private static void EnsureIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioConflict,
                422,
                $"{parameterName} is required.",
                recoveryAction: "correct-request");
        }
    }

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
                "Planning scenario branches are restricted to internal users.",
                recoveryAction: "use-internal-planning-account");
        }
    }

    private static SpaceProblemException BaseInvalid(string detail) =>
        new(
            SpaceErrorCodes.PlanningScenarioBaseInvalid,
            409,
            detail,
            recoveryAction: "refresh-current-published-version");

    private static SpaceProblemException NotFound(string detail) =>
        new(
            SpaceErrorCodes.PlanningScenarioNotFound,
            404,
            detail,
            recoveryAction: "refresh");

    private sealed record BranchAggregate(
        SpacePlanningScenarioBranch Branch,
        SpaceModelVersion BaseVersion,
        SpaceModelVersion ScenarioVersion,
        SpaceJob Job,
        SpaceModel Model);
}

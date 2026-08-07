using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.Infrastructure;

public sealed class DefaultSpaceValidationProfileProvider :
    ISpaceValidationProfileProvider
{
    private readonly IServiceProvider _services;

    public DefaultSpaceValidationProfileProvider(IServiceProvider services) =>
        _services =
            services ?? throw new ArgumentNullException(nameof(services));

    public async Task<SpaceValidationProfile> GetProfileAsync(
        Guid tenantId,
        Guid siteId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site is required.", nameof(siteId));
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Correlation ID is required.",
                nameof(correlationId));
        }

        var adapter = _services.GetRequiredService<ISpaceWmsAdapter>();
        var snapshot = await adapter.GetCapabilitiesAsync(
            new SpaceWmsContext(
                tenantId,
                siteId,
                siteId.ToString("N"),
                correlationId),
            cancellationToken);
        return SpaceValidationProfile.FromCapabilities(snapshot);
    }
}

public sealed class SpaceValidationService : ISpaceValidationService
{
    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceValidationProfileProvider _profiles;
    private readonly SpaceValidationEngine _engine;
    private readonly EfSpaceValidationSnapshotReader _snapshots;

    public SpaceValidationService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceValidationProfileProvider profiles,
        SpaceValidationEngine engine)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _profiles = profiles;
        _engine = engine;
        _snapshots = new EfSpaceValidationSnapshotReader(context);
    }

    public async Task<CreateSpaceValidationResponse> RequestValidationAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty)
            throw Invalid("A non-empty versionId is required.");

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        if (_context.Database.IsSqlServer())
        {
            var lockResource =
                $"CP6:Space:Validation:{_execution.TenantId:D}:{versionId:D}";
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = {lockResource},
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 15000;
                IF @result < 0
                    THROW 51000, 'SPACE_VALIDATION_LOCK_UNAVAILABLE', 1;
                """,
                cancellationToken);
        }
        var scope = await RequireScopeAsync(versionId, cancellationToken);
        _access.EnsureSiteAccess(scope.Model.SiteId, write: true);
        if (scope.Version.Status is not (
                SpaceVersionStatus.Draft or
                SpaceVersionStatus.Ready or
                SpaceVersionStatus.Validating))
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                $"Version state {scope.Version.Status} cannot start or reuse validation.",
                "wait-for-version-state");
        }
        var correlationId =
            _execution is ISpaceCorrelationContext correlation &&
            correlation.CorrelationId != Guid.Empty
                ? correlation.CorrelationId
                : Guid.NewGuid();
        var profile = await _profiles.GetProfileAsync(
            _execution.TenantId,
            scope.Model.SiteId,
            correlationId,
            cancellationToken);
        var snapshot = await _snapshots.ReadAsync(
            scope.Model,
            scope.Version,
            cancellationToken);
        var contentHash = _engine.ComputeContentHash(snapshot);

        var existing = await _context.ValidationRuns
            .Where(run =>
                run.ModelVersionId == versionId &&
                run.ContentHash == contentHash &&
                run.RuleSetVersion == SpaceValidationRuleSet.Version &&
                run.AdapterId == profile.AdapterId &&
                run.CapabilityHash == profile.CapabilityHash &&
                run.Status != SpaceValidationStatus.Failed)
            .OrderByDescending(run => run.RequestedAtUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            BindReusableResult(scope.Version, existing);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new CreateSpaceValidationResponse(
                await ToDtoAsync(existing, cancellationToken),
                Reused: true);
        }

        if (scope.Version.Status == SpaceVersionStatus.Validating)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "A different validation is already active for this version.",
                "wait-for-version-state");
        }

        var now = RequireUtcNow();
        var inputHash = Hash(
            string.Join(
                "\n",
                contentHash,
                SpaceValidationRuleSet.Version,
                profile.AdapterId,
                profile.CapabilityHash));
        var enqueue = new SpaceJobEnqueueRequest(
            SpaceJobType.Validate,
            SpaceJobSubjectType.ModelVersion,
            versionId,
            inputHash,
            SpaceValidationRuleSet.ProcessorVersion,
            VariantKey:
                $"{SpaceValidationRuleSet.Version}:{profile.AdapterId}:{profile.CapabilityHash}",
            Priority: 20,
            MaxAttempts: 3);
        var businessKey = SpaceJobBusinessKey.Create(enqueue);
        var job = SpaceJob.CreateQueued(
            _execution.TenantId,
            enqueue.JobType,
            enqueue.SubjectType,
            enqueue.SubjectId,
            businessKey,
            enqueue.InputHash,
            enqueue.Priority,
            enqueue.MaxAttempts,
            _execution.ActorId,
            now,
            correlationId);
        var run = SpaceValidationRun.CreateQueued(
            _execution.TenantId,
            versionId,
            scope.Version.ContentRevision,
            contentHash,
            SpaceValidationRuleSet.Version,
            profile.AdapterId,
            profile.CapabilityHash,
            _execution.ActorId,
            now,
            job.Id,
            correlationId);

        scope.Version.BeginValidation();
        _context.AddRange(job, run);
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new CreateSpaceValidationResponse(
            await ToDtoAsync(run, cancellationToken),
            Reused: false);
    }

    public async Task<SpaceValidationRunDto> GetValidationAsync(
        Guid validationId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (validationId == Guid.Empty)
            throw Invalid("A non-empty validationId is required.");

        var run = await _context.ValidationRuns
            .SingleOrDefaultAsync(
                candidate => candidate.Id == validationId,
                cancellationToken);
        if (run is null)
        {
            throw NotFound(
                SpaceErrorCodes.ValidationNotFound,
                "The ValidationRun was not found.");
        }
        var scope = await RequireScopeAsync(
            run.ModelVersionId,
            cancellationToken);
        _access.EnsureSiteAccess(scope.Model.SiteId, write: false);
        return await ToDtoAsync(run, cancellationToken);
    }

    private async Task<SpaceValidationRunDto> ToDtoAsync(
        SpaceValidationRun run,
        CancellationToken cancellationToken)
    {
        var issues = await _context.Issues
            .AsNoTracking()
            .Where(issue => issue.ValidationRunId == run.Id)
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Category)
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.TargetLogicalId)
            .ThenBy(issue => issue.Id)
            .Select(issue => ToIssueDto(issue))
            .ToArrayAsync(cancellationToken);
        return new SpaceValidationRunDto(
            run.Id,
            run.ModelVersionId,
            run.ContentRevision,
            run.ContentHash,
            run.RuleSetVersion,
            run.AdapterId,
            run.CapabilityHash,
            run.Status.ToString(),
            run.BlockingCount,
            run.WarningCount,
            run.InfoCount,
            run.RequestedAtUtc,
            run.RequestedBy,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.JobId,
            run.CorrelationId,
            run.FailureCode,
            run.FailureSummary,
            Convert.ToBase64String(run.RowVersion),
            issues);
    }

    private static SpaceValidationIssueDto ToIssueDto(
        SpaceModelIssue issue) =>
        new(
            issue.Id,
            issue.ValidationRunId!.Value,
            issue.Severity.ToString(),
            issue.Category ?? SpaceValidationCategories.ModelIssue,
            issue.Code,
            issue.SourceId,
            issue.SourceRef,
            issue.TargetLogicalId,
            issue.FieldPath,
            issue.MessageArgsJson,
            issue.SuggestedActionCode,
            issue.GenerationRunId,
            issue.GenerationProposalId,
            issue.EvidenceJson,
            issue.CreatedAtUtc);

    private static void BindReusableResult(
        SpaceModelVersion version,
        SpaceValidationRun run)
    {
        if (run.Status == SpaceValidationStatus.Passed)
        {
            if (version.Status == SpaceVersionStatus.Ready &&
                string.Equals(
                    version.ValidatedHash,
                    run.ContentHash,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    version.RuleSetVersion,
                    run.RuleSetVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    version.WmsCapabilityHash,
                    run.CapabilityHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (version.Status is SpaceVersionStatus.Draft or SpaceVersionStatus.Ready)
                version.BeginValidation();
            if (version.Status == SpaceVersionStatus.Validating)
            {
                version.MarkReady(
                    run.ContentHash,
                    run.RuleSetVersion,
                    run.CapabilityHash);
            }
            return;
        }

        if (run.Status == SpaceValidationStatus.Blocked &&
            version.Status == SpaceVersionStatus.Validating)
        {
            version.CompleteValidationWithErrors();
        }
    }

    private async Task<(SpaceModel Model, SpaceModelVersion Version)>
        RequireScopeAsync(
            Guid versionId,
            CancellationToken cancellationToken)
    {
        var version = await _context.Versions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == versionId,
                cancellationToken);
        if (version is null)
        {
            throw NotFound(
                SpaceErrorCodes.VersionNotFound,
                "The model version was not found.");
        }
        var model = await _context.Models.SingleAsync(
            candidate => candidate.Id == version.ModelId,
            cancellationToken);
        return (model, version);
    }

    private void EnsureExecutionContext()
    {
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The validation request is invalid.",
            detail,
            "correct-request");

    private static SpaceProblemException NotFound(
        string code,
        string detail) =>
        new(
            code,
            404,
            "The requested Space resource was not found.",
            detail,
            "refresh-resource");

    private static SpaceProblemException Conflict(
        string code,
        string detail,
        string recoveryAction) =>
        new(
            code,
            409,
            "The validation request conflicts with current state.",
            detail,
            recoveryAction);
}

public static class SpaceValidationJobSteps
{
    public const string ValidateAuthoritativeSnapshot =
        nameof(ValidateAuthoritativeSnapshot);

    public static IReadOnlyList<string> All { get; } =
        [ValidateAuthoritativeSnapshot];
}

public sealed class SpaceValidationJobProcessor : ISpaceJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;
    private readonly ISpaceValidationProfileProvider _profiles;
    private readonly SpaceValidationEngine _engine;
    private readonly EfSpaceValidationSnapshotReader _snapshots;

    public SpaceValidationJobProcessor(
        SpaceContext context,
        ISpaceClock clock,
        ISpaceValidationProfileProvider profiles,
        SpaceValidationEngine engine)
    {
        _context = context;
        _clock = clock;
        _profiles = profiles;
        _engine = engine;
        _snapshots = new EfSpaceValidationSnapshotReader(context);
    }

    public SpaceJobType JobType => SpaceJobType.Validate;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.ModelVersion;
    public string ProcessorVersion =>
        SpaceValidationRuleSet.ProcessorVersion;
    public IReadOnlyList<string> StepCodes =>
        SpaceValidationJobSteps.All;

    public async Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.StepCode !=
            SpaceValidationJobSteps.ValidateAuthoritativeSnapshot)
        {
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                "The validation Job contains an unknown step.");
        }

        var run = await _context.ValidationRuns.SingleOrDefaultAsync(
                      candidate =>
                          candidate.JobId == execution.Lease.JobId &&
                          candidate.ModelVersionId ==
                          execution.Lease.SubjectId,
                      cancellationToken)
                  ?? throw new SpaceJobProcessingException(
                      SpaceJobFailureKind.Input,
                      SpaceErrorCodes.ValidationNotFound,
                      "The ValidationRun for the Job was not found.");

        if (run.Status == SpaceValidationStatus.Failed)
        {
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Input,
                run.FailureCode ?? SpaceErrorCodes.ValidationStale,
                run.FailureSummary ??
                "The ValidationRun previously failed.");
        }
        if (run.IsTerminal)
            return Output(run);

        var version = await _context.Versions.SingleAsync(
            candidate => candidate.Id == run.ModelVersionId,
            cancellationToken);
        var model = await _context.Models.SingleAsync(
            candidate => candidate.Id == version.ModelId,
            cancellationToken);
        var profile = await _profiles.GetProfileAsync(
            run.TenantId,
            model.SiteId,
            run.CorrelationId,
            cancellationToken);
        if (!string.Equals(
                profile.AdapterId,
                run.AdapterId,
                StringComparison.Ordinal) ||
            !string.Equals(
                profile.CapabilityHash,
                run.CapabilityHash,
                StringComparison.OrdinalIgnoreCase))
        {
            await FailStaleAsync(
                run,
                version,
                "The WMS capability profile changed.",
                cancellationToken);
            throw Stale(run);
        }

        if (run.Status == SpaceValidationStatus.Queued)
        {
            if (version.Status != SpaceVersionStatus.Validating)
            {
                await FailStaleAsync(
                    run,
                    version,
                    "The model version left the validating state.",
                    cancellationToken);
                throw Stale(run);
            }
            run.Start(RequireUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
        }

        var snapshot = await _snapshots.ReadAsync(
            model,
            version,
            cancellationToken);
        var result = _engine.Validate(snapshot, profile);
        if (version.Status != SpaceVersionStatus.Validating ||
            version.ContentRevision != run.ContentRevision ||
            !string.Equals(
                result.ContentHash,
                run.ContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            await FailStaleAsync(
                run,
                version,
                "The model content changed after validation was queued.",
                cancellationToken);
            throw Stale(run);
        }

        var existingIssueCount = await _context.Issues
            .CountAsync(
                issue => issue.ValidationRunId == run.Id,
                cancellationToken);
        if (existingIssueCount != 0)
        {
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                "A non-terminal ValidationRun already contains issues.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        foreach (var candidate in result.Issues)
        {
            _context.Issues.Add(
                SpaceModelIssue.Create(
                    run.TenantId,
                    run.ModelVersionId,
                    candidate.SourceId,
                    run.JobId,
                    candidate.Severity,
                    candidate.Code,
                    candidate.SourceRef,
                    candidate.TargetLogicalId,
                    candidate.MessageArgsJson,
                    candidate.SuggestedActionCode,
                    candidate.GenerationRunId,
                    candidate.GenerationProposalId,
                    run.Id,
                    candidate.Category,
                    candidate.FieldPath,
                    candidate.EvidenceJson));
        }

        var now = RequireUtcNow();
        if (result.BlockingCount == 0)
        {
            run.Pass(
                result.BlockingCount,
                result.WarningCount,
                result.InfoCount,
                now);
            version.MarkReady(
                run.ContentHash,
                run.RuleSetVersion,
                run.CapabilityHash);
        }
        else
        {
            run.Block(
                result.BlockingCount,
                result.WarningCount,
                result.InfoCount,
                now);
            version.CompleteValidationWithErrors();
        }
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return Output(run);
    }

    private async Task FailStaleAsync(
        SpaceValidationRun run,
        SpaceModelVersion version,
        string summary,
        CancellationToken cancellationToken)
    {
        if (!run.IsTerminal)
            run.Fail(SpaceErrorCodes.ValidationStale, summary, RequireUtcNow());
        if (version.Status == SpaceVersionStatus.Validating)
            version.CompleteValidationWithErrors();
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static SpaceJobProcessingException Stale(
        SpaceValidationRun run) =>
        new(
            SpaceJobFailureKind.Input,
            run.FailureCode ?? SpaceErrorCodes.ValidationStale,
            run.FailureSummary ?? "The ValidationRun is stale.");

    private static SpaceJobStepOutput Output(SpaceValidationRun run)
    {
        var checkpoint = JsonSerializer.Serialize(
            new
            {
                validationRunId = run.Id,
                status = run.Status.ToString(),
                run.BlockingCount,
                run.WarningCount,
                run.InfoCount,
            },
            JsonOptions);
        return new SpaceJobStepOutput(checkpoint, Hash(checkpoint));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException(
                "The Space clock must return UTC.");
        return now;
    }
}

internal sealed class EfSpaceValidationSnapshotReader
{
    private readonly SpaceContext _context;

    public EfSpaceValidationSnapshotReader(SpaceContext context)
    {
        _context = context;
    }

    public async Task<SpaceValidationSnapshot> ReadAsync(
        SpaceModel model,
        SpaceModelVersion version,
        CancellationToken cancellationToken)
    {
        var floors = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var aisles = await _context.AisleRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var rackLevels = await _context.RackLevelRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var locations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var elements = await _context.ElementRevisions
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var elementIds = elements.Select(value => value.Id).ToArray();
        var attributes = await _context.ElementAttributes
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == version.Id &&
                elementIds.Contains(value.ElementRevisionId))
            .ToArrayAsync(cancellationToken);
        var attributesByElement = attributes
            .GroupBy(value => value.ElementRevisionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SpaceValidationElementAttribute>)group
                    .Select(value => new SpaceValidationElementAttribute(
                        value.Namespace,
                        value.Key,
                        value.ValueType,
                        value.Value,
                        value.Unit))
                    .OrderBy(value => value.Namespace, StringComparer.Ordinal)
                    .ThenBy(value => value.Key, StringComparer.Ordinal)
                    .ToArray());
        var sources = await _context.Sources
            .AsNoTracking()
            .Where(value => value.ModelVersionId == version.Id)
            .ToArrayAsync(cancellationToken);
        var assetIds = elements
            .Where(value => value.ModelAssetId.HasValue)
            .Select(value => value.ModelAssetId!.Value)
            .Distinct()
            .ToArray();
        var assetVersions = assetIds.Length == 0
            ? []
            : await _context.AssetVersions
                .AsNoTracking()
                .Where(value => assetIds.Contains(value.Id))
                .ToArrayAsync(cancellationToken);
        var existingIssues = await _context.Issues
            .AsNoTracking()
            .Where(issue =>
                issue.ModelVersionId == version.Id &&
                issue.ValidationRunId == null &&
                issue.Status == SpaceIssueStatus.Open)
            .ToArrayAsync(cancellationToken);
        var publishedLocations =
            model.CurrentPublishedVersionId.HasValue &&
            model.CurrentPublishedVersionId.Value != version.Id
                ? await _context.LocationRevisions
                    .AsNoTracking()
                    .Where(value =>
                        value.ModelVersionId ==
                        model.CurrentPublishedVersionId.Value)
                    .ToArrayAsync(cancellationToken)
                : [];

        return new SpaceValidationSnapshot(
            version.TenantId,
            version.ModelId,
            version.Id,
            model.SiteId,
            version.ContentRevision,
            floors.Select(value => new SpaceValidationFloor(
                    Ref(value),
                    value.SiteLogicalId,
                    value.Level,
                    value.FloorCode,
                    value.Elevation,
                    value.Height,
                    value.BoundaryJson,
                    value.CoordinateSystem,
                    value.UnderlaySourceId,
                    value.UnderlayScale))
                .ToArray(),
            zones.Select(value => new SpaceValidationZone(
                    Ref(value),
                    value.FloorLogicalId,
                    value.ZoneCode,
                    value.PolygonJson))
                .ToArray(),
            aisles.Select(value => new SpaceValidationAisle(
                    Ref(value),
                    value.ZoneLogicalId,
                    value.AisleCode,
                    value.PolygonJson,
                    value.CenterlineJson))
                .ToArray(),
            racks.Select(value => new SpaceValidationRack(
                    Ref(value),
                    value.FloorLogicalId,
                    value.ZoneLogicalId,
                    value.AisleLogicalId,
                    value.RackCode,
                    value.X,
                    value.Y,
                    value.Z,
                    value.RotationZ,
                    value.Width,
                    value.Depth,
                    value.Height))
                .ToArray(),
            rackLevels.Select(value => new SpaceValidationRackLevel(
                    Ref(value),
                    value.RackLogicalId,
                    value.LevelNo,
                    value.BottomZ,
                    value.ClearHeight,
                    value.BinCount,
                    value.DepthCount,
                    value.CellWidth,
                    value.CellDepth,
                    value.BeamHeight))
                .ToArray(),
            locations.Select(value => new SpaceValidationLocation(
                    Ref(value),
                    value.FloorLogicalId,
                    value.RackLogicalId,
                    value.LocationCode,
                    value.ColumnNo,
                    value.LevelNo,
                    value.DepthNo,
                    value.Width,
                    value.Height,
                    value.Depth,
                    value.CodeOrigin,
                    value.ExternalBindingState))
                .ToArray(),
            elements.Select(value => new SpaceValidationElement(
                    Ref(value),
                    value.FloorLogicalId,
                    value.ParentLogicalId,
                    value.ElementType,
                    value.GeometryJson,
                    value.ModelAssetId,
                    value.ModelAssetScope,
                    value.ModelAssetOwnerTenantId,
                    value.X,
                    value.Y,
                    value.Z,
                    value.RotationZ,
                    value.Width,
                    value.Height,
                    value.Depth,
                    value.BusinessCode,
                    value.LinkedEntityType,
                    value.LinkedLogicalId,
                    attributesByElement.GetValueOrDefault(value.Id) ?? []))
                .ToArray(),
            sources.Select(value => new SpaceValidationSource(
                    value.Id,
                    value.SourceType,
                    value.Sha256,
                    value.State,
                    value.Unit,
                    value.ScaleToMillimeters))
                .ToArray(),
            assetVersions.Select(value => new SpaceValidationAssetVersion(
                    value.Id,
                    value.Scope,
                    value.OwnerTenantId,
                    value.Status))
                .ToArray(),
            publishedLocations.Select(value =>
                    new SpaceValidationPublishedLocation(
                        value.LogicalId,
                        value.LocationCode,
                        value.ExternalBindingState))
                .ToArray(),
            existingIssues.Select(value => new SpaceValidationExistingIssue(
                    value.Severity,
                    value.Category,
                    value.Code,
                    value.SourceId,
                    value.SourceRef,
                    value.TargetLogicalId,
                    value.FieldPath,
                    value.MessageArgsJson,
                    value.SuggestedActionCode,
                    value.GenerationRunId,
                    value.GenerationProposalId,
                    value.EvidenceJson))
                .ToArray());
    }

    private static SpaceValidationRevisionRef Ref(
        SpaceRevisionEntity revision) =>
        new(
            revision.LogicalId,
            revision.SourceId,
            revision.SourceRef,
            revision.LifecycleState);
}

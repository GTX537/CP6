using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed partial class SpacePublishOrchestrator :
    ISpacePublishOrchestrator,
    ISpacePublishJobExecutor
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceWarehouseResolver _warehouses;
    private readonly ISpaceWmsAdapter _adapter;
    private readonly ISpaceRuntimeMaterializer _runtime;
    private readonly SpacePublishPlanEngine _planEngine;
    private readonly EfSpacePublishSnapshotReader _snapshots;

    public SpacePublishOrchestrator(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceWarehouseResolver warehouses,
        ISpaceWmsAdapter adapter,
        ISpaceRuntimeMaterializer runtime,
        SpacePublishPlanEngine planEngine)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _warehouses = warehouses;
        _adapter = adapter;
        _runtime = runtime;
        _planEngine = planEngine;
        _snapshots = new EfSpacePublishSnapshotReader(context);
    }

    public async Task<CreateSpacePublishAttemptResponse> StartAsync(
        Guid versionId,
        CreateSpacePublishAttemptRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        ValidateStart(versionId, request);
        var normalizedKey = RequireIdempotencyKey(idempotencyKey);
        var requestHash = RequestHash(versionId, request);
        var correlationId = CorrelationId();

        var replay = await _context.PublishAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.BusinessIdempotencyKey == normalizedKey,
                cancellationToken);
        if (replay is not null)
        {
            if (!string.Equals(
                    replay.RequestHash,
                    requestHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Conflict(
                    SpaceErrorCodes.IdempotencyConflict,
                    "The Idempotency-Key was already used for another " +
                    "publish request.",
                    "use-new-idempotency-key");
            }
            _access.EnsureSiteAccess(replay.SiteId, write: false);
            return new CreateSpacePublishAttemptResponse(
                await ToDtoAsync(replay.Id, cancellationToken),
                IdempotentReplay: true);
        }

        var target = await _context.Versions
                         .SingleOrDefaultAsync(
                             value => value.Id == versionId,
                             cancellationToken)
                     ?? throw NotFound(
                         SpaceErrorCodes.VersionNotFound,
                         "The target model version was not found.");
        var model = await _context.Models
                        .SingleOrDefaultAsync(
                            value => value.Id == target.ModelId,
                            cancellationToken)
                    ?? throw NotFound(
                        SpaceErrorCodes.ModelNotFound,
                        "The target model was not found.");
        _access.EnsureSiteAccess(model.SiteId, write: true);
        EnsureProductionTarget(target);

        var warehouse = await _warehouses.ResolveAsync(
                            model.SiteId,
                            cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.ModelNotFound,
                            "The CP6 runtime site was not found.");
        var wmsContext = new SpaceWmsContext(
            _execution.TenantId,
            model.SiteId,
            warehouse.WarehouseCode,
            correlationId);
        var capabilities = await _adapter.GetCapabilitiesAsync(
            wmsContext,
            cancellationToken);
        if (!capabilities.SupportsProductionPublishing)
        {
            throw Unprocessable(
                SpaceErrorCodes.WmsCapabilityMissing,
                "The selected WMS adapter is not certified for " +
                "production publishing.",
                "verify-wms-adapter");
        }

        var validation = await _context.ValidationRuns
                             .AsNoTracking()
                             .SingleOrDefaultAsync(
                                 value =>
                                     value.Id ==
                                     request.ValidationRunId,
                                 cancellationToken)
                         ?? throw NotFound(
                             SpaceErrorCodes.ValidationNotFound,
                             "The selected ValidationRun was not found.");
        EnsureValidation(target, validation, capabilities);
        EnsureBase(model, request.ExpectedPublishedVersionId);

        var baseObjects = model.CurrentPublishedVersionId.HasValue
            ? await _snapshots.ReadAsync(
                model.CurrentPublishedVersionId.Value,
                capabilities.AdapterId,
                cancellationToken)
            : [];
        var targetObjects = await _snapshots.ReadAsync(
            target.Id,
            capabilities.AdapterId,
            cancellationToken);
        var plan = _planEngine.Build(
            new SpacePublishPlanInput(
                target.Id,
                model.CurrentPublishedVersionId,
                validation.Id,
                validation.Status.ToString(),
                validation.BlockingCount,
                validation.ContentHash,
                capabilities.AdapterId,
                capabilities.CapabilityHash,
                targetObjects,
                baseObjects));
        if (!string.Equals(
                plan.PlanHash,
                request.PlanHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                SpaceErrorCodes.ValidationStale,
                "The submitted PlanHash does not match the current " +
                "validated content, base version, and WMS capability.",
                "refresh-publish-preview");
        }
        if (plan.HasBlockingImpact)
        {
            throw Unprocessable(
                SpaceErrorCodes.ValidationBlocked,
                "The publish plan contains a blocking WMS impact.",
                "resolve-publish-blockers");
        }

        var plannedContentRevision = target.ContentRevision;

        SpacePublishAttempt attempt;
        await using (var transaction =
                     await _context.Database.BeginTransactionAsync(
                         IsolationLevel.Serializable,
                         cancellationToken))
        {
            var concurrentReplay = await _context.PublishAttempts
                .SingleOrDefaultAsync(
                    value =>
                        value.BusinessIdempotencyKey == normalizedKey,
                    cancellationToken);
            if (concurrentReplay is not null)
            {
                if (!string.Equals(
                        concurrentReplay.RequestHash,
                        requestHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Conflict(
                        SpaceErrorCodes.IdempotencyConflict,
                        "The Idempotency-Key was concurrently used for " +
                        "another request.",
                        "use-new-idempotency-key");
                }
                await transaction.CommitAsync(cancellationToken);
                return new CreateSpacePublishAttemptResponse(
                    await ToDtoAsync(
                        concurrentReplay.Id,
                        cancellationToken),
                    IdempotentReplay: true);
            }

            var active = await _context.PublishAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.SiteId == model.SiteId &&
                        value.OwnsPublishSlot,
                    cancellationToken);
            if (active is not null)
            {
                throw Conflict(
                    SpaceErrorCodes.PublishSlotBusy,
                    $"Site publish attempt {active.Id:D} is still active.",
                    "view-active-publish-attempt");
            }

            await _context.Entry(model).ReloadAsync(cancellationToken);
            await _context.Entry(target).ReloadAsync(cancellationToken);
            EnsureBase(model, request.ExpectedPublishedVersionId);
            EnsureProductionTarget(target);
            var persistedValidation = await _context.ValidationRuns
                .AsNoTracking()
                .SingleAsync(
                    value => value.Id == validation.Id,
                    cancellationToken);
            EnsureValidation(target, persistedValidation, capabilities);
            if (target.ContentRevision != plannedContentRevision)
            {
                throw Conflict(
                    SpaceErrorCodes.ValidationStale,
                    "The target content changed while the publish plan was being prepared.",
                    "refresh-publish-preview");
            }

            var persistedPlan = await _context.PublishPlans
                .SingleOrDefaultAsync(
                    value => value.PlanHash == plan.PlanHash,
                    cancellationToken);
            if (persistedPlan is null)
            {
                persistedPlan = SpacePublishPlan.Create(
                    _execution.TenantId,
                    model.SiteId,
                    target.Id,
                    model.CurrentPublishedVersionId,
                    validation.Id,
                    validation.ContentHash,
                    capabilities.AdapterId,
                    capabilities.CapabilityHash,
                    plan.PlanHash,
                    plan.Items.Count,
                    JsonSerializer.Serialize(plan, Json));
                _context.PublishPlans.Add(persistedPlan);
            }

            attempt = SpacePublishAttempt.Create(
                _execution.TenantId,
                model.SiteId,
                persistedPlan.Id,
                target.Id,
                model.CurrentPublishedVersionId,
                capabilities.AdapterId,
                normalizedKey,
                requestHash,
                _execution.ActorId,
                approvedBy: null,
                request.ApprovalReference,
                JsonSerializer.Serialize(request, Json),
                RequireUtcNow(),
                correlationId);
            var jobRequest = new SpaceJobEnqueueRequest(
                SpaceJobType.Publish,
                SpaceJobSubjectType.PublishAttempt,
                attempt.Id,
                plan.PlanHash,
                SpacePublishJobProcessor.Version,
                VariantKey: attempt.Id.ToString("D"),
                Priority: 50,
                MaxAttempts: 5,
                PayloadJson: JsonSerializer.Serialize(
                    new
                    {
                        publishAttemptId = attempt.Id,
                        publishPlanId = persistedPlan.Id,
                        targetVersionId = target.Id,
                        siteId = model.SiteId,
                        requestHash,
                    },
                    Json));
            var job = SpaceJob.CreateQueued(
                _execution.TenantId,
                jobRequest.JobType,
                jobRequest.SubjectType,
                jobRequest.SubjectId,
                SpaceJobBusinessKey.Create(jobRequest),
                jobRequest.InputHash,
                jobRequest.Priority,
                jobRequest.MaxAttempts,
                _execution.ActorId,
                RequireUtcNow(),
                correlationId,
                jobRequest.PayloadJson);
            attempt.BindInitialJob(job.Id);
            _context.Jobs.Add(job);
            _context.PublishAttempts.Add(attempt);
            _context.PublishAuditEvents.Add(
                SpacePublishAuditEvent.Create(
                    _execution.TenantId,
                    attempt.Id,
                    job.Id,
                    batchId: null,
                    eventNo: 1,
                    SpacePublishAuditEventType.Queued,
                    attempt.Status,
                    attempt.CurrentStep,
                    _execution.ActorId,
                    correlationId,
                    RequireUtcNow(),
                    "initial-queue",
                    "The warehouse publish Job was queued.",
                    errorCode: null,
                    JsonSerializer.Serialize(
                        new
                        {
                            plan.PlanHash,
                            persistedPlan.ValidationRunId,
                            persistedPlan.ContentHash,
                            persistedPlan.CapabilityHash,
                        },
                        Json),
                    previousEventHash: null));
            target.BeginPublishing();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new CreateSpacePublishAttemptResponse(
            await ToDtoAsync(attempt.Id, cancellationToken),
            IdempotentReplay: false);
    }

    public async Task<RetrySpacePublishAttemptResponse> RetryAsync(
        Guid attemptId,
        RetrySpacePublishAttemptRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (attemptId == Guid.Empty)
            throw Invalid("A non-empty attemptId is required.");
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw Invalid("A retry reason between 1 and 1000 characters is required.");
        var normalizedKey = RequireIdempotencyKey(idempotencyKey);
        var deduplicationKey = "manual-retry:" + Hash(normalizedKey);

        var visible = await _context.PublishAttempts
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              value => value.Id == attemptId,
                              cancellationToken)
                      ?? throw NotFound(
                          SpaceErrorCodes.PublishAttemptNotFound,
                          "The publish attempt was not found.");
        _access.EnsureSiteAccess(visible.SiteId, write: true);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        var replay = await _context.PublishAuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.AttemptId == attemptId &&
                    value.DeduplicationKey == deduplicationKey,
                cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RetrySpacePublishAttemptResponse(
                await ToDtoAsync(attemptId, cancellationToken),
                IdempotentReplay: true);
        }

        var attempt = await _context.PublishAttempts.SingleAsync(
            value => value.Id == attemptId,
            cancellationToken);
        var previousJob = attempt.JobId.HasValue
            ? await _context.Jobs.SingleOrDefaultAsync(
                value => value.Id == attempt.JobId.Value,
                cancellationToken)
            : null;
        if (previousJob is not null && !previousJob.IsTerminal)
        {
            throw Conflict(
                SpaceErrorCodes.PublishRetryNotAllowed,
                "The current publish Job is still queued or running.",
                "wait-for-current-publish-job");
        }
        if (attempt.Status is not (
                SpacePublishAttemptStatus.ReconciliationRequired or
                SpacePublishAttemptStatus.ManualIntervention or
                SpacePublishAttemptStatus.WaitingRetry))
        {
            throw Conflict(
                SpaceErrorCodes.PublishRetryNotAllowed,
                "The publish attempt is not in a recoverable failed state.",
                "view-publish-attempt");
        }

        var hasOpenReconciliation = await _context.ReconciliationIssues.AnyAsync(
            value =>
                value.AttemptId == attempt.Id &&
                value.Status != SpaceReconciliationStatus.Resolved,
            cancellationToken);
        var reconciliation =
            attempt.Status == SpacePublishAttemptStatus.ReconciliationRequired ||
            attempt.CurrentStep == SpacePublishStep.Reconcile ||
            hasOpenReconciliation;
        var resolution = request.Resolution?.Trim();
        if (reconciliation && string.IsNullOrWhiteSpace(resolution))
        {
            throw Invalid(
                "A reconciliation resolution note is required for this retry.");
        }

        var jobType = reconciliation
            ? SpaceJobType.Reconcile
            : SpaceJobType.Publish;
        var processorVersion = reconciliation
            ? SpacePublishReconciliationJobProcessor.Version
            : SpacePublishJobProcessor.Version;
        var inputHash = Hash(string.Join(
            "\n",
            attempt.RequestHash,
            deduplicationKey,
            ((short)jobType).ToString(CultureInfo.InvariantCulture)));
        var jobRequest = new SpaceJobEnqueueRequest(
            jobType,
            SpaceJobSubjectType.PublishAttempt,
            attempt.Id,
            inputHash,
            processorVersion,
            VariantKey: deduplicationKey,
            Priority: 80,
            MaxAttempts: 5,
            PayloadJson: JsonSerializer.Serialize(
                new
                {
                    publishAttemptId = attempt.Id,
                    retryOfJobId = previousJob?.Id,
                    reason,
                    resolution,
                },
                Json));
        var now = RequireUtcNow();
        var retryJob = SpaceJob.CreateQueued(
            _execution.TenantId,
            jobRequest.JobType,
            jobRequest.SubjectType,
            jobRequest.SubjectId,
            SpaceJobBusinessKey.Create(jobRequest),
            jobRequest.InputHash,
            jobRequest.Priority,
            jobRequest.MaxAttempts,
            _execution.ActorId,
            now,
            CorrelationId(),
            jobRequest.PayloadJson,
            previousJob?.Id);
        _context.Jobs.Add(retryJob);
        attempt.ScheduleManualRetry(
            retryJob.Id,
            _execution.ActorId,
            now,
            reconciliation);
        if (reconciliation)
        {
            var issues = await _context.ReconciliationIssues
                .Where(value =>
                    value.AttemptId == attempt.Id &&
                    value.Status != SpaceReconciliationStatus.Resolved)
                .ToArrayAsync(cancellationToken);
            foreach (var issue in issues)
                issue.BeginInvestigation(resolution!);
        }
        await AppendAuditAsync(
            attempt,
            retryJob.Id,
            batchId: null,
            SpacePublishAuditEventType.ManualRetryRequested,
            deduplicationKey,
            "A manual publish recovery Job was requested.",
            errorCode: null,
            JsonSerializer.Serialize(
                new
                {
                    reason,
                    resolution,
                    retryOfJobId = previousJob?.Id,
                    jobType = jobType.ToString(),
                },
                Json),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RetrySpacePublishAttemptResponse(
            await ToDtoAsync(attempt.Id, cancellationToken),
            IdempotentReplay: false);
    }

    public async Task<SpacePublishAttemptDto> GetAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (attemptId == Guid.Empty)
            throw Invalid("A non-empty attemptId is required.");
        var attempt = await _context.PublishAttempts
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              value => value.Id == attemptId,
                              cancellationToken)
                      ?? throw NotFound(
                          SpaceErrorCodes.PublishAttemptNotFound,
                          "The publish attempt was not found.");
        _access.EnsureSiteAccess(attempt.SiteId, write: false);
        return await ToDtoAsync(attempt.Id, cancellationToken);
    }

    private async Task<SpaceWmsOperationStatus?> TryGetStatusAsync(
        SpaceWmsBatch batch,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adapter.GetOperationStatusAsync(
                new SpaceWmsOperationQuery(
                    batch.Context,
                    batch.OperationKey,
                    batch.PayloadHash),
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task AddReceiptsAsync(
        SpacePublishBatch batch,
        SpaceWmsBatchResult result,
        CancellationToken cancellationToken)
    {
        var existing = await _context.WmsReceipts
            .Where(value => value.BatchId == batch.Id)
            .ToDictionaryAsync(value => value.LogicalId, cancellationToken);
        foreach (var receipt in result.Items)
        {
            var outcome = receipt.Outcome switch
            {
                SpaceWmsItemOutcome.Applied or
                    SpaceWmsItemOutcome.AlreadyApplied =>
                    SpaceWmsReceiptOutcome.Applied,
                SpaceWmsItemOutcome.Unknown =>
                    SpaceWmsReceiptOutcome.Unknown,
                _ => SpaceWmsReceiptOutcome.NotApplied,
            };
            if (existing.TryGetValue(receipt.LogicalId, out var saved))
            {
                if (saved.Outcome != outcome ||
                    !string.Equals(saved.LocationCode, receipt.LocationCode, StringComparison.Ordinal) ||
                    !string.Equals(saved.ExternalLocationId, receipt.ExternalLocationId, StringComparison.Ordinal) ||
                    !string.Equals(saved.ExternalVersion, receipt.ExternalVersion, StringComparison.Ordinal) ||
                    !string.Equals(saved.ResponseHash, receipt.ResponseHash, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(saved.ErrorCode, receipt.ErrorCode, StringComparison.Ordinal))
                {
                    throw Processing(
                        SpaceJobFailureKind.Security,
                        SpaceErrorCodes.WmsResultUncertain,
                        "An idempotent WMS replay returned different receipt evidence.");
                }
                continue;
            }
            var created = SpaceWmsReceipt.Create(
                _execution.TenantId,
                batch.Id,
                receipt.LogicalId,
                receipt.LocationCode,
                (short)receipt.Action,
                outcome,
                receipt.ExternalLocationId,
                receipt.ExternalVersion,
                receipt.ResponseHash,
                receipt.ErrorCode,
                result.ObservedAtUtc.UtcDateTime);
            _context.WmsReceipts.Add(created);
            existing.Add(receipt.LogicalId, created);
        }
    }

    private static string? VerifyReadBack(
        SpaceWmsBatch batch,
        SpaceWmsReadBackResult readBack,
        IReadOnlyDictionary<Guid, SpaceWmsReceipt> receipts)
    {
        if (readBack.Source.Kind == SpaceWmsDataSourceKind.Unavailable)
            return "WMS readback data source was unavailable.";
        if (readBack.Items.Count != batch.Items.Count)
            return "WMS readback did not return every planned location.";

        var states = readBack.Items
            .GroupBy(value => value.LogicalId)
            .ToDictionary(value => value.Key, value => value.ToArray());
        foreach (var item in batch.Items)
        {
            if (!states.TryGetValue(item.LogicalId, out var matches) ||
                matches.Length != 1)
            {
                return $"WMS readback identity mismatch for " +
                       $"{item.LogicalId:D}.";
            }
            if (!receipts.TryGetValue(item.LogicalId, out var receipt) ||
                receipt.Outcome != SpaceWmsReceiptOutcome.Applied)
            {
                return $"A successful WMS receipt is missing for " +
                       $"{item.LogicalId:D}.";
            }

            var state = matches[0];
            var expectedActive =
                item.Action != SpaceWmsLocationAction.Disable;
            if (!string.Equals(
                    state.LocationCode,
                    item.LocationCode,
                    StringComparison.Ordinal) ||
                state.IsActive != expectedActive ||
                !string.Equals(
                    state.ExternalVersion,
                    receipt.ExternalVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    state.StateHash,
                    receipt.ResponseHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"WMS readback state mismatch for " +
                       $"{item.LogicalId:D}.";
            }
        }
        return null;
    }

    private async Task RequireReconciliationAsync(
        SpacePublishAttempt attempt,
        SpaceModelVersion target,
        string errorCode,
        SpaceReconciliationClassification classification,
        string summary,
        CancellationToken cancellationToken)
    {
        attempt.RequireReconciliation(errorCode, summary);
        target.MarkReconciliationRequired();
        var duplicate = await _context.ReconciliationIssues.AnyAsync(
            value =>
                value.AttemptId == attempt.Id &&
                value.Classification == classification &&
                value.Status != SpaceReconciliationStatus.Resolved &&
                value.Summary == summary,
            cancellationToken);
        if (!duplicate)
        {
            _context.ReconciliationIssues.Add(
                SpaceReconciliationIssue.Create(
                    _execution.TenantId,
                    attempt.Id,
                    logicalId: null,
                    expectedStateHash: null,
                    wmsStateHash: null,
                    runtimeStateHash: null,
                    classification,
                    summary));
        }
        var jobId = attempt.JobId ?? throw Processing(
            SpaceJobFailureKind.Bug,
            SpaceErrorCodes.PublishJobMismatch,
            "The active publish attempt has no recovery Job identity.");
        await AppendAuditAsync(
            attempt,
            jobId,
            batchId: null,
            SpacePublishAuditEventType.ReconciliationRequired,
            $"job:{jobId:D}:reconciliation:{errorCode}",
            summary,
            errorCode,
            JsonSerializer.Serialize(
                new { classification = classification.ToString() },
                Json),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SpaceWmsLocationMutation>>
        BuildMutationsAsync(
            SpaceModelVersion target,
            Guid? baseVersionId,
            string siteCode,
            SpacePublishPlanResult plan,
            IReadOnlyDictionary<Guid, SpaceWmsLocationState> currentWms,
            IReadOnlyDictionary<Guid, SpaceWmsAdoption> bindingMap,
            CancellationToken cancellationToken)
    {
        var wmsItems = plan.Items
            .Where(IsWmsMutation)
            .ToArray();
        if (wmsItems.Length == 0)
            return [];

        var versionIds = baseVersionId.HasValue
            ? new[] { target.Id, baseVersionId.Value }
            : new[] { target.Id };
        var locations = await _context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                versionIds.Contains(value.ModelVersionId))
            .ToArrayAsync(cancellationToken);
        var racks = await _context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                versionIds.Contains(value.ModelVersionId))
            .ToArrayAsync(cancellationToken);
        var aisles = await _context.AisleRevisions
            .AsNoTracking()
            .Where(value =>
                versionIds.Contains(value.ModelVersionId))
            .ToArrayAsync(cancellationToken);
        var zones = await _context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                versionIds.Contains(value.ModelVersionId))
            .ToArrayAsync(cancellationToken);
        var floors = await _context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                versionIds.Contains(value.ModelVersionId))
            .ToArrayAsync(cancellationToken);

        var targetLocations = locations
            .Where(value => value.ModelVersionId == target.Id)
            .ToDictionary(value => value.LogicalId);
        var baseLocations = locations
            .Where(value => value.ModelVersionId == baseVersionId)
            .ToDictionary(value => value.LogicalId);
        var rackMap = PreferTarget(racks, target.Id);
        var aisleMap = PreferTarget(aisles, target.Id);
        var zoneMap = PreferTarget(zones, target.Id);
        var floorMap = PreferTarget(floors, target.Id);
        var mutations = new List<SpaceWmsLocationMutation>(
            wmsItems.Length);
        foreach (var item in wmsItems)
        {
            var source =
                targetLocations.GetValueOrDefault(item.LogicalId) ??
                baseLocations.GetValueOrDefault(item.LogicalId) ??
                throw new InvalidOperationException(
                    $"Publish plan location {item.LogicalId:D} " +
                    "has no revision payload.");
            var rack = source.RackLogicalId.HasValue
                ? rackMap.GetValueOrDefault(source.RackLogicalId.Value)
                : null;
            var zone = rack is null
                ? null
                : zoneMap.GetValueOrDefault(rack.ZoneLogicalId);
            var aisle = rack?.AisleLogicalId is Guid aisleId
                ? aisleMap.GetValueOrDefault(aisleId)
                : null;
            var floor = floorMap.GetValueOrDefault(
                source.FloorLogicalId);
            var action = item.ImpactCode switch
            {
                SpacePublishImpactCodes.WmsCreateLocation =>
                    SpaceWmsLocationAction.Create,
                SpacePublishImpactCodes.WmsUpdateLocation =>
                    SpaceWmsLocationAction.Update,
                SpacePublishImpactCodes.WmsDisableLocation =>
                    SpaceWmsLocationAction.Disable,
                SpacePublishImpactCodes.WmsRestoreLocation =>
                    SpaceWmsLocationAction.Restore,
                _ => throw new InvalidOperationException(
                    "Unsupported WMS publish impact."),
            };
            bindingMap.TryGetValue(item.LogicalId, out var binding);
            var externalLogicalId =
                binding?.WmsLogicalId ?? item.LogicalId;
            mutations.Add(
                SpaceWmsLocationMutation.Create(
                    item.SequenceNo,
                    externalLogicalId,
                    source.LocationCode ??
                    throw new InvalidOperationException(
                        "A WMS location requires a code."),
                    action,
                    new SpaceWmsLocationPath(
                        siteCode,
                        floor?.Level ?? 0,
                        zone?.ZoneCode,
                        aisle?.AisleCode,
                        rack?.RackCode,
                        source.ColumnNo,
                        source.LevelNo,
                        source.DepthNo),
                    new Dictionary<string, string?>
                    {
                        ["width"] = Invariant(source.Width),
                        ["height"] = Invariant(source.Height),
                        ["depth"] = Invariant(source.Depth),
                        ["maxLoad"] = source.MaxLoad.HasValue
                            ? Invariant(source.MaxLoad.Value)
                            : null,
                    },
                    binding?.ExternalLocationId ?? item.ExternalBindingId,
                    NextExternalVersion(
                        externalLogicalId,
                        target.VersionNo,
                        currentWms)));
        }
        return mutations;
    }

    private async Task<IReadOnlyDictionary<Guid, SpaceWmsAdoption>>
        LoadWmsBindingsAsync(
            Guid siteId,
            string adapterId,
            IReadOnlyCollection<Guid> locationLogicalIds,
            CancellationToken cancellationToken)
    {
        if (locationLogicalIds.Count == 0)
            return new Dictionary<Guid, SpaceWmsAdoption>();
        return await _context.WmsAdoptions
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.AdapterId == adapterId &&
                value.Status == SpaceWmsAdoptionStatus.Bound &&
                value.LocationLogicalId.HasValue &&
                locationLogicalIds.Contains(value.LocationLogicalId.Value))
            .ToDictionaryAsync(
                value => value.LocationLogicalId!.Value,
                cancellationToken);
    }

    private static bool IsWmsMutation(SpacePublishPlanItem value) =>
        value.ImpactCode is
            SpacePublishImpactCodes.WmsCreateLocation or
            SpacePublishImpactCodes.WmsUpdateLocation or
            SpacePublishImpactCodes.WmsDisableLocation or
            SpacePublishImpactCodes.WmsRestoreLocation;

    private static long NextExternalVersion(
        Guid logicalId,
        long targetVersionNo,
        IReadOnlyDictionary<Guid, SpaceWmsLocationState> currentWms)
    {
        if (!currentWms.TryGetValue(logicalId, out var current))
            return Math.Max(1, targetVersionNo);
        if (!long.TryParse(
                current.ExternalVersion,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var externalVersion) ||
            externalVersion < 0 ||
            externalVersion == long.MaxValue)
        {
            throw Unprocessable(
                SpaceErrorCodes.WmsResultUncertain,
                $"WMS returned an invalid external version for " +
                $"{logicalId:D}.",
                "reconcile-wms-location");
        }
        return checked(externalVersion + 1);
    }

    private static Dictionary<Guid, T> PreferTarget<T>(
        IEnumerable<T> values,
        Guid targetVersionId)
        where T : SpaceRevisionEntity =>
        values
            .GroupBy(value => value.LogicalId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(value =>
                        value.ModelVersionId == targetVersionId)
                    .First());

    private static IReadOnlyList<(
        SpacePublishBatch Entity,
        SpaceWmsBatch Request)> CreateBatches(
        SpacePublishAttempt attempt,
        SpaceWmsContext context,
        string planHash,
        IReadOnlyList<SpaceWmsLocationMutation> mutations,
        int batchSize)
    {
        if (batchSize < 1)
            throw new InvalidOperationException(
                "The WMS adapter returned an invalid batch size.");
        var result = new List<(
            SpacePublishBatch Entity,
            SpaceWmsBatch Request)>();
        for (var offset = 0; offset < mutations.Count; offset += batchSize)
        {
            var request = SpaceWmsBatch.Create(
                context,
                attempt.Id,
                result.Count + 1,
                planHash,
                mutations.Skip(offset).Take(batchSize).ToArray());
            result.Add((
                SpacePublishBatch.Create(
                    attempt.TenantId,
                    attempt.Id,
                    request.BatchNo,
                    request.OperationKey,
                    request.PayloadHash,
                    JsonSerializer.Serialize(
                        new PersistedBatchRequest(request.Items),
                        Json)),
                request));
        }
        return result;
    }

    private async Task<SpacePublishAttemptDto> ToDtoAsync(
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await _context.PublishAttempts
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              value => value.Id == attemptId,
                              cancellationToken)
                      ?? throw NotFound(
                          SpaceErrorCodes.PublishAttemptNotFound,
                          "The publish attempt was not found.");
        var planHash = await _context.PublishPlans
            .AsNoTracking()
            .Where(value => value.Id == attempt.PublishPlanId)
            .Select(value => value.PlanHash)
            .SingleAsync(cancellationToken);
        var batches = await _context.PublishBatches
            .AsNoTracking()
            .Where(value => value.AttemptId == attempt.Id)
            .OrderBy(value => value.BatchNo)
            .ToArrayAsync(cancellationToken);
        var batchIds = batches.Select(value => value.Id).ToArray();
        var receipts = await _context.WmsReceipts
            .AsNoTracking()
            .Where(value => batchIds.Contains(value.BatchId))
            .OrderBy(value => value.LogicalId)
            .ToArrayAsync(cancellationToken);
        var receiptLookup = receipts
            .GroupBy(value => value.BatchId)
            .ToDictionary(value => value.Key, value => value.ToArray());
        var openIssueCount = await _context.ReconciliationIssues
            .CountAsync(
                value =>
                    value.AttemptId == attempt.Id &&
                    value.Status != SpaceReconciliationStatus.Resolved,
                cancellationToken);
        var job = attempt.JobId.HasValue
            ? await _context.Jobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Id == attempt.JobId.Value,
                    cancellationToken)
            : null;
        var auditEvents = await _context.PublishAuditEvents
            .AsNoTracking()
            .Where(value => value.AttemptId == attempt.Id)
            .OrderBy(value => value.EventNo)
            .ToArrayAsync(cancellationToken);
        return new SpacePublishAttemptDto(
            attempt.Id,
            attempt.SiteId,
            attempt.PublishPlanId,
            attempt.TargetVersionId,
            attempt.BaseVersionId,
            attempt.AdapterId,
            planHash,
            attempt.Status.ToString(),
            attempt.CurrentStep.ToString(),
            attempt.StartedAtUtc,
            attempt.FinishedAtUtc,
            attempt.RequestedBy,
            attempt.ApprovedBy,
            attempt.ApprovalReference,
            attempt.WmsCommittedAtUtc,
            attempt.RuntimeActivatedAtUtc,
            attempt.LastErrorCode,
            attempt.Summary,
            attempt.CorrelationId,
            attempt.JobId,
            job?.JobType.ToString() ?? "LegacySynchronous",
            job?.Status.ToString() ?? attempt.Status.ToString(),
            job?.AttemptCount ?? 0,
            job?.MaxAttempts ?? 0,
            job?.Status == SpaceJobStatus.Queued
                ? job.NextAttemptAtUtc
                : null,
            job?.LockExpiresAtUtc,
            attempt.ManualRetryCount,
            attempt.LastRetriedAtUtc,
            attempt.LastRetriedBy,
            openIssueCount,
            batches.Select(value => new SpacePublishBatchDto(
                value.Id,
                value.BatchNo,
                value.OperationKey,
                value.PayloadHash,
                value.Status.ToString(),
                value.AttemptCount,
                value.BatchAttemptNo,
                value.ExternalOperationId,
                value.ObservedAtUtc,
                receiptLookup.GetValueOrDefault(value.Id, [])
                    .Select(receipt => new SpacePublishReceiptDto(
                        receipt.LogicalId,
                        receipt.LocationCode,
                        ((SpaceWmsLocationAction)receipt.Action).ToString(),
                        receipt.Outcome.ToString(),
                        receipt.ExternalLocationId,
                        receipt.ExternalVersion,
                        receipt.ResponseHash,
                        receipt.ErrorCode,
                        receipt.ReceivedAtUtc))
                    .ToArray()))
                .ToArray(),
            auditEvents.Select(value => new SpacePublishAuditEventDto(
                    value.Id,
                    value.EventNo,
                    value.EventType.ToString(),
                    value.AttemptStatus.ToString(),
                    value.Step.ToString(),
                    value.JobId,
                    value.BatchId,
                    value.ActorId,
                    value.CorrelationId,
                    value.OccurredAtUtc,
                    value.Summary,
                    value.ErrorCode,
                    value.EvidenceHash,
                    value.PreviousEventHash,
                    value.EventHash))
                .ToArray());
    }

    private async Task<SpacePublishAuditEvent> AppendAuditAsync(
        SpacePublishAttempt attempt,
        Guid jobId,
        Guid? batchId,
        SpacePublishAuditEventType eventType,
        string deduplicationKey,
        string summary,
        string? errorCode,
        string evidenceJson,
        CancellationToken cancellationToken)
    {
        var localExisting = _context.PublishAuditEvents.Local
            .SingleOrDefault(value =>
                value.AttemptId == attempt.Id &&
                value.DeduplicationKey == deduplicationKey);
        if (localExisting is not null)
            return localExisting;
        var existing = await _context.PublishAuditEvents
            .SingleOrDefaultAsync(
                value =>
                    value.AttemptId == attempt.Id &&
                    value.DeduplicationKey == deduplicationKey,
                cancellationToken);
        if (existing is not null)
            return existing;
        var persistedPrevious = await _context.PublishAuditEvents
            .Where(value => value.AttemptId == attempt.Id)
            .OrderByDescending(value => value.EventNo)
            .FirstOrDefaultAsync(cancellationToken);
        var localPrevious = _context.PublishAuditEvents.Local
            .Where(value => value.AttemptId == attempt.Id)
            .OrderByDescending(value => value.EventNo)
            .FirstOrDefault();
        var previous = localPrevious is not null &&
                       (persistedPrevious is null ||
                        localPrevious.EventNo >= persistedPrevious.EventNo)
            ? localPrevious
            : persistedPrevious;
        var result = SpacePublishAuditEvent.Create(
            attempt.TenantId,
            attempt.Id,
            jobId,
            batchId,
            (previous?.EventNo ?? 0) + 1,
            eventType,
            attempt.Status,
            attempt.CurrentStep,
            _execution.ActorId,
            CorrelationId(),
            RequireUtcNow(),
            deduplicationKey,
            summary,
            errorCode,
            evidenceJson,
            previous?.EventHash);
        _context.PublishAuditEvents.Add(result);
        return result;
    }

    private static void EnsureValidation(
        SpaceModelVersion target,
        SpaceValidationRun validation,
        SpaceWmsCapabilitySnapshot capabilities)
    {
        if (target.Status != SpaceVersionStatus.Ready ||
            validation.ModelVersionId != target.Id ||
            validation.Status != SpaceValidationStatus.Passed ||
            validation.BlockingCount != 0)
        {
            throw Unprocessable(
                SpaceErrorCodes.ValidationBlocked,
                "A passed, unblocked ValidationRun bound to the Ready " +
                "target version is required.",
                "run-validation");
        }
        if (!string.Equals(
                target.ContentHash,
                validation.ContentHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                target.ValidatedHash,
                validation.ContentHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                target.RuleSetVersion,
                validation.RuleSetVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                target.WmsCapabilityHash,
                validation.CapabilityHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                validation.AdapterId,
                capabilities.AdapterId,
                StringComparison.Ordinal) ||
            !string.Equals(
                validation.CapabilityHash,
                capabilities.CapabilityHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                SpaceErrorCodes.ValidationStale,
                "Validation evidence no longer matches the target " +
                "content or WMS capability.",
                "run-validation");
        }
    }

    private static void EnsureProductionTarget(SpaceModelVersion target)
    {
        if (target.Purpose != SpaceModelVersionPurpose.Production)
        {
            throw Unprocessable(
                SpaceErrorCodes.PlanningScenarioProductionDenied,
                "A planning scenario version can never enter the " +
                "production publish lifecycle.",
                "use-production-draft");
        }
    }

    private static void EnsureBase(
        SpaceModel model,
        Guid? expectedPublishedVersionId)
    {
        if (expectedPublishedVersionId == Guid.Empty ||
            model.CurrentPublishedVersionId != expectedPublishedVersionId)
        {
            throw Conflict(
                SpaceErrorCodes.PublishedVersionChanged,
                "The current Published version does not match the " +
                "request precondition.",
                "refresh-publish-preview");
        }
    }

    private static void ValidateStart(
        Guid versionId,
        CreateSpacePublishAttemptRequest request)
    {
        if (versionId == Guid.Empty)
            throw Invalid("A non-empty versionId is required.");
        ArgumentNullException.ThrowIfNull(request);
        if (request.ValidationRunId == Guid.Empty)
            throw Invalid("A non-empty validationRunId is required.");
        _ = SpaceWmsContract.RequireSha256(
            request.PlanHash,
            nameof(request.PlanHash));
        if (request.ExpectedPublishedVersionId == Guid.Empty)
        {
            throw Invalid(
                "expectedPublishedVersionId cannot be an empty GUID.");
        }
    }

    private static string RequireIdempotencyKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "An Idempotency-Key header is required.",
                recoveryAction: "supply-idempotency-key");
        }
        if (normalized.Length > 128)
            throw Invalid("Idempotency-Key cannot exceed 128 characters.");
        return normalized;
    }

    private static string RequestHash(
        Guid versionId,
        CreateSpacePublishAttemptRequest request) =>
        Hash(string.Join(
            "\n",
            versionId.ToString("D"),
            request.ExpectedPublishedVersionId?.ToString("D") ?? "-",
            request.ValidationRunId.ToString("D"),
            request.PlanHash.ToLowerInvariant(),
            request.ApprovalReference?.Trim() ?? "-"));

    private static string Invariant<T>(T value)
        where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private Guid CorrelationId() =>
        _execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : Guid.NewGuid();

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Space clock must return UTC.");
        return now;
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

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.RequestInvalid,
            400,
            "The publish request is invalid.",
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
            "The publish request conflicts with current state.",
            detail,
            recoveryAction);

    private static SpaceProblemException Unprocessable(
        string code,
        string detail,
        string recoveryAction) =>
        new(
            code,
            422,
            "The publish request cannot be applied safely.",
            detail,
            recoveryAction);
}

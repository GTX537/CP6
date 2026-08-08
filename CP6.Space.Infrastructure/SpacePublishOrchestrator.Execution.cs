using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed partial class SpacePublishOrchestrator
{
    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureExecutionContext();
        var expectedStep = execution.Lease.JobType switch
        {
            SpaceJobType.Publish => SpacePublishJobSteps.ExecutePublishSaga,
            SpaceJobType.Reconcile => SpacePublishJobSteps.ReconcilePublishSaga,
            _ => throw Processing(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.PublishJobMismatch,
                "The claimed Job is not a publish recovery Job."),
        };
        if (!string.Equals(
                execution.StepCode,
                expectedStep,
                StringComparison.Ordinal))
        {
            throw Processing(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.PublishJobMismatch,
                "The publish Job contains an unknown step.");
        }

        var attempt = await _context.PublishAttempts.SingleOrDefaultAsync(
                          value => value.Id == execution.Lease.SubjectId,
                          cancellationToken)
                      ?? throw Processing(
                          SpaceJobFailureKind.Input,
                          SpaceErrorCodes.PublishAttemptNotFound,
                          "The publish attempt for the Job was not found.");
        if (attempt.JobId != execution.Lease.JobId)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishJobMismatch,
                "The publish attempt is bound to another recovery Job.");
        }
        if (attempt.Status is (
                SpacePublishAttemptStatus.Completed or
                SpacePublishAttemptStatus.FailedNoEffect))
        {
            return Output(attempt);
        }

        var plan = await _context.PublishPlans.SingleAsync(
            value => value.Id == attempt.PublishPlanId,
            cancellationToken);
        var planResult = JsonSerializer.Deserialize<SpacePublishPlanResult>(
                             plan.PlanJson,
                             Json)
                         ?? throw Processing(
                             SpaceJobFailureKind.Bug,
                             SpaceErrorCodes.PublishJobMismatch,
                             "The immutable publish plan could not be read.");
        if (!string.Equals(
                planResult.PlanHash,
                plan.PlanHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Processing(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.PublishJobMismatch,
                "The immutable publish plan hash did not match its payload.");
        }

        var target = await _context.Versions.SingleAsync(
            value => value.Id == attempt.TargetVersionId,
            cancellationToken);
        var model = await _context.Models.SingleAsync(
            value => value.Id == target.ModelId,
            cancellationToken);
        if (target.Status == SpaceVersionStatus.ReconciliationRequired)
        {
            if (execution.Lease.JobType != SpaceJobType.Reconcile)
            {
                throw Processing(
                    SpaceJobFailureKind.Input,
                    SpaceErrorCodes.PublishRetryNotAllowed,
                    "A reconciliation-required version needs a manual recovery Job.");
            }
            target.ResumePublishingAfterReconciliation();
        }
        else if (target.Status != SpaceVersionStatus.Publishing)
        {
            throw Processing(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.PublishJobMismatch,
                "The target version left the active publishing state.");
        }

        attempt.BeginPreflight();
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.ProcessingStarted,
            JobEventKey(execution, "start"),
            "The publish worker acquired the Job and started processing.",
            errorCode: null,
            JsonSerializer.Serialize(
                new
                {
                    execution.Lease.AttemptNo,
                    execution.Lease.WorkerId,
                    execution.Lease.JobType,
                    plan.PlanHash,
                },
                Json),
            cancellationToken);
        await _context.SaveChangesAsync(CancellationToken.None);

        var warehouse = await _warehouses.ResolveAsync(
                            attempt.SiteId,
                            cancellationToken)
                        ?? throw Processing(
                            SpaceJobFailureKind.Input,
                            SpaceErrorCodes.ModelNotFound,
                            "The CP6 runtime site for the publish Job was not found.");
        var wmsContext = new SpaceWmsContext(
            attempt.TenantId,
            attempt.SiteId,
            warehouse.WarehouseCode,
            attempt.CorrelationId);
        SpaceWmsCapabilitySnapshot capabilities;
        SpaceWmsHealth health;
        try
        {
            capabilities = await _adapter.GetCapabilitiesAsync(
                wmsContext,
                cancellationToken);
            health = await _adapter.CheckHealthAsync(
                wmsContext,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            throw await ScheduleRetryAsync(
                execution,
                attempt,
                SpacePublishStep.Preflight,
                SpaceErrorCodes.WmsUnavailable,
                "WMS capability or health discovery timed out.",
                cancellationToken);
        }
        if (!string.Equals(
                capabilities.AdapterId,
                plan.AdapterId,
                StringComparison.Ordinal) ||
            !string.Equals(
                capabilities.CapabilityHash,
                plan.CapabilityHash,
                StringComparison.OrdinalIgnoreCase) ||
            !capabilities.SupportsProductionPublishing)
        {
            var hasExternalEvidence = await _context.PublishBatches.AnyAsync(
                value =>
                    value.AttemptId == attempt.Id &&
                    value.Status != SpacePublishBatchStatus.Pending,
                cancellationToken);
            if (hasExternalEvidence)
            {
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    SpaceErrorCodes.WmsCapabilityMissing,
                    SpaceReconciliationClassification.WmsUncertain,
                    "The WMS capability changed after external publish evidence was recorded.",
                    CancellationToken.None);
                return Output(attempt);
            }
            return await FailNoEffectAsync(
                execution,
                attempt,
                target,
                SpaceErrorCodes.WmsCapabilityMissing,
                "The certified WMS capability no longer matches the immutable plan.",
                cancellationToken);
        }
        if (!SpaceWmsContract.CanPublish(capabilities, health))
        {
            throw await ScheduleRetryAsync(
                execution,
                attempt,
                SpacePublishStep.Preflight,
                SpaceErrorCodes.WmsUnavailable,
                "WMS is temporarily unavailable; the current production version remains active.",
                cancellationToken);
        }

        var batches = await LoadOrCreateBatchesAsync(
            execution,
            attempt,
            target,
            model,
            plan,
            planResult,
            warehouse.SiteCode,
            wmsContext,
            capabilities,
            cancellationToken);

        foreach (var batch in batches.Where(value => value.Entity.Status is
                     SpacePublishBatchStatus.Pending or
                     SpacePublishBatchStatus.FailedNoEffect))
        {
            SpaceWmsPreflightResult preflight;
            try
            {
                preflight = await _adapter.PreflightAsync(
                    new SpaceWmsPreflightRequest(
                        wmsContext,
                        attempt.Id,
                        plan.PlanHash,
                        capabilities.CapabilityHash,
                        batch.Request.Items),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                throw await ScheduleRetryAsync(
                    execution,
                    attempt,
                    SpacePublishStep.Preflight,
                    SpaceErrorCodes.WmsUnavailable,
                    "WMS preflight timed out without a confirmed external effect.",
                    cancellationToken);
            }
            if (!string.Equals(
                    preflight.CapabilityHash,
                    capabilities.CapabilityHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !preflight.CanApply)
            {
                var code = preflight.Issues
                    .FirstOrDefault(value => value.Blocking)?.Code ??
                    SpaceErrorCodes.WmsCapabilityMissing;
                var hasExternalEvidence = batches.Any(value => value.Entity.Status is
                    SpacePublishBatchStatus.Applied or
                    SpacePublishBatchStatus.Verified or
                    SpacePublishBatchStatus.Partial or
                    SpacePublishBatchStatus.Uncertain);
                if (!hasExternalEvidence)
                {
                    return await FailNoEffectAsync(
                        execution,
                        attempt,
                        target,
                        code,
                        "WMS preflight rejected the publish plan without external effects.",
                        cancellationToken);
                }
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    code,
                    SpaceReconciliationClassification.WmsPartial,
                    "WMS preflight changed after earlier external publish evidence.",
                    CancellationToken.None);
                return Output(attempt);
            }
        }
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.PreflightPassed,
            JobEventKey(execution, "preflight"),
            "WMS preflight passed for every pending batch.",
            errorCode: null,
            JsonSerializer.Serialize(
                new { batchCount = batches.Count, capabilities.CapabilityHash },
                Json),
            cancellationToken);

        attempt.BeginApplyingWms();
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.WmsApplyStarted,
            JobEventKey(execution, "apply"),
            "The publish worker entered the WMS apply phase.",
            errorCode: null,
            JsonSerializer.Serialize(new { batchCount = batches.Count }, Json),
            cancellationToken);
        await _context.SaveChangesAsync(CancellationToken.None);

        foreach (var batch in batches)
        {
            if (batch.Entity.Status is (
                    SpacePublishBatchStatus.Applied or
                    SpacePublishBatchStatus.Verified))
            {
                continue;
            }

            if (batch.Entity.Status is (
                    SpacePublishBatchStatus.Applying or
                    SpacePublishBatchStatus.Uncertain or
                    SpacePublishBatchStatus.Partial))
            {
                var recovered = await TryGetStatusAsync(
                    batch.Request,
                    cancellationToken);
                if (recovered is null || recovered.State is (
                        SpaceWmsOperationState.Pending or
                        SpaceWmsOperationState.Unknown))
                {
                    if (batch.Entity.Status == SpacePublishBatchStatus.Applying)
                    {
                        batch.Entity.RecordResult(
                            SpacePublishBatchStatus.Uncertain,
                            recovered?.ExternalOperationId,
                            JsonSerializer.Serialize(
                                (object?)recovered ?? new { state = "StatusUnavailable" },
                                Json),
                            recovered?.ObservedAtUtc.UtcDateTime ?? RequireUtcNow());
                    }
                    throw await ScheduleRetryAsync(
                        execution,
                        attempt,
                        SpacePublishStep.ApplyWms,
                        SpaceErrorCodes.WmsResultUncertain,
                        "WMS operation status is not terminal yet.",
                        cancellationToken);
                }
                if (recovered.State == SpaceWmsOperationState.Partial)
                {
                    if (batch.Entity.Status == SpacePublishBatchStatus.Applying)
                    {
                        batch.Entity.RecordResult(
                            SpacePublishBatchStatus.Partial,
                            recovered.ExternalOperationId,
                            JsonSerializer.Serialize(recovered, Json),
                            recovered.ObservedAtUtc.UtcDateTime);
                    }
                    await RequireReconciliationAsync(
                        attempt,
                        target,
                        SpaceErrorCodes.WmsPartialResult,
                        SpaceReconciliationClassification.WmsPartial,
                        "WMS reported a confirmed partial publish result.",
                        CancellationToken.None);
                    return Output(attempt);
                }
                if (recovered.State == SpaceWmsOperationState.FailedNoEffect &&
                    batch.Entity.Status == SpacePublishBatchStatus.Applying)
                {
                    batch.Entity.RecordResult(
                        SpacePublishBatchStatus.FailedNoEffect,
                        recovered.ExternalOperationId,
                        JsonSerializer.Serialize(recovered, Json),
                        recovered.ObservedAtUtc.UtcDateTime);
                }
            }

            if (batch.Entity.Status != SpacePublishBatchStatus.Applying)
            {
                batch.Entity.BeginApply(execution.Lease.AttemptNo);
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            SpaceWmsBatchResult? result = null;
            try
            {
                result = await _adapter.ApplyBatchAsync(
                    batch.Request,
                    cancellationToken);
            }
            catch
            {
                var recovered = await TryGetStatusAsync(
                    batch.Request,
                    CancellationToken.None);
                if (recovered?.State == SpaceWmsOperationState.Applied)
                {
                    try
                    {
                        result = await _adapter.ApplyBatchAsync(
                            batch.Request,
                            cancellationToken);
                    }
                    catch
                    {
                        // A complete idempotent replay receipt is still required.
                    }
                }
                if (result is null && recovered?.State == SpaceWmsOperationState.Partial)
                {
                    batch.Entity.RecordResult(
                        SpacePublishBatchStatus.Partial,
                        recovered.ExternalOperationId,
                        JsonSerializer.Serialize(recovered, Json),
                        recovered.ObservedAtUtc.UtcDateTime);
                    await RequireReconciliationAsync(
                        attempt,
                        target,
                        SpaceErrorCodes.WmsPartialResult,
                        SpaceReconciliationClassification.WmsPartial,
                        "WMS reported a partial result after an apply timeout.",
                        CancellationToken.None);
                    return Output(attempt);
                }
            }

            if (result is null)
            {
                batch.Entity.RecordResult(
                    SpacePublishBatchStatus.Uncertain,
                    externalOperationId: null,
                    JsonSerializer.Serialize(new { state = "StatusUnavailable" }, Json),
                    RequireUtcNow());
                throw await ScheduleRetryAsync(
                    execution,
                    attempt,
                    SpacePublishStep.ApplyWms,
                    SpaceErrorCodes.WmsResultUncertain,
                    "WMS apply timed out and no terminal result was available.",
                    cancellationToken);
            }

            var assessment = SpaceWmsContract.AssessBatchResult(
                batch.Request,
                result);
            var batchStatus = assessment.Kind switch
            {
                SpaceWmsBatchAssessmentKind.Succeeded =>
                    SpacePublishBatchStatus.Applied,
                SpaceWmsBatchAssessmentKind.FailedNoEffect =>
                    SpacePublishBatchStatus.FailedNoEffect,
                SpaceWmsBatchAssessmentKind.Partial =>
                    SpacePublishBatchStatus.Partial,
                _ => SpacePublishBatchStatus.Uncertain,
            };
            batch.Entity.RecordResult(
                batchStatus,
                result.ExternalOperationId,
                JsonSerializer.Serialize(result, Json),
                result.ObservedAtUtc.UtcDateTime);
            await AddReceiptsAsync(
                batch.Entity,
                result,
                CancellationToken.None);
            await AppendAuditAsync(
                attempt,
                execution.Lease.JobId,
                batch.Entity.Id,
                SpacePublishAuditEventType.WmsApplyObserved,
                JobEventKey(execution, $"batch:{batch.Entity.BatchNo}:{batch.Entity.AttemptCount}"),
                $"WMS batch {batch.Entity.BatchNo} returned {assessment.Kind}.",
                result.Items.FirstOrDefault(value => value.ErrorCode is not null)?.ErrorCode,
                JsonSerializer.Serialize(
                    new
                    {
                        batch.Entity.BatchNo,
                        assessment.Kind,
                        result.ExternalOperationId,
                        result.ObservedAtUtc,
                    },
                    Json),
                cancellationToken);
            await _context.SaveChangesAsync(CancellationToken.None);

            if (assessment.Kind == SpaceWmsBatchAssessmentKind.Succeeded)
                continue;
            if (assessment.Kind == SpaceWmsBatchAssessmentKind.FailedNoEffect &&
                result.Items.All(value => value.Outcome == SpaceWmsItemOutcome.Rejected) &&
                !batches.Any(value => value.Entity.Status is
                    SpacePublishBatchStatus.Applied or
                    SpacePublishBatchStatus.Verified))
            {
                return await FailNoEffectAsync(
                    execution,
                    attempt,
                    target,
                    result.Items.FirstOrDefault()?.ErrorCode ??
                    SpaceErrorCodes.WmsUnavailable,
                    "WMS rejected the publish batch and proved zero effect.",
                    cancellationToken);
            }
            if (assessment.Kind == SpaceWmsBatchAssessmentKind.FailedNoEffect)
            {
                throw await ScheduleRetryAsync(
                    execution,
                    attempt,
                    SpacePublishStep.ApplyWms,
                    SpaceErrorCodes.WmsUnavailable,
                    "WMS proved zero effect for a retryable apply attempt.",
                    cancellationToken);
            }

            await RequireReconciliationAsync(
                attempt,
                target,
                assessment.Kind == SpaceWmsBatchAssessmentKind.Partial
                    ? SpaceErrorCodes.WmsPartialResult
                    : SpaceErrorCodes.WmsResultUncertain,
                assessment.Kind == SpaceWmsBatchAssessmentKind.Partial
                    ? SpaceReconciliationClassification.WmsPartial
                    : SpaceReconciliationClassification.WmsUncertain,
                assessment.Kind == SpaceWmsBatchAssessmentKind.Partial
                    ? "WMS applied only part of the publish batch."
                    : "WMS returned contradictory publish evidence.",
                CancellationToken.None);
            return Output(attempt);
        }

        attempt.BeginVerifyingWms(RequireUtcNow());
        await _context.SaveChangesAsync(CancellationToken.None);
        foreach (var batch in batches)
        {
            if (batch.Entity.Status == SpacePublishBatchStatus.Verified)
                continue;
            SpaceWmsReadBackResult readBack;
            try
            {
                readBack = await _adapter.ReadBackAsync(
                    new SpaceWmsReadBackRequest(
                        wmsContext,
                        batch.Request.OperationKey,
                        batch.Request.PayloadHash,
                        plan.PlanHash,
                        batch.Request.Items
                            .Select(value => value.LogicalId)
                            .ToArray()),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                throw await ScheduleRetryAsync(
                    execution,
                    attempt,
                    SpacePublishStep.VerifyWms,
                    SpaceErrorCodes.WmsResultUncertain,
                    "WMS readback timed out; runtime activation remains blocked.",
                    cancellationToken);
            }
            var receipts = await _context.WmsReceipts
                .AsNoTracking()
                .Where(value => value.BatchId == batch.Entity.Id)
                .ToDictionaryAsync(value => value.LogicalId, cancellationToken);
            var mismatch = VerifyReadBack(batch.Request, readBack, receipts);
            if (mismatch is not null)
            {
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    SpaceErrorCodes.WmsResultUncertain,
                    SpaceReconciliationClassification.WmsReadBackMismatch,
                    mismatch,
                    CancellationToken.None);
                return Output(attempt);
            }
            batch.Entity.MarkVerified();
            await AppendAuditAsync(
                attempt,
                execution.Lease.JobId,
                batch.Entity.Id,
                SpacePublishAuditEventType.WmsVerified,
                JobEventKey(execution, $"verify:{batch.Entity.BatchNo}"),
                $"WMS batch {batch.Entity.BatchNo} passed authoritative readback.",
                errorCode: null,
                JsonSerializer.Serialize(
                    new { batch.Entity.BatchNo, readBack.AggregateHash },
                    Json),
                cancellationToken);
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        attempt.BeginActivatingRuntime();
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.RuntimeActivationStarted,
            JobEventKey(execution, "runtime"),
            "All WMS batches were verified; runtime activation started.",
            errorCode: null,
            JsonSerializer.Serialize(new { plan.PlanHash }, Json),
            cancellationToken);
        await _context.SaveChangesAsync(CancellationToken.None);
        try
        {
            var activated = await _runtime.ActivateAsync(
                new SpaceRuntimeActivationRequest(
                    attempt.Id,
                    model.SiteId,
                    target.Id,
                    attempt.BaseVersionId,
                    plan.PlanHash,
                    attempt.RequestedBy),
                cancellationToken);
            attempt.Complete(
                RequireUtcNow(),
                "Runtime activated with hash " +
                activated.MaterializedHash + ".");
            var resolvedIssueCount = await ResolveReconciliationIssuesAsync(
                attempt.Id,
                $"Recovered and completed by Job {execution.Lease.JobId:D}.",
                CancellationToken.None);
            if (resolvedIssueCount > 0)
            {
                await AppendAuditAsync(
                    attempt,
                    execution.Lease.JobId,
                    batchId: null,
                    SpacePublishAuditEventType.ReconciliationResolved,
                    JobEventKey(execution, "reconciliation-resolved"),
                    "Open publish reconciliation issues were resolved by verified recovery.",
                    errorCode: null,
                    JsonSerializer.Serialize(new { resolvedIssueCount }, Json),
                    CancellationToken.None);
            }
            await AppendAuditAsync(
                attempt,
                execution.Lease.JobId,
                batchId: null,
                SpacePublishAuditEventType.Completed,
                JobEventKey(execution, "completed"),
                "The Published pointer changed only after WMS and runtime readback succeeded.",
                errorCode: null,
                JsonSerializer.Serialize(activated, Json),
                CancellationToken.None);
            await _context.SaveChangesAsync(CancellationToken.None);
            return Output(attempt);
        }
        catch (Exception exception)
        {
            _context.ChangeTracker.Clear();
            var persistedModel = await _context.Models.SingleAsync(
                value => value.Id == model.Id,
                CancellationToken.None);
            var persistedTarget = await _context.Versions.SingleAsync(
                value => value.Id == target.Id,
                CancellationToken.None);
            var persistedAttempt = await _context.PublishAttempts.SingleAsync(
                value => value.Id == attempt.Id,
                CancellationToken.None);
            if (persistedModel.CurrentPublishedVersionId == persistedTarget.Id &&
                persistedTarget.Status == SpaceVersionStatus.Published)
            {
                persistedAttempt.Complete(
                    RequireUtcNow(),
                    "Runtime activation committed; completion was recovered from the Published pointer.");
                var resolvedIssueCount = await ResolveReconciliationIssuesAsync(
                    persistedAttempt.Id,
                    $"Recovered from the committed Published pointer by Job {execution.Lease.JobId:D}.",
                    CancellationToken.None);
                if (resolvedIssueCount > 0)
                {
                    await AppendAuditAsync(
                        persistedAttempt,
                        execution.Lease.JobId,
                        batchId: null,
                        SpacePublishAuditEventType.ReconciliationResolved,
                        JobEventKey(execution, "reconciliation-resolved-recovered"),
                        "Open reconciliation issues were resolved from the committed Published pointer.",
                        errorCode: null,
                        JsonSerializer.Serialize(new { resolvedIssueCount }, Json),
                        CancellationToken.None);
                }
                await AppendAuditAsync(
                    persistedAttempt,
                    execution.Lease.JobId,
                    batchId: null,
                    SpacePublishAuditEventType.Completed,
                    JobEventKey(execution, "completed-recovered"),
                    "Runtime completion was recovered from the committed Published pointer.",
                    errorCode: null,
                    JsonSerializer.Serialize(new { recovered = true }, Json),
                    CancellationToken.None);
                await _context.SaveChangesAsync(CancellationToken.None);
                return Output(persistedAttempt);
            }
            throw await ScheduleRetryAsync(
                execution,
                persistedAttempt,
                SpacePublishStep.ActivateRuntime,
                SpaceErrorCodes.RuntimeActivationFailed,
                "WMS is verified, but runtime activation did not commit: " +
                exception.GetType().Name + ".",
                CancellationToken.None);
        }
    }

    private async Task<IReadOnlyList<(
        SpacePublishBatch Entity,
        SpaceWmsBatch Request)>> LoadOrCreateBatchesAsync(
        SpaceJobStepExecution execution,
        SpacePublishAttempt attempt,
        SpaceModelVersion target,
        SpaceModel model,
        SpacePublishPlan plan,
        SpacePublishPlanResult planResult,
        string siteCode,
        SpaceWmsContext wmsContext,
        SpaceWmsCapabilitySnapshot capabilities,
        CancellationToken cancellationToken)
    {
        var existing = await _context.PublishBatches
            .Where(value => value.AttemptId == attempt.Id)
            .OrderBy(value => value.BatchNo)
            .ToArrayAsync(cancellationToken);
        if (existing.Length != 0)
        {
            return existing.Select(value =>
            {
                var persisted = JsonSerializer.Deserialize<PersistedBatchRequest>(
                                    value.RequestJson,
                                    Json)
                                ?? throw Processing(
                                    SpaceJobFailureKind.Security,
                                    SpaceErrorCodes.PublishJobMismatch,
                                    "A persisted WMS batch request could not be read.");
                var request = SpaceWmsBatch.Create(
                    wmsContext,
                    attempt.Id,
                    value.BatchNo,
                    plan.PlanHash,
                    persisted.Items);
                if (!string.Equals(
                        request.OperationKey,
                        value.OperationKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        request.PayloadHash,
                        value.PayloadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Processing(
                        SpaceJobFailureKind.Security,
                        SpaceErrorCodes.PublishJobMismatch,
                        "A persisted WMS batch identity did not match its request payload.");
                }
                return (value, request);
            }).ToArray();
        }

        IReadOnlyDictionary<Guid, SpaceWmsAdoption> bindingMap;
        IReadOnlyDictionary<Guid, SpaceWmsLocationState> currentWms;
        try
        {
            bindingMap = await LoadWmsBindingsAsync(
                model.SiteId,
                capabilities.AdapterId,
                planResult.Items
                    .Where(IsWmsMutation)
                    .Select(value => value.LogicalId)
                    .ToArray(),
                cancellationToken);
            var wmsLogicalIds = planResult.Items
                .Where(IsWmsMutation)
                .Select(value =>
                    bindingMap.TryGetValue(value.LogicalId, out var binding)
                        ? binding.WmsLogicalId
                        : value.LogicalId)
                .Distinct()
                .ToArray();
            currentWms = wmsLogicalIds.Length == 0
                ? new Dictionary<Guid, SpaceWmsLocationState>()
                : (await _adapter.QueryLocationsAsync(
                        new SpaceWmsLocationQuery(wmsContext, wmsLogicalIds),
                        cancellationToken))
                    .Items
                    .ToDictionary(value => value.LogicalId);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            throw await ScheduleRetryAsync(
                execution,
                attempt,
                SpacePublishStep.Preflight,
                SpaceErrorCodes.WmsUnavailable,
                "WMS location discovery timed out before batch freezing.",
                cancellationToken);
        }
        var mutations = await BuildMutationsAsync(
            target,
            model.CurrentPublishedVersionId,
            siteCode,
            planResult,
            currentWms,
            bindingMap,
            cancellationToken);
        var created = CreateBatches(
            attempt,
            wmsContext,
            plan.PlanHash,
            mutations,
            capabilities.Capabilities.BatchMaxSize);
        _context.PublishBatches.AddRange(created.Select(value => value.Entity));
        await _context.SaveChangesAsync(CancellationToken.None);
        return created;
    }

    private async Task<SpaceJobProcessingException> ScheduleRetryAsync(
        SpaceJobStepExecution execution,
        SpacePublishAttempt attempt,
        SpacePublishStep step,
        string errorCode,
        string summary,
        CancellationToken cancellationToken)
    {
        attempt.WaitForRetry(step, errorCode, summary);
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.RetryableFailureObserved,
            JobEventKey(execution, $"failure:{step}:{errorCode}"),
            summary,
            errorCode,
            JsonSerializer.Serialize(
                new { execution.Lease.AttemptNo, step = step.ToString() },
                Json),
            CancellationToken.None);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Processing(
            SpaceJobFailureKind.Transient,
            errorCode,
            summary);
    }

    private async Task<SpaceJobStepOutput> FailNoEffectAsync(
        SpaceJobStepExecution execution,
        SpacePublishAttempt attempt,
        SpaceModelVersion target,
        string errorCode,
        string summary,
        CancellationToken cancellationToken)
    {
        target.ReturnToReadyBeforeExternalCommit();
        attempt.FailNoEffect(errorCode, summary, RequireUtcNow());
        await AppendAuditAsync(
            attempt,
            execution.Lease.JobId,
            batchId: null,
            SpacePublishAuditEventType.FailedNoEffect,
            JobEventKey(execution, $"failed-no-effect:{errorCode}"),
            summary,
            errorCode,
            JsonSerializer.Serialize(new { externalEffect = false }, Json),
            cancellationToken);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Output(attempt);
    }

    private async Task<int> ResolveReconciliationIssuesAsync(
        Guid attemptId,
        string resolution,
        CancellationToken cancellationToken)
    {
        var issues = await _context.ReconciliationIssues
            .Where(value =>
                value.AttemptId == attemptId &&
                value.Status != SpaceReconciliationStatus.Resolved)
            .ToArrayAsync(cancellationToken);
        foreach (var issue in issues)
            issue.Resolve(resolution);
        return issues.Length;
    }

    private static SpaceJobStepOutput Output(SpacePublishAttempt attempt)
    {
        var checkpoint = JsonSerializer.Serialize(
            new
            {
                publishAttemptId = attempt.Id,
                status = attempt.Status.ToString(),
                step = attempt.CurrentStep.ToString(),
                attempt.JobId,
                attempt.LastErrorCode,
            },
            Json);
        return new SpaceJobStepOutput(checkpoint, Hash(checkpoint));
    }

    private static string JobEventKey(
        SpaceJobStepExecution execution,
        string suffix) =>
        $"job:{execution.Lease.JobId:D}:attempt:{execution.Lease.AttemptNo}:{suffix}";

    private static SpaceJobProcessingException Processing(
        SpaceJobFailureKind kind,
        string code,
        string summary) => new(kind, code, summary);

    private sealed record PersistedBatchRequest(
        IReadOnlyList<SpaceWmsLocationMutation> Items);
}

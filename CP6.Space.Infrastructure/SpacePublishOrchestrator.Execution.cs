using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed partial class SpacePublishOrchestrator
{
    private async Task ExecuteAsync(
        SpacePublishAttempt attempt,
        SpaceModelVersion target,
        SpaceModel model,
        SpaceWmsContext wmsContext,
        SpaceWmsCapabilitySnapshot capabilities,
        string planHash,
        IReadOnlyList<(SpacePublishBatch Entity, SpaceWmsBatch Request)> batches,
        CancellationToken cancellationToken)
    {
        var persistenceToken = CancellationToken.None;
        attempt.BeginPreflight();
        await _context.SaveChangesAsync(persistenceToken);

        foreach (var batch in batches)
        {
            SpaceWmsPreflightResult preflight;
            try
            {
                preflight = await _adapter.PreflightAsync(
                    new SpaceWmsPreflightRequest(
                        wmsContext,
                        attempt.Id,
                        planHash,
                        capabilities.CapabilityHash,
                        batch.Request.Items),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                target.ReturnToReadyBeforeExternalCommit();
                attempt.FailNoEffect(
                    SpaceErrorCodes.WmsUnavailable,
                    "WMS preflight failed without external effects: " +
                    exception.GetType().Name + ".",
                    RequireUtcNow());
                await _context.SaveChangesAsync(persistenceToken);
                return;
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
                target.ReturnToReadyBeforeExternalCommit();
                attempt.FailNoEffect(
                    code,
                    "WMS preflight rejected the publish plan without external effects.",
                    RequireUtcNow());
                await _context.SaveChangesAsync(persistenceToken);
                return;
            }
        }

        attempt.BeginApplyingWms();
        await _context.SaveChangesAsync(persistenceToken);

        // An external write may now exist. Do not lose evidence when the
        // initiating HTTP request is cancelled.
        var sagaToken = CancellationToken.None;
        var appliedBatchCount = 0;
        foreach (var batch in batches)
        {
            batch.Entity.BeginApply();
            await _context.SaveChangesAsync(sagaToken);

            SpaceWmsBatchResult? result = null;
            SpaceWmsOperationStatus? recoveredStatus = null;
            try
            {
                result = await _adapter.ApplyBatchAsync(
                    batch.Request,
                    sagaToken);
            }
            catch
            {
                recoveredStatus = await TryGetStatusAsync(
                    batch.Request,
                    sagaToken);
            }

            if (result is null &&
                recoveredStatus?.State == SpaceWmsOperationState.Applied)
            {
                try
                {
                    result = await _adapter.ApplyBatchAsync(
                        batch.Request,
                        sagaToken);
                }
                catch
                {
                    // A complete idempotent replay receipt is still required.
                }
            }

            if (result is null)
            {
                if (recoveredStatus?.State ==
                    SpaceWmsOperationState.FailedNoEffect)
                {
                    batch.Entity.RecordResult(
                        SpacePublishBatchStatus.FailedNoEffect,
                        recoveredStatus.ExternalOperationId,
                        JsonSerializer.Serialize(recoveredStatus, Json),
                        recoveredStatus.ObservedAtUtc.UtcDateTime);
                    await _context.SaveChangesAsync(sagaToken);
                    if (appliedBatchCount == 0)
                    {
                        target.ReturnToReadyBeforeExternalCommit();
                        attempt.FailNoEffect(
                            SpaceErrorCodes.WmsUnavailable,
                            "WMS proved that the operation had no effect.",
                            RequireUtcNow());
                        await _context.SaveChangesAsync(sagaToken);
                    }
                    else
                    {
                        await RequireReconciliationAsync(
                            attempt,
                            target,
                            SpaceErrorCodes.WmsPartialResult,
                            SpaceReconciliationClassification.WmsPartial,
                            "A later WMS batch had no effect after an earlier batch was applied.",
                            sagaToken);
                    }
                    return;
                }

                var partial = recoveredStatus?.State ==
                    SpaceWmsOperationState.Partial;
                batch.Entity.RecordResult(
                    partial
                        ? SpacePublishBatchStatus.Partial
                        : SpacePublishBatchStatus.Uncertain,
                    recoveredStatus?.ExternalOperationId,
                    JsonSerializer.Serialize(
                        (object?)recoveredStatus ??
                        new { State = "StatusUnavailable" },
                        Json),
                    recoveredStatus?.ObservedAtUtc.UtcDateTime ??
                    RequireUtcNow());
                await _context.SaveChangesAsync(sagaToken);
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    partial
                        ? SpaceErrorCodes.WmsPartialResult
                        : SpaceErrorCodes.WmsResultUncertain,
                    partial
                        ? SpaceReconciliationClassification.WmsPartial
                        : SpaceReconciliationClassification.WmsUncertain,
                    partial
                        ? "Recovered WMS status was partial."
                        : "WMS apply evidence was unavailable or uncertain.",
                    sagaToken);
                return;
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
            AddReceipts(batch.Entity, result);
            await _context.SaveChangesAsync(sagaToken);

            if (assessment.Kind == SpaceWmsBatchAssessmentKind.Succeeded)
            {
                appliedBatchCount++;
                continue;
            }

            if (assessment.Kind ==
                    SpaceWmsBatchAssessmentKind.FailedNoEffect &&
                appliedBatchCount == 0)
            {
                target.ReturnToReadyBeforeExternalCommit();
                attempt.FailNoEffect(
                    SpaceErrorCodes.WmsUnavailable,
                    "WMS rejected the batch and proved zero effect.",
                    RequireUtcNow());
                await _context.SaveChangesAsync(sagaToken);
                return;
            }

            var isPartial = assessment.Kind is
                SpaceWmsBatchAssessmentKind.Partial or
                SpaceWmsBatchAssessmentKind.FailedNoEffect;
            await RequireReconciliationAsync(
                attempt,
                target,
                isPartial
                    ? SpaceErrorCodes.WmsPartialResult
                    : SpaceErrorCodes.WmsResultUncertain,
                isPartial
                    ? SpaceReconciliationClassification.WmsPartial
                    : SpaceReconciliationClassification.WmsUncertain,
                isPartial
                    ? "WMS applied only part of the publish plan."
                    : "WMS result evidence was incomplete or contradictory.",
                sagaToken);
            return;
        }

        attempt.BeginVerifyingWms(RequireUtcNow());
        await _context.SaveChangesAsync(sagaToken);

        foreach (var batch in batches)
        {
            SpaceWmsReadBackResult readBack;
            try
            {
                readBack = await _adapter.ReadBackAsync(
                    new SpaceWmsReadBackRequest(
                        wmsContext,
                        batch.Request.OperationKey,
                        batch.Request.PayloadHash,
                        planHash,
                        batch.Request.Items
                            .Select(value => value.LogicalId)
                            .ToArray()),
                    sagaToken);
            }
            catch (Exception exception)
            {
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    SpaceErrorCodes.WmsResultUncertain,
                    SpaceReconciliationClassification.WmsReadBackMismatch,
                    "WMS readback failed: " +
                    exception.GetType().Name + ".",
                    sagaToken);
                return;
            }

            var receipts = await _context.WmsReceipts
                .AsNoTracking()
                .Where(value => value.BatchId == batch.Entity.Id)
                .ToDictionaryAsync(value => value.LogicalId, sagaToken);
            var mismatch = VerifyReadBack(
                batch.Request,
                readBack,
                receipts);
            if (mismatch is not null)
            {
                await RequireReconciliationAsync(
                    attempt,
                    target,
                    SpaceErrorCodes.WmsResultUncertain,
                    SpaceReconciliationClassification.WmsReadBackMismatch,
                    mismatch,
                    sagaToken);
                return;
            }

            batch.Entity.MarkVerified();
            await _context.SaveChangesAsync(sagaToken);
        }

        attempt.BeginActivatingRuntime();
        await _context.SaveChangesAsync(sagaToken);

        try
        {
            var activated = await _runtime.ActivateAsync(
                new SpaceRuntimeActivationRequest(
                    attempt.Id,
                    model.SiteId,
                    target.Id,
                    attempt.BaseVersionId,
                    planHash,
                    attempt.RequestedBy),
                sagaToken);
            attempt.Complete(
                RequireUtcNow(),
                "Runtime activated with hash " +
                activated.MaterializedHash + ".");
            await _context.SaveChangesAsync(sagaToken);
        }
        catch (Exception exception)
        {
            _context.ChangeTracker.Clear();
            var persistedModel = await _context.Models.SingleAsync(
                value => value.Id == model.Id,
                sagaToken);
            var persistedTarget = await _context.Versions.SingleAsync(
                value => value.Id == target.Id,
                sagaToken);
            var persistedAttempt =
                await _context.PublishAttempts.SingleAsync(
                    value => value.Id == attempt.Id,
                    sagaToken);
            if (persistedModel.CurrentPublishedVersionId ==
                    persistedTarget.Id &&
                persistedTarget.Status == SpaceVersionStatus.Published)
            {
                persistedAttempt.Complete(
                    RequireUtcNow(),
                    "Runtime activation committed; completion was recovered from the version pointer.");
                await _context.SaveChangesAsync(sagaToken);
                return;
            }

            await RequireReconciliationAsync(
                persistedAttempt,
                persistedTarget,
                SpaceErrorCodes.RuntimeActivationFailed,
                SpaceReconciliationClassification.RuntimeActivationFailed,
                "WMS was verified, but runtime activation failed: " +
                exception.GetType().Name + ".",
                sagaToken);
        }
    }
}

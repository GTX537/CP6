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

public sealed partial class SpacePublishOrchestrator : ISpacePublishOrchestrator
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
            CorrelationId());
        var capabilities = await _adapter.GetCapabilitiesAsync(
            wmsContext,
            cancellationToken);
        var health = await _adapter.CheckHealthAsync(
            wmsContext,
            cancellationToken);
        if (!SpaceWmsContract.CanPublish(capabilities, health))
        {
            throw Unprocessable(
                health.IsPublishAvailable
                    ? SpaceErrorCodes.WmsCapabilityMissing
                    : SpaceErrorCodes.WmsUnavailable,
                "The selected WMS adapter is not certified and healthy " +
                "for production publishing.",
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

        var bindingMap = await LoadWmsBindingsAsync(
            model.SiteId,
            capabilities.AdapterId,
            plan.Items
                .Where(IsWmsMutation)
                .Select(value => value.LogicalId)
                .ToArray(),
            cancellationToken);
        var wmsLogicalIds = plan.Items
            .Where(IsWmsMutation)
            .Select(value =>
                bindingMap.TryGetValue(value.LogicalId, out var binding)
                    ? binding.WmsLogicalId
                    : value.LogicalId)
            .Distinct()
            .ToArray();
        var currentWms = wmsLogicalIds.Length == 0
            ? new Dictionary<Guid, SpaceWmsLocationState>()
            : (await _adapter.QueryLocationsAsync(
                    new SpaceWmsLocationQuery(
                        wmsContext,
                        wmsLogicalIds),
                    cancellationToken))
                .Items
                .ToDictionary(value => value.LogicalId);
        var mutations = await BuildMutationsAsync(
            target,
            model.CurrentPublishedVersionId,
            warehouse.SiteCode,
            plan,
            currentWms,
            bindingMap,
            cancellationToken);
        var plannedContentRevision = target.ContentRevision;

        SpacePublishAttempt attempt;
        IReadOnlyList<(SpacePublishBatch Entity, SpaceWmsBatch Request)>
            batches;
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
                RequireUtcNow(),
                CorrelationId());
            _context.PublishAttempts.Add(attempt);
            batches = CreateBatches(
                attempt,
                wmsContext,
                plan.PlanHash,
                mutations,
                capabilities.Capabilities.BatchMaxSize);
            _context.PublishBatches.AddRange(
                batches.Select(value => value.Entity));
            target.BeginPublishing();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await ExecuteAsync(
            attempt,
            target,
            model,
            wmsContext,
            capabilities,
            plan.PlanHash,
            batches,
            cancellationToken);

        return new CreateSpacePublishAttemptResponse(
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

    private void AddReceipts(
        SpacePublishBatch batch,
        SpaceWmsBatchResult result)
    {
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
            _context.WmsReceipts.Add(
                SpaceWmsReceipt.Create(
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
                    result.ObservedAtUtc.UtcDateTime));
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
                    request.PayloadHash),
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
            openIssueCount,
            batches.Select(value => new SpacePublishBatchDto(
                value.Id,
                value.BatchNo,
                value.OperationKey,
                value.PayloadHash,
                value.Status.ToString(),
                value.AttemptCount,
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
                .ToArray());
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

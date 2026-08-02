using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CP6.Space.Application;

namespace CP6.Space.Infrastructure;

public sealed class StandardSpaceWmsSimulator :
    ISpaceWmsAdapter,
    ISpaceWmsSimulatorControl
{
    public const string AdapterId = "standard-wms-simulator-v1";
    public const string DataSourceId = "STANDARD_WMS_SIMULATOR";

    private static readonly SpaceWmsCapabilities Capabilities = new(
        AtomicStaging: true,
        IdempotentUpsert: true,
        IdempotentDisable: true,
        RenameLocation: true,
        QueryByLogicalId: true,
        QueryBlockingReferences: true,
        QueryInventory: true,
        QueryTasks: true,
        ReliableOperationStatus: true,
        ReadBackHash: true,
        BatchMaxSize: 1_000,
        AllowedCodePattern: "^[A-Za-z0-9][A-Za-z0-9._/-]{0,63}$",
        CodeMaxLength: 64);

    private readonly ConcurrentDictionary<ScopeKey, WarehouseState> _states =
        new();

    public string RuntimeAdapterId => AdapterId;
    public string RuntimeDataSourceId => DataSourceId;
    public SpaceWmsDataSourceKind RuntimeDataSourceKind =>
        SpaceWmsDataSourceKind.Simulated;

    public Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(
        SpaceWmsContext context,
        CancellationToken ct = default)
    {
        EnsureContext(context);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CapabilitySnapshot());
    }

    public async Task<SpaceWmsHealth> CheckHealthAsync(
        SpaceWmsContext context,
        CancellationToken ct = default)
    {
        var state = State(context);
        var started = DateTimeOffset.UtcNow;
        var fault = Fault(state);
        await ApplyDelayAsync(fault, ct);
        var unavailable = fault.Mode is
            SpaceWmsSimulatorFaultMode.Unavailable or
            SpaceWmsSimulatorFaultMode.Timeout;
        var finished = DateTimeOffset.UtcNow;
        return new SpaceWmsHealth(
            AdapterId,
            unavailable
                ? SpaceWmsHealthState.Unavailable
                : SpaceWmsHealthState.Healthy,
            finished,
            finished - started,
            unavailable ? ErrorCode(fault) : null);
    }

    public async Task<SpaceWmsPreflightResult> PreflightAsync(
        SpaceWmsPreflightRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = State(request.Context);
        var fault = Fault(state);
        await ThrowForTransportFaultAsync(fault, ct);
        var snapshot = CapabilitySnapshot();
        var issues = new List<SpaceWmsPreflightIssue>();
        if (!string.Equals(
                request.CapabilityHash,
                snapshot.CapabilityHash,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new SpaceWmsPreflightIssue(
                null,
                "SPACE_WMS_CAPABILITY_MISSING",
                true));
        }
        if (request.Items.Count > Capabilities.BatchMaxSize)
        {
            issues.Add(new SpaceWmsPreflightIssue(
                null,
                "SPACE_WMS_BATCH_LIMIT_EXCEEDED",
                true));
        }

        var codePattern = new Regex(
            Capabilities.AllowedCodePattern,
            RegexOptions.CultureInvariant);
        lock (state.Gate)
        {
            foreach (var item in request.Items)
            {
                if (item.LocationCode.Length > Capabilities.CodeMaxLength ||
                    !codePattern.IsMatch(item.LocationCode))
                {
                    issues.Add(new SpaceWmsPreflightIssue(
                        item.LogicalId,
                        "SPACE_WMS_LOCATION_CODE_UNSUPPORTED",
                        true));
                }
            }
            foreach (var failure in Validate(state, request.Items))
            {
                if (!issues.Any(issue =>
                        issue.LogicalId == failure.Key &&
                        string.Equals(
                            issue.Code,
                            failure.Value,
                            StringComparison.Ordinal)))
                {
                    issues.Add(new SpaceWmsPreflightIssue(
                        failure.Key,
                        failure.Value,
                        true));
                }
            }
        }
        return new SpaceWmsPreflightResult(
            snapshot.CapabilityHash,
            issues,
            DateTimeOffset.UtcNow);
    }

    public async Task<SpaceWmsBatchResult> ApplyBatchAsync(
        SpaceWmsBatch batch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var state = State(batch.Context);
        var fault = Fault(state);
        await ThrowForTransportFaultAsync(fault, ct);
        lock (state.Gate)
        {
            if (state.Operations.TryGetValue(
                    batch.OperationKey,
                    out var existing))
            {
                return string.Equals(
                    existing.PayloadHash,
                    batch.PayloadHash,
                    StringComparison.OrdinalIgnoreCase)
                    ? existing.Result
                    : FailureResult(
                        batch,
                        "WMS_IDEMPOTENCY_CONFLICT");
            }

            var result = fault.Mode switch
            {
                SpaceWmsSimulatorFaultMode.RejectAll =>
                    FailureResult(batch, ErrorCode(fault)),
                SpaceWmsSimulatorFaultMode.Partial =>
                    ApplyPartial(state, batch, fault),
                SpaceWmsSimulatorFaultMode.UnknownAfterApply =>
                    ApplyUnknown(state, batch, fault),
                _ => ApplyAtomic(state, batch),
            };
            state.Operations.Add(
                batch.OperationKey,
                new SimOperation(
                    batch.PayloadHash,
                    result,
                    ToOperationState(
                        SpaceWmsContract.AssessBatchResult(batch, result)
                            .Kind)));
            return result;
        }
    }

    public async Task<SpaceWmsOperationStatus> GetOperationStatusAsync(
        SpaceWmsOperationQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SpaceWmsContract.ValidateOperationKeyScope(
            request.Context,
            request.OperationKey);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            if (!state.Operations.TryGetValue(
                    request.OperationKey,
                    out var operation) ||
                !string.Equals(
                    operation.PayloadHash,
                    request.PayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SpaceWmsOperationStatus(
                    request.OperationKey,
                    request.PayloadHash,
                    SpaceWmsOperationState.FailedNoEffect,
                    true,
                    DateTimeOffset.UtcNow);
            }
            return new SpaceWmsOperationStatus(
                request.OperationKey,
                operation.PayloadHash,
                operation.State,
                true,
                operation.Result.ObservedAtUtc,
                operation.Result.ExternalOperationId);
        }
    }

    public async Task<SpaceWmsReadBackResult> ReadBackAsync(
        SpaceWmsReadBackRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SpaceWmsContract.ValidateOperationKeyScope(
            request.Context,
            request.OperationKey);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            var items = Locations(state, request.LogicalIds);
            var material = new StringBuilder()
                .Append(request.OperationKey).Append('\n')
                .Append(request.PayloadHash.ToLowerInvariant()).Append('\n')
                .Append(request.PlanHash.ToLowerInvariant()).Append('\n');
            foreach (var item in items.OrderBy(value => value.LogicalId))
            {
                material
                    .Append(item.LogicalId.ToString("D"))
                    .Append('|')
                    .Append(item.StateHash)
                    .Append('\n');
            }
            return new SpaceWmsReadBackResult(
                Source(),
                items,
                Hash(material.ToString()));
        }
    }

    public async Task<SpaceWmsBlockingReferences> GetBlockingReferencesAsync(
        SpaceWmsBlockingReferencesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            var ids = Filter(request.LogicalIds);
            var inventory = state.Inventory
                .Where(item =>
                    ids(item.LogicalId) &&
                    (item.PhysicalQuantity != 0 ||
                     item.AllocatedQuantity != 0))
                .Select(item => new SpaceWmsBlockingReference(
                    item.LogicalId,
                    SpaceWmsBlockingReferenceKind.Inventory,
                    $"{item.MaterialNumber ?? "STOCK"}:{item.LotNumber ?? "-"}",
                    item.PhysicalQuantity));
            var tasks = state.Tasks
                .Where(item =>
                    ids(item.LogicalId) &&
                    IsActiveTask(item.Status))
                .Select(item => new SpaceWmsBlockingReference(
                    item.LogicalId,
                    SpaceWmsBlockingReferenceKind.ActiveTask,
                    item.TaskId,
                    item.Quantity));
            return new SpaceWmsBlockingReferences(
                Source(),
                inventory.Concat(tasks).ToArray());
        }
    }

    public async Task<SpaceWmsLocationResult> QueryLocationsAsync(
        SpaceWmsLocationQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            return new SpaceWmsLocationResult(
                Source(),
                Locations(state, request.LogicalIds));
        }
    }

    public async Task<SpaceWmsInventoryResult> QueryInventoryAsync(
        SpaceWmsInventoryQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            var filter = Filter(request.LogicalIds);
            var locate = request.LocateCriteria;
            return new SpaceWmsInventoryResult(
                Source(),
                state.Inventory
                    .Where(item =>
                        filter(item.LogicalId) &&
                        (request.OwnerIds is null ||
                         (item.OwnerId is not null &&
                          request.OwnerIds.Any(ownerId =>
                              string.Equals(
                                  ownerId?.Trim(),
                                  item.OwnerId,
                                  StringComparison.OrdinalIgnoreCase)))) &&
                         (locate is null ||
                          (item.PhysicalQuantity > 0 &&
                           Matches(item.MaterialNumber, locate.MaterialNumber) &&
                           Matches(item.LotNumber, locate.LotNumber) &&
                           Matches(item.ContainerNumber, locate.ContainerNumber) &&
                           MatchesOwner(item.OwnerId, locate.OwnerId))))
                    .OrderBy(item => item.LocationCode, StringComparer.Ordinal)
                    .ThenBy(item => item.MaterialNumber, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    private static bool Matches(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(actual, expected.Trim(), StringComparison.Ordinal);

    private static bool MatchesOwner(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<SpaceWmsTaskResult> QueryTasksAsync(
        SpaceWmsTaskQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            var filter = Filter(request.LogicalIds);
            return new SpaceWmsTaskResult(
                Source(),
                state.Tasks
                    .Where(item =>
                        filter(item.LogicalId) &&
                        (request.TaskIds is null ||
                         request.TaskIds.Contains(
                             item.TaskId,
                             StringComparer.Ordinal)) &&
                        IsActiveTask(item.Status))
                    .OrderBy(item => item.TaskId, StringComparer.Ordinal)
                    .ThenBy(item => item.SequenceNo)
                    .ToArray());
        }
    }

    public async Task<SpaceWmsAbcResult> QueryAbcAsync(
        SpaceWmsAbcQuery request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FromDateInclusive == default ||
            request.ToDateExclusive == default ||
            request.FromDateInclusive >= request.ToDateExclusive)
        {
            throw new ArgumentException(
                "A valid half-open ABC analysis date window is required.",
                nameof(request));
        }
        var state = State(request.Context);
        await ThrowForTransportFaultAsync(Fault(state), ct);
        lock (state.Gate)
        {
            var items = state.OutboundMovements
                .Where(value =>
                    value.OccurredOn >= request.FromDateInclusive &&
                    value.OccurredOn < request.ToDateExclusive &&
                    value.Quantity > 0)
                .GroupBy(value => value.MaterialNumber, StringComparer.Ordinal)
                .Select(group => new SpaceWmsAbcAggregate(
                    group.Key,
                    group.Count(),
                    group.Sum(value => value.Quantity)))
                .OrderByDescending(value => value.OutboundQuantity)
                .ThenBy(value => value.MaterialNumber, StringComparer.Ordinal)
                .ToArray();
            return new SpaceWmsAbcResult(Source(), items);
        }
    }

    public void ConfigureFault(
        SpaceWmsContext context,
        SpaceWmsSimulatorFaultProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ApplyCount < 0)
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile.Delay < TimeSpan.Zero ||
            profile.Delay > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }
        var state = State(context);
        lock (state.Gate)
            state.Fault = profile;
    }

    public void SeedInventory(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsInventoryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var state = State(context);
        lock (state.Gate)
        {
            state.Inventory.Clear();
            state.Inventory.AddRange(items);
        }
    }

    public void SeedTasks(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsTaskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var state = State(context);
        lock (state.Gate)
        {
            state.Tasks.Clear();
            state.Tasks.AddRange(items);
        }
    }

    public void SeedOutboundMovements(
        SpaceWmsContext context,
        IReadOnlyCollection<SpaceWmsOutboundMovement> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(value =>
                value is null ||
                string.IsNullOrWhiteSpace(value.MovementId) ||
                string.IsNullOrWhiteSpace(value.MaterialNumber) ||
                value.OccurredOn == default ||
                value.Quantity <= 0) ||
            items.GroupBy(value => value.MovementId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Simulated outbound movements must be positive and uniquely identified.",
                nameof(items));
        }
        var state = State(context);
        lock (state.Gate)
        {
            state.OutboundMovements.Clear();
            state.OutboundMovements.AddRange(items);
        }
    }

    public void Reset(SpaceWmsContext context)
    {
        EnsureContext(context);
        _states.TryRemove(ScopeKey.From(context), out _);
    }

    private static SpaceWmsBatchResult ApplyAtomic(
        WarehouseState state,
        SpaceWmsBatch batch)
    {
        var failures = Validate(state, batch.Items);
        if (failures.Count > 0)
        {
            return new SpaceWmsBatchResult(
                batch.OperationKey,
                batch.PayloadHash,
                null,
                batch.Items.Select(item => FailureReceipt(
                    item,
                    failures.GetValueOrDefault(
                        item.LogicalId,
                        "WMS_ATOMIC_BATCH_REJECTED"))).ToArray(),
                DateTimeOffset.UtcNow);
        }
        var receipts = batch.Items
            .Select(item => Apply(state, item))
            .ToArray();
        return SuccessResult(batch, receipts);
    }

    private static SpaceWmsBatchResult ApplyPartial(
        WarehouseState state,
        SpaceWmsBatch batch,
        SpaceWmsSimulatorFaultProfile fault)
    {
        var applyCount = fault.ApplyCount > 0
            ? Math.Min(fault.ApplyCount, batch.Items.Count)
            : Math.Max(1, batch.Items.Count / 2);
        var receipts = new List<SpaceWmsItemReceipt>(batch.Items.Count);
        foreach (var item in batch.Items)
        {
            if (receipts.Count >= applyCount)
            {
                receipts.Add(FailureReceipt(
                    item,
                    ErrorCode(fault, "SPACE_WMS_PARTIAL_RESULT")));
                continue;
            }
            var failure = ValidateOne(state, item);
            receipts.Add(failure is null
                ? Apply(state, item)
                : FailureReceipt(item, failure));
        }
        return SuccessResult(batch, receipts);
    }

    private static SpaceWmsBatchResult ApplyUnknown(
        WarehouseState state,
        SpaceWmsBatch batch,
        SpaceWmsSimulatorFaultProfile fault)
    {
        var failures = Validate(state, batch.Items);
        if (failures.Count > 0)
            return ApplyAtomic(state, batch);
        foreach (var item in batch.Items)
            Apply(state, item);
        return new SpaceWmsBatchResult(
            batch.OperationKey,
            batch.PayloadHash,
            $"sim-op-{Guid.NewGuid():N}",
            batch.Items.Select(item => new SpaceWmsItemReceipt(
                item.LogicalId,
                item.LocationCode,
                item.Action,
                SpaceWmsItemOutcome.Unknown,
                null,
                null,
                null,
                ErrorCode(fault, "SPACE_WMS_RESULT_UNCERTAIN")))
                .ToArray(),
            DateTimeOffset.UtcNow);
    }

    private static Dictionary<Guid, string> Validate(
        WarehouseState state,
        IReadOnlyList<SpaceWmsLocationMutation> items)
    {
        var failures = new Dictionary<Guid, string>();
        foreach (var item in items)
        {
            var failure = ValidateOne(state, item);
            if (failure is not null)
                failures[item.LogicalId] = failure;
        }
        var duplicateCodes = items
            .Where(item => item.Action != SpaceWmsLocationAction.Disable)
            .GroupBy(item => item.LocationCode, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.LogicalId).Distinct().Count() > 1);
        foreach (var group in duplicateCodes)
            foreach (var item in group)
                failures[item.LogicalId] = "WMS_LOCATION_CODE_CONFLICT";
        return failures;
    }

    private static string? ValidateOne(
        WarehouseState state,
        SpaceWmsLocationMutation item)
    {
        if (item.Action == SpaceWmsLocationAction.Disable &&
            HasBlockingReference(state, item.LogicalId))
        {
            return "SPACE_LOCATION_IN_USE";
        }
        if (state.Locations.TryGetValue(item.LogicalId, out var existing))
        {
            if (item.Version < existing.Version)
                return "WMS_VERSION_CONFLICT";
            if (item.Version == existing.Version &&
                !string.Equals(
                    item.PayloadHash,
                    existing.PayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "WMS_VERSION_CONFLICT";
            }
        }
        var codeOwner = state.Locations.Values.FirstOrDefault(value =>
            value.LogicalId != item.LogicalId &&
            value.IsActive &&
            string.Equals(
                value.LocationCode,
                item.LocationCode,
                StringComparison.Ordinal));
        return codeOwner is null ? null : "WMS_LOCATION_CODE_CONFLICT";
    }

    private static SpaceWmsItemReceipt Apply(
        WarehouseState state,
        SpaceWmsLocationMutation item)
    {
        if (state.Locations.TryGetValue(item.LogicalId, out var existing) &&
            existing.Version == item.Version &&
            string.Equals(
                existing.PayloadHash,
                item.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return Receipt(item, existing, SpaceWmsItemOutcome.AlreadyApplied);
        }
        var active = item.Action != SpaceWmsLocationAction.Disable;
        var location = new SimLocation(
            item.LogicalId,
            item.LocationCode,
            item.ExternalLocationId ?? $"sim-location-{item.LogicalId:N}",
            active,
            item.Version,
            item.PayloadHash,
            StateHash(item, active));
        state.Locations[item.LogicalId] = location;
        return Receipt(item, location, SpaceWmsItemOutcome.Applied);
    }

    private static SpaceWmsItemReceipt Receipt(
        SpaceWmsLocationMutation item,
        SimLocation location,
        SpaceWmsItemOutcome outcome) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            outcome,
            location.ExternalLocationId,
            location.Version.ToString(CultureInfo.InvariantCulture),
            location.StateHash,
            null);

    private static SpaceWmsItemReceipt FailureReceipt(
        SpaceWmsLocationMutation item,
        string errorCode) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            SpaceWmsItemOutcome.NotApplied,
            null,
            null,
            null,
            errorCode);

    private static SpaceWmsBatchResult FailureResult(
        SpaceWmsBatch batch,
        string errorCode) =>
        new(
            batch.OperationKey,
            batch.PayloadHash,
            null,
            batch.Items
                .Select(item => FailureReceipt(item, errorCode))
                .ToArray(),
            DateTimeOffset.UtcNow);

    private static SpaceWmsBatchResult SuccessResult(
        SpaceWmsBatch batch,
        IReadOnlyList<SpaceWmsItemReceipt> receipts) =>
        new(
            batch.OperationKey,
            batch.PayloadHash,
            $"sim-op-{Guid.NewGuid():N}",
            receipts,
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<SpaceWmsLocationState> Locations(
        WarehouseState state,
        IReadOnlyList<Guid> logicalIds)
    {
        var filter = Filter(logicalIds);
        return state.Locations.Values
            .Where(value => filter(value.LogicalId))
            .OrderBy(value => value.LocationCode, StringComparer.Ordinal)
            .Select(value => new SpaceWmsLocationState(
                value.LogicalId,
                value.LocationCode,
                value.ExternalLocationId,
                value.IsActive,
                value.Version.ToString(CultureInfo.InvariantCulture),
                value.StateHash))
            .ToArray();
    }

    private static bool HasBlockingReference(
        WarehouseState state,
        Guid logicalId) =>
        state.Inventory.Any(item =>
            item.LogicalId == logicalId &&
            (item.PhysicalQuantity != 0 || item.AllocatedQuantity != 0)) ||
        state.Tasks.Any(item =>
            item.LogicalId == logicalId &&
            IsActiveTask(item.Status));

    private static bool IsActiveTask(string status) =>
        !string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);

    private static Func<Guid, bool> Filter(IReadOnlyList<Guid> logicalIds)
    {
        var ids = logicalIds?.ToHashSet() ?? [];
        return ids.Count == 0 ? _ => true : ids.Contains;
    }

    private WarehouseState State(SpaceWmsContext context)
    {
        EnsureContext(context);
        return _states.GetOrAdd(ScopeKey.From(context), _ => new WarehouseState());
    }

    private static SpaceWmsSimulatorFaultProfile Fault(WarehouseState state)
    {
        lock (state.Gate)
            return state.Fault;
    }

    private static async Task ThrowForTransportFaultAsync(
        SpaceWmsSimulatorFaultProfile fault,
        CancellationToken ct)
    {
        await ApplyDelayAsync(fault, ct);
        if (fault.Mode == SpaceWmsSimulatorFaultMode.Unavailable)
            throw new InvalidOperationException(ErrorCode(fault));
        if (fault.Mode == SpaceWmsSimulatorFaultMode.Timeout)
            throw new TimeoutException(ErrorCode(
                fault,
                "SPACE_WMS_RETRYABLE"));
    }

    private static Task ApplyDelayAsync(
        SpaceWmsSimulatorFaultProfile fault,
        CancellationToken ct) =>
        fault.Delay > TimeSpan.Zero
            ? Task.Delay(fault.Delay, ct)
            : Task.CompletedTask;

    private static string ErrorCode(
        SpaceWmsSimulatorFaultProfile fault,
        string defaultCode = "SPACE_WMS_UNAVAILABLE") =>
        string.IsNullOrWhiteSpace(fault.ErrorCode)
            ? defaultCode
            : fault.ErrorCode.Trim();

    private static void EnsureContext(SpaceWmsContext context) =>
        SpaceWmsContract.ValidateContext(context);

    private static SpaceWmsCapabilitySnapshot CapabilitySnapshot() =>
        SpaceWmsCapabilitySnapshot.Create(
            AdapterId,
            SpaceWmsDataSourceKind.Simulated,
            SpaceWmsCertificationLevel.CertifiedAtomic,
            Capabilities,
            DateTimeOffset.UtcNow);

    private static SpaceWmsSourceMetadata Source() =>
        new(
            SpaceWmsDataSourceKind.Simulated,
            DataSourceId,
            DateTimeOffset.UtcNow);

    private static SpaceWmsOperationState ToOperationState(
        SpaceWmsBatchAssessmentKind assessment) =>
        assessment switch
        {
            SpaceWmsBatchAssessmentKind.Succeeded =>
                SpaceWmsOperationState.Applied,
            SpaceWmsBatchAssessmentKind.FailedNoEffect =>
                SpaceWmsOperationState.FailedNoEffect,
            SpaceWmsBatchAssessmentKind.Partial =>
                SpaceWmsOperationState.Partial,
            _ => SpaceWmsOperationState.Unknown,
        };

    private static string StateHash(
        SpaceWmsLocationMutation item,
        bool active) =>
        Hash(string.Join(
            "\n",
            item.LogicalId.ToString("D"),
            item.LocationCode,
            item.Version.ToString(CultureInfo.InvariantCulture),
            active ? "1" : "0",
            item.PayloadHash));

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class WarehouseState
    {
        public object Gate { get; } = new();
        public Dictionary<Guid, SimLocation> Locations { get; } = [];
        public Dictionary<string, SimOperation> Operations { get; } =
            new(StringComparer.Ordinal);
        public List<SpaceWmsInventoryItem> Inventory { get; } = [];
        public List<SpaceWmsTaskItem> Tasks { get; } = [];
        public List<SpaceWmsOutboundMovement> OutboundMovements { get; } = [];
        public SpaceWmsSimulatorFaultProfile Fault { get; set; } =
            SpaceWmsSimulatorFaultProfile.None;
    }

    private sealed record SimLocation(
        Guid LogicalId,
        string LocationCode,
        string ExternalLocationId,
        bool IsActive,
        long Version,
        string PayloadHash,
        string StateHash);

    private sealed record SimOperation(
        string PayloadHash,
        SpaceWmsBatchResult Result,
        SpaceWmsOperationState State);

    private readonly record struct ScopeKey(
        Guid TenantId,
        Guid SiteId,
        string WarehouseCode)
    {
        public static ScopeKey From(SpaceWmsContext context) =>
            new(
                context.TenantId,
                context.SiteId,
                context.WarehouseCode.Trim());
    }
}

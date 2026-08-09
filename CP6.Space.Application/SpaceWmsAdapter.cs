using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace CP6.Space.Application;

public static class SpaceWmsContractVersion
{
    public const string V1 = "space-wms-adapter-v1";
}

public enum SpaceWmsDataSourceKind
{
    Real = 0,
    Simulated = 1,
    Unavailable = 2,
}

public enum SpaceWmsCertificationLevel
{
    CertifiedAtomic = 0,
    CertifiedIdempotent = 1,
    PreviewOnly = 2,
}

public enum SpaceWmsHealthState
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2,
}

public enum SpaceWmsLocationAction
{
    Create = 0,
    Update = 1,
    Disable = 2,
    Restore = 3,
}

public enum SpaceWmsItemOutcome
{
    Applied = 0,
    AlreadyApplied = 1,
    Rejected = 2,
    NotApplied = 3,
    Unknown = 4,
}

public enum SpaceWmsBatchAssessmentKind
{
    Succeeded = 0,
    FailedNoEffect = 1,
    Partial = 2,
    Uncertain = 3,
}

public enum SpaceWmsOperationState
{
    Pending = 0,
    Applied = 1,
    FailedNoEffect = 2,
    Partial = 3,
    Unknown = 4,
}

public enum SpaceWmsBlockingReferenceKind
{
    Inventory = 0,
    ActiveTask = 1,
    Container = 2,
    Other = 3,
}

public sealed record SpaceWmsContext(
    Guid TenantId,
    Guid SiteId,
    string WarehouseCode,
    Guid CorrelationId);

public sealed record SpaceWmsCapabilities(
    bool AtomicStaging,
    bool IdempotentUpsert,
    bool IdempotentDisable,
    bool RenameLocation,
    bool QueryByLogicalId,
    bool QueryBlockingReferences,
    bool QueryInventory,
    bool QueryTasks,
    bool ReliableOperationStatus,
    bool ReadBackHash,
    int BatchMaxSize,
    string AllowedCodePattern,
    int CodeMaxLength);

public sealed class SpaceWmsCapabilitySnapshot
{
    private SpaceWmsCapabilitySnapshot(
        string adapterId,
        SpaceWmsDataSourceKind dataSourceKind,
        SpaceWmsCertificationLevel certificationLevel,
        SpaceWmsCapabilities capabilities,
        string capabilityHash,
        DateTimeOffset observedAtUtc)
    {
        AdapterId = adapterId;
        DataSourceKind = dataSourceKind;
        CertificationLevel = certificationLevel;
        Capabilities = capabilities;
        CapabilityHash = capabilityHash;
        ObservedAtUtc = observedAtUtc;
    }

    public string AdapterId { get; }
    public string ContractVersion => SpaceWmsContractVersion.V1;
    public SpaceWmsDataSourceKind DataSourceKind { get; }
    public SpaceWmsCertificationLevel CertificationLevel { get; }
    public SpaceWmsCapabilities Capabilities { get; }
    public string CapabilityHash { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public bool SupportsProductionPublishing =>
        DataSourceKind != SpaceWmsDataSourceKind.Unavailable &&
        CertificationLevel != SpaceWmsCertificationLevel.PreviewOnly;

    public static SpaceWmsCapabilitySnapshot Create(
        string adapterId,
        SpaceWmsDataSourceKind dataSourceKind,
        SpaceWmsCertificationLevel certificationLevel,
        SpaceWmsCapabilities capabilities,
        DateTimeOffset observedAtUtc)
    {
        var normalizedAdapterId = SpaceWmsContract.RequireText(
            adapterId,
            nameof(adapterId),
            100);
        ArgumentNullException.ThrowIfNull(capabilities);
        SpaceWmsContract.ValidateCapabilities(
            dataSourceKind,
            certificationLevel,
            capabilities);
        return new SpaceWmsCapabilitySnapshot(
            normalizedAdapterId,
            dataSourceKind,
            certificationLevel,
            capabilities,
            SpaceWmsContract.ComputeCapabilityHash(
                normalizedAdapterId,
                dataSourceKind,
                certificationLevel,
                capabilities),
            observedAtUtc.ToUniversalTime());
    }
}

public sealed record SpaceWmsHealth(
    string AdapterId,
    SpaceWmsHealthState State,
    DateTimeOffset CheckedAtUtc,
    TimeSpan ResponseTime,
    string? ErrorCode = null)
{
    public bool IsPublishAvailable =>
        State == SpaceWmsHealthState.Healthy;
}

public sealed record SpaceWmsSourceMetadata(
    SpaceWmsDataSourceKind Kind,
    string DataSourceId,
    DateTimeOffset ObservedAtUtc)
{
    public bool IsAvailable =>
        Kind != SpaceWmsDataSourceKind.Unavailable;

    public bool IsSimulated =>
        Kind == SpaceWmsDataSourceKind.Simulated;
}

public sealed record SpaceWmsLocationPath(
    string? SiteCode,
    int FloorLevel,
    string? ZoneCode,
    string? AisleCode,
    string? RackCode,
    int Column,
    int Level,
    int Depth);

public sealed class SpaceWmsLocationMutation
{
    private SpaceWmsLocationMutation(
        int sequenceNo,
        Guid logicalId,
        string locationCode,
        SpaceWmsLocationAction action,
        long version,
        string? externalLocationId,
        SpaceWmsLocationPath path,
        IReadOnlyDictionary<string, string?> attributes,
        string payloadHash)
    {
        SequenceNo = sequenceNo;
        LogicalId = logicalId;
        LocationCode = locationCode;
        Action = action;
        Version = version;
        ExternalLocationId = externalLocationId;
        Path = path;
        Attributes = attributes;
        PayloadHash = payloadHash;
    }

    public int SequenceNo { get; }
    public Guid LogicalId { get; }
    public string LocationCode { get; }
    public SpaceWmsLocationAction Action { get; }
    public long Version { get; }
    public string? ExternalLocationId { get; }
    public SpaceWmsLocationPath Path { get; }
    public IReadOnlyDictionary<string, string?> Attributes { get; }
    public string PayloadHash { get; }

    public static SpaceWmsLocationMutation Create(
        int sequenceNo,
        Guid logicalId,
        string locationCode,
        SpaceWmsLocationAction action,
        SpaceWmsLocationPath path,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? externalLocationId = null,
        long version = 1)
    {
        if (sequenceNo < 1)
            throw new ArgumentOutOfRangeException(nameof(sequenceNo));
        if (logicalId == Guid.Empty)
            throw new ArgumentException(
                "A location logical identity is required.",
                nameof(logicalId));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        var normalizedCode = SpaceWmsContract.RequireText(
            locationCode,
            nameof(locationCode),
            256);
        ArgumentNullException.ThrowIfNull(path);
        var normalizedExternalId =
            SpaceWmsContract.NormalizeOptional(externalLocationId, 200);
        var normalizedAttributes =
            new SortedDictionary<string, string?>(StringComparer.Ordinal);
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                var key = SpaceWmsContract.RequireText(
                    pair.Key,
                    nameof(attributes),
                    100);
                if (!normalizedAttributes.TryAdd(
                        key,
                        SpaceWmsContract.NormalizeOptional(pair.Value, 500)))
                {
                    throw new ArgumentException(
                        $"Duplicate attribute '{key}'.",
                        nameof(attributes));
                }
            }
        }

        var payloadHash = SpaceWmsContract.ComputeMutationHash(
            sequenceNo,
            logicalId,
            normalizedCode,
            action,
            version,
            normalizedExternalId,
            path,
            normalizedAttributes);
        return new SpaceWmsLocationMutation(
            sequenceNo,
            logicalId,
            normalizedCode,
            action,
            version,
            normalizedExternalId,
            path,
            normalizedAttributes,
            payloadHash);
    }
}

public sealed class SpaceWmsBatch
{
    private SpaceWmsBatch(
        SpaceWmsContext context,
        Guid publishAttemptId,
        int batchNo,
        string planHash,
        string operationKey,
        string payloadHash,
        IReadOnlyList<SpaceWmsLocationMutation> items)
    {
        Context = context;
        PublishAttemptId = publishAttemptId;
        BatchNo = batchNo;
        PlanHash = planHash;
        OperationKey = operationKey;
        PayloadHash = payloadHash;
        Items = items;
    }

    public SpaceWmsContext Context { get; }
    public Guid PublishAttemptId { get; }
    public int BatchNo { get; }
    public string PlanHash { get; }
    public string OperationKey { get; }
    public string PayloadHash { get; }
    public IReadOnlyList<SpaceWmsLocationMutation> Items { get; }

    public static SpaceWmsBatch Create(
        SpaceWmsContext context,
        Guid publishAttemptId,
        int batchNo,
        string planHash,
        IReadOnlyCollection<SpaceWmsLocationMutation> items)
    {
        SpaceWmsContract.ValidateContext(context);
        if (publishAttemptId == Guid.Empty)
            throw new ArgumentException(
                "A publish attempt identity is required.",
                nameof(publishAttemptId));
        if (batchNo < 1)
            throw new ArgumentOutOfRangeException(nameof(batchNo));
        SpaceWmsContract.RequireSha256(planHash, nameof(planHash));
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException(
                "A WMS batch requires at least one item.",
                nameof(items));

        var ordered = items
            .OrderBy(item => item.SequenceNo)
            .ToArray();
        if (ordered.Select(item => item.SequenceNo).Distinct().Count() !=
            ordered.Length)
        {
            throw new ArgumentException(
                "WMS batch sequence numbers must be unique.",
                nameof(items));
        }
        if (ordered.Select(item => item.LogicalId).Distinct().Count() !=
            ordered.Length)
        {
            throw new ArgumentException(
                "WMS batch logical identities must be unique.",
                nameof(items));
        }

        var operationKey = SpaceWmsContract.CreateOperationKey(
            context.TenantId,
            context.SiteId,
            publishAttemptId,
            batchNo);
        var payloadHash = SpaceWmsContract.ComputeBatchHash(
            planHash,
            ordered);
        return new SpaceWmsBatch(
            context,
            publishAttemptId,
            batchNo,
            planHash.ToLowerInvariant(),
            operationKey,
            payloadHash,
            ordered);
    }
}

public sealed record SpaceWmsItemReceipt(
    Guid LogicalId,
    string LocationCode,
    SpaceWmsLocationAction Action,
    SpaceWmsItemOutcome Outcome,
    string? ExternalLocationId,
    string? ExternalVersion,
    string? ResponseHash,
    string? ErrorCode);

public sealed record SpaceWmsBatchResult(
    string OperationKey,
    string PayloadHash,
    string? ExternalOperationId,
    IReadOnlyList<SpaceWmsItemReceipt> Items,
    DateTimeOffset ObservedAtUtc);

public sealed record SpaceWmsBatchAssessment(
    SpaceWmsBatchAssessmentKind Kind,
    IReadOnlyList<string> ContractViolations)
{
    public bool RequiresReconciliation =>
        Kind is SpaceWmsBatchAssessmentKind.Partial or
            SpaceWmsBatchAssessmentKind.Uncertain;
}

public sealed record SpaceWmsOperationQuery(
    SpaceWmsContext Context,
    string OperationKey,
    string PayloadHash);

public sealed record SpaceWmsOperationStatus(
    string OperationKey,
    string PayloadHash,
    SpaceWmsOperationState State,
    bool IsTerminal,
    DateTimeOffset ObservedAtUtc,
    string? ExternalOperationId = null);

public sealed record SpaceWmsPreflightRequest(
    SpaceWmsContext Context,
    Guid PublishAttemptId,
    string PlanHash,
    string CapabilityHash,
    IReadOnlyList<SpaceWmsLocationMutation> Items);

public sealed record SpaceWmsPreflightIssue(
    Guid? LogicalId,
    string Code,
    bool Blocking,
    string? ReferenceId = null);

public sealed record SpaceWmsPreflightResult(
    string CapabilityHash,
    IReadOnlyList<SpaceWmsPreflightIssue> Issues,
    DateTimeOffset ObservedAtUtc)
{
    public bool CanApply => Issues.All(issue => !issue.Blocking);
}

/// <summary>
/// Queries specific WMS locations by logical identity. An empty identity list
/// requests the complete warehouse location catalog.
/// </summary>
public sealed record SpaceWmsLocationQuery(
    SpaceWmsContext Context,
    IReadOnlyList<Guid> LogicalIds);

public sealed record SpaceWmsLocationState(
    Guid LogicalId,
    string LocationCode,
    string? ExternalLocationId,
    bool IsActive,
    string ExternalVersion,
    string StateHash);

public sealed record SpaceWmsLocationResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsLocationState> Items);

public sealed record SpaceWmsReadBackRequest(
    SpaceWmsContext Context,
    string OperationKey,
    string PayloadHash,
    string PlanHash,
    IReadOnlyList<Guid> LogicalIds);

public sealed record SpaceWmsReadBackResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsLocationState> Items,
    string AggregateHash);

public sealed record SpaceWmsBlockingReferencesRequest(
    SpaceWmsContext Context,
    IReadOnlyList<Guid> LogicalIds);

public sealed record SpaceWmsBlockingReference(
    Guid LogicalId,
    SpaceWmsBlockingReferenceKind Kind,
    string ReferenceId,
    decimal? Quantity);

public sealed record SpaceWmsBlockingReferences(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsBlockingReference> Items);

public sealed record SpaceWmsInventoryQuery(
    SpaceWmsContext Context,
    IReadOnlyList<Guid> LogicalIds,
    IReadOnlyList<string>? OwnerIds = null,
    SpaceWmsInventoryLocateCriteria? LocateCriteria = null);

public sealed record SpaceWmsInventoryLocateCriteria(
    string? MaterialNumber,
    string? LotNumber,
    string? ContainerNumber,
    string? OwnerId = null);

public sealed record SpaceWmsInventoryItem(
    Guid LogicalId,
    string LocationCode,
    decimal PhysicalQuantity,
    decimal AllocatedQuantity,
    string? MaterialNumber,
    string? LotNumber,
    string? ContainerNumber,
    string? OwnerId = null);

public sealed record SpaceWmsInventoryResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsInventoryItem> Items);

public sealed record SpaceWmsTaskQuery(
    SpaceWmsContext Context,
    IReadOnlyList<Guid> LogicalIds,
    IReadOnlyList<string>? TaskIds = null);

public sealed record SpaceWmsTaskItem(
    string TaskId,
    string TaskType,
    string Status,
    int SequenceNo,
    Guid LogicalId,
    string LocationCode,
    decimal? Quantity,
    string? MaterialNumber);

public sealed record SpaceWmsTaskResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsTaskItem> Items);

public sealed record SpaceWmsDispatchTaskQuery(
    SpaceWmsContext Context);

public sealed record SpaceWmsDispatchTaskItem(
    string TaskId,
    string TaskType,
    string Status,
    string? AssignedTo,
    int Priority,
    int ContractVersion,
    int ExecutionVersion,
    string RowVersion,
    Guid? LogicalId,
    string? LocationCode,
    string LocationRole,
    decimal Quantity,
    string? MaterialNumber);

public sealed record SpaceWmsDispatchTaskResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsDispatchTaskItem> Items);

public sealed record SpaceWmsAbcQuery(
    SpaceWmsContext Context,
    DateOnly FromDateInclusive,
    DateOnly ToDateExclusive);

public sealed record SpaceWmsAbcAggregate(
    string MaterialNumber,
    int OutboundMovementCount,
    decimal OutboundQuantity);

public sealed record SpaceWmsAbcResult(
    SpaceWmsSourceMetadata Source,
    IReadOnlyList<SpaceWmsAbcAggregate> Items);

/// <summary>
/// Read-only WMS runtime boundary shared by production adapters and
/// simulators. Runtime consumers depend on this surface rather than the
/// publishing mutation contract.
/// </summary>
public interface ISpaceWmsRuntimeSource
{
    string RuntimeAdapterId { get; }
    string RuntimeDataSourceId { get; }
    SpaceWmsDataSourceKind RuntimeDataSourceKind { get; }

    Task<SpaceWmsInventoryResult> QueryInventoryAsync(
        SpaceWmsInventoryQuery request,
        CancellationToken ct = default);

    Task<SpaceWmsTaskResult> QueryTasksAsync(
        SpaceWmsTaskQuery request,
        CancellationToken ct = default);

    Task<SpaceWmsDispatchTaskResult> QueryDispatchTasksAsync(
        SpaceWmsDispatchTaskQuery request,
        CancellationToken ct = default) =>
        Task.FromException<SpaceWmsDispatchTaskResult>(
            new NotSupportedException(
                "The WMS runtime source does not expose dispatch-task facts."));

    Task<SpaceWmsAbcResult> QueryAbcAsync(
        SpaceWmsAbcQuery request,
        CancellationToken ct = default);
}

public interface ISpaceWmsAdapter : ISpaceWmsRuntimeSource
{
    Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(
        SpaceWmsContext context,
        CancellationToken ct = default);

    Task<SpaceWmsHealth> CheckHealthAsync(
        SpaceWmsContext context,
        CancellationToken ct = default);

    Task<SpaceWmsPreflightResult> PreflightAsync(
        SpaceWmsPreflightRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// The same operation key and payload hash must replay the original
    /// result. Reusing a key with a different hash must be rejected as an
    /// idempotency conflict without applying any item.
    /// </summary>
    Task<SpaceWmsBatchResult> ApplyBatchAsync(
        SpaceWmsBatch batch,
        CancellationToken ct = default);

    Task<SpaceWmsOperationStatus> GetOperationStatusAsync(
        SpaceWmsOperationQuery request,
        CancellationToken ct = default);

    Task<SpaceWmsReadBackResult> ReadBackAsync(
        SpaceWmsReadBackRequest request,
        CancellationToken ct = default);

    Task<SpaceWmsBlockingReferences> GetBlockingReferencesAsync(
        SpaceWmsBlockingReferencesRequest request,
        CancellationToken ct = default);

    Task<SpaceWmsLocationResult> QueryLocationsAsync(
        SpaceWmsLocationQuery request,
        CancellationToken ct = default);
}

public static class SpaceWmsContract
{
    private static readonly Regex Sha256Pattern =
        new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public static string CreateOperationKey(
        Guid tenantId,
        Guid siteId,
        Guid publishAttemptId,
        int batchNo)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "A tenant identity is required.",
                nameof(tenantId));
        if (siteId == Guid.Empty)
            throw new ArgumentException(
                "A site identity is required.",
                nameof(siteId));
        if (publishAttemptId == Guid.Empty)
            throw new ArgumentException(
                "A publish attempt identity is required.",
                nameof(publishAttemptId));
        if (batchNo < 1)
            throw new ArgumentOutOfRangeException(nameof(batchNo));
        return $"space:{tenantId:D}:{siteId:D}:{publishAttemptId:D}:{batchNo}";
    }

    public static void ValidateOperationKeyScope(
        SpaceWmsContext context,
        string operationKey)
    {
        ValidateContext(context);
        var normalizedKey = RequireText(
            operationKey,
            nameof(operationKey),
            250);
        var expectedPrefix =
            $"space:{context.TenantId:D}:{context.SiteId:D}:";
        if (!normalizedKey.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SPACE_WMS_OPERATION_SCOPE_DENIED");
        }
    }

    public static bool CanPublish(
        SpaceWmsCapabilitySnapshot capabilities,
        SpaceWmsHealth health)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(health);
        return capabilities.SupportsProductionPublishing &&
               health.IsPublishAvailable &&
               string.Equals(
                   capabilities.AdapterId,
                   health.AdapterId,
                   StringComparison.Ordinal);
    }

    public static SpaceWmsBatchAssessment AssessBatchResult(
        SpaceWmsBatch request,
        SpaceWmsBatchResult response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        var violations = new List<string>();
        if (!string.Equals(
                request.OperationKey,
                response.OperationKey,
                StringComparison.Ordinal))
        {
            violations.Add("WMS_OPERATION_KEY_MISMATCH");
        }
        if (!string.Equals(
                request.PayloadHash,
                response.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("WMS_PAYLOAD_HASH_MISMATCH");
        }

        var receipts = response.Items ?? [];
        var duplicateIds = receipts
            .GroupBy(item => item.LogicalId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
            violations.Add("WMS_DUPLICATE_ITEM_RECEIPT");

        var requestById = request.Items.ToDictionary(item => item.LogicalId);
        var responseById = receipts
            .GroupBy(item => item.LogicalId)
            .ToDictionary(group => group.Key, group => group.First());
        if (requestById.Keys.Except(responseById.Keys).Any())
            violations.Add("WMS_MISSING_ITEM_RECEIPT");
        if (responseById.Keys.Except(requestById.Keys).Any())
            violations.Add("WMS_UNEXPECTED_ITEM_RECEIPT");

        foreach (var pair in requestById)
        {
            if (!responseById.TryGetValue(pair.Key, out var receipt))
                continue;
            var item = pair.Value;
            if (!string.Equals(
                    item.LocationCode,
                    receipt.LocationCode,
                    StringComparison.Ordinal) ||
                item.Action != receipt.Action)
            {
                violations.Add("WMS_ITEM_IDENTITY_MISMATCH");
            }
            if (receipt.Outcome == SpaceWmsItemOutcome.Unknown)
                violations.Add("WMS_ITEM_OUTCOME_UNKNOWN");
            if (receipt.Outcome is
                    SpaceWmsItemOutcome.Applied or
                    SpaceWmsItemOutcome.AlreadyApplied &&
                (!IsSha256(receipt.ResponseHash) ||
                 string.IsNullOrWhiteSpace(receipt.ExternalLocationId) ||
                 string.IsNullOrWhiteSpace(receipt.ExternalVersion)))
            {
                violations.Add("WMS_SUCCESS_EVIDENCE_MISSING");
            }
            if (receipt.Outcome is
                    SpaceWmsItemOutcome.Rejected or
                    SpaceWmsItemOutcome.NotApplied &&
                string.IsNullOrWhiteSpace(receipt.ErrorCode))
            {
                violations.Add("WMS_FAILURE_EVIDENCE_MISSING");
            }
        }

        if (violations.Count > 0)
        {
            return new SpaceWmsBatchAssessment(
                SpaceWmsBatchAssessmentKind.Uncertain,
                violations.Distinct(StringComparer.Ordinal).ToArray());
        }

        var succeeded = receipts.Count(item =>
            item.Outcome is
                SpaceWmsItemOutcome.Applied or
                SpaceWmsItemOutcome.AlreadyApplied);
        var failed = receipts.Count(item =>
            item.Outcome is
                SpaceWmsItemOutcome.Rejected or
                SpaceWmsItemOutcome.NotApplied);
        var kind = (succeeded, failed) switch
        {
            ( > 0, 0) when succeeded == request.Items.Count =>
                SpaceWmsBatchAssessmentKind.Succeeded,
            (0, > 0) when failed == request.Items.Count =>
                SpaceWmsBatchAssessmentKind.FailedNoEffect,
            ( > 0, > 0) =>
                SpaceWmsBatchAssessmentKind.Partial,
            _ => SpaceWmsBatchAssessmentKind.Uncertain,
        };
        return new SpaceWmsBatchAssessment(kind, []);
    }

    public static IReadOnlyList<SpaceWmsPreflightIssue> CheckCompatibility(
        SpaceWmsBatch batch,
        SpaceWmsCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(snapshot);
        var issues = new List<SpaceWmsPreflightIssue>();
        var capabilities = snapshot.Capabilities;
        if (batch.Items.Count > capabilities.BatchMaxSize)
        {
            issues.Add(new SpaceWmsPreflightIssue(
                null,
                "SPACE_WMS_BATCH_LIMIT_EXCEEDED",
                true));
        }

        var codePattern = new Regex(
            capabilities.AllowedCodePattern,
            RegexOptions.CultureInvariant);
        foreach (var item in batch.Items)
        {
            if (item.LocationCode.Length > capabilities.CodeMaxLength ||
                !codePattern.IsMatch(item.LocationCode))
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_LOCATION_CODE_UNSUPPORTED",
                    true));
            }

            if (snapshot.CertificationLevel !=
                SpaceWmsCertificationLevel.CertifiedIdempotent)
            {
                continue;
            }
            var supported = item.Action == SpaceWmsLocationAction.Disable
                ? capabilities.IdempotentDisable
                : capabilities.IdempotentUpsert;
            if (!supported)
            {
                issues.Add(new SpaceWmsPreflightIssue(
                    item.LogicalId,
                    "SPACE_WMS_CAPABILITY_MISSING",
                    true));
            }
        }

        return issues;
    }

    public static void ValidateContext(SpaceWmsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TenantId == Guid.Empty)
            throw new ArgumentException(
                "A tenant identity is required.",
                nameof(context));
        if (context.SiteId == Guid.Empty)
            throw new ArgumentException(
                "A site identity is required.",
                nameof(context));
        if (context.CorrelationId == Guid.Empty)
            throw new ArgumentException(
                "A correlation identity is required.",
                nameof(context));
        RequireText(context.WarehouseCode, nameof(context), 100);
    }

    internal static void ValidateCapabilities(
        SpaceWmsDataSourceKind dataSourceKind,
        SpaceWmsCertificationLevel certificationLevel,
        SpaceWmsCapabilities capabilities)
    {
        if (capabilities.BatchMaxSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(
                nameof(capabilities),
                "BatchMaxSize must be between 1 and 10,000.");
        if (capabilities.CodeMaxLength is < 1 or > 256)
            throw new ArgumentOutOfRangeException(
                nameof(capabilities),
                "CodeMaxLength must be between 1 and 256.");
        var pattern = RequireText(
            capabilities.AllowedCodePattern,
            nameof(capabilities),
            500);
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException(
                "AllowedCodePattern must be a valid regular expression.",
                nameof(capabilities),
                error);
        }

        if (dataSourceKind == SpaceWmsDataSourceKind.Unavailable &&
            certificationLevel != SpaceWmsCertificationLevel.PreviewOnly)
        {
            throw new ArgumentException(
                "An unavailable adapter cannot be certified for publishing.",
                nameof(certificationLevel));
        }

        if (certificationLevel ==
                SpaceWmsCertificationLevel.CertifiedAtomic &&
            (!capabilities.AtomicStaging ||
             !capabilities.ReliableOperationStatus ||
             !capabilities.QueryByLogicalId ||
             !capabilities.ReadBackHash))
        {
            throw new ArgumentException(
                "CertifiedAtomic requires staging, reliable status, " +
                "logical-id lookup, and read-back hashes.",
                nameof(capabilities));
        }
        if (certificationLevel ==
                SpaceWmsCertificationLevel.CertifiedIdempotent &&
            (!capabilities.IdempotentUpsert ||
             !capabilities.IdempotentDisable ||
             !capabilities.ReliableOperationStatus ||
             !capabilities.QueryByLogicalId ||
             !capabilities.ReadBackHash))
        {
            throw new ArgumentException(
                "CertifiedIdempotent requires idempotent upsert/disable, " +
                "reliable status, logical-id lookup, and read-back hashes.",
                nameof(capabilities));
        }
    }

    internal static string ComputeCapabilityHash(
        string adapterId,
        SpaceWmsDataSourceKind dataSourceKind,
        SpaceWmsCertificationLevel certificationLevel,
        SpaceWmsCapabilities capabilities)
    {
        var material = string.Join(
            "\n",
            SpaceWmsContractVersion.V1,
            adapterId,
            ((int)dataSourceKind).ToString(CultureInfo.InvariantCulture),
            ((int)certificationLevel).ToString(
                CultureInfo.InvariantCulture),
            Bool(capabilities.AtomicStaging),
            Bool(capabilities.IdempotentUpsert),
            Bool(capabilities.IdempotentDisable),
            Bool(capabilities.RenameLocation),
            Bool(capabilities.QueryByLogicalId),
            Bool(capabilities.QueryBlockingReferences),
            Bool(capabilities.QueryInventory),
            Bool(capabilities.QueryTasks),
            Bool(capabilities.ReliableOperationStatus),
            Bool(capabilities.ReadBackHash),
            capabilities.BatchMaxSize.ToString(
                CultureInfo.InvariantCulture),
            capabilities.AllowedCodePattern.Trim(),
            capabilities.CodeMaxLength.ToString(
                CultureInfo.InvariantCulture));
        return Hash(material);
    }

    internal static string ComputeMutationHash(
        int sequenceNo,
        Guid logicalId,
        string locationCode,
        SpaceWmsLocationAction action,
        long version,
        string? externalLocationId,
        SpaceWmsLocationPath path,
        IReadOnlyDictionary<string, string?> attributes)
    {
        var builder = new StringBuilder();
        builder
            .Append(sequenceNo.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append(logicalId.ToString("D")).Append('\n')
            .Append(locationCode).Append('\n')
            .Append(((int)action).ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('\n')
            .Append(externalLocationId ?? "-").Append('\n')
            .Append(path.SiteCode ?? "-").Append('|')
            .Append(path.FloorLevel.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(path.ZoneCode ?? "-").Append('|')
            .Append(path.AisleCode ?? "-").Append('|')
            .Append(path.RackCode ?? "-").Append('|')
            .Append(path.Column.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(path.Level.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(path.Depth.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var pair in attributes.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            builder
                .Append(pair.Key)
                .Append('=')
                .Append(pair.Value ?? "-")
                .Append('\n');
        }
        return Hash(builder.ToString());
    }

    internal static string ComputeBatchHash(
        string planHash,
        IReadOnlyList<SpaceWmsLocationMutation> items)
    {
        var builder = new StringBuilder();
        builder.Append(planHash.ToLowerInvariant()).Append('\n');
        foreach (var item in items)
        {
            builder
                .Append(item.SequenceNo.ToString(
                    CultureInfo.InvariantCulture))
                .Append('|')
                .Append(item.LogicalId.ToString("D"))
                .Append('|')
                .Append(item.PayloadHash)
                .Append('\n');
        }
        return Hash(builder.ToString());
    }

    public static string RequireSha256(string value, string parameterName)
    {
        if (!IsSha256(value))
            throw new ArgumentException(
                "A SHA-256 hexadecimal hash is required.",
                parameterName);
        return value.ToLowerInvariant();
    }

    internal static string RequireText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value up to {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    internal static string? NormalizeOptional(
        string? value,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.");
        return normalized;
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is not null && Sha256Pattern.IsMatch(value);

    private static string Bool(bool value) => value ? "1" : "0";
}

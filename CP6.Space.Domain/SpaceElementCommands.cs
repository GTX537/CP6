using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceElementCommandBatch : SpaceTenantEntity
{
    private SpaceElementCommandBatch()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public Guid ClientInstanceId { get; private set; }
    public Guid? LeaseId { get; private set; }
    public long ExpectedFloorRevision { get; private set; }
    public long? ResultFloorRevision { get; private set; }
    public long? ResultVersionContentRevision { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public string? ResponseJson { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }
    public Guid AppliedBy { get; private set; }

    public static SpaceElementCommandBatch Create(
        Guid tenantId,
        Guid commandBatchId,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid clientInstanceId,
        long expectedFloorRevision,
        string requestHash,
        Guid actorId,
        DateTime appliedAtUtc) =>
        Create(
            tenantId,
            commandBatchId,
            modelVersionId,
            floorLogicalId,
            clientInstanceId,
            null,
            expectedFloorRevision,
            requestHash,
            actorId,
            appliedAtUtc);

    public static SpaceElementCommandBatch Create(
        Guid tenantId,
        Guid commandBatchId,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid clientInstanceId,
        Guid? leaseId,
        long expectedFloorRevision,
        string requestHash,
        Guid actorId,
        DateTime appliedAtUtc)
    {
        RequireIdentity(commandBatchId, nameof(commandBatchId));
        RequireIdentity(modelVersionId, nameof(modelVersionId));
        RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        RequireIdentity(clientInstanceId, nameof(clientInstanceId));
        if (leaseId == Guid.Empty)
            throw new ArgumentException("Lease identity cannot be empty.", nameof(leaseId));
        RequireIdentity(actorId, nameof(actorId));
        if (expectedFloorRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedFloorRevision));
        if (appliedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Applied time must be UTC.", nameof(appliedAtUtc));

        var batch = new SpaceElementCommandBatch
        {
            ModelVersionId = modelVersionId,
            FloorLogicalId = floorLogicalId,
            ClientInstanceId = clientInstanceId,
            LeaseId = leaseId,
            ExpectedFloorRevision = expectedFloorRevision,
            RequestHash = RequireHash(requestHash),
            AppliedAtUtc = appliedAtUtc,
            AppliedBy = actorId,
        };
        batch.SetTenant(tenantId);
        batch.SetId(commandBatchId);
        return batch;
    }

    public void Complete(
        long resultFloorRevision,
        long resultVersionContentRevision,
        string responseJson)
    {
        if (ResultFloorRevision.HasValue || ResponseJson is not null)
            throw new InvalidOperationException("The command batch is already complete.");
        if (resultFloorRevision <= ExpectedFloorRevision)
            throw new ArgumentOutOfRangeException(nameof(resultFloorRevision));
        if (resultVersionContentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(resultVersionContentRevision));
        RequireJson(responseJson, nameof(responseJson));

        ResultFloorRevision = resultFloorRevision;
        ResultVersionContentRevision = resultVersionContentRevision;
        ResponseJson = responseJson;
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static string RequireHash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Request hash must be a SHA-256 hexadecimal value.",
                nameof(value));
        }
        return normalized;
    }

    internal static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("JSON is required.", parameterName);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON is invalid.", parameterName, exception);
        }
        return value;
    }
}

public sealed class SpaceElementCommandRecord : SpaceTenantEntity
{
    private SpaceElementCommandRecord()
    {
    }

    public Guid CommandBatchId { get; private set; }
    public Guid ModelVersionId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public int SequenceNo { get; private set; }
    public string CommandType { get; private set; } = string.Empty;
    public Guid TargetLogicalId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public string BeforeJson { get; private set; } = string.Empty;
    public string AfterJson { get; private set; } = string.Empty;

    public static SpaceElementCommandRecord Create(
        Guid tenantId,
        Guid commandId,
        SpaceElementCommandBatch batch,
        int sequenceNo,
        string commandType,
        Guid targetLogicalId,
        string payloadJson,
        string beforeJson,
        string afterJson)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.TenantId != tenantId)
            throw new SpaceTenantScopeException("Command and batch tenants must match.");
        if (commandId == Guid.Empty)
            throw new ArgumentException("Command identity is required.", nameof(commandId));
        if (targetLogicalId == Guid.Empty)
            throw new ArgumentException("Target identity is required.", nameof(targetLogicalId));
        if (sequenceNo < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceNo));
        var normalizedType = commandType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType) || normalizedType.Length > 100)
            throw new ArgumentException("Command type is required.", nameof(commandType));

        var record = new SpaceElementCommandRecord
        {
            CommandBatchId = batch.Id,
            ModelVersionId = batch.ModelVersionId,
            FloorLogicalId = batch.FloorLogicalId,
            SequenceNo = sequenceNo,
            CommandType = normalizedType,
            TargetLogicalId = targetLogicalId,
            PayloadJson = SpaceElementCommandBatch.RequireJson(
                payloadJson,
                nameof(payloadJson)),
            BeforeJson = SpaceElementCommandBatch.RequireJson(
                beforeJson,
                nameof(beforeJson)),
            AfterJson = SpaceElementCommandBatch.RequireJson(
                afterJson,
                nameof(afterJson)),
        };
        record.SetTenant(tenantId);
        record.SetId(commandId);
        return record;
    }
}

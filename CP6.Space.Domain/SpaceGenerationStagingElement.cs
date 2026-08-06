using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceGenerationStagingElement : SpaceTenantEntity
{
    private SpaceGenerationStagingElement()
    {
    }

    public Guid RunId { get; private set; }
    public Guid ProposalId { get; private set; }
    public Guid ModelVersionId { get; private set; }
    public int SequenceNo { get; private set; }
    public Guid LogicalId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public string ElementType { get; private set; } = string.Empty;
    public string NormalizedPayloadJson { get; private set; } = "{}";
    public SpaceGenerationStagingValidationStatus ValidationStatus
    {
        get;
        private set;
    }
    public string? ValidationHash { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceGenerationStagingElement Create(
        Guid tenantId,
        Guid runId,
        Guid proposalId,
        Guid modelVersionId,
        int sequenceNo,
        Guid logicalId,
        Guid floorLogicalId,
        string elementType,
        string normalizedPayloadJson)
    {
        RequireId(runId, nameof(runId));
        RequireId(proposalId, nameof(proposalId));
        RequireId(modelVersionId, nameof(modelVersionId));
        RequireId(logicalId, nameof(logicalId));
        RequireId(floorLogicalId, nameof(floorLogicalId));
        if (sequenceNo < 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceNo));

        var staging = new SpaceGenerationStagingElement
        {
            RunId = runId,
            ProposalId = proposalId,
            ModelVersionId = modelVersionId,
            SequenceNo = sequenceNo,
            LogicalId = logicalId,
            FloorLogicalId = floorLogicalId,
            ElementType = SpaceGenerationRun.RequireText(
                elementType,
                64,
                nameof(elementType)),
            NormalizedPayloadJson = RequireJson(
                normalizedPayloadJson,
                nameof(normalizedPayloadJson)),
            ValidationStatus =
                SpaceGenerationStagingValidationStatus.Prepared,
        };
        staging.SetTenant(tenantId);
        return staging;
    }

    public void MarkValidated(string validationHash)
    {
        if (ValidationStatus !=
            SpaceGenerationStagingValidationStatus.Prepared)
        {
            throw new SpaceGenerationStateException(
                "The staging element is already validated.");
        }
        ValidationHash = SpaceGenerationRun.RequireHash(
            validationHash,
            nameof(validationHash));
        ValidationStatus =
            SpaceGenerationStagingValidationStatus.Validated;
    }

    private static string RequireJson(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16 * 1024 * 1024)
        {
            throw new ArgumentException(
                "Normalized staging JSON is required and bounded.",
                parameterName);
        }
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Normalized staging JSON is invalid.",
                parameterName,
                exception);
        }
        return value;
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }
}

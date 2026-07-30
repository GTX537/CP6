using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceProposalDecision : SpaceTenantEntity
{
    private SpaceProposalDecision()
    {
    }

    public Guid RunId { get; private set; }
    public Guid ProposalId { get; private set; }
    public SpaceProposalDecisionType DecisionType { get; private set; }
    public string BeforeJson { get; private set; } = "{}";
    public string? AfterJson { get; private set; }
    public string? LockedFieldsJson { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Comment { get; private set; }
    public Guid DecisionBatchId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceProposalDecision Create(
        Guid tenantId,
        Guid runId,
        Guid proposalId,
        SpaceProposalDecisionType decisionType,
        string beforeJson,
        string? afterJson,
        string? lockedFieldsJson,
        string? reasonCode,
        string? comment,
        Guid decisionBatchId)
    {
        RequireId(runId, nameof(runId));
        RequireId(proposalId, nameof(proposalId));
        RequireId(decisionBatchId, nameof(decisionBatchId));
        if (!Enum.IsDefined(decisionType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decisionType));
        }
        var normalizedBefore = RequireJson(
            beforeJson,
            nameof(beforeJson));
        var normalizedAfter = OptionalJson(afterJson, nameof(afterJson));
        if ((decisionType is
                SpaceProposalDecisionType.Accept or
                SpaceProposalDecisionType.Modify) &&
            normalizedAfter is null)
        {
            throw new ArgumentException(
                "Accept and Modify decisions require the final value.",
                nameof(afterJson));
        }
        if (decisionType == SpaceProposalDecisionType.Reject &&
            normalizedAfter is not null)
        {
            throw new ArgumentException(
                "Reject decisions cannot carry a final value.",
                nameof(afterJson));
        }

        var decision = new SpaceProposalDecision
        {
            RunId = runId,
            ProposalId = proposalId,
            DecisionType = decisionType,
            BeforeJson = normalizedBefore,
            AfterJson = normalizedAfter,
            LockedFieldsJson = OptionalJson(
                lockedFieldsJson,
                nameof(lockedFieldsJson)),
            ReasonCode = OptionalText(
                reasonCode,
                64,
                nameof(reasonCode)),
            Comment = OptionalText(comment, 512, nameof(comment)),
            DecisionBatchId = decisionBatchId,
        };
        decision.SetTenant(tenantId);
        return decision;
    }

    private static string RequireJson(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Canonical JSON is required.",
                parameterName);
        }
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Valid JSON is required.",
                parameterName,
                exception);
        }
        return value;
    }

    private static string? OptionalJson(
        string? value,
        string parameterName) =>
        value is null
            ? null
            : RequireJson(value, parameterName);

    private static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"A value up to {maxLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }
}

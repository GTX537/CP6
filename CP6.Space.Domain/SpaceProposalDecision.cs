using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Space.Domain;

public sealed class SpaceProposalDecision : SpaceTenantEntity
{
    private static readonly Regex ReasonCodePattern = new(
        "^[A-Z][A-Z0-9_]{0,63}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveCommentPattern = new(
        "(?i)(authorization\\s*:\\s*bearer|api[_-]?key\\s*[:=]|password\\s*[:=]|secret\\s*[:=])",
        RegexOptions.CultureInvariant);

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
        var normalizedLockedFields = OptionalJson(
            lockedFieldsJson,
            nameof(lockedFieldsJson));
        if (decisionType == SpaceProposalDecisionType.Modify &&
            normalizedLockedFields is null)
        {
            throw new ArgumentException(
                "Modify decisions require locked fields.",
                nameof(lockedFieldsJson));
        }
        if (decisionType != SpaceProposalDecisionType.Modify &&
            normalizedLockedFields is not null)
        {
            throw new ArgumentException(
                "Only Modify decisions can carry locked fields.",
                nameof(lockedFieldsJson));
        }

        var decision = new SpaceProposalDecision
        {
            RunId = runId,
            ProposalId = proposalId,
            DecisionType = decisionType,
            BeforeJson = normalizedBefore,
            AfterJson = normalizedAfter,
            LockedFieldsJson = normalizedLockedFields,
            ReasonCode = OptionalReasonCode(
                reasonCode,
                nameof(reasonCode)),
            Comment = OptionalSafeComment(comment, nameof(comment)),
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

    private static string? OptionalReasonCode(
        string? value,
        string parameterName)
    {
        var normalized = OptionalText(value, 64, parameterName);
        if (normalized is not null && !ReasonCodePattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Reason code must be an uppercase machine-readable token.",
                parameterName);
        }
        return normalized;
    }

    private static string? OptionalSafeComment(
        string? value,
        string parameterName)
    {
        var normalized = OptionalText(value, 512, parameterName);
        if (normalized is not null &&
            (normalized.Any(char.IsControl) ||
             SensitiveCommentPattern.IsMatch(normalized)))
        {
            throw new ArgumentException(
                "Comments cannot contain control characters or credential-like content.",
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

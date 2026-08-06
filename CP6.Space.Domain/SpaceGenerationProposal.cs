using System.Text.Json;

namespace CP6.Space.Domain;

public sealed record SpaceGenerationProposalDefinition(
    Guid TenantId,
    Guid RunId,
    Guid ModelVersionId,
    long BaseContentRevision,
    string SourceHash,
    string SourceKey,
    string ProposalType,
    string SuggestedGeometryJson,
    string SuggestedAttributesJson,
    string SuggestedRelationsJson,
    string SourceRefsJson,
    string EvidenceJson,
    string FieldProvenanceJson,
    decimal ConfidenceScore,
    SpaceConfidenceBand ConfidenceBand,
    bool HasBlockingIssue);

public sealed class SpaceGenerationProposal : SpaceTenantEntity
{
    private SpaceGenerationProposal()
    {
    }

    public Guid RunId { get; private set; }
    public Guid ModelVersionId { get; private set; }
    public long BaseContentRevision { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string SourceKey { get; private set; } = string.Empty;
    public string ProposalType { get; private set; } = string.Empty;
    public string SuggestedGeometryJson { get; private set; } = "{}";
    public string SuggestedAttributesJson { get; private set; } = "{}";
    public string SuggestedRelationsJson { get; private set; } = "[]";
    public string SourceRefsJson { get; private set; } = "[]";
    public string EvidenceJson { get; private set; } = "[]";
    public string FieldProvenanceJson { get; private set; } = "{}";
    public decimal ConfidenceScore { get; private set; }
    public SpaceConfidenceBand ConfidenceBand { get; private set; }
    public SpaceGenerationProposalStatus Status { get; private set; }
    public bool HasBlockingIssue { get; private set; }
    public string? HumanPatchJson { get; private set; }
    public string? LockedFieldsJson { get; private set; }
    public Guid? AppliedLogicalId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceGenerationProposal Create(
        SpaceGenerationProposalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        RequireId(definition.RunId, nameof(definition.RunId));
        RequireId(
            definition.ModelVersionId,
            nameof(definition.ModelVersionId));
        if (definition.BaseContentRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.BaseContentRevision));
        }
        if (definition.ConfidenceScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.ConfidenceScore));
        }
        if (!Enum.IsDefined(definition.ConfidenceBand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.ConfidenceBand));
        }

        var proposal = new SpaceGenerationProposal
        {
            RunId = definition.RunId,
            ModelVersionId = definition.ModelVersionId,
            BaseContentRevision = definition.BaseContentRevision,
            SourceHash = SpaceGenerationRun.RequireHash(
                definition.SourceHash,
                nameof(definition.SourceHash)),
            SourceKey = SpaceGenerationRun.RequireText(
                definition.SourceKey,
                256,
                nameof(definition.SourceKey)),
            ProposalType = SpaceGenerationRun.RequireText(
                definition.ProposalType,
                64,
                nameof(definition.ProposalType)),
            SuggestedGeometryJson = RequireJson(
                definition.SuggestedGeometryJson,
                nameof(definition.SuggestedGeometryJson)),
            SuggestedAttributesJson = RequireJson(
                definition.SuggestedAttributesJson,
                nameof(definition.SuggestedAttributesJson)),
            SuggestedRelationsJson = RequireJson(
                definition.SuggestedRelationsJson,
                nameof(definition.SuggestedRelationsJson)),
            SourceRefsJson = RequireJson(
                definition.SourceRefsJson,
                nameof(definition.SourceRefsJson)),
            EvidenceJson = RequireJson(
                definition.EvidenceJson,
                nameof(definition.EvidenceJson)),
            FieldProvenanceJson = RequireJson(
                definition.FieldProvenanceJson,
                nameof(definition.FieldProvenanceJson)),
            ConfidenceScore = definition.ConfidenceScore,
            ConfidenceBand = definition.ConfidenceBand,
            Status = SpaceGenerationProposalStatus.Proposed,
            HasBlockingIssue = definition.HasBlockingIssue,
        };
        proposal.SetTenant(definition.TenantId);
        return proposal;
    }

    public void Accept()
    {
        RequireStatus(SpaceGenerationProposalStatus.Proposed);
        EnsureNotBlocking();
        Status = SpaceGenerationProposalStatus.Accepted;
    }

    public void Reject()
    {
        RequireStatus(SpaceGenerationProposalStatus.Proposed);
        Status = SpaceGenerationProposalStatus.Rejected;
    }

    public void Modify(
        string humanPatchJson,
        string lockedFieldsJson,
        bool resolvesBlockingIssues = false)
    {
        RequireStatus(SpaceGenerationProposalStatus.Proposed);
        if (HasBlockingIssue && !resolvesBlockingIssues)
            EnsureNotBlocking();
        HumanPatchJson = RequireJson(
            humanPatchJson,
            nameof(humanPatchJson));
        LockedFieldsJson = RequireJson(
            lockedFieldsJson,
            nameof(lockedFieldsJson));
        if (resolvesBlockingIssues)
            HasBlockingIssue = false;
        Status = SpaceGenerationProposalStatus.Modified;
    }

    public void MarkApplied(Guid logicalId)
    {
        if (Status is not (
            SpaceGenerationProposalStatus.Accepted or
            SpaceGenerationProposalStatus.Modified))
        {
            throw StateError("be applied");
        }
        RequireId(logicalId, nameof(logicalId));
        AppliedLogicalId = logicalId;
        Status = SpaceGenerationProposalStatus.Applied;
    }

    public void MarkObsolete()
    {
        if (Status == SpaceGenerationProposalStatus.Applied)
        {
            throw new SpaceProposalStateException(
                "An applied proposal cannot become obsolete.");
        }
        if (Status == SpaceGenerationProposalStatus.Obsolete)
        {
            throw new SpaceProposalStateException(
                "Proposal is already obsolete.");
        }
        Status = SpaceGenerationProposalStatus.Obsolete;
    }

    private void EnsureNotBlocking()
    {
        if (HasBlockingIssue)
        {
            throw new SpaceProposalStateException(
                "A blocking proposal cannot be accepted or modified.");
        }
    }

    private void RequireStatus(
        SpaceGenerationProposalStatus expected)
    {
        if (Status != expected)
            throw StateError($"transition as {expected}");
    }

    private SpaceProposalStateException StateError(string action) =>
        new($"Proposal cannot {action} from {Status}.");

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

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }
}

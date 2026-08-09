using System.Text.Json;

namespace CP6.Space.Domain;

public sealed class SpaceGenerationLockedFact : SpaceTenantEntity
{
    private SpaceGenerationLockedFact()
    {
    }

    public Guid RunId { get; private set; }
    public Guid BasedOnRunId { get; private set; }
    public Guid SourceProposalId { get; private set; }
    public Guid SourceDecisionId { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string SourceKey { get; private set; } = string.Empty;
    public string ProposalType { get; private set; } = string.Empty;
    public string FieldPath { get; private set; } = string.Empty;
    public string ValueJson { get; private set; } = "null";
    public SpaceLockedFactMatchMethod MatchMethod { get; private set; }
    public decimal MatchScore { get; private set; }
    public bool IsConfirmed { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceGenerationLockedFact CreateSameSource(
        Guid tenantId,
        Guid runId,
        Guid basedOnRunId,
        Guid sourceProposalId,
        Guid sourceDecisionId,
        string sourceHash,
        string sourceKey,
        string proposalType,
        string fieldPath,
        string valueJson)
    {
        RequireId(runId, nameof(runId));
        RequireId(basedOnRunId, nameof(basedOnRunId));
        RequireId(sourceProposalId, nameof(sourceProposalId));
        RequireId(sourceDecisionId, nameof(sourceDecisionId));
        if (runId == basedOnRunId)
            throw new ArgumentException("A locked fact must target a later run.");
        var normalizedPath = RequireText(fieldPath, 256, nameof(fieldPath));
        if (!normalizedPath.StartsWith("/attributes/", StringComparison.Ordinal) &&
            !normalizedPath.StartsWith("/relations/", StringComparison.Ordinal))
            throw new ArgumentException(
                "A locked fact path must address an allowlisted proposal value.",
                nameof(fieldPath));

        var fact = new SpaceGenerationLockedFact
        {
            RunId = runId,
            BasedOnRunId = basedOnRunId,
            SourceProposalId = sourceProposalId,
            SourceDecisionId = sourceDecisionId,
            SourceHash = SpaceGenerationRun.RequireHash(
                sourceHash,
                nameof(sourceHash)),
            SourceKey = RequireText(sourceKey, 256, nameof(sourceKey)),
            ProposalType = RequireText(proposalType, 64, nameof(proposalType)),
            FieldPath = normalizedPath,
            ValueJson = RequireJson(valueJson, nameof(valueJson)),
            MatchMethod = SpaceLockedFactMatchMethod.SameSourceIdentity,
            MatchScore = 1m,
            IsConfirmed = true,
        };
        fact.SetTenant(tenantId);
        return fact;
    }

    private static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
            throw new ArgumentException("A bounded JSON value is required.", parameterName);
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is
                JsonValueKind.Object or JsonValueKind.Array)
                throw new ArgumentException(
                    "A locked fact must contain one scalar JSON value.",
                    parameterName);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The locked fact JSON is invalid.", parameterName, exception);
        }
        return value;
    }

    private static string RequireText(
        string value,
        int maximum,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximum ||
            normalized.Any(char.IsControl))
            throw new ArgumentException("A bounded text value is required.", parameterName);
        return normalized;
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }
}

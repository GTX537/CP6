namespace CP6.Space.Domain;

public sealed record SpacePlanningScenarioBranchData(
    Guid SiteId,
    Guid ModelId,
    Guid BasePublishedVersionId,
    Guid ScenarioVersionId,
    Guid CloneJobId,
    string Name,
    string DefinitionVersion,
    string RequestHash);

public sealed class SpacePlanningScenarioBranch : SpaceTenantEntity
{
    private SpacePlanningScenarioBranch()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid BasePublishedVersionId { get; private set; }
    public Guid ScenarioVersionId { get; private set; }
    public Guid CloneJobId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DefinitionVersion { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;

    public static SpacePlanningScenarioBranch Create(
        Guid tenantId,
        Guid branchId,
        SpacePlanningScenarioBranchData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Identity(value.SiteId, nameof(value.SiteId));
        Identity(value.ModelId, nameof(value.ModelId));
        Identity(
            value.BasePublishedVersionId,
            nameof(value.BasePublishedVersionId));
        Identity(value.ScenarioVersionId, nameof(value.ScenarioVersionId));
        Identity(value.CloneJobId, nameof(value.CloneJobId));
        if (value.BasePublishedVersionId == value.ScenarioVersionId)
        {
            throw new ArgumentException(
                "The planning scenario must be distinct from its base.",
                nameof(value));
        }

        var result = new SpacePlanningScenarioBranch
        {
            SiteId = value.SiteId,
            ModelId = value.ModelId,
            BasePublishedVersionId = value.BasePublishedVersionId,
            ScenarioVersionId = value.ScenarioVersionId,
            CloneJobId = value.CloneJobId,
            Name = Text(value.Name, 200, nameof(value.Name)),
            DefinitionVersion = Text(
                value.DefinitionVersion,
                100,
                nameof(value.DefinitionVersion)),
            RequestHash = Hash(value.RequestHash),
        };
        result.SetTenant(tenantId);
        result.SetId(branchId);
        return result;
    }

    private static void Identity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static string Text(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A value of at most {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static string Hash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A SHA-256 request hash is required.",
                nameof(value));
        }
        return normalized;
    }
}

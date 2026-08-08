namespace CP6.Space.Domain;

public enum SpaceFieldPolicyStatus : short
{
    Active = 0,
    Retired = 1,
}

public enum SpaceFieldPolicyResourceType : short
{
    PublishedScene = 0,
    Stock = 1,
    Task = 2,
}

public enum SpaceFieldMaskingRule : short
{
    None = 0,
    Partial = 1,
    Hash = 2,
    Redact = 3,
}

public sealed class SpaceFieldPolicy : SpaceTenantEntity
{
    private SpaceFieldPolicy()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public SpaceExternalOrganizationType AudienceType { get; private set; }
    public bool CanExport { get; private set; }
    public SpaceFieldPolicyStatus Status { get; private set; }
    public long PolicyVersion { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceFieldPolicy Create(
        Guid tenantId,
        string name,
        SpaceExternalOrganizationType audienceType,
        bool canExport,
        SpaceFieldPolicyStatus status = SpaceFieldPolicyStatus.Active)
    {
        if (status == SpaceFieldPolicyStatus.Retired)
        {
            throw new SpaceExternalAccessStateException(
                "A field policy must be created as Active.");
        }

        var policy = new SpaceFieldPolicy
        {
            AudienceType = audienceType,
            CanExport = canExport,
            Status = status,
            PolicyVersion = 1,
        };
        policy.SetTenant(tenantId);
        policy.SetName(name);
        return policy;
    }

    public void Update(
        string name,
        bool canExport,
        SpaceFieldPolicyStatus status)
    {
        if (Status == SpaceFieldPolicyStatus.Retired &&
            status != SpaceFieldPolicyStatus.Retired)
        {
            throw new SpaceExternalAccessStateException(
                "A retired field policy cannot be reactivated.");
        }

        SetName(name);
        CanExport = canExport;
        Status = status;
        PolicyVersion = checked(PolicyVersion + 1);
    }

    private void SetName(string name)
    {
        Name = SpaceFieldPolicyValue.RequireText(name, 200, nameof(name));
        NormalizedName = Name.ToUpperInvariant();
    }
}

public sealed class SpaceFieldPolicyField : SpaceTenantEntity
{
    private SpaceFieldPolicyField()
    {
    }

    public Guid PolicyId { get; private set; }
    public SpaceFieldPolicyResourceType ResourceType { get; private set; }
    public string FieldName { get; private set; } = string.Empty;
    public string NormalizedFieldName { get; private set; } = string.Empty;
    public SpaceFieldMaskingRule MaskingRule { get; private set; }

    public static SpaceFieldPolicyField Create(
        Guid tenantId,
        Guid policyId,
        SpaceFieldPolicyResourceType resourceType,
        string fieldName,
        SpaceFieldMaskingRule maskingRule)
    {
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy is required.", nameof(policyId));
        var field = SpaceFieldPolicyValue.RequireText(
            fieldName,
            100,
            nameof(fieldName));
        var result = new SpaceFieldPolicyField
        {
            PolicyId = policyId,
            ResourceType = resourceType,
            FieldName = field,
            NormalizedFieldName = field.ToUpperInvariant(),
            MaskingRule = maskingRule,
        };
        result.SetTenant(tenantId);
        return result;
    }

    public void Retire() => MarkEntityDeleted();
}

internal static class SpaceFieldPolicyValue
{
    internal static string RequireText(
        string? value,
        int maxLength,
        string parameterName)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0 || result.Length > maxLength)
        {
            throw new ArgumentException(
                $"{parameterName} must contain 1 to {maxLength} characters.",
                parameterName);
        }
        return result;
    }
}

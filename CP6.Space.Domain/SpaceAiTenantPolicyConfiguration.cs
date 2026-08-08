namespace CP6.Space.Domain;

/// <summary>
/// Append-only tenant AI policy version. The only permitted mutation is
/// deactivating the previous current version while a successor is inserted.
/// </summary>
public sealed class SpaceAiTenantPolicyConfiguration : SpaceTenantEntity
{
    private SpaceAiTenantPolicyConfiguration()
    {
    }

    public int Version { get; private set; }
    public string DataPolicy { get; private set; } = string.Empty;
    public string AllowedSiteIdsJson { get; private set; } = "[]";
    public string AllowedProviderAliasesJson { get; private set; } = "[]";
    public int MaxConcurrentRuns { get; private set; }
    public bool ExternalProviderEnabled { get; private set; }
    public long? DailyBudgetMinor { get; private set; }
    public long? MonthlyBudgetMinor { get; private set; }
    public string? Currency { get; private set; }
    public bool IsActive { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceAiTenantPolicyConfiguration Create(
        Guid tenantId,
        int version,
        string dataPolicy,
        string allowedSiteIdsJson,
        string allowedProviderAliasesJson,
        int maxConcurrentRuns,
        bool externalProviderEnabled,
        long? dailyBudgetMinor,
        long? monthlyBudgetMinor,
        string? currency,
        Guid updatedBy,
        DateTime updatedAtUtc)
    {
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(dataPolicy))
            throw new ArgumentException("Data policy is required.", nameof(dataPolicy));
        if (updatedBy == Guid.Empty)
            throw new ArgumentException("Actor is required.", nameof(updatedBy));
        if (updatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Update time must be UTC.", nameof(updatedAtUtc));

        var entity = new SpaceAiTenantPolicyConfiguration
        {
            Version = version,
            DataPolicy = dataPolicy,
            AllowedSiteIdsJson = allowedSiteIdsJson,
            AllowedProviderAliasesJson = allowedProviderAliasesJson,
            MaxConcurrentRuns = maxConcurrentRuns,
            ExternalProviderEnabled = externalProviderEnabled,
            DailyBudgetMinor = dailyBudgetMinor,
            MonthlyBudgetMinor = monthlyBudgetMinor,
            Currency = currency,
            IsActive = true,
            UpdatedBy = updatedBy,
            UpdatedAtUtc = updatedAtUtc,
        };
        entity.SetTenant(tenantId);
        return entity;
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new InvalidOperationException("Only the active AI policy can be superseded.");
        IsActive = false;
    }
}

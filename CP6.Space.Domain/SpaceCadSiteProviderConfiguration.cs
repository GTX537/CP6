namespace CP6.Space.Domain;

public enum SpaceCadProviderRole
{
    Primary = 1,
    Backup = 2,
}

public enum SpaceCadProviderDeploymentMode
{
    OnPremisesIsolatedWorker = 1,
    PrivateCloudWorker = 2,
    ApprovedCloudService = 3,
}

public enum SpaceCadProviderDataBoundary
{
    SiteLocal = 1,
    CustomerPrivateCloud = 2,
    CustomerApprovedCloudRegion = 3,
}

public static class SpaceCadProviderKey
{
    public static string Normalize(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 64 ||
            normalized[0] is '.' or '_' or '-' ||
            normalized.Any(character =>
                !(character is >= 'a' and <= 'z' ||
                  character is >= '0' and <= '9' ||
                  character is '.' or '_' or '-')))
        {
            throw new ArgumentException(
                "A lowercase deployment-approved Provider key is required.",
                nameof(value));
        }
        return normalized;
    }
}

public static class SpaceCadProviderVersion
{
    public const int MaximumLength = 100;

    public static string Normalize(string value)
    {
        var normalized = value?.Trim();
        if (!IsValid(normalized))
        {
            throw new ArgumentException(
                "A bounded opaque Provider version is required.",
                nameof(value));
        }
        return normalized!;
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumLength &&
        !value.Any(char.IsControl) &&
        !value.Any(char.IsWhiteSpace);
}

public sealed class SpaceCadSiteProviderConfiguration : SpaceTenantEntity
{
    private SpaceCadSiteProviderConfiguration()
    {
    }

    public Guid SiteId { get; private set; }
    public long ConfigurationRevision { get; private set; }
    public bool IsCurrent { get; private set; }
    public string ChangeReason { get; private set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; private set; }
    public Guid ApprovedBy { get; private set; }

    public static SpaceCadSiteProviderConfiguration Create(
        Guid tenantId,
        Guid siteId,
        long configurationRevision,
        string reason,
        Guid approvedBy,
        DateTime approvedAtUtc)
    {
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site is required.", nameof(siteId));
        if (configurationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationRevision));
        if (approvedBy == Guid.Empty)
            throw new ArgumentException("Approver is required.", nameof(approvedBy));
        if (approvedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Approval time must be UTC.", nameof(approvedAtUtc));
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
            throw new ArgumentException("A bounded change reason is required.", nameof(reason));

        var value = new SpaceCadSiteProviderConfiguration
        {
            SiteId = siteId,
            ConfigurationRevision = configurationRevision,
            IsCurrent = true,
            ChangeReason = normalizedReason,
            ApprovedBy = approvedBy,
            ApprovedAtUtc = approvedAtUtc,
        };
        value.SetTenant(tenantId);
        return value;
    }

    public void Supersede() => IsCurrent = false;
}

public sealed class SpaceCadSiteProviderCertification : SpaceTenantEntity
{
    public const int MinimumQualificationScore = 80;

    private SpaceCadSiteProviderCertification()
    {
    }

    public Guid ConfigurationId { get; private set; }
    public Guid SiteId { get; private set; }
    public string ProviderKey { get; private set; } = string.Empty;
    public string ProviderVersion { get; private set; } = string.Empty;
    public SpaceCadProviderRole Role { get; private set; }
    public SpaceCadProviderDeploymentMode DeploymentMode { get; private set; }
    public SpaceCadProviderDataBoundary DataBoundary { get; private set; }
    public string ApprovalEvidenceReference { get; private set; } = string.Empty;
    public string? SecretReference { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public bool SupportsDwg { get; private set; }
    public bool SupportsDxf { get; private set; }
    public bool LicensingApproved { get; private set; }
    public bool SecurityApproved { get; private set; }
    public bool DataRegionApproved { get; private set; }
    public bool DeletionRetentionApproved { get; private set; }
    public int? QualificationScore { get; private set; }
    public string? QualificationRubricVersion { get; private set; }
    public string? GoldenDatasetSha256 { get; private set; }
    public string? FrozenEnvironmentSha256 { get; private set; }
    public string? QualificationEvidenceReference { get; private set; }

    public static SpaceCadSiteProviderCertification Create(
        Guid tenantId,
        Guid configurationId,
        Guid siteId,
        string providerKey,
        string providerVersion,
        SpaceCadProviderRole role,
        SpaceCadProviderDeploymentMode deploymentMode,
        SpaceCadProviderDataBoundary dataBoundary,
        string approvalEvidenceReference,
        string? secretReference,
        DateTime validFromUtc,
        DateTime expiresAtUtc,
        bool supportsDwg,
        bool supportsDxf,
        bool licensingApproved,
        bool securityApproved,
        bool dataRegionApproved,
        bool deletionRetentionApproved,
        int qualificationScore,
        string qualificationRubricVersion,
        string goldenDatasetSha256,
        string frozenEnvironmentSha256,
        string qualificationEvidenceReference)
    {
        if (configurationId == Guid.Empty)
            throw new ArgumentException("Configuration is required.", nameof(configurationId));
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site is required.", nameof(siteId));
        if (!Enum.IsDefined(role) || !Enum.IsDefined(deploymentMode) ||
            !Enum.IsDefined(dataBoundary))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (validFromUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc ||
            expiresAtUtc <= validFromUtc)
            throw new ArgumentException("A valid UTC certification window is required.");
        if (!supportsDwg && !supportsDxf)
            throw new ArgumentException("At least one CAD format must be certified.");
        if (!licensingApproved || !securityApproved || !dataRegionApproved ||
            !deletionRetentionApproved)
            throw new ArgumentException(
                "Licensing, security, data-region and deletion/retention gates must all pass.");
        if (qualificationScore is < MinimumQualificationScore or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(qualificationScore),
                $"Qualification score must be between {MinimumQualificationScore} and 100.");

        var evidence = RequireReference(
            approvalEvidenceReference,
            500,
            nameof(approvalEvidenceReference));
        var secret = string.IsNullOrWhiteSpace(secretReference)
            ? null
            : RequireReference(secretReference, 256, nameof(secretReference));
        if (deploymentMode == SpaceCadProviderDeploymentMode.ApprovedCloudService &&
            secret is null)
            throw new ArgumentException(
                "Approved cloud Providers require a managed Secret reference.",
                nameof(secretReference));
        var rubricVersion = RequireReference(
            qualificationRubricVersion,
            100,
            nameof(qualificationRubricVersion));
        var datasetSha256 = RequireSha256(
            goldenDatasetSha256,
            nameof(goldenDatasetSha256));
        var environmentSha256 = RequireSha256(
            frozenEnvironmentSha256,
            nameof(frozenEnvironmentSha256));
        var qualificationEvidence = RequireReference(
            qualificationEvidenceReference,
            500,
            nameof(qualificationEvidenceReference));

        var value = new SpaceCadSiteProviderCertification
        {
            ConfigurationId = configurationId,
            SiteId = siteId,
            ProviderKey = SpaceCadProviderKey.Normalize(providerKey),
            ProviderVersion = SpaceCadProviderVersion.Normalize(providerVersion),
            Role = role,
            DeploymentMode = deploymentMode,
            DataBoundary = dataBoundary,
            ApprovalEvidenceReference = evidence,
            SecretReference = secret,
            ValidFromUtc = validFromUtc,
            ExpiresAtUtc = expiresAtUtc,
            SupportsDwg = supportsDwg,
            SupportsDxf = supportsDxf,
            LicensingApproved = licensingApproved,
            SecurityApproved = securityApproved,
            DataRegionApproved = dataRegionApproved,
            DeletionRetentionApproved = deletionRetentionApproved,
            QualificationScore = qualificationScore,
            QualificationRubricVersion = rubricVersion,
            GoldenDatasetSha256 = datasetSha256,
            FrozenEnvironmentSha256 = environmentSha256,
            QualificationEvidenceReference = qualificationEvidence,
        };
        value.SetTenant(tenantId);
        return value;
    }

    public bool IsValidAt(DateTime utcNow) =>
        utcNow.Kind == DateTimeKind.Utc && utcNow >= ValidFromUtc && utcNow < ExpiresAtUtc;

    public bool HasCompleteQualification =>
        LicensingApproved &&
        SecurityApproved &&
        DataRegionApproved &&
        DeletionRetentionApproved &&
        SpaceCadProviderVersion.IsValid(ProviderVersion) &&
        QualificationScore is >= MinimumQualificationScore and <= 100 &&
        IsReference(QualificationRubricVersion, 100) &&
        IsSha256(GoldenDatasetSha256) &&
        IsSha256(FrozenEnvironmentSha256) &&
        IsReference(QualificationEvidenceReference, 500);

    private static string RequireReference(string value, int maximum, string parameterName)
    {
        var normalized = value?.Trim();
        if (!IsReference(normalized, maximum))
            throw new ArgumentException("A bounded opaque reference is required.", parameterName);
        return normalized!;
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (!IsSha256(normalized))
            throw new ArgumentException(
                "A 64-character SHA-256 is required.",
                parameterName);
        return normalized!;
    }

    private static bool IsReference(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum &&
        !value.Any(char.IsControl) &&
        !value.Any(char.IsWhiteSpace);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

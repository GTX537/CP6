namespace CP6.Space.Contracts;

public sealed record SpaceCadProviderCertificationInputDto(
    string ProviderKey,
    string ProviderVersion,
    string Role,
    string DeploymentMode,
    string DataBoundary,
    string ApprovalEvidenceReference,
    string? SecretReference,
    DateTime ValidFromUtc,
    DateTime ExpiresAtUtc,
    bool SupportsDwg,
    bool SupportsDxf,
    bool LicensingApproved,
    bool SecurityApproved,
    bool DataRegionApproved,
    bool DeletionRetentionApproved,
    int QualificationScore,
    string QualificationRubricVersion,
    string GoldenDatasetSha256,
    string FrozenEnvironmentSha256,
    string QualificationEvidenceReference);

public sealed record ReplaceSpaceCadProviderConfigurationRequest(
    long ExpectedConfigurationRevision,
    string Reason,
    IReadOnlyList<SpaceCadProviderCertificationInputDto> Certifications);

public sealed record SpaceCadProviderSlotDto(
    string ProviderKey,
    string ProviderVersion,
    string DisplayName,
    string Role,
    string DeploymentMode,
    string DataBoundary,
    string ApprovalEvidenceReference,
    bool SecretReferenceConfigured,
    DateTime ValidFromUtc,
    DateTime ExpiresAtUtc,
    bool SupportsDwg,
    bool SupportsDxf,
    bool LicensingApproved,
    bool SecurityApproved,
    bool DataRegionApproved,
    bool DeletionRetentionApproved,
    int? QualificationScore,
    string? QualificationRubricVersion,
    string? GoldenDatasetSha256,
    string? FrozenEnvironmentSha256,
    string? QualificationEvidenceReference,
    bool Qualified,
    bool RuntimeAvailable,
    bool CurrentlyValid);

public sealed record SpaceCadSiteCapabilityDto(
    Guid SiteId,
    long ConfigurationRevision,
    bool CanPrepareCad,
    bool CadGaReady,
    SpaceCadProviderSlotDto? Primary,
    SpaceCadProviderSlotDto? Backup,
    IReadOnlyList<string> BlockingCodes,
    DateTime EvaluatedAtUtc,
    DateTime? UpdatedAtUtc,
    Guid? UpdatedBy);

public sealed record ReplaceSpaceCadProviderConfigurationResponse(
    SpaceCadSiteCapabilityDto Capability,
    bool IdempotentReplay);

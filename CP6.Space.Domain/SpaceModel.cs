namespace CP6.Space.Domain;

public sealed class SpaceModel : SpaceTenantEntity
{
    private SpaceModel()
    {
    }

    public Guid SiteId { get; private set; }
    public SpaceModelMode Mode { get; private set; }
    public SpaceModelCutoverState CutoverState { get; private set; }
    public Guid? CutoverOperationId { get; private set; }
    public Guid? ActiveDraftVersionId { get; private set; }
    public Guid? CurrentPublishedVersionId { get; private set; }
    public string? LastMaterializedHash { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceModel Create(Guid tenantId, Guid siteId)
    {
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site is required.", nameof(siteId));

        var model = new SpaceModel
        {
            SiteId = siteId,
            Mode = SpaceModelMode.Legacy,
            CutoverState = SpaceModelCutoverState.LegacyOpen,
        };
        model.SetTenant(tenantId);
        return model;
    }

    public void ReserveDraft(SpaceModelVersion version)
    {
        EnsureOwnVersion(version);
        if (version.Status != SpaceVersionStatus.Draft)
            throw new SpaceVersionStateException("Only a Draft version can occupy the active draft slot.");

        if (ActiveDraftVersionId == version.Id)
            return;

        if (ActiveDraftVersionId.HasValue)
            throw new SpaceVersionConflictException("The model already has an active draft.");

        ActiveDraftVersionId = version.Id;
    }

    public void ReleaseDraft(Guid versionId)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("Version is required.", nameof(versionId));

        if (ActiveDraftVersionId != versionId)
            throw new SpaceVersionConflictException("The version does not own the active draft slot.");

        ActiveDraftVersionId = null;
    }

    public void SetPublishedVersion(SpaceModelVersion version, string materializedHash)
    {
        EnsureOwnVersion(version);
        if (version.Status != SpaceVersionStatus.Published)
            throw new SpaceVersionStateException("Only a Published version can become the runtime source.");

        LastMaterializedHash = RequireHash(materializedHash, nameof(materializedHash));
        CurrentPublishedVersionId = version.Id;

        if (ActiveDraftVersionId == version.Id)
            ActiveDraftVersionId = null;
    }

    public void BeginCutover(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Cutover operation is required.", nameof(operationId));

        RequireCutoverState(SpaceModelCutoverState.LegacyOpen);
        CutoverOperationId = operationId;
        CutoverState = SpaceModelCutoverState.FreezeRequested;
    }

    public void MarkFrozen()
    {
        RequireCutoverState(SpaceModelCutoverState.FreezeRequested);
        CutoverState = SpaceModelCutoverState.Frozen;
    }

    public void MarkBootstrapping()
    {
        RequireCutoverState(SpaceModelCutoverState.Frozen);
        CutoverState = SpaceModelCutoverState.Bootstrapping;
    }

    public void MarkVerified(SpaceModelVersion bootstrapVersion)
    {
        RequireCutoverState(SpaceModelCutoverState.Bootstrapping);
        EnsureOwnVersion(bootstrapVersion);
        if (bootstrapVersion.Status != SpaceVersionStatus.Published)
            throw new SpaceVersionStateException("Bootstrap verification requires a Published version.");

        CurrentPublishedVersionId = bootstrapVersion.Id;
        CutoverState = SpaceModelCutoverState.Verified;
    }

    public void ActivateDesignV1()
    {
        RequireCutoverState(SpaceModelCutoverState.Verified);
        if (!CurrentPublishedVersionId.HasValue)
            throw new SpaceVersionStateException("DesignV1 requires a verified Published version.");

        Mode = SpaceModelMode.DesignV1;
        CutoverState = SpaceModelCutoverState.DesignV1;
    }

    public void FailCutover()
    {
        if (CutoverState is SpaceModelCutoverState.LegacyOpen or SpaceModelCutoverState.DesignV1)
            throw new SpaceVersionStateException("The current cutover cannot enter FailedFrozen.");

        CutoverState = SpaceModelCutoverState.FailedFrozen;
    }

    public void ReopenLegacy(bool approved, bool designWritesAccepted)
    {
        RequireCutoverState(SpaceModelCutoverState.FailedFrozen);
        if (!approved || designWritesAccepted || Mode != SpaceModelMode.Legacy)
            throw new SpaceVersionStateException("Legacy can reopen only with approval and before Design writes.");

        CutoverOperationId = null;
        CutoverState = SpaceModelCutoverState.LegacyOpen;
    }

    private void EnsureOwnVersion(SpaceModelVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.TenantId != TenantId)
            throw new SpaceTenantScopeException("The version belongs to another tenant.");
        if (version.ModelId != Id)
            throw new SpaceVersionConflictException("The version belongs to another model.");
    }

    private void RequireCutoverState(SpaceModelCutoverState expected)
    {
        if (CutoverState != expected)
            throw new SpaceVersionStateException(
                $"Cutover state must be {expected}, but was {CutoverState}.");
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A lowercase or uppercase SHA-256 hex value is required.", parameterName);
        return value.ToLowerInvariant();
    }
}

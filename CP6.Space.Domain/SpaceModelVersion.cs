namespace CP6.Space.Domain;

public sealed class SpaceModelVersion : SpaceTenantEntity
{
    private SpaceModelVersion()
    {
    }

    public Guid ModelId { get; private set; }
    public long VersionNo { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public SpaceVersionStatus Status { get; private set; }
    public Guid? BasedOnVersionId { get; private set; }
    public long ContentRevision { get; private set; }
    public string? ContentHash { get; private set; }
    public string? RuleSetVersion { get; private set; }
    public string? ValidatedHash { get; private set; }
    public string? WmsCapabilityHash { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceModelVersion CreateDraft(
        Guid tenantId,
        Guid modelId,
        long versionNo,
        string name,
        Guid? basedOnVersionId = null)
    {
        if (modelId == Guid.Empty)
            throw new ArgumentException("Model is required.", nameof(modelId));
        if (versionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(versionNo), "Version number must be positive.");

        var version = new SpaceModelVersion
        {
            ModelId = modelId,
            VersionNo = versionNo,
            Name = RequireName(name),
            Status = SpaceVersionStatus.Draft,
            BasedOnVersionId = basedOnVersionId,
        };
        version.SetTenant(tenantId);
        return version;
    }

    public void Rename(string name)
    {
        EnsureEditable();
        Name = RequireName(name);
    }

    public void TouchContent()
    {
        EnsureEditable();
        ContentRevision = checked(ContentRevision + 1);
        ClearValidationBinding();
        Status = SpaceVersionStatus.Draft;
    }

    public void BeginValidation()
    {
        RequireStatus(SpaceVersionStatus.Draft);
        Status = SpaceVersionStatus.Validating;
    }

    public void MarkReady(string contentHash, string ruleSetVersion, string wmsCapabilityHash)
    {
        RequireStatus(SpaceVersionStatus.Validating);
        ContentHash = RequireHash(contentHash, nameof(contentHash));
        ValidatedHash = ContentHash;
        RuleSetVersion = RequireRuleSet(ruleSetVersion);
        WmsCapabilityHash = RequireHash(wmsCapabilityHash, nameof(wmsCapabilityHash));
        Status = SpaceVersionStatus.Ready;
    }

    public void CompleteValidationWithErrors()
    {
        RequireStatus(SpaceVersionStatus.Validating);
        ClearValidationBinding();
        Status = SpaceVersionStatus.Draft;
    }

    public void BeginPublishing()
    {
        RequireStatus(SpaceVersionStatus.Ready);
        if (ContentHash is null ||
            ValidatedHash != ContentHash ||
            RuleSetVersion is null ||
            WmsCapabilityHash is null)
        {
            throw new SpaceVersionStateException("Ready validation evidence is incomplete or stale.");
        }

        Status = SpaceVersionStatus.Publishing;
    }

    public void ReturnToReadyBeforeExternalCommit()
    {
        RequireStatus(SpaceVersionStatus.Publishing);
        Status = SpaceVersionStatus.Ready;
    }

    public void MarkReconciliationRequired()
    {
        RequireStatus(SpaceVersionStatus.Publishing);
        Status = SpaceVersionStatus.ReconciliationRequired;
    }

    public void ResumePublishingAfterReconciliation()
    {
        RequireStatus(SpaceVersionStatus.ReconciliationRequired);
        Status = SpaceVersionStatus.Publishing;
    }

    public void MarkPublished(Guid actorId, DateTime nowUtc)
    {
        RequireStatus(SpaceVersionStatus.Publishing);
        if (actorId == Guid.Empty)
            throw new ArgumentException("Publisher is required.", nameof(actorId));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Published time must be UTC.", nameof(nowUtc));

        PublishedBy = actorId;
        PublishedAtUtc = nowUtc;
        Status = SpaceVersionStatus.Published;
    }

    public void MarkSuperseded()
    {
        RequireStatus(SpaceVersionStatus.Published);
        Status = SpaceVersionStatus.Superseded;
    }

    private void EnsureEditable()
    {
        if (Status is not (SpaceVersionStatus.Draft or SpaceVersionStatus.Ready))
            throw new SpaceVersionStateException("Only Draft or Ready versions can be edited.");
    }

    private void RequireStatus(SpaceVersionStatus expected)
    {
        if (Status != expected)
            throw new SpaceVersionStateException(
                $"Version state must be {expected}, but was {Status}.");
    }

    private void ClearValidationBinding()
    {
        ContentHash = null;
        ValidatedHash = null;
        RuleSetVersion = null;
        WmsCapabilityHash = null;
    }

    private static string RequireName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
            throw new ArgumentException("Version name is required and cannot exceed 200 characters.", nameof(value));
        return normalized;
    }

    private static string RequireRuleSet(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50)
            throw new ArgumentException("Rule set version is required and cannot exceed 50 characters.", nameof(value));
        return normalized;
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A lowercase or uppercase SHA-256 hex value is required.", parameterName);
        return value.ToLowerInvariant();
    }
}

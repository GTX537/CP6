namespace CP6.Space.Domain;

public sealed class SpaceWmsAdoption : SpaceTenantEntity
{
    private SpaceWmsAdoption()
    {
    }

    public Guid SiteId { get; private set; }
    public string AdapterId { get; private set; } = string.Empty;
    public string DataSource { get; private set; } = string.Empty;
    public string DataSourceKind { get; private set; } = string.Empty;
    public Guid WmsLogicalId { get; private set; }
    public string? ExternalLocationId { get; private set; }
    public string WmsLocationCode { get; private set; } = string.Empty;
    public bool WmsIsActive { get; private set; }
    public string ExternalVersion { get; private set; } = string.Empty;
    public string WmsStateHash { get; private set; } = string.Empty;
    public DateTime LastObservedAtUtc { get; private set; }
    public SpaceWmsAdoptionStatus Status { get; private set; }
    public Guid? ModelVersionId { get; private set; }
    public Guid? LocationLogicalId { get; private set; }
    public string? BoundLocationCode { get; private set; }
    public DateTime? BoundAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceWmsAdoption Discover(
        Guid tenantId,
        Guid siteId,
        string adapterId,
        string dataSource,
        string dataSourceKind,
        Guid wmsLogicalId,
        string? externalLocationId,
        string wmsLocationCode,
        bool wmsIsActive,
        string externalVersion,
        string wmsStateHash,
        DateTime observedAtUtc)
    {
        RequireIdentity(siteId, nameof(siteId));
        RequireIdentity(wmsLogicalId, nameof(wmsLogicalId));
        RequireUtc(observedAtUtc, nameof(observedAtUtc));
        var adoption = new SpaceWmsAdoption
        {
            SiteId = siteId,
            AdapterId = RequireText(adapterId, 100, nameof(adapterId)),
            DataSource = RequireText(dataSource, 100, nameof(dataSource)),
            DataSourceKind = RequireText(
                dataSourceKind,
                20,
                nameof(dataSourceKind)),
            WmsLogicalId = wmsLogicalId,
            ExternalLocationId = OptionalText(
                externalLocationId,
                200,
                nameof(externalLocationId)),
            WmsLocationCode = RequireText(
                wmsLocationCode,
                200,
                nameof(wmsLocationCode)),
            WmsIsActive = wmsIsActive,
            ExternalVersion = RequireText(
                externalVersion,
                100,
                nameof(externalVersion)),
            WmsStateHash = RequireHash(wmsStateHash, nameof(wmsStateHash)),
            LastObservedAtUtc = observedAtUtc,
            Status = SpaceWmsAdoptionStatus.Unbound,
        };
        adoption.SetTenant(tenantId);
        return adoption;
    }

    public void Observe(
        string dataSource,
        string dataSourceKind,
        string? externalLocationId,
        string wmsLocationCode,
        bool wmsIsActive,
        string externalVersion,
        string wmsStateHash,
        DateTime observedAtUtc)
    {
        RequireUtc(observedAtUtc, nameof(observedAtUtc));
        DataSource = RequireText(dataSource, 100, nameof(dataSource));
        DataSourceKind = RequireText(
            dataSourceKind,
            20,
            nameof(dataSourceKind));
        ExternalLocationId = OptionalText(
            externalLocationId,
            200,
            nameof(externalLocationId));
        WmsLocationCode = RequireText(
            wmsLocationCode,
            200,
            nameof(wmsLocationCode));
        WmsIsActive = wmsIsActive;
        ExternalVersion = RequireText(
            externalVersion,
            100,
            nameof(externalVersion));
        WmsStateHash = RequireHash(wmsStateHash, nameof(wmsStateHash));
        LastObservedAtUtc = observedAtUtc;
        Status = LocationLogicalId.HasValue
            ? string.Equals(
                BoundLocationCode,
                WmsLocationCode,
                StringComparison.Ordinal)
                ? SpaceWmsAdoptionStatus.Bound
                : SpaceWmsAdoptionStatus.Diverged
            : SpaceWmsAdoptionStatus.Unbound;
    }

    public void MarkMissing(DateTime observedAtUtc)
    {
        RequireUtc(observedAtUtc, nameof(observedAtUtc));
        LastObservedAtUtc = observedAtUtc;
        Status = SpaceWmsAdoptionStatus.MissingInWms;
    }

    public void Bind(
        Guid modelVersionId,
        Guid locationLogicalId,
        DateTime boundAtUtc)
    {
        RequireIdentity(modelVersionId, nameof(modelVersionId));
        RequireIdentity(locationLogicalId, nameof(locationLogicalId));
        RequireUtc(boundAtUtc, nameof(boundAtUtc));
        if (Status == SpaceWmsAdoptionStatus.MissingInWms)
        {
            throw new InvalidOperationException(
                "A WMS location missing from the latest catalog cannot be bound.");
        }
        if (LocationLogicalId.HasValue &&
            LocationLogicalId != locationLogicalId)
        {
            throw new InvalidOperationException(
                "The WMS location is already bound to another Space location.");
        }

        ModelVersionId = modelVersionId;
        LocationLogicalId = locationLogicalId;
        BoundLocationCode = WmsLocationCode;
        BoundAtUtc = boundAtUtc;
        Status = SpaceWmsAdoptionStatus.Bound;
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An identity is required.", parameterName);
    }

    private static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, maximumLength, parameterName);

    private static string RequireHash(string value, string parameterName)
    {
        var normalized = RequireText(value, 64, parameterName);
        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 hash is required.",
                parameterName);
        }
        return normalized.ToLowerInvariant();
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The timestamp must be UTC.",
                parameterName);
        }
    }
}

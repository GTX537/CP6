namespace CP6.Space.Domain;

public sealed class SpaceWarehouseTemplate : SpaceTenantEntity
{
    private SpaceWarehouseTemplate()
    {
    }

    public string TemplateCode { get; private set; } = string.Empty;
    public string NormalizedTemplateCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int CurrentVersion { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceWarehouseTemplate CreateTenant(
        Guid tenantId,
        string templateCode,
        string name,
        string? description)
    {
        var template = new SpaceWarehouseTemplate
        {
            CurrentVersion = 1,
        };
        template.SetTenant(tenantId);
        template.SetMetadata(templateCode, name, description);
        return template;
    }

    private void SetMetadata(
        string templateCode,
        string name,
        string? description)
    {
        TemplateCode = RequiredText(
            templateCode,
            100,
            nameof(templateCode));
        NormalizedTemplateCode = TemplateCode.ToUpperInvariant();
        Name = RequiredText(name, 200, nameof(name));
        Description = OptionalText(description, 1000, nameof(description));
    }

    private static string RequiredText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{parameterName} must contain 1 to {maximumLength} characters.",
                parameterName);
        }
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }
        return normalized;
    }
}

public sealed class SpaceWarehouseTemplateVersion : SpaceTenantEntity
{
    private SpaceWarehouseTemplateVersion()
    {
    }

    public Guid TemplateId { get; private set; }
    public int VersionNo { get; private set; }
    public int SchemaVersion { get; private set; }
    public string ContentJson { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public int FloorCount { get; private set; }
    public int ZoneCount { get; private set; }
    public int AisleCount { get; private set; }
    public int RackCount { get; private set; }
    public int LocationCount { get; private set; }

    public static SpaceWarehouseTemplateVersion CreateReady(
        Guid tenantId,
        Guid versionId,
        Guid templateId,
        int versionNo,
        int schemaVersion,
        string contentJson,
        string contentHash,
        int floorCount,
        int zoneCount,
        int aisleCount,
        int rackCount,
        int locationCount)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("Template version is required.", nameof(versionId));
        if (templateId == Guid.Empty)
            throw new ArgumentException("Template is required.", nameof(templateId));
        if (versionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(versionNo));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new ArgumentException("Template content is required.", nameof(contentJson));

        var normalizedHash = contentHash?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedHash.Length != 64 ||
            normalizedHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Template content hash must be SHA-256.",
                nameof(contentHash));
        }

        if (floorCount <= 0 ||
            zoneCount < 0 ||
            aisleCount < 0 ||
            rackCount < 0 ||
            locationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(floorCount),
                "Template counts must contain at least one floor and cannot be negative.");
        }

        var version = new SpaceWarehouseTemplateVersion
        {
            TemplateId = templateId,
            VersionNo = versionNo,
            SchemaVersion = schemaVersion,
            ContentJson = contentJson,
            ContentHash = normalizedHash,
            FloorCount = floorCount,
            ZoneCount = zoneCount,
            AisleCount = aisleCount,
            RackCount = rackCount,
            LocationCount = locationCount,
        };
        version.SetId(versionId);
        version.SetTenant(tenantId);
        return version;
    }
}

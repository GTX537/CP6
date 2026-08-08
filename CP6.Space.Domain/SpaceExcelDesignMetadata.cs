namespace CP6.Space.Domain;

public static class SpaceLocationTypes
{
    public const string Storage = "Storage";
    public const string Staging = "Staging";
    public const string Picking = "Picking";
    public const string Buffer = "Buffer";

    private static readonly string[] Values =
    [
        Storage,
        Staging,
        Picking,
        Buffer,
    ];

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return Values.SingleOrDefault(candidate => candidate.Equals(
                   normalized,
                   StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   "Location type must be Storage, Staging, Picking, or Buffer.",
                   nameof(value));
    }
}

public static class SpaceDesignAttributeObjectTypes
{
    public const string Rack = "Rack";
    public const string RackLevel = "RackLevel";
    public const string Location = "Location";

    private static readonly string[] Values = [Rack, RackLevel, Location];

    public static string Normalize(string value)
    {
        var normalized = Required(value, 20, nameof(value));
        return Values.SingleOrDefault(candidate => candidate.Equals(
                   normalized,
                   StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   "Design attribute object type must be Rack, RackLevel, or Location.",
                   nameof(value));
    }

    private static string Required(
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
}

public static class SpaceDesignAttributeNamespaces
{
    public const string Owner = "Owner";
    public const string Batch = "Batch";
    public const string Container = "Container";
    public const string Manufacturing = "Manufacturing";
    public const string Custom = "Custom";

    private static readonly string[] Values =
    [
        Owner,
        Batch,
        Container,
        Manufacturing,
        Custom,
    ];

    public static string Normalize(string value)
    {
        var normalized = Required(value, 100, nameof(value));
        return Values.SingleOrDefault(candidate => candidate.Equals(
                   normalized,
                   StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   "Design attribute namespace is outside the standard Excel contract.",
                   nameof(value));
    }

    private static string Required(
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
}

public sealed class SpaceLocationExternalBinding : SpaceTenantEntity
{
    private SpaceLocationExternalBinding()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid LocationLogicalId { get; private set; }
    public string AdapterId { get; private set; } = string.Empty;
    public string WarehouseCode { get; private set; } = string.Empty;
    public string ExternalLocationId { get; private set; } = string.Empty;
    public SpaceLocationBindingMode BindingMode { get; private set; }
    public Guid SourceId { get; private set; }
    public string SourceRef { get; private set; } = string.Empty;

    public static SpaceLocationExternalBinding Create(
        Guid tenantId,
        Guid id,
        SpaceLocationRevision location,
        string adapterId,
        string warehouseCode,
        string externalLocationId,
        SpaceLocationBindingMode bindingMode,
        SpaceModelSource source,
        string sourceRef)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(source);
        EnsureSameSnapshot(tenantId, location, source);
        var binding = new SpaceLocationExternalBinding
        {
            ModelVersionId = location.ModelVersionId,
            LocationLogicalId = location.LogicalId,
            AdapterId = Required(adapterId, 100, nameof(adapterId)),
            WarehouseCode = Required(
                warehouseCode,
                100,
                nameof(warehouseCode)),
            ExternalLocationId = Required(
                externalLocationId,
                200,
                nameof(externalLocationId)),
            BindingMode = ValidateMode(bindingMode),
            SourceId = source.Id,
            SourceRef = Required(sourceRef, 500, nameof(sourceRef)),
        };
        binding.SetTenant(tenantId);
        binding.SetId(id);
        return binding;
    }

    public void UpdateTarget(
        Guid tenantId,
        SpaceLocationRevision location,
        SpaceLocationBindingMode bindingMode,
        SpaceModelSource source,
        string sourceRef)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(source);
        EnsureSameSnapshot(tenantId, location, source);
        if (TenantId != tenantId || ModelVersionId != location.ModelVersionId)
        {
            throw new SpaceTenantScopeException(
                "External binding cannot move across tenants or model versions.");
        }
        LocationLogicalId = location.LogicalId;
        BindingMode = ValidateMode(bindingMode);
        SourceId = source.Id;
        SourceRef = Required(sourceRef, 500, nameof(sourceRef));
    }

    public void ChangeBindingMode(SpaceLocationBindingMode bindingMode) =>
        BindingMode = ValidateMode(bindingMode);

    public void Remove() => MarkEntityDeleted();

    private static void EnsureSameSnapshot(
        Guid tenantId,
        SpaceLocationRevision location,
        SpaceModelSource source)
    {
        if (tenantId == Guid.Empty || location.TenantId != tenantId ||
            source.TenantId != tenantId ||
            location.ModelVersionId != source.ModelVersionId)
        {
            throw new SpaceTenantScopeException(
                "External binding location and source must share one tenant snapshot.");
        }
    }

    private static SpaceLocationBindingMode ValidateMode(
        SpaceLocationBindingMode value) =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));

    private static string Required(
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
}

public sealed class SpaceDesignAttribute : SpaceTenantEntity
{
    private SpaceDesignAttribute()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public string ObjectType { get; private set; } = string.Empty;
    public Guid ObjectLogicalId { get; private set; }
    public string Namespace { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string? Unit { get; private set; }
    public Guid SourceId { get; private set; }
    public string SourceRef { get; private set; } = string.Empty;

    public static SpaceDesignAttribute Create(
        Guid tenantId,
        Guid id,
        Guid modelVersionId,
        string objectType,
        Guid objectLogicalId,
        string attributeNamespace,
        string key,
        string value,
        string? unit,
        SpaceModelSource source,
        string sourceRef)
    {
        if (tenantId == Guid.Empty || modelVersionId == Guid.Empty ||
            objectLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant, model version, and target logical identities are required.");
        }
        ArgumentNullException.ThrowIfNull(source);
        if (source.TenantId != tenantId ||
            source.ModelVersionId != modelVersionId)
        {
            throw new SpaceTenantScopeException(
                "Design attribute source must belong to the target snapshot.");
        }
        var attribute = new SpaceDesignAttribute
        {
            ModelVersionId = modelVersionId,
            ObjectType = SpaceDesignAttributeObjectTypes.Normalize(objectType),
            ObjectLogicalId = objectLogicalId,
            Namespace = SpaceDesignAttributeNamespaces.Normalize(
                attributeNamespace),
            Key = Required(key, 100, nameof(key)),
            Value = Required(value, 4000, nameof(value)),
            Unit = Optional(unit, 50, nameof(unit)),
            SourceId = source.Id,
            SourceRef = Required(sourceRef, 500, nameof(sourceRef)),
        };
        attribute.SetTenant(tenantId);
        attribute.SetId(id);
        return attribute;
    }

    public void UpdateValue(
        string value,
        string? unit,
        SpaceModelSource source,
        string sourceRef)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TenantId != TenantId ||
            source.ModelVersionId != ModelVersionId)
        {
            throw new SpaceTenantScopeException(
                "Design attribute source must belong to the target snapshot.");
        }
        Value = Required(value, 4000, nameof(value));
        Unit = Optional(unit, 50, nameof(unit));
        SourceId = source.Id;
        SourceRef = Required(sourceRef, 500, nameof(sourceRef));
    }

    public void Remove() => MarkEntityDeleted();

    private static string Required(
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

    private static string? Optional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, parameterName);
}

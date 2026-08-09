using System.Text.Json;

namespace CP6.Space.Domain;

public enum SpaceAssetScope : short
{
    System = 0,
    Tenant = 1,
}

public enum SpaceAssetStatus : short
{
    Active = 0,
    Retired = 1,
}

public enum SpaceAssetVersionStatus : short
{
    Ready = 0,
    Retired = 1,
}

public enum SpaceAssetFormat : short
{
    Parametric = 0,
    Glb = 1,
    Gltf = 2,
}

public sealed class SpaceAsset
{
    private SpaceAsset()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public SpaceAssetScope Scope { get; private set; }
    public Guid OwnerTenantId { get; private set; }
    public string AssetCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public SpaceAssetStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceAsset CreateTenant(
        Guid tenantId,
        string assetCode,
        string name,
        string category,
        string? description,
        Guid actorId,
        DateTime nowUtc) =>
        Create(
            SpaceAssetScope.Tenant,
            tenantId,
            assetCode,
            name,
            category,
            description,
            actorId,
            nowUtc);

    public static SpaceAsset CreateSystem(
        string assetCode,
        string name,
        string category,
        string? description,
        Guid actorId,
        DateTime nowUtc) =>
        Create(
            SpaceAssetScope.System,
            Guid.Empty,
            assetCode,
            name,
            category,
            description,
            actorId,
            nowUtc);

    private static SpaceAsset Create(
        SpaceAssetScope scope,
        Guid ownerTenantId,
        string assetCode,
        string name,
        string category,
        string? description,
        Guid actorId,
        DateTime nowUtc)
    {
        SpaceAssetValue.ValidateScope(scope, ownerTenantId);
        SpaceAssetValue.RequireActorAndUtc(actorId, nowUtc);
        return new SpaceAsset
        {
            Scope = scope,
            OwnerTenantId = ownerTenantId,
            AssetCode = SpaceRevisionValue.RequiredText(
                assetCode,
                100,
                nameof(assetCode)),
            Name = SpaceRevisionValue.RequiredText(
                name,
                200,
                nameof(name)),
            Category = SpaceRevisionValue.RequiredText(
                category,
                100,
                nameof(category)),
            Description = SpaceRevisionValue.OptionalText(
                description,
                1000,
                nameof(description)),
            Status = SpaceAssetStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
        };
    }
}

public sealed class SpaceAssetVersion
{
    private SpaceAssetVersion()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public SpaceAssetScope Scope { get; private set; }
    public Guid OwnerTenantId { get; private set; }
    public Guid AssetId { get; private set; }
    public long VersionNo { get; private set; }
    public SpaceAssetFormat Format { get; private set; }
    public string ParameterSchemaJson { get; private set; } = "{}";
    public string? PreviewRef { get; private set; }
    public string? RenderArtifactRef { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public SpaceAssetVersionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceAssetVersion CreateReady(
        SpaceAsset asset,
        long versionNo,
        SpaceAssetFormat format,
        string parameterSchemaJson,
        string? previewRef,
        string? renderArtifactRef,
        string contentHash,
        Guid actorId,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Status != SpaceAssetStatus.Active)
        {
            throw new InvalidOperationException(
                "A version cannot be added to a retired asset.");
        }
        if (versionNo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNo),
                "Asset version numbers start at 1.");
        }
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        SpaceAssetValue.RequireActorAndUtc(actorId, nowUtc);

        return new SpaceAssetVersion
        {
            Scope = asset.Scope,
            OwnerTenantId = asset.OwnerTenantId,
            AssetId = asset.Id,
            VersionNo = versionNo,
            Format = format,
            ParameterSchemaJson = SpaceAssetValue.JsonObject(
                parameterSchemaJson,
                nameof(parameterSchemaJson)),
            PreviewRef = SpaceAssetValue.SafeObjectRef(
                previewRef,
                nameof(previewRef)),
            RenderArtifactRef = SpaceAssetValue.SafeObjectRef(
                renderArtifactRef,
                nameof(renderArtifactRef)),
            ContentHash = SpaceAssetValue.Sha256(
                contentHash,
                nameof(contentHash)),
            Status = SpaceAssetVersionStatus.Ready,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
        };
    }

    public bool IsVisibleTo(Guid tenantId) =>
        Scope == SpaceAssetScope.System ||
        OwnerTenantId == tenantId;
}

internal static class SpaceAssetValue
{
    public static void ValidateScope(
        SpaceAssetScope scope,
        Guid ownerTenantId)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == SpaceAssetScope.System && ownerTenantId != Guid.Empty)
        {
            throw new ArgumentException(
                "System assets use the platform owner identity.",
                nameof(ownerTenantId));
        }
        if (scope == SpaceAssetScope.Tenant && ownerTenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant assets require an owning tenant.",
                nameof(ownerTenantId));
        }
    }

    public static void RequireActorAndUtc(
        Guid actorId,
        DateTime nowUtc)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException(
                "An actor is required.",
                nameof(actorId));
        }
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "UTC time is required.",
                nameof(nowUtc));
        }
    }

    public static string JsonObject(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 262_144)
        {
            throw new ArgumentException(
                "A JSON object up to 256 KiB is required.",
                parameterName);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "The parameter schema must be a JSON object.",
                    parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The parameter schema is invalid JSON.",
                parameterName,
                exception);
        }

        return value;
    }

    public static string? SafeObjectRef(
        string? value,
        string parameterName)
    {
        var normalized = SpaceRevisionValue.OptionalText(
            value,
            500,
            parameterName);
        if (normalized is null)
            return null;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Asset references must be safe internal object keys.",
                parameterName);
        }
        return normalized;
    }

    public static string Sha256(
        string value,
        string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A 64-character SHA-256 hash is required.",
                parameterName);
        }
        return normalized;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Space.Domain;

public enum SpaceRackGenerationProfileScope : short
{
    System = 0,
    Tenant = 1,
}

public enum SpaceRackGenerationProfileStatus : short
{
    Active = 0,
    Retired = 1,
}

public enum SpaceRackGenerationProfileVersionStatus : short
{
    Ready = 0,
    Retired = 1,
}

public sealed record SpaceRackGenerationProfileLevel(
    int LevelNo,
    int BottomZMillimeters,
    int ClearHeightMillimeters,
    int BinCount,
    int DepthCount,
    int CellWidthMillimeters,
    int CellDepthMillimeters,
    int BeamHeightMillimeters = 0,
    decimal? MaxLoadKilograms = null);

public sealed class SpaceRackGenerationProfile
{
    private SpaceRackGenerationProfile()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public SpaceRackGenerationProfileScope Scope { get; private set; }
    public Guid OwnerTenantId { get; private set; }
    public string ProfileCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public SpaceRackGenerationProfileStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceRackGenerationProfile CreateTenant(
        Guid tenantId,
        string profileCode,
        string name,
        string? description,
        Guid actorId,
        DateTime nowUtc) =>
        Create(
            SpaceRackGenerationProfileScope.Tenant,
            tenantId,
            profileCode,
            name,
            description,
            actorId,
            nowUtc);

    public static SpaceRackGenerationProfile CreateSystem(
        string profileCode,
        string name,
        string? description,
        Guid actorId,
        DateTime nowUtc) =>
        Create(
            SpaceRackGenerationProfileScope.System,
            Guid.Empty,
            profileCode,
            name,
            description,
            actorId,
            nowUtc);

    private static SpaceRackGenerationProfile Create(
        SpaceRackGenerationProfileScope scope,
        Guid ownerTenantId,
        string profileCode,
        string name,
        string? description,
        Guid actorId,
        DateTime nowUtc)
    {
        SpaceRackGenerationProfileValue.ValidateScope(scope, ownerTenantId);
        SpaceRackGenerationProfileValue.RequireActorAndUtc(actorId, nowUtc);
        return new SpaceRackGenerationProfile
        {
            Scope = scope,
            OwnerTenantId = ownerTenantId,
            ProfileCode = SpaceRevisionValue.RequiredText(
                profileCode,
                100,
                nameof(profileCode)),
            Name = SpaceRevisionValue.RequiredText(name, 200, nameof(name)),
            Description = SpaceRevisionValue.OptionalText(
                description,
                1000,
                nameof(description)),
            Status = SpaceRackGenerationProfileStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
        };
    }
}

public sealed class SpaceRackGenerationProfileVersion
{
    private const long MaximumDerivedLocations = 10_000_000;
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private SpaceRackGenerationProfileVersion()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public SpaceRackGenerationProfileScope Scope { get; private set; }
    public Guid OwnerTenantId { get; private set; }
    public Guid ProfileId { get; private set; }
    public long VersionNo { get; private set; }
    public int RackWidthMillimeters { get; private set; }
    public int RackDepthMillimeters { get; private set; }
    public int RackHeightMillimeters { get; private set; }
    public string LevelsJson { get; private set; } = "[]";
    public long LocationCount { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public SpaceRackGenerationProfileVersionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceRackGenerationProfileVersion CreateReady(
        SpaceRackGenerationProfile profile,
        long versionNo,
        int rackWidthMillimeters,
        int rackDepthMillimeters,
        int rackHeightMillimeters,
        IReadOnlyList<SpaceRackGenerationProfileLevel> levels,
        Guid actorId,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(levels);
        if (profile.Status != SpaceRackGenerationProfileStatus.Active)
        {
            throw new InvalidOperationException(
                "A version cannot be added to a retired rack generation profile.");
        }
        if (versionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(versionNo));
        SpaceRackGenerationProfileValue.RequireActorAndUtc(actorId, nowUtc);
        var canonicalLevels = ValidateAndNormalize(
            rackWidthMillimeters,
            rackDepthMillimeters,
            rackHeightMillimeters,
            levels,
            out var locationCount);
        var levelsJson = JsonSerializer.Serialize(
            canonicalLevels,
            CanonicalJsonOptions);
        var definitionJson = JsonSerializer.Serialize(
            new
            {
                rackWidthMillimeters,
                rackDepthMillimeters,
                rackHeightMillimeters,
                levels = canonicalLevels,
            },
            CanonicalJsonOptions);

        return new SpaceRackGenerationProfileVersion
        {
            Scope = profile.Scope,
            OwnerTenantId = profile.OwnerTenantId,
            ProfileId = profile.Id,
            VersionNo = versionNo,
            RackWidthMillimeters = rackWidthMillimeters,
            RackDepthMillimeters = rackDepthMillimeters,
            RackHeightMillimeters = rackHeightMillimeters,
            LevelsJson = levelsJson,
            LocationCount = locationCount,
            ContentHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(definitionJson)))
                .ToLowerInvariant(),
            Status = SpaceRackGenerationProfileVersionStatus.Ready,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
        };
    }

    public IReadOnlyList<SpaceRackGenerationProfileLevel> ReadLevels() =>
        JsonSerializer.Deserialize<SpaceRackGenerationProfileLevel[]>(
            LevelsJson,
            CanonicalJsonOptions) ?? throw new InvalidOperationException(
            "The rack generation profile levels are invalid.");

    public bool IsVisibleTo(Guid tenantId) =>
        Scope == SpaceRackGenerationProfileScope.System ||
        OwnerTenantId == tenantId;

    private static SpaceRackGenerationProfileLevel[] ValidateAndNormalize(
        int rackWidthMillimeters,
        int rackDepthMillimeters,
        int rackHeightMillimeters,
        IReadOnlyList<SpaceRackGenerationProfileLevel> levels,
        out long locationCount)
    {
        if (rackWidthMillimeters <= 0 ||
            rackDepthMillimeters <= 0 ||
            rackHeightMillimeters <= 0 ||
            levels.Count is < 1 or > 1_000)
        {
            throw new ArgumentException(
                "Rack dimensions and between 1 and 1000 levels are required.",
                nameof(levels));
        }

        var levelNumbers = new HashSet<int>();
        locationCount = 0;
        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            if (level.LevelNo <= 0 ||
                !levelNumbers.Add(level.LevelNo) ||
                level.BottomZMillimeters < 0 ||
                level.ClearHeightMillimeters <= 0 ||
                level.BinCount <= 0 ||
                level.DepthCount <= 0 ||
                level.CellWidthMillimeters <= 0 ||
                level.CellDepthMillimeters <= 0 ||
                level.BeamHeightMillimeters < 0 ||
                level.MaxLoadKilograms < 0 ||
                (long)level.BottomZMillimeters +
                    level.ClearHeightMillimeters +
                    level.BeamHeightMillimeters > rackHeightMillimeters ||
                (long)level.BinCount * level.CellWidthMillimeters >
                    rackWidthMillimeters ||
                (long)level.DepthCount * level.CellDepthMillimeters >
                    rackDepthMillimeters)
            {
                throw new ArgumentException(
                    "A rack generation profile level is invalid.",
                    nameof(levels));
            }
            var levelLocationCount =
                (long)level.BinCount * level.DepthCount;
            if (levelLocationCount > MaximumDerivedLocations - locationCount)
            {
                throw new ArgumentException(
                    "The rack generation profile exceeds the derived location limit.",
                    nameof(levels));
            }
            locationCount += levelLocationCount;
        }
        return levels.OrderBy(level => level.LevelNo).ToArray();
    }
}

internal static class SpaceRackGenerationProfileValue
{
    public static void ValidateScope(
        SpaceRackGenerationProfileScope scope,
        Guid ownerTenantId)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == SpaceRackGenerationProfileScope.System &&
            ownerTenantId != Guid.Empty)
        {
            throw new ArgumentException(
                "System profiles use the platform owner identity.",
                nameof(ownerTenantId));
        }
        if (scope == SpaceRackGenerationProfileScope.Tenant &&
            ownerTenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant profiles require an owning tenant.",
                nameof(ownerTenantId));
        }
    }

    public static void RequireActorAndUtc(Guid actorId, DateTime nowUtc)
    {
        if (actorId == Guid.Empty)
            throw new ArgumentException("An actor is required.", nameof(actorId));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC time is required.", nameof(nowUtc));
    }
}

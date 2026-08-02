namespace CP6.Space.Domain;

public sealed class SpaceExcelMappingProfile : SpaceTenantEntity
{
    private SpaceExcelMappingProfile()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public int CurrentVersion { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceExcelMappingProfile Create(Guid tenantId, string name)
    {
        var profile = new SpaceExcelMappingProfile();
        profile.SetTenant(tenantId);
        profile.SetName(name);
        return profile;
    }

    public void Advance(string name, int version)
    {
        if (version != checked(CurrentVersion + 1))
        {
            throw new InvalidOperationException(
                "Mapping profile versions must advance by exactly one.");
        }
        SetName(name);
        CurrentVersion = version;
    }

    private void SetName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200)
        {
            throw new ArgumentException(
                "Mapping profile name must contain 1 to 200 characters.",
                nameof(name));
        }
        Name = normalized;
        NormalizedName = normalized.ToUpperInvariant();
    }
}

public sealed class SpaceExcelMappingProfileVersion : SpaceTenantEntity
{
    private SpaceExcelMappingProfileVersion()
    {
    }

    public Guid ProfileId { get; private set; }
    public int Version { get; private set; }
    public string DefinitionJson { get; private set; } = string.Empty;
    public string DefinitionHash { get; private set; } = string.Empty;
    public Guid? BasedOnProfileId { get; private set; }
    public int? BasedOnVersion { get; private set; }

    public static SpaceExcelMappingProfileVersion Create(
        Guid tenantId,
        Guid profileId,
        int version,
        string definitionJson,
        string definitionHash,
        Guid? basedOnProfileId,
        int? basedOnVersion)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("Profile is required.", nameof(profileId));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(definitionJson))
            throw new ArgumentException("Definition JSON is required.", nameof(definitionJson));
        var hash = definitionHash?.Trim().ToLowerInvariant() ?? string.Empty;
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Definition hash must be SHA-256.", nameof(definitionHash));
        if (basedOnProfileId == Guid.Empty)
            throw new ArgumentException("Base profile cannot be empty.", nameof(basedOnProfileId));
        if (basedOnProfileId.HasValue != basedOnVersion.HasValue ||
            basedOnVersion.HasValue && basedOnVersion.Value <= 0)
        {
            throw new ArgumentException(
                "Base profile identity and version must be supplied together.");
        }

        var item = new SpaceExcelMappingProfileVersion
        {
            ProfileId = profileId,
            Version = version,
            DefinitionJson = definitionJson,
            DefinitionHash = hash,
            BasedOnProfileId = basedOnProfileId,
            BasedOnVersion = basedOnVersion,
        };
        item.SetTenant(tenantId);
        return item;
    }
}

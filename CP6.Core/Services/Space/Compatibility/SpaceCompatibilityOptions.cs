namespace CP6.Core.Services.Space.Compatibility;

/// <summary>
/// E00 compatibility control plane. This is intentionally configuration-backed:
/// E01 may replace the resolver with SpaceContext without changing write guards.
/// </summary>
public sealed class SpaceCompatibilityOptions
{
    public const string SectionName = "Space:Compatibility";

    public bool DesignApiEnabled { get; set; }

    public List<SpaceSiteCompatibilityOptions> Sites { get; set; } = [];
}

public sealed class SpaceSiteCompatibilityOptions
{
    public Guid TenantId { get; set; }

    public Guid SiteId { get; set; }

    public SpaceSiteMode Mode { get; set; } = SpaceSiteMode.Legacy;

    public SpaceCutoverState CutoverState { get; set; } = SpaceCutoverState.LegacyOpen;

    public SpaceCutoverEvidence Evidence { get; set; } = new();
}

public sealed class SpaceCutoverEvidence
{
    public bool BootstrapVerified { get; set; }

    public bool RuntimeHashVerified { get; set; }

    public bool WmsIdentityVerified { get; set; }

    public bool ReopenApproved { get; set; }

    public bool DesignWritesAccepted { get; set; }

    public bool IsVerified =>
        BootstrapVerified &&
        RuntimeHashVerified &&
        WmsIdentityVerified;
}

public enum SpaceSiteMode
{
    Legacy = 0,
    DesignV1 = 1,
}

public enum SpaceCutoverState
{
    LegacyOpen = 0,
    FreezeRequested = 1,
    Frozen = 2,
    Bootstrapping = 3,
    Verified = 4,
    DesignV1 = 5,
    FailedFrozen = 6,
}

public sealed record SpaceCompatibilityStatus(
    Guid TenantId,
    Guid SiteId,
    SpaceSiteMode Mode,
    SpaceCutoverState CutoverState,
    SpaceCutoverEvidence Evidence);

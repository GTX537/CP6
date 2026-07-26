namespace CP6.Space.Contracts;

/// <summary>
/// Stable Design API error codes frozen by the Space MVP baseline.
/// HTTP Problem Details mapping is introduced with E01-S05.
/// </summary>
public static class SpaceErrorCodes
{
    public const string TenantScopeDenied = "SPACE_TENANT_SCOPE_DENIED";
    public const string VersionConflict = "SPACE_VERSION_CONFLICT";
    public const string VersionStateInvalid = "SPACE_VERSION_STATE_INVALID";
}

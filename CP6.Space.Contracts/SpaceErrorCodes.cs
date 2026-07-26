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
    public const string FileTooLarge = "SPACE_FILE_TOO_LARGE";
    public const string FileTypeMismatch = "SPACE_FILE_TYPE_MISMATCH";
    public const string FileQuarantined = "SPACE_FILE_QUARANTINED";
    public const string SourceUnsafe = "SPACE_SOURCE_UNSAFE";
    public const string JobLeaseLost = "SPACE_JOB_LEASE_LOST";
    public const string JobNotRetryable = "SPACE_JOB_NOT_RETRYABLE";
    public const string ParseFailed = "SPACE_PARSE_FAILED";
}

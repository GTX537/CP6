namespace CP6.Space.Contracts;

/// <summary>
/// Stable Design API error codes frozen by the Space MVP baseline.
/// HTTP Problem Details mapping is introduced with E01-S05.
/// </summary>
public static class SpaceErrorCodes
{
    public const string AuthenticationRequired = "SPACE_AUTHENTICATION_REQUIRED";
    public const string TenantScopeDenied = "SPACE_TENANT_SCOPE_DENIED";
    public const string ExternalSubjectDenied = "SPACE_EXTERNAL_SUBJECT_DENIED";
    public const string PermissionDenied = "SPACE_PERMISSION_DENIED";
    public const string DesignApiDisabled = "SPACE_DESIGN_API_DISABLED";
    public const string ModelNotFound = "SPACE_MODEL_NOT_FOUND";
    public const string VersionNotFound = "SPACE_VERSION_NOT_FOUND";
    public const string SourceNotFound = "SPACE_SOURCE_NOT_FOUND";
    public const string JobNotFound = "SPACE_JOB_NOT_FOUND";
    public const string IssueNotFound = "SPACE_ISSUE_NOT_FOUND";
    public const string VersionConflict = "SPACE_VERSION_CONFLICT";
    public const string VersionStateInvalid = "SPACE_VERSION_STATE_INVALID";
    public const string SourceConflict = "SPACE_SOURCE_CONFLICT";
    public const string IdempotencyConflict = "SPACE_IDEMPOTENCY_KEY_REUSED";
    public const string IdempotencyKeyRequired = "SPACE_IDEMPOTENCY_KEY_REQUIRED";
    public const string CursorInvalid = "SPACE_CURSOR_INVALID";
    public const string CursorScopeMismatch = "SPACE_CURSOR_SCOPE_MISMATCH";
    public const string RequestInvalid = "SPACE_REQUEST_INVALID";
    public const string ConcurrencyConflict = "SPACE_CONCURRENCY_CONFLICT";
    public const string FileTooLarge = "SPACE_FILE_TOO_LARGE";
    public const string FileTypeMismatch = "SPACE_FILE_TYPE_MISMATCH";
    public const string FileQuarantined = "SPACE_FILE_QUARANTINED";
    public const string FileMalwareDetected = "SPACE_FILE_MALWARE_DETECTED";
    public const string FileArchiveBomb = "SPACE_FILE_ARCHIVE_BOMB";
    public const string FileEncryptedUnsupported =
        "SPACE_FILE_ENCRYPTED_UNSUPPORTED";
    public const string FileActiveContent = "SPACE_FILE_ACTIVE_CONTENT";
    public const string FileCorrupt = "SPACE_FILE_CORRUPT";
    public const string SourceUnsafe = "SPACE_SOURCE_UNSAFE";
    public const string JobLeaseLost = "SPACE_JOB_LEASE_LOST";
    public const string JobNotRetryable = "SPACE_JOB_NOT_RETRYABLE";
    public const string JobProcessorUnavailable =
        "SPACE_JOB_PROCESSOR_UNAVAILABLE";
    public const string JobProcessorFailed =
        "SPACE_JOB_PROCESSOR_FAILED";
    public const string JobTimeout = "SPACE_JOB_TIMEOUT";
    public const string ParseFailed = "SPACE_PARSE_FAILED";
    public const string AiDisabled = "SPACE_AI_DISABLED";
    public const string AiQuotaExceeded = "SPACE_AI_QUOTA_EXCEEDED";
    public const string AiProviderUnavailable =
        "SPACE_AI_PROVIDER_UNAVAILABLE";
    public const string AiSourcePolicyDenied =
        "SPACE_AI_SOURCE_POLICY_DENIED";
}

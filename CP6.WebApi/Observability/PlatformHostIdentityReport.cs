namespace CP6.WebApi.Observability;

// These values describe a comparison with operator input. They are not package provenance,
// signature validation, runtime qualification, or PostgreSQL compatibility evidence.
public sealed record PlatformHostIdentityValue(string PackageVersion, string SourceSha, string AssemblySha256);
public sealed record PlatformHostObservedIdentity(string? AssemblyVersion, string? InformationalVersion, string? DeclaredPackageVersion, string? SourceSha,
    string? AssemblySha256, Guid? LoadedMvid, Guid? BackingMvid, bool DefaultLoadContext);
public sealed record PlatformHostModuleIdentity(string PackageId, string Status, PlatformHostIdentityValue? Expected,
    PlatformHostObservedIdentity? Observed, IReadOnlyList<string> ReasonCodes);
public sealed record PlatformHostRollbackDecision(bool Allowed, string Status, string ReasonCode);
public sealed record PlatformHostIdentityReport(int ProcessId, DateTimeOffset ProcessStartedAtUtc, DateTimeOffset CapturedAtUtc,
    string IdentityStatus, IReadOnlyList<PlatformHostModuleIdentity> Modules, IReadOnlyList<string> ReasonCodes,
    PlatformHostRollbackDecision Rollback)
{
    public string RuntimeQualification => "Unknown";
    public string BusinessAcceptance => "Unknown";
    public string TrustState => "Unknown";
}

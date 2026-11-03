namespace CP6.Core.Services.Wf;

/// <summary>Explicit read contract for historical rows; a stored legacy status is not publication evidence.</summary>
public sealed record LegacyDefinitionViewV2(string Kind, Guid DefinitionId, string Key, Guid? VersionId,
    int RecordedVersion, int? RecordedStatus, string Source, Dictionary<string, string?> Fields,
    DateTime? RecordedPublishedAtUtc, Guid? RecordedPublishedBy,
    string EvidenceState = "UNVERIFIED_LEGACY", bool CanWrite = false)
{
    internal static bool Reconstructed(string? creator) => creator is "oa-p0-backfill" or "legacy-runtime-backfill";
}

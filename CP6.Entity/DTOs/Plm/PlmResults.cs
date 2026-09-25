using System.Text.Json;

namespace CP6.Entity.DTOs.Plm;

public sealed record PlmOperation<T>(T Value, bool Created);
public sealed record IterationCandidateResult(Guid IterationCandidateId, string IterationKey, int IterationVersion, string? ReviewPackageDigest, string Status);
public sealed record EngineeringCandidateResult(Guid CandidateId, Guid IterationCandidateId, string ProductCd, int CandidateVersion, string Status, byte[] RowVersion);
public sealed record TechnicalManifestResult(Guid ManifestId, Guid CandidateId, int CandidateVersion, string ProductCd, string SchemaVersion, string ManifestDigest, int ItemCount, byte[] CanonicalBytes, byte[] CandidateRowVersion);
public sealed record TechnicalManifestItemResult(string ItemType, string SourceOwnerType, string IdentityKey, JsonElement Identity, JsonElement Content, string ItemDigest, byte[] ItemCanonicalBytes, Guid SourceOwnerId, byte[]? SourceRowVersion);
public sealed record TechnicalManifestDetailResult(TechnicalManifestResult Manifest, IReadOnlyList<TechnicalManifestItemResult> Items);
public sealed record EngineeringBaselineResult(Guid BaselineId, Guid CandidateId, int CandidateVersion, Guid ManifestId, string ManifestDigest, string Status, DateTime FrozenAtUtc, DateTime? SupersededAtUtc, byte[] RowVersion);

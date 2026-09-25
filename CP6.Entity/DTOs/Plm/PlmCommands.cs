namespace CP6.Entity.DTOs.Plm;

public sealed record CreateIterationCandidateCommand(string IterationKey, int IterationVersion, string? ReviewPackageDigest);
public sealed record CreateEngineeringCandidateCommand(Guid IterationCandidateId, string CandidateKey, string ProductCd);
public sealed record CaptureTechnicalManifestCommand(Guid CaptureRequestId, int ExpectedCandidateVersion, byte[] ExpectedCandidateRowVersion);
public sealed record ValidateTechnicalManifestCommand(int ExpectedCandidateVersion, byte[] ExpectedCandidateRowVersion, string ExpectedManifestDigest);
public sealed record FreezeEngineeringBaselineCommand(Guid FreezeRequestId, int ExpectedCandidateVersion, byte[] ExpectedCandidateRowVersion, Guid ManifestId, string ExpectedManifestDigest);
public sealed record SupersedeEngineeringBaselineCommand(Guid SupersedeRequestId, byte[] ExpectedBaselineRowVersion, string Reason);

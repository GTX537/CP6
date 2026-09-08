using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public sealed record InspectionResult(string SchemaId, string Sha256, string? CandidateKind,
    bool? Deployable, bool CandidateAccepted = false);

public static class ContractInspection
{
    public static InspectionResult Inspect(byte[] bytes, string schemaId, string expectedSha256)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw new Cp6ReleaseContractException("input-size", "Input size is outside policy.");
        var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        if (!string.Equals(hash, expectedSha256, StringComparison.Ordinal))
            throw new Cp6ReleaseContractException("input-hash", "Input hash differs from the trusted expectation.");
        var result = schemaId switch
        {
            Cp6ReleaseContractIds.PlatformCandidate => Cp6ReleaseValidator.ValidatePlatformCandidate(bytes),
            Cp6ReleaseContractIds.CandidateLocator => ValidatePlatformLocator(bytes),
            Cp6ReleaseContractIds.ReleaseGateResult => Cp6SupportingContractValidator.ValidateReleaseGateResult(bytes),
            Cp6ReleaseContractIds.EvidenceRecord => Cp6SupportingContractValidator.ValidateEvidenceRecord(bytes),
            Cp6ReleaseContractIds.BuildProvenance => Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(bytes),
            Cp6ReleaseContractIds.PinnedTrustStore => Cp6PinnedTrustPolicy.Parse(bytes).ValidatedDocument,
            _ => throw new Cp6ReleaseContractException("inspection-schema", "Contract is not supported by this inspection command.")
        };
        return new(result.SchemaId, result.Sha256, result.CandidateKind, result.Deployable);
    }

    private static Cp6ValidatedReleaseDocument ValidatePlatformLocator(byte[] bytes)
    {
        var result = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        if (result.SubjectKind != "PlatformReleaseCandidate")
            throw new Cp6ReleaseContractException("inspection-lane", "Only the non-deployable Platform lane is supported.");
        return result;
    }

    public static byte[] ReadBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[Cp6DeterministicJson.MaximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = input.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw new Cp6ReleaseContractException("input-size", "Input size is outside policy.");
        return buffer.AsSpan(0, count).ToArray();
    }
}

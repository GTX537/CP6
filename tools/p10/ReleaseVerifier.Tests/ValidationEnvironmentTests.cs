using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.Tests.EvidenceGraphFixture;

namespace CP6.P10.ReleaseVerifier.Tests;

// Unpublished graph regression only; this is not proof of an approved workflow run.
public sealed class ValidationEnvironmentTests
{
    [Fact]
    public void Validation_declares_the_existing_protected_P10_environment()
    {
        var input = Build(candidateChange: root => root["verifier"]!["environment"] = "p10-platform-candidate");
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        Assert.Equal("p10-platform-candidate",
            result.Candidate.GetProperty("verifier").GetProperty("environment").GetString());
        Assert.NotEqual(result.Candidate.GetProperty("verifier").GetProperty("runId").GetInt64(),
            result.Candidate.GetProperty("publisher").GetProperty("runId").GetInt64());
        Assert.Equal("none", result.Candidate.GetProperty("crmConsumer").GetProperty("environment").GetString());
        Assert.False(result.CandidateAccepted);
    }

    [Fact]
    public void Validation_cannot_claim_an_unprotected_environment()
    {
        var input = Build(candidateChange: root => root["verifier"]!["environment"] = "none");
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            EvidenceGraphInspection.Inspect(input.Candidate, input.Objects));
        Assert.Equal("graph-workflow-identity", failure.Code);
    }
}

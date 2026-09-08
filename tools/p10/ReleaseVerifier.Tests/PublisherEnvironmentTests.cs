using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.Tests.EvidenceGraphFixture;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class PublisherEnvironmentTests
{
    [Fact]
    public void Approved_P10_environment_is_the_only_publisher_identity()
    {
        var input = Build(candidateChange: root => root["publisher"]!["environment"] = "p10-platform-candidate");
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        Assert.Equal("p10-platform-candidate", result.Candidate.GetProperty("publisher").GetProperty("environment").GetString());
        Assert.False(result.CandidateAccepted);
    }

    [Fact]
    public void Historical_R2_environment_cannot_substitute_for_the_P10_publisher()
    {
        var input = Build(candidateChange: root => root["publisher"]!["environment"] = "r2-candidate");
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            EvidenceGraphInspection.Inspect(input.Candidate, input.Objects));
        Assert.Equal("graph-workflow-identity", failure.Code);
    }
}

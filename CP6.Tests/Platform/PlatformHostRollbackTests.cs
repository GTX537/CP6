using CP6.WebApi.Observability;

namespace CP6.Tests.Platform;

public class PlatformHostRollbackTests
{
    [Fact]
    public void Missing_loaded_identity_never_authorizes_rollback()
    {
        var report = PlatformHostIdentityTests.Observer(PlatformHostIdentityTests.Config(), []).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.False(report.Rollback.Allowed);
        Assert.Equal("COMPATIBILITY_EVIDENCE_MISSING", report.Rollback.ReasonCode);
    }

    [Fact]
    public void Matching_host_identity_cannot_authorize_rollback_without_compatibility_evidence()
    {
        var report = PlatformHostIdentityTests.Observer(PlatformHostIdentityTests.Config(), PlatformHostIdentityTests.Facts()).Capture();
        Assert.False(report.Rollback.Allowed);
        Assert.Equal("COMPATIBILITY_EVIDENCE_MISSING", report.Rollback.ReasonCode);
        Assert.Equal("Unknown", report.Rollback.Status);
    }

    [Fact]
    public void Mismatched_host_identity_refuses_rollback()
    {
        var facts = PlatformHostIdentityTests.Facts();
        facts[0] = facts[0] with { DefaultLoadContext = false };
        var report = PlatformHostIdentityTests.Observer(PlatformHostIdentityTests.Config(), facts).Capture();
        Assert.False(report.Rollback.Allowed);
        Assert.Equal("IDENTITY_MISMATCH", report.Rollback.ReasonCode);
        Assert.Equal("Mismatch", report.Rollback.Status);
    }
}

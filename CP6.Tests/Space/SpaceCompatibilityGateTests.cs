using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Compatibility;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Space;

public class SpaceCompatibilityGateTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SiteId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void MissingSiteConfiguration_DefaultsToLegacyOpen()
    {
        var gate = Gate(new SpaceCompatibilityOptions());

        var status = gate.GetStatus(TenantId, SiteId);

        Assert.Equal(SpaceSiteMode.Legacy, status.Mode);
        Assert.Equal(SpaceCutoverState.LegacyOpen, status.CutoverState);
        gate.EnsureLegacyWriteAllowed(TenantId, SiteId);
    }

    [Fact]
    public void DesignV1_AllowsDesignWritesAndBlocksLegacyWrites()
    {
        var gate = Gate(DesignOptions());

        gate.EnsureDesignWriteAllowed(TenantId, SiteId);
        var error = Assert.Throws<BizException>(
            () => gate.EnsureLegacyWriteAllowed(TenantId, SiteId));

        Assert.Equal(SpaceCompatibilityErrors.LegacyWriteDisabled, error.Code);
        Assert.Equal(409, error.HttpStatus);
    }

    [Fact]
    public void FrozenSite_BlocksWritesWithStableVersionError()
    {
        var gate = Gate(new SpaceCompatibilityOptions
        {
            Sites =
            [
                new SpaceSiteCompatibilityOptions
                {
                    TenantId = TenantId,
                    SiteId = SiteId,
                    CutoverState = SpaceCutoverState.Frozen,
                },
            ],
        });

        var error = Assert.Throws<BizException>(
            () => gate.EnsureLegacyWriteAllowed(TenantId, SiteId));

        Assert.Equal(SpaceCompatibilityErrors.VersionStateInvalid, error.Code);
        Assert.Equal(409, error.HttpStatus);
    }

    [Fact]
    public void CrossTenantLookup_IsDenied()
    {
        var gate = Gate(new SpaceCompatibilityOptions());

        var error = Assert.Throws<BizException>(
            () => gate.GetStatus(Guid.NewGuid(), SiteId));

        Assert.Equal(SpaceCompatibilityErrors.TenantScopeDenied, error.Code);
        Assert.Equal(403, error.HttpStatus);
    }

    [Fact]
    public void TransitionToVerified_RequiresAllEvidence()
    {
        var error = Assert.Throws<BizException>(() =>
            SpaceCutoverPolicy.EnsureTransitionAllowed(
                SpaceCutoverState.Bootstrapping,
                SpaceCutoverState.Verified,
                new SpaceCutoverEvidence { BootstrapVerified = true },
                designApiEnabled: true));

        Assert.Equal(SpaceCompatibilityErrors.VersionStateInvalid, error.Code);
    }

    [Fact]
    public void FailedCutover_CanReopenOnlyBeforeDesignWrites()
    {
        var approved = new SpaceCutoverEvidence { ReopenApproved = true };
        SpaceCutoverPolicy.EnsureTransitionAllowed(
            SpaceCutoverState.FailedFrozen,
            SpaceCutoverState.LegacyOpen,
            approved,
            designApiEnabled: false);

        approved.DesignWritesAccepted = true;
        var error = Assert.Throws<BizException>(() =>
            SpaceCutoverPolicy.EnsureTransitionAllowed(
                SpaceCutoverState.FailedFrozen,
                SpaceCutoverState.LegacyOpen,
                approved,
                designApiEnabled: false));

        Assert.Equal(SpaceCompatibilityErrors.VersionStateInvalid, error.Code);
    }

    [Fact]
    public void OptionsValidator_RejectsDuplicateAndInconsistentSites()
    {
        var options = new SpaceCompatibilityOptions
        {
            Sites =
            [
                new SpaceSiteCompatibilityOptions
                {
                    TenantId = TenantId,
                    SiteId = SiteId,
                    Mode = SpaceSiteMode.DesignV1,
                    CutoverState = SpaceCutoverState.LegacyOpen,
                },
                new SpaceSiteCompatibilityOptions
                {
                    TenantId = TenantId,
                    SiteId = SiteId,
                },
            ],
        };

        var result = new SpaceCompatibilityOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, x => x.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(result.Failures, x => x.Contains("requires DesignV1 cutover state", StringComparison.Ordinal));
    }

    private static SpaceCompatibilityGate Gate(SpaceCompatibilityOptions options)
    {
        var tenant = new TenantContext { CurrentTenantId = TenantId };
        return new SpaceCompatibilityGate(tenant, Options.Create(options));
    }

    internal static SpaceCompatibilityOptions DesignOptions() => new()
    {
        DesignApiEnabled = true,
        Sites =
        [
            new SpaceSiteCompatibilityOptions
            {
                TenantId = TenantId,
                SiteId = SiteId,
                Mode = SpaceSiteMode.DesignV1,
                CutoverState = SpaceCutoverState.DesignV1,
                Evidence = new SpaceCutoverEvidence
                {
                    BootstrapVerified = true,
                    RuntimeHashVerified = true,
                    WmsIdentityVerified = true,
                    DesignWritesAccepted = true,
                },
            },
        ],
    };
}

using CP6.Core.Services.Common;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Space.Compatibility;

public interface ISpaceCompatibilityGate
{
    SpaceCompatibilityStatus GetStatus(Guid tenantId, Guid siteId);

    void EnsureLegacyWriteAllowed(Guid tenantId, Guid siteId);

    void EnsureLegacyTenantWideWriteAllowed(Guid tenantId);

    void EnsureDesignWriteAllowed(Guid tenantId, Guid siteId);
}

public sealed class SpaceCompatibilityGate : ISpaceCompatibilityGate
{
    private readonly ITenantContext _tenant;
    private readonly SpaceCompatibilityOptions _options;

    public SpaceCompatibilityGate(
        ITenantContext tenant,
        IOptions<SpaceCompatibilityOptions> options)
    {
        _tenant = tenant;
        _options = options.Value;
    }

    public SpaceCompatibilityStatus GetStatus(Guid tenantId, Guid siteId)
    {
        EnsureTenantScope(tenantId);

        var configured = _options.Sites.SingleOrDefault(
            x => x.TenantId == tenantId && x.SiteId == siteId);

        return configured is null
            ? new SpaceCompatibilityStatus(
                tenantId,
                siteId,
                SpaceSiteMode.Legacy,
                SpaceCutoverState.LegacyOpen,
                new SpaceCutoverEvidence())
            : new SpaceCompatibilityStatus(
                tenantId,
                siteId,
                configured.Mode,
                configured.CutoverState,
                configured.Evidence);
    }

    public void EnsureLegacyWriteAllowed(Guid tenantId, Guid siteId)
    {
        var status = GetStatus(tenantId, siteId);
        if (status.Mode == SpaceSiteMode.DesignV1 ||
            status.CutoverState == SpaceCutoverState.DesignV1)
            throw new BizException(SpaceCompatibilityErrors.LegacyWriteDisabled, 409);

        if (status.CutoverState != SpaceCutoverState.LegacyOpen)
            throw new BizException(SpaceCompatibilityErrors.VersionStateInvalid, 409);
    }

    public void EnsureLegacyTenantWideWriteAllowed(Guid tenantId)
    {
        EnsureTenantScope(tenantId);
        var tenantSites = _options.Sites.Where(x => x.TenantId == tenantId).ToList();

        if (tenantSites.Any(x =>
                x.Mode == SpaceSiteMode.DesignV1 ||
                x.CutoverState == SpaceCutoverState.DesignV1))
            throw new BizException(SpaceCompatibilityErrors.LegacyWriteDisabled, 409);

        if (tenantSites.Any(x => x.CutoverState != SpaceCutoverState.LegacyOpen))
            throw new BizException(SpaceCompatibilityErrors.VersionStateInvalid, 409);
    }

    public void EnsureDesignWriteAllowed(Guid tenantId, Guid siteId)
    {
        var status = GetStatus(tenantId, siteId);
        if (!_options.DesignApiEnabled ||
            status.Mode != SpaceSiteMode.DesignV1 ||
            status.CutoverState != SpaceCutoverState.DesignV1 ||
            !status.Evidence.IsVerified)
            throw new BizException(SpaceCompatibilityErrors.VersionStateInvalid, 409);
    }

    private void EnsureTenantScope(Guid tenantId)
    {
        if (tenantId != _tenant.CurrentTenantId)
            throw new BizException(SpaceCompatibilityErrors.TenantScopeDenied, 403);
    }
}

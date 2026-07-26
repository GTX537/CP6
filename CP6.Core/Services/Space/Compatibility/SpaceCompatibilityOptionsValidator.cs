using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Space.Compatibility;

public sealed class SpaceCompatibilityOptionsValidator
    : IValidateOptions<SpaceCompatibilityOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SpaceCompatibilityOptions options)
    {
        var errors = new List<string>();
        var duplicateKeys = options.Sites
            .GroupBy(x => (x.TenantId, x.SiteId))
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);

        foreach (var key in duplicateKeys)
            errors.Add($"Duplicate Space compatibility entry for tenant {key.TenantId} and site {key.SiteId}.");

        foreach (var site in options.Sites)
        {
            if (site.TenantId == Guid.Empty)
                errors.Add("Space compatibility TenantId must not be empty.");
            if (site.SiteId == Guid.Empty)
                errors.Add("Space compatibility SiteId must not be empty.");
            if (site.Mode == SpaceSiteMode.DesignV1 &&
                site.CutoverState != SpaceCutoverState.DesignV1)
                errors.Add($"Site {site.SiteId}: DesignV1 mode requires DesignV1 cutover state.");
            if (site.CutoverState == SpaceCutoverState.DesignV1 &&
                site.Mode != SpaceSiteMode.DesignV1)
                errors.Add($"Site {site.SiteId}: DesignV1 cutover state requires DesignV1 mode.");
            if (site.CutoverState is SpaceCutoverState.Verified or SpaceCutoverState.DesignV1 &&
                !site.Evidence.IsVerified)
                errors.Add($"Site {site.SiteId}: verified cutover evidence is incomplete.");
            if (site.CutoverState == SpaceCutoverState.DesignV1 &&
                !options.DesignApiEnabled)
                errors.Add($"Site {site.SiteId}: DesignV1 requires DesignApiEnabled.");
            if (site.Evidence.DesignWritesAccepted &&
                site.CutoverState != SpaceCutoverState.DesignV1)
                errors.Add($"Site {site.SiteId}: DesignWritesAccepted is only valid in DesignV1.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

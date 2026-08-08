using CP6.WebApi.Localization;

namespace CP6.Core.Services.Space.Compatibility;

public static class SpaceCutoverPolicy
{
    public static void EnsureTransitionAllowed(
        SpaceCutoverState current,
        SpaceCutoverState target,
        SpaceCutoverEvidence evidence,
        bool designApiEnabled)
    {
        if (current == target)
            return;

        var allowed = (current, target) switch
        {
            (SpaceCutoverState.LegacyOpen, SpaceCutoverState.FreezeRequested) => true,
            (SpaceCutoverState.FreezeRequested, SpaceCutoverState.Frozen) => true,
            (SpaceCutoverState.Frozen, SpaceCutoverState.Bootstrapping) => true,
            (SpaceCutoverState.Bootstrapping, SpaceCutoverState.Verified) => evidence.IsVerified,
            (SpaceCutoverState.Verified, SpaceCutoverState.DesignV1) =>
                designApiEnabled && evidence.IsVerified,
            (SpaceCutoverState.FreezeRequested, SpaceCutoverState.FailedFrozen) => true,
            (SpaceCutoverState.Frozen, SpaceCutoverState.FailedFrozen) => true,
            (SpaceCutoverState.Bootstrapping, SpaceCutoverState.FailedFrozen) => true,
            (SpaceCutoverState.Verified, SpaceCutoverState.FailedFrozen) => true,
            (SpaceCutoverState.FailedFrozen, SpaceCutoverState.LegacyOpen) =>
                evidence.ReopenApproved && !evidence.DesignWritesAccepted,
            _ => false,
        };

        if (!allowed)
            throw new BizException(SpaceCompatibilityErrors.VersionStateInvalid, 409);
    }
}

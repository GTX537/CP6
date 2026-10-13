using CP6.Entity.Contracts.ErpCredit;

namespace Cp6.ErpCredit.CurrentV1.Generated;

public sealed class CurrentCreditCodec(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files) : CreditCodecSet(true, files)
{
    /// <summary>Explicit adoption assessment. Trusted Owner/current AR/GL/member/finite and Store/issuer ports are not wired by this task.</summary>
    public static AdaptationAssessment AssessAdaptation(NativeCreditDocument original)
    {
        ArgumentNullException.ThrowIfNull(original);
        // Even a valid FOUND/no-effect wire or a self-asserted claim cannot supply the absent producer proof.
        return new("READ_ONLY_UNPROVEN", "MANUAL_RECONCILIATION", "UNKNOWN", false, false, false, original);
    }
}

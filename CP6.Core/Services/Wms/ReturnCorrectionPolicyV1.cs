using System.Text.Json;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;

namespace CP6.Core.Services.Wms;

// Explicit external approval/error-fact simulator. It binds the exact actual candidate;
// callers cannot supply an approved flag or a fabricated Stock success receipt.
public sealed class ReturnCorrectionApprovalSourceV1
{
    private readonly ReturnCorrectionSimulationCaseV1[] cases;
    public ReturnCorrectionApprovalSourceV1(IOptions<ReturnCorrectionSimulationOptionsV1> options)
    {
        cases =options.Value.Enabled ? JsonSerializer.Deserialize<ReturnCorrectionSimulationCaseV1[]>(options.Value.CasesJson,RmaFormalStoreV1.Json)
            ?? throw new InvalidOperationException("CORRECTION_CASES_REQUIRED") : [];
        foreach (var c in cases)
        {
            if (c.TenantId ==Guid.Empty || c.SiteId ==Guid.Empty || c.IndependentApproverId ==Guid.Empty
                || string.IsNullOrWhiteSpace(c.Version) || c.Version.Length >64 || string.IsNullOrWhiteSpace(c.Label) || c.Label.Length >200
                || c.ValidFrom >=c.ValidTo || !c.RecordedPhysicalEffectDidNotOccur || c.ErrorEvidenceRefs.Length is <1 or >100)
                throw new InvalidOperationException("CORRECTION_CASE_INVALID");
            Reference(c.OriginalEffectRef); foreach (var e in c.ErrorEvidenceRefs) Reference(e);
        }
        if (cases.Select(c => (c.TenantId,c.OriginalEffectRef)).Distinct().Count() !=cases.Length) throw new InvalidOperationException("CORRECTION_CASE_DUPLICATE");
    }
    internal ReturnCorrectionPolicyV1? Find(Guid tenant,ReturnCorrectionTargetV1 target)
    {
        var c =cases.SingleOrDefault(c => c.TenantId ==tenant && c.SiteId ==target.Source.SiteId && c.OriginalEffectRef ==target.Reference);
        return c ==null ? null : new(new("Simulation.ReturnCorrection.Policy.v1",Digest(new { c.TenantId,c.OriginalEffectRef }),c.Version,Digest(c)),
            c.Label,c.ErrorEvidenceRefs,c.IndependentApproverId,c.ValidFrom,c.ValidTo,true);
    }
    internal ReturnCorrectionApprovalV1 Read(Guid tenant,ReturnCorrectionCandidateV1 candidate)
    {
        var policy =Find(tenant,candidate.Target) ?? throw Error("CORRECTION_APPROVAL_NOT_AVAILABLE",409); var at =DateTimeOffset.UtcNow;
        if (at <policy.ValidFrom || at >=policy.ValidTo || !policy.ErrorEvidenceRefs.Contains(candidate.ErrorEvidenceRef)) throw Error("CORRECTION_APPROVAL_NOT_EFFECTIVE",409);
        if (candidate.CreatorId ==policy.ApproverId) throw Error("CORRECTION_INDEPENDENT_APPROVER_REQUIRED",403);
        var body =new { candidate.Id,candidate.Version,candidate.ContentDigest,policy.Reference,policy.ApproverId,policy.ValidFrom,policy.ValidTo };
        return new(RmaInboundAdapterV1.Ref("Simulation.Approval.ReturnCorrection.v1",candidate.Id.ToString(),body),candidate.Id,candidate.Version,candidate.ContentDigest,
            policy.Reference,policy.ApproverId,policy.ValidFrom,policy.ValidTo,true);
    }
}

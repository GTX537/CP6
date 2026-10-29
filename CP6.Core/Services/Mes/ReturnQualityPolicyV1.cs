using System.Text.Json;
using CP6.Core.Services.Wms;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Mes;

public sealed class ReturnQualityPolicySourceV1
{
    private readonly ReturnQualitySimulationCaseV1[] cases;
    public ReturnQualityPolicySourceV1(IOptions<ReturnQualitySimulationOptionsV1> options)
    {
        cases = options.Value.Enabled
            ? JsonSerializer.Deserialize<ReturnQualitySimulationCaseV1[]>(options.Value.CasesJson,RmaFormalStoreV1.Json)
                ?? throw new InvalidOperationException("QUALITY_SIMULATION_CASES_REQUIRED") : [];
        foreach (var c in cases)
        {
            if (c == null || c.TenantId == Guid.Empty || c.SiteId == Guid.Empty || string.IsNullOrWhiteSpace(c.Version)
                || c.EvidenceRefs == null || c.EvidenceRefs.Length is <1 or >100 || c.EvidenceRefs.Distinct().Count() != c.EvidenceRefs.Length)
                throw new InvalidOperationException("QUALITY_SIMULATION_CASE_INVALID");
            Reference(c.QualityPolicyRef); foreach (var evidence in c.EvidenceRefs) Reference(evidence);
        }
        if (cases.Select(x => (x.TenantId,x.SiteId,x.QualityPolicyRef)).Distinct().Count() != cases.Length)
            throw new InvalidOperationException("QUALITY_SIMULATION_CASE_DUPLICATE");
    }
    internal ReturnQualityPolicyV1? Find(Guid tenant, RmaQualitySourceV1 source)
    {
        var c = cases.SingleOrDefault(x => x.TenantId == tenant && x.SiteId == source.SiteId && x.QualityPolicyRef == source.QualityPolicyRef);
        return c == null ? null : new(new("Simulation.Quality.ReturnPolicy.v1",Digest(new { tenant,c.SiteId,c.QualityPolicyRef }),c.Version,Digest(c)),
            c.RequireIndependentRegistrar,c.EvidenceRefs,true);
    }
    internal static void Reference(FulfilmentRefV1? value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.Owner) || value.Owner.Length >64 || string.IsNullOrWhiteSpace(value.Id)
            || value.Id.Length >200 || string.IsNullOrWhiteSpace(value.Version) || value.Version.Length >64
            || value.Digest?.Length !=64 || value.Digest.Any(x => !char.IsAsciiHexDigitLower(x))) throw Error("QUALITY_REFERENCE_INVALID",400);
    }
    internal static void Reason(string value)
    { if (string.IsNullOrWhiteSpace(value) || value.Length >500) throw Error("QUALITY_REASON_REQUIRED",400); }
}

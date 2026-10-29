using System.Text.Json;
using CP6.Core.Services.Mes;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;

namespace CP6.Core.Services.Wms;

public sealed class ReturnDispositionPolicyV2
{
    private readonly ReturnDispositionSimulationCaseV2[] cases;
    public ReturnDispositionPolicyV2(IOptions<ReturnDispositionSimulationOptionsV2> options)
    {
        cases = options.Value.Enabled ? JsonSerializer.Deserialize<ReturnDispositionSimulationCaseV2[]>(options.Value.CasesJson,RmaFormalStoreV1.Json)
            ?? throw new InvalidOperationException("DISPOSITION_CASES_REQUIRED") : [];
        foreach (var c in cases)
        {
            var definition = ReturnDispositionOwnersV2.Get(c.Disposition);
            if (c.TenantId ==Guid.Empty || c.SiteId ==Guid.Empty || string.IsNullOrWhiteSpace(c.Version) || c.Version.Length >64
                || string.IsNullOrWhiteSpace(c.Label) || c.Label.Length >200
                || c.AllowedQualities.Length is <1 or >2 || c.AllowedQualities.Any(x => x is not ("ACCEPTED" or "REJECTED"))
                || c.AllowedQualities.Distinct().Count() !=c.AllowedQualities.Length || c.EvidenceRefs.Length is <1 or >100
                || definition.PostingKind =="MOVE" !=c.DestinationLocationId.HasValue || c.DestinationLocationId ==Guid.Empty
                || definition.PostingKind =="SHIP" !=(c.PartyRef !=null)
                || definition.PostingKind =="SHIP" !=(c.ExternalCustodyRef !=null)
                || c.Disposition =="RESALE_STOCK" && !c.AllowedQualities.SequenceEqual(["ACCEPTED"]))
                throw new InvalidOperationException("DISPOSITION_CASE_INVALID");
            Reference(c.QualityPolicyRef); Reference(c.BusinessRef); Reference(c.AuthorityRef);
            if (c.PartyRef !=null) Reference(c.PartyRef); if (c.ExternalCustodyRef !=null) Reference(c.ExternalCustodyRef);
            foreach (var proof in c.EvidenceRefs) Reference(proof);
        }
        if (cases.Select(x => (x.TenantId,x.SiteId,x.Disposition,x.QualityPolicyRef,x.BusinessRef)).Distinct().Count() !=cases.Length)
            throw new InvalidOperationException("DISPOSITION_CASE_DUPLICATE");
    }
    internal ReturnDispositionOptionV2[] Options(Guid tenant,RmaQualitySourceV1 source,string kind,string quality)
        => cases.Where(x => x.TenantId ==tenant && x.SiteId ==source.SiteId && x.QualityPolicyRef ==source.QualityPolicyRef
            && x.Disposition ==kind && x.AllowedQualities.Contains(quality)).Select(c => new ReturnDispositionOptionV2(
                new("Simulation.ReturnDisposition.v2",Digest(new { c.TenantId,c.SiteId,c.Disposition,c.BusinessRef }),c.Version,Digest(c)),
                c.Disposition,c.Label,c.BusinessRef,c.AuthorityRef,c.DestinationLocationId,c.PartyRef,c.ExternalCustodyRef,c.EvidenceRefs,true)).ToArray();
}

public static class ReturnDispositionOwnersV2
{
    public static readonly ReturnDispositionDefinitionV2[] All = [
        new("RESALE_STOCK","wms-return-resale","Main.Wms.ReturnResale.v2","RMA-DSP-RESALE-MOVE-2","MOVE","MOVE"),
        new("REPAIR","mes-return-repair","Main.Mes.ReturnRepairTransfer.v2","RMA-DSP-REPAIR-MOVE-2","MOVE","MOVE"),
        new("SCRAP","quality-return-scrap","Main.Quality.ReturnScrap.v2","RMA-DSP-SCRAP-ADJUST-LOSS-2","ADJUSTMENT","ADJUST"),
        new("SUPPLIER_RETURN","purchasing-return-shipment","Main.Purchasing.ReturnShipment.v2","RMA-DSP-SUPPLIER-RETURN-SHIP-2","SHIP","SHIP"),
        new("RETURN_TO_CUSTOMER","wms-customer-return-shipment","Main.Wms.CustomerReturnShipment.v2","RMA-DSP-CUSTOMER-RETURN-SHIP-2","SHIP","SHIP")];
    public static ReturnDispositionDefinitionV2 Get(string kind) => All.SingleOrDefault(x => x.Kind ==kind) ?? throw Error("DISPOSITION_INVALID",400);
    internal static bool ContainsResource(string key) => All.Any(x => x.Resource ==key);
}

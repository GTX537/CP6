using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public partial class StockQcService
{
    internal async Task<FulfilmentRefV1> CorrectReturnReleaseAsync(OperationsAccessV1 parent,ReturnCorrectionCandidateV1 c,CancellationToken ct)
    {
        var access =await parent.ActionAsync(OperationsOwnerV1.StockQc,"correct-return-release",ct);
        await ReturnCorrectionTargetsV1.Scope(access,c.Target,ct);
        var original =await _db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_return_disposition_v2.quality_releases WHERE tenant_id={_db.CurrentTenantId} AND work_id={c.Target.DispositionWorkId}").SingleAsync(ct);
        var body =new { c.Id,c.Target.QualityReleaseRef,c.Ranges,Spans =ReturnCorrectionTargetsV1.Spans(c.Target,c.Ranges),OriginalDigest =Digest(original),Actor =access.ActorId };
        var reference =RmaInboundAdapterV1.Ref("Main.Wms.StockQc.ReturnReleaseCorrection.v1",c.Id.ToString(),body);
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.quality_releases(tenant_id,proposal_id,body) VALUES({_db.CurrentTenantId},{c.Id},{Encode(new { Reference =reference,Value =body })})",ct);
        return reference;
    }
}

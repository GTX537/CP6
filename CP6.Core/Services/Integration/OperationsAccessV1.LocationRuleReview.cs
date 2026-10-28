using CP6.Core.Services.Wms;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    internal async Task RequireLocationRule(Guid id,CancellationToken ct)
    {
        var h=await new LocationRuleStoreV1(db).Heads().SingleOrDefaultAsync(x=>x.Id==id,ct)
            ??throw new OrderPriceCorrectionException("RULE_PROPOSAL_NOT_FOUND",404);
        await RequireLocationRuleTarget(h.Location,ct);
    }
    internal void RequireLocationRuleFields(bool write)
    {
        if(LocationRuleFields().Any(f=>!f.CanRead))throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);
        if(write)RequireLocationRuleWrite(LocationRuleValuesV1.Fields);
    }
}

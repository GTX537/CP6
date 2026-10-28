using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    internal async Task<Location> RequireLocationRuleTarget(Guid id,CancellationToken ct)
    {
        if(authority.Resource!=OperationsOwnerV1.LocationRule)throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        // Rule scope is the same native Location identity/creator/department and warehouse/area.
        var row=await (await MasterLocations(ct,true)).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new OrderPriceCorrectionException("MASTER_NOT_FOUND",404);
        await (await SourceAsync(OperationsOwnerV1.Location,ct)).RequireMasterNameTarget("locations",id,ct);
        return row;
    }
    internal LocationRuleFieldV1[] LocationRuleFields()=>LocationRuleValuesV1.Fields.Select(f=>new LocationRuleFieldV1(f,authority.Fields.Fields[f].CanRead,authority.Fields.Fields[f].CanWrite)).ToArray();
    internal void RequireLocationRuleWrite(IEnumerable<string> fields)
    {
        if(fields.Any(f=>!authority.Fields.Fields[f].CanWrite))throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);
    }
}

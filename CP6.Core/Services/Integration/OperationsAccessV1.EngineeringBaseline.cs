using CP6.Core.Services.Plm;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    internal IQueryable<EngineeringBaselineHeadV1> EngineeringBaselines(Guid environment)
    {
        if(authority.Source.TenantId!=db.CurrentTenantId || authority.Resource!=OperationsOwnerV1.EngineeringBaseline
            || authority.Action is not("query" or "freeze" or "cancel" or "successor"))throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        var rows=EngineeringBaselineStoreV1.Heads(db,environment);
        if(authority.Scope==1)return rows.Where(x=>x.CreatorId==ActorId);if(authority.Scope==5)return rows;
        var allowed=StandaloneDepartmentScopeV1.Objects(db,authority,"plm-engineering-revision");return rows.Where(x=>allowed.Contains(x.RevisionId));
    }
    internal async Task RequireEngineeringBaseline(Guid environment,Guid id,CancellationToken ct)
    {if(!await EngineeringBaselines(environment).AnyAsync(x=>x.Id==id,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);}
    internal async Task RequireEngineeringFreeze(Guid revision,CancellationToken ct)
    {
        if(authority.Resource!=OperationsOwnerV1.EngineeringBaseline || authority.Action!="freeze")throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        if(authority.Scope is 1 or 5)return;
        if(!await StandaloneDepartmentScopeV1.Objects(db,authority,"plm-engineering-revision").AnyAsync(x=>x==revision,ct))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN",403);
    }
}

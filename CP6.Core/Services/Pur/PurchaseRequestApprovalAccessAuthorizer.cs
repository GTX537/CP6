using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

public sealed class PurchaseRequestApprovalAccessAuthorizer : IApprovalBusinessAccessAuthorizer
{
    private readonly CP6Context _db;
    private readonly IDataScopeFilter _scope;
    public PurchaseRequestApprovalAccessAuthorizer(CP6Context db, IDataScopeFilter scope)
    {
        _db = db;
        _scope = scope;
    }

    public string BizType => "PUR_PR";

    public async Task<BusinessApprovalAccess> AuthorizeAsync(
        string bizId, UserPermissionContext permission, CancellationToken ct = default)
    {
        if (!permission.ActionKeys.Contains("pur-pr:query"))
            throw new UnauthorizedAccessException("E-PUR-059");
        var query = _scope.Apply(
            _db.PurchaseRequests.AsNoTracking().Where(x => x.PrNo == bizId && !x.IsDeleted),
            "pur-pr", permission);
        var pr = await query.SingleOrDefaultAsync(ct)
                 ?? throw new UnauthorizedAccessException("E-PUR-059");
        return new(pr.Status.ToString(), pr.Status == PrStatus.Draft &&
            permission.ActionKeys.Contains("pur-pr:submit"));
    }
}

using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed partial class IamOrganizationPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Organization_legacy_unversioned_leader_write_is_rejected_without_saving_foreign_identity()
    {
        await using var db=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});var id=Guid.NewGuid();
        db.Sys_Depts.Add(new(){Id=id,DeptCode="own",DeptName="own",Path=$"/{id}/"});await db.SaveChangesAsync();db.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>new DeptService(db).SetLeaderAsync(id,Guid.NewGuid(),"fixture"));
        Assert.Null(await db.Sys_Depts.Select(x=>x.LeaderId).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_orphan_is_not_silently_promoted_to_root()
    {
        await using var db=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});var id=Guid.NewGuid();
        db.Sys_Depts.Add(new(){Id=id,ParentId=Guid.NewGuid(),DeptCode="orphan",DeptName="orphan",Path=$"/{id}/"});await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Empty(await new DeptService(db).TreeAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_missing_source_cannot_turn_empty_subtree_path_into_all_departments()
    {
        await using var db=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});var id=Guid.NewGuid();
        var actor=new Sys_User{UserName="no-department",Password="owned"};db.Sys_Users.Add(actor);
        db.Sys_Depts.Add(new(){Id=id,DeptCode="other",DeptName="other",Path=$"/{id}/"});
        db.PurchaseRequests.Add(new(){PrNo="owned-scope",DeptId=id,RequestDate=DateTime.Today});await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var current=new UserPermissionContext{UserId=actor.Id,DeptPath="",DataScopes=new(){["order"]=3}};
        Assert.Empty(await new DataScopeFilter(db).Apply(db.PurchaseRequests,"order",current).ToListAsync());
    }
}

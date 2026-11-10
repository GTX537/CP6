using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public sealed class PaperRollPermissionSeedTests
{
    [Fact]
    public void New_view_capability_is_registered_idempotently_without_a_role_grant()
    {
        using var db=TestHelper.CreateInMemoryContext();var tenant=Guid.NewGuid();
        db.Sys_Tenants.Add(new(){Id=tenant,TenantCode=tenant.ToString("N"),TenantName="owned seed test"});db.SaveChanges();
        WmsMenuSeed.EnsureSeeded(db);WmsPermissionSeed.EnsureSeeded(db);WmsPermissionSeed.EnsureSeeded(db);
        Assert.Single(db.Sys_MenuActions.IgnoreQueryFilters().Where(a=>a.TenantId==tenant&&a.MenuId==441&&a.ActionCode=="view"));
        Assert.False(db.Sys_RoleActions.IgnoreQueryFilters().Any(a=>a.TenantId==tenant&&a.MenuId==441&&a.ActionCode=="view"));
        Assert.True(db.Sys_RoleActions.IgnoreQueryFilters().Any(a=>a.TenantId==tenant&&a.MenuId==441&&a.ActionCode=="add"));
    }
}

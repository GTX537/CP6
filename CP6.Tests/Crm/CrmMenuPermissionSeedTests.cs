using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Crm;

public class CrmMenuPermissionSeedTests
{
    private static CP6Context NewDb() => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Fact]
    public void Seed_CreatesDisabledMenuCatalogueAndAdminGrantsPerTenant()
    {
        using var db = NewDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Sys_Tenants.AddRange(
            new Sys_Tenant { Id = tenantA, TenantCode = "CRM-A", TenantName = "CRM A" },
            new Sys_Tenant { Id = tenantB, TenantCode = "CRM-B", TenantName = "CRM B" });
        db.SaveChanges();

        CrmMenuPermissionSeed.EnsureSeeded(db);

        var menus = db.Sys_Menus.Where(x => x.MenuId >= 800 && x.MenuId <= 805).ToList();
        Assert.Equal(6, menus.Count);
        Assert.All(menus, x => Assert.False(x.Enable));
        Assert.Equal("crm-lead", menus.Single(x => x.MenuId == 802).MenuKey);

        foreach (var tenantId in new[] { tenantA, tenantB })
        {
            Assert.Equal(6, db.Sys_RoleMenus.IgnoreQueryFilters().Count(x =>
                x.TenantId == tenantId && x.RoleId == 1 && x.MenuId >= 800 && x.MenuId <= 805));
            Assert.Equal(22, db.Sys_MenuActions.IgnoreQueryFilters().Count(x =>
                x.TenantId == tenantId && x.MenuId >= 801 && x.MenuId <= 805));
            Assert.Equal(22, db.Sys_RoleActions.IgnoreQueryFilters().Count(x =>
                x.TenantId == tenantId && x.RoleId == 1 && x.MenuId >= 801 && x.MenuId <= 805));
        }
    }

    [Fact]
    public void Seed_IsIdempotentAndDoesNotDisableLaterEnabledMenu()
    {
        using var db = NewDb();
        var tenant = Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "CRM", TenantName = "CRM" });
        db.SaveChanges();

        CrmMenuPermissionSeed.EnsureSeeded(db);
        db.Sys_Menus.Single(x => x.MenuId == 802).Enable = true;
        db.SaveChanges();
        CrmMenuPermissionSeed.EnsureSeeded(db);

        Assert.True(db.Sys_Menus.Single(x => x.MenuId == 802).Enable);
        Assert.Equal(6, db.Sys_RoleMenus.IgnoreQueryFilters().Count(x => x.TenantId == tenant));
        Assert.Equal(22, db.Sys_MenuActions.IgnoreQueryFilters().Count(x => x.TenantId == tenant));
    }
}
